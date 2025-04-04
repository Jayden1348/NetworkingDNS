using System;
using System.Data;
using System.Data.SqlTypes;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using LibData;

// ReceiveFrom();
class Program
{
    static void Main(string[] args)
    {
        ServerUDP.start();
    }
}

public class Setting
{
    public int ServerPortNumber { get; set; }
    public string? ServerIPAddress { get; set; }
    public int ClientPortNumber { get; set; }
    public string? ClientIPAddress { get; set; }
}


class ServerUDP
{
    // Fills the "setting" parameter with the settings
    static string configFile = @"../Setting.json";
    static string configContent = File.ReadAllText(configFile);
    static Setting? setting = JsonSerializer.Deserialize<Setting>(configContent);


    // Reads the DNSrecords json and Lists them
    static List<DNSRecord> DNSRecords = ReadDNSRecords();
    public static List<DNSRecord> ReadDNSRecords()
    {
        string dnsRecordsFile = "DNSrecords.json";
        string dnsRecordsContent = File.ReadAllText(dnsRecordsFile);
        return JsonSerializer.Deserialize<List<DNSRecord>>(dnsRecordsContent);
    }

    public static DNSRecord SearchDNSRecords(object content)
    {
        try
        {
            Dictionary<string, string> contents = JsonSerializer.Deserialize<Dictionary<string, string>>(content.ToString());

            string dnstype = contents["Type"];
            string dnsvalue = contents["Value"];
            return DNSRecords.FirstOrDefault(x => x.Type == dnstype && x.Name == dnsvalue);
        }
        catch (Exception e)
        {
            return null;
        }
    }




    public static void start()
    {
        // Making a new msgid
        int msgcounter = 0;
        int GetNextMsgId() => msgcounter++;

        void print(Message newMessage) => Console.WriteLine($"-----------------------------------\nReceived a {newMessage.MsgType} message:\nID: {newMessage.MsgId}\nContent: {newMessage.Content}\n-----------------------------------");
        byte[] encrypt(Message JSONmsg) => Encoding.ASCII.GetBytes(JsonSerializer.Serialize(JSONmsg));
        Message decrypt(byte[] bytemsg, int end) => JsonSerializer.Deserialize<Message>(Encoding.ASCII.GetString(bytemsg, 0, end));

        byte[] buffer = new byte[1000];
        int endcondition = 0;

        IPAddress ipAddress = IPAddress.Parse(setting.ServerIPAddress);
        IPEndPoint localEndpoint = new IPEndPoint(ipAddress, setting.ServerPortNumber);
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        EndPoint remoteEndpoint = (EndPoint)sender;

        Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(localEndpoint);

        Console.WriteLine("\nWaiting for messages...\n");
        while (true)   // for infinite running = true
        {

            int recievedmessage = sock.ReceiveFrom(buffer, ref remoteEndpoint);
            Message newmsg = decrypt(buffer, recievedmessage);
            print(newmsg);

            switch (newmsg.MsgType)
            {
                case MessageType.Hello:
                    Message Welcome = new Message
                    {
                        MsgId = GetNextMsgId(),
                        MsgType = MessageType.Welcome,
                        Content = "Welcome Client!"
                    };
                    byte[] WelcomeMessage = encrypt(Welcome);
                    sock.SendTo(WelcomeMessage, WelcomeMessage.Length, SocketFlags.None, remoteEndpoint);
                    Console.WriteLine("Send Welcome\n\n");
                    break;

                case MessageType.DNSLookup:
                    DNSRecord FoundRecord = SearchDNSRecords(newmsg.Content);
                    Message DNSLookupReply = new Message
                    {
                        MsgId = newmsg.MsgId,
                        MsgType = FoundRecord == null ? MessageType.Error : MessageType.DNSLookupReply,
                        Content = FoundRecord == null ? "Domain not found" : FoundRecord
                    };
                    byte[] DNSLookupReplyMessage = encrypt(DNSLookupReply);
                    sock.SendTo(DNSLookupReplyMessage, DNSLookupReplyMessage.Length, SocketFlags.None, remoteEndpoint);
                    Console.WriteLine("Send DNSLookupReply\n\n");
                    break;

                case MessageType.Ack:
                    Console.WriteLine("\n");
                    break;

                default:
                    Console.WriteLine($"Unknown message type received ({newmsg.MsgType}).");
                    break;
            }






            // ✅ TODO: [Create a socket and endpoints and bind it to the server IP address and port number]
            // ✅ TODO:[Receive and print a received Message from the client]
            // ✅ TODO:[Receive and print Hello]
            // ✅ TODO:[Send Welcome to the client]
            // ✅ TODO:[Receive and print DNSLookup]
            // ✅ TODO:[Query the DNSRecord in Json file]
            // ✅ TODO:[If found Send DNSLookupReply containing the DNSRecord]
            // ✅ TODO:[If not found Send Error]

            // ✅? TODO:[Receive Ack about correct DNSLookupReply from the client]


            // TODO:[If no further requests receieved send End to the client]

            endcondition++;
        }
    }


}