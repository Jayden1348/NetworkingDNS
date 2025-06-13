using System.Collections.Immutable;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LibData;

// SendTo();
class Program
{
    static void Main(string[] args)
    {
        ClientUDP.start();
    }
}

public class Setting
{
    public int ServerPortNumber { get; set; }
    public string? ServerIPAddress { get; set; }
    public int ClientPortNumber { get; set; }
    public string? ClientIPAddress { get; set; }
}

class ClientUDP
{
    static string configFile = @"../Setting.json";
    static string configContent = File.ReadAllText(configFile);
    static Setting? setting = JsonSerializer.Deserialize<Setting>(configContent);



    public static void start()
    {
        // Making a new msgid
        int msgcounter = 0;
        int GetNextMsgId() => msgcounter++;

        var dnsLookups = new List<Message>
        {
            new Message
            {
                MsgId = GetNextMsgId(),
                MsgType = MessageType.DNSLookup,
                Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "www.test.com" } } // Valid record
            },
            new Message
            {
                MsgId = GetNextMsgId(),
                MsgType = MessageType.DNSLookup,
                Content = new Dictionary<string, string> { { "Type", "MX" }, { "Value", "example.com" } } // Valid record
            },
            new Message
            {
                MsgId = GetNextMsgId(),
                MsgType = MessageType.DNSLookup,
                Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "www.somewebsite.com" } } // Invalid record, not found
            },
            new Message
            {
                MsgId = GetNextMsgId(),
                MsgType = MessageType.DNSLookup,
                Content = new Dictionary<string, string> { { "Type", "B" }, { "Value", "example.com" } } // Invalid record, wrong type
            },
            new Message
            {
                MsgId = GetNextMsgId(),
                MsgType = MessageType.DNSLookup,
                Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "FAULTY123!" } } // Invalid record, wrong value
            },
            new Message
            {
                MsgId = GetNextMsgId(),
                MsgType = (MessageType)100000,
                Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "FAULTY123!" } } // Invalid record, nonexistent msgtype
            },
            new Message
            {
                // Empty message
            }


            // content error possibilities 
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = "Invalid content" }, // Invalid content
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = null }, // Null content
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = "" } // Empty content

            // type error possibilities 
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = new Dictionary<string, string> { { "Type", "InvalidType" }, { "Value", "www.test.com" } } }, // Invalid type
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = new Dictionary<string, string> { { "Type", "" }, { "Value", "www.test.com" } } }, // Empty type
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = new Dictionary<string, string> { { "Type", null }, { "Value", "www.test.com" } } }, // Null type

            // value error possibilities 
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "" } } }, // Empty value
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", null } } }, // Null value
            // new Message { MsgId = GetNextMsgId(), MsgType = MessageType.DNSLookup, Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "InvalidValue" } } } // Invalid value

        };


        void print(Message newMessage) => Console.WriteLine($"-----------------------------------\nReceived a {newMessage.MsgType} message:\nID: {newMessage.MsgId}\nContent: {newMessage.Content}\n-----------------------------------\n\n");
        byte[] encrypt(Message JSONmsg) => Encoding.ASCII.GetBytes(JsonSerializer.Serialize(JSONmsg));
        Message decrypt(byte[] bytemsg, int end) => JsonSerializer.Deserialize<Message>(Encoding.ASCII.GetString(bytemsg, 0, end));

        byte[] buffer = new byte[1000];

        IPAddress ipAddress = IPAddress.Parse(setting.ServerIPAddress);
        IPEndPoint ServerEndpoint = new IPEndPoint(ipAddress, setting.ServerPortNumber);
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        EndPoint remoteEndpoint = (EndPoint)sender;

        Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.ReceiveTimeout = 2000;     // When not recieving a confirmation, time out



        Message Hello = new Message
        {
            MsgId = GetNextMsgId(),
            MsgType = MessageType.Hello,
            Content = "Hello Server!"
        };
        byte[] HelloMessage = encrypt(Hello);
        sock.SendTo(HelloMessage, HelloMessage.Length, SocketFlags.None, ServerEndpoint);


        Console.WriteLine("\nSend Hello, waiting for Welcome...");
        try
        {
            int receivedMessage = sock.ReceiveFrom(buffer, ref remoteEndpoint);
            Message newMsg = decrypt(buffer, receivedMessage);
            print(newMsg);
            if (newMsg.MsgType != MessageType.Welcome) { Console.WriteLine("The recieved message wasn't the expected 'Welcome' message!"); return; }
        }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode == SocketError.TimedOut)
            {
                Console.WriteLine("ReceiveFrom timed out. No response received from server.");
            }
            else
            {
                Console.WriteLine($"SocketException occurred: {ex.Message}");
            }
            sock.Close();
            return; // Stop the program
        }




        foreach (var dnsLookup in dnsLookups)
        {
            byte[] dnsLookupMessage = encrypt(dnsLookup);
            sock.SendTo(dnsLookupMessage, dnsLookupMessage.Length, SocketFlags.None, ServerEndpoint);

            Console.WriteLine($"Send DNSLookup, waiting for DNSLookupReply...");
            try
            {
                int receivedMessage = sock.ReceiveFrom(buffer, ref remoteEndpoint);
                Message newMsg = decrypt(buffer, receivedMessage);
                print(newMsg);

                if (newMsg.MsgType == MessageType.DNSLookupReply || newMsg.MsgType == MessageType.Error)
                {
                    if (newMsg.MsgId != dnsLookup.MsgId)
                    {
                        Console.WriteLine($"The ID of the received message ({newMsg.MsgId}) does not match the expected ID ({dnsLookup.MsgId})!");
                        return;
                    }

                    // Skip sending Ack for MsgId == 2 to test server resend logic
                    if (dnsLookup.MsgId == 2)
                    {
                        Console.WriteLine("Intentionally NOT sending Ack for MsgId 2 to test server resend.");
                    }
                    else
                    {
                        // Send acknowledgment
                        Message acknowledge = new Message
                        {
                            MsgId = GetNextMsgId(),
                            MsgType = MessageType.Ack,
                            Content = dnsLookup.MsgId
                        };
                        byte[] acknowledgeMessage = encrypt(acknowledge);
                        sock.SendTo(acknowledgeMessage, acknowledgeMessage.Length, SocketFlags.None, ServerEndpoint);
                    }
                }
                // else if (newMsg.MsgType == MessageType.Error)
                // {
                // }
                else
                {
                    Console.WriteLine("Unexpected message type received!");
                    return;
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    Console.WriteLine("ReceiveFrom timed out. No response received from server.");
                }
                else
                {
                    Console.WriteLine($"SocketException occurred: {ex.Message}");
                }
                sock.Close();
                return; // Stop the program
            }
        }




        // Receive and print End message from server
        sock.ReceiveTimeout = 5000; // 10 seconds

        Console.WriteLine("Waiting for End message from server...");
        try
        {
            bool endReceived = false;
            while (!endReceived)
            {
                int receivedMessage = sock.ReceiveFrom(buffer, ref remoteEndpoint);
                Message endMsg = decrypt(buffer, receivedMessage);
                print(endMsg);

                if (endMsg.MsgType == MessageType.End)
                {
                    Console.WriteLine("Received End message. Terminating client.");
                    endReceived = true;
                }
                else
                {
                    Console.WriteLine("Received non-End message while waiting for End. Ignoring and waiting...");
                    // Optionally: handle or log the message here
                }
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"SocketException occurred: {ex.Message}");
        }
        finally
        {
            sock.Close();
        }

        // ✅ TODO: [Deserialize Setting.json]
        // ✅ TODO: [Create endpoints and socket]
        // ✅ TODO: [Create and send HELLO]
        // ✅ TODO: [Receive and print Welcome from server]
        // ✅ TODO: [Create and send DNSLookup Message]
        // ✅ TODO: [Receive and print DNSLookupReply from server]
        // ✅ TODO: [Send Acknowledgment to Server]
        // ✅ TODO: [Send next DNSLookup to server]
        // ✅ repeat the process until all DNSLoopkups (correct and incorrect onces) are sent to server and the replies with DNSLookupReply


    }
}