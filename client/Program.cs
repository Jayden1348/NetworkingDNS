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

    // ✅ TODO: [Deserialize Setting.json]
    static string configFile = @"../Setting.json";
    static string configContent = File.ReadAllText(configFile);
    static Setting? setting = JsonSerializer.Deserialize<Setting>(configContent);


    // Turning a Message object into JSON into bytes
    static byte[] byteify(Message JSONmsg) => Encoding.ASCII.GetBytes(JsonSerializer.Serialize(JSONmsg));


    // Making a new msgid
    static int msgcounter = 0;
    static int GetNextMsgId() => msgcounter++;


    public static void start()
    {

        // ✅ TODO: [Create endpoints and socket]
        IPAddress ipAddress = IPAddress.Parse(setting.ServerIPAddress);
        IPEndPoint serverEndpoint = new IPEndPoint(ipAddress, 32000);
        Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        sock.Connect(serverEndpoint);

        // ✅ TODO: [Create and send HELLO]
        Message HelloMessage = new Message
        {
            MsgId = GetNextMsgId(),
            MsgType = MessageType.Hello,
            Content = "Hello Server!"
        };
        sock.Send(byteify(HelloMessage));


        //TODO: [Receive and print Welcome from server]

        // TODO: [Create and send DNSLookup Message]


        //TODO: [Receive and print DNSLookupReply from server]


        //TODO: [Send Acknowledgment to Server]

        // TODO: [Send next DNSLookup to server]
        // repeat the process until all DNSLoopkups (correct and incorrect onces) are sent to server and the replies with DNSLookupReply

        //TODO: [Receive and print End from server]





    }
}