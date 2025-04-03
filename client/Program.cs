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

        void print(Message newMessage) => Console.WriteLine($"-----------------------------------\nReceived a {newMessage.MsgType} message:\nID: {newMessage.MsgId}\nContent: {newMessage.Content}\n-----------------------------------\n");
        byte[] encrypt(Message JSONmsg) => Encoding.ASCII.GetBytes(JsonSerializer.Serialize(JSONmsg));
        Message decrypt(byte[] bytemsg, int end) => JsonSerializer.Deserialize<Message>(Encoding.ASCII.GetString(bytemsg, 0, end));

        byte[] buffer = new byte[1000];



        IPAddress ipAddress = IPAddress.Parse(setting.ServerIPAddress);
        IPEndPoint ServerEndpoint = new IPEndPoint(ipAddress, setting.ServerPortNumber);
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        EndPoint remoteEndpoint = (EndPoint)sender;

        Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.ReceiveTimeout = 5000;     // When not recieving a confirmation, time out



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



        Message DNSLookup = new Message
        {
            MsgId = GetNextMsgId(),
            MsgType = MessageType.DNSLookup,
            Content = new Dictionary<string, string> { { "Type", "A" }, { "Value", "www.test.com" } }
        };
        byte[] DNSLookupMessage = encrypt(DNSLookup);
        sock.SendTo(DNSLookupMessage, DNSLookupMessage.Length, SocketFlags.None, ServerEndpoint);


        Console.WriteLine("Send DNSLookup, waiting for DNSLookupReply...");
        try
        {
            int receivedMessage = sock.ReceiveFrom(buffer, ref remoteEndpoint);
            Message newMsg = decrypt(buffer, receivedMessage);
            print(newMsg);
            if (newMsg.MsgType != MessageType.DNSLookupReply && newMsg.MsgType != MessageType.Error) { Console.WriteLine("The recieved message wasn't either of the expected 'DNSLookupReply' or 'Error' messages!"); return; }
            if (newMsg.MsgId != DNSLookup.MsgId) { Console.WriteLine($"The id of recieved message wasn't the expected id {DNSLookup.MsgId}!"); return; }
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


        Message Acknowledge = new Message
        {
            MsgId = GetNextMsgId(),
            MsgType = MessageType.Ack,
            Content = DNSLookup.MsgId
        };
        byte[] AcknowledgeMessage = encrypt(Acknowledge);
        sock.SendTo(AcknowledgeMessage, AcknowledgeMessage.Length, SocketFlags.None, ServerEndpoint);

        // ✅ TODO: [Deserialize Setting.json]
        // ✅ TODO: [Create endpoints and socket]
        // ✅ TODO: [Create and send HELLO]
        // ✅ TODO: [Receive and print Welcome from server]
        // ✅ TODO: [Create and send DNSLookup Message]
        // ✅ TODO: [Receive and print DNSLookupReply from server]
        // ✅ TODO: [Send Acknowledgment to Server]

        // TODO: [Send next DNSLookup to server]
        // repeat the process until all DNSLoopkups (correct and incorrect onces) are sent to server and the replies with DNSLookupReply

        //TODO: [Receive and print End from server]





    }
}