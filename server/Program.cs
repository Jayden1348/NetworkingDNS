using System;
using System.Data;
using System.Data.SqlTypes;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

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
        try
        {
            string dnsRecordsFile = "DNSrecords.json";
            string dnsRecordsContent = File.ReadAllText(dnsRecordsFile);
            var records = JsonSerializer.Deserialize<List<DNSRecord>>(dnsRecordsContent);
            if (records == null)
            {
                // no files found
                // Console.WriteLine("No DNS records found in the file.");
                return new List<DNSRecord>();
            }
            return records;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading DNS records: {ex.Message}");
            return new List<DNSRecord>();
        }

    }

    public static DNSRecord SearchDNSRecords(object content)
    {
        try
        {
            var contentString = content?.ToString();
            if (string.IsNullOrEmpty(contentString))
            {
                return null;
            }

            Dictionary<string, string> contents = JsonSerializer.Deserialize<Dictionary<string, string>>(content.ToString());

            if (contents == null || !contents.ContainsKey("Type") || !contents.ContainsKey("Value")) return null;

            string dnstype = contents["Type"];
            string dnsvalue = contents["Value"];
            return DNSRecords.FirstOrDefault(x => x.Type == dnstype && x.Name == dnsvalue);
        }
        catch (Exception e)
        {
            return null;
        }
    }
    static Message decrypt(byte[] bytemsg, int end)
    {
        try
        {
            return JsonSerializer.Deserialize<Message>(Encoding.ASCII.GetString(bytemsg, 0, end));
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to deserialize message: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<int, (Message reply, EndPoint remote, int attempts, DateTime lastSent)> pendingReplies = new();
    private const int MaxRetries = 3;
    private static TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private static bool sessionEnded = false;


    public static void start()
    {
        // Making a new msgid
        int msgcounter = 0;
        int GetNextMsgId() => msgcounter++;
        int msgtracker = 0;
        bool hellorecieved = false;

        void print(Message newMessage) => Console.WriteLine($"-----------------------------------\nReceived a {newMessage.MsgType} message:\nID: {newMessage.MsgId}\nContent: {newMessage.Content}\n-----------------------------------");
        byte[] encrypt(Message JSONmsg) => Encoding.ASCII.GetBytes(JsonSerializer.Serialize(JSONmsg));

        byte[] buffer = new byte[1000];


        IPAddress ipAddress = IPAddress.Parse(setting.ServerIPAddress);
        IPEndPoint localEndpoint = new IPEndPoint(ipAddress, setting.ServerPortNumber);
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        EndPoint remoteEndpoint = (EndPoint)sender;

        Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(localEndpoint);

        sock.ReceiveTimeout = 1000;

        Console.WriteLine("\nWaiting for messages...\n");
        while (true)
        {
            // Only resend if session has not ended
            if (!sessionEnded)
            {
                foreach (var kvp in pendingReplies.ToList())
                {
                    var (reply, remote, attempts, lastSent) = kvp.Value;
                    if (attempts < MaxRetries && DateTime.Now - lastSent > RetryInterval)
                    {
                        byte[] replyBytes = encrypt(reply);
                        sock.SendTo(replyBytes, replyBytes.Length, SocketFlags.None, remote);
                        pendingReplies[kvp.Key] = (reply, remote, attempts + 1, DateTime.Now);
                        Console.WriteLine($"Resent DNSLookupReply for MsgId {kvp.Key}, attempt {attempts + 1}");
                    }
                    else if (attempts >= MaxRetries)
                    {
                        pendingReplies.Remove(kvp.Key);
                        Console.WriteLine($"Max retries reached for MsgId {kvp.Key}, giving up.");
                    }
                }
            }

            try
            {
                int recievedmessage = sock.ReceiveFrom(buffer, ref remoteEndpoint);


                Message newmsg = decrypt(buffer, recievedmessage);
                if (newmsg == null)
                {
                    Console.WriteLine("Received an invalid message.");
                    // send error message back to client
                    continue;
                }

                // catches invalid msgtyoes
                if (!Enum.IsDefined(typeof(MessageType), newmsg.MsgType))
                {
                    Console.WriteLine($"Invalid MsgType received: {newmsg.MsgType}");
                    Message Error = new Message
                    {
                        MsgId = newmsg.MsgId,
                        MsgType = MessageType.Error,
                        Content = "Invalid MsgType received!"
                    };
                    byte[] ErrorMessage = encrypt(Error);
                    sock.SendTo(ErrorMessage, ErrorMessage.Length, SocketFlags.None, remoteEndpoint);
                    continue;
                }
                print(newmsg);

                if (newmsg.MsgType == MessageType.Hello && newmsg.MsgId == 0 && newmsg.Content == null)
                {
                    Console.WriteLine($"Empty message was received.");
                    Message Error = new Message
                    {
                        MsgId = newmsg.MsgId,
                        MsgType = MessageType.Error,
                        Content = "Empty message was sent!"
                    };
                    byte[] ErrorMessage = encrypt(Error);
                    sock.SendTo(ErrorMessage, ErrorMessage.Length, SocketFlags.None, remoteEndpoint);
                    Console.WriteLine("Send Error\n\n");
                    continue;
                }


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
                        hellorecieved = true;
                        break;

                    case MessageType.DNSLookup:
                        if (!hellorecieved) break;

                        if (newmsg.Content == null)
                        {
                            Console.WriteLine("DNSLookup message content is null.");
                            Message NullContentError = new Message
                            {
                                MsgId = newmsg.MsgId,
                                MsgType = MessageType.Error,
                                Content = "DNSLookup message content is null."
                            };
                            byte[] NullContentErrorMessage = encrypt(NullContentError);
                            sock.SendTo(NullContentErrorMessage, NullContentErrorMessage.Length, SocketFlags.None, remoteEndpoint);
                            Console.WriteLine("Send NullContentError\n\n");
                            break;
                        }

                        var domain = JsonSerializer.Deserialize<Dictionary<string, string>>(newmsg.Content.ToString());
                        if (domain == null ||
                            !domain.ContainsKey("Value") || !(domain["Value"] is string value) ||
                            !domain.ContainsKey("Type") || !(domain["Type"] is string type))
                        {
                            Console.WriteLine("DNSLookup message is missing required keys (Value or Type).");
                            Message MissingKeysError = new Message
                            {
                                MsgId = newmsg.MsgId,
                                MsgType = MessageType.Error,
                                Content = "DNSLookup message is missing required keys (Value or Type)."
                            };
                            byte[] MissingKeysErrorMessage = encrypt(MissingKeysError);
                            sock.SendTo(MissingKeysErrorMessage, MissingKeysErrorMessage.Length, SocketFlags.None, remoteEndpoint);
                            Console.WriteLine("Send MissingKeysError\n\n");
                            break;
                        }

                        if (!IsValidDomain(domain["Value"]))
                        {
                            Message InvalidDomainError = new Message
                            {
                                MsgId = newmsg.MsgId,
                                MsgType = MessageType.Error,
                                Content = "Invalid domain format"
                            };
                            byte[] InvalidDomainErrorMessage = encrypt(InvalidDomainError);
                            sock.SendTo(InvalidDomainErrorMessage, InvalidDomainErrorMessage.Length, SocketFlags.None, remoteEndpoint);
                            Console.WriteLine("Send InvalidDomainError\n\n");
                            break;
                        }

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

                        // Track for selective repeat for both DNSLookupReply and Error (for DNSLookup)
                        if (DNSLookupReply.MsgType == MessageType.DNSLookupReply || DNSLookupReply.MsgType == MessageType.Error)
                        {
                            pendingReplies[DNSLookupReply.MsgId] = (DNSLookupReply, remoteEndpoint, 1, DateTime.Now);
                        }
                        break;

                    case MessageType.Ack:
                        if (newmsg.Content is JsonElement elem && elem.ValueKind == JsonValueKind.Number)
                        {
                            int ackedMsgId = elem.GetInt32();
                            if (pendingReplies.ContainsKey(ackedMsgId))
                            {
                                pendingReplies.Remove(ackedMsgId);
                                Console.WriteLine($"Ack received for MsgId {ackedMsgId}, removed from pending.\n\n");
                            }
                        }
                        break;

                    default:
                        Console.WriteLine($"Unknown message type received ({newmsg.MsgType}).");
                        Message Error = new Message
                        {
                            MsgId = newmsg.MsgId,
                            MsgType = MessageType.Error,
                            Content = "Unknown messagetype was sent!"
                        };
                        byte[] ErrorMessage = encrypt(Error);
                        sock.SendTo(ErrorMessage, ErrorMessage.Length, SocketFlags.None, remoteEndpoint);
                        Console.WriteLine("Send Error\n\n");
                        continue;
                }

            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    if (msgcounter == msgtracker) { continue; }

                    // Wait for all pending retries to finish before ending session
                    while (pendingReplies.Count > 0)
                    {
                        foreach (var kvp in pendingReplies.ToList())
                        {
                            var (reply, remote, attempts, lastSent) = kvp.Value;
                            if (attempts < MaxRetries && DateTime.Now - lastSent > RetryInterval)
                            {
                                byte[] replyBytes = encrypt(reply);
                                sock.SendTo(replyBytes, replyBytes.Length, SocketFlags.None, remote);
                                pendingReplies[kvp.Key] = (reply, remote, attempts + 1, DateTime.Now);
                                Console.WriteLine($"Resent DNSLookupReply for MsgId {kvp.Key}, attempt {attempts + 1}");
                            }
                            else if (attempts >= MaxRetries)
                            {
                                pendingReplies.Remove(kvp.Key);
                                Console.WriteLine($"Max retries reached for MsgId {kvp.Key}, giving up.");
                            }
                        }
                        // Give time for retries to be sent and avoid tight loop
                        Thread.Sleep(100);
                    }

                    Console.WriteLine("No further requests. Sending End message to client...");
                    Message EndMessage = new Message
                    {
                        MsgId = GetNextMsgId(),
                        MsgType = MessageType.End,
                        Content = "“End of DNSLookup"
                    };
                    byte[] EndMessageBytes = encrypt(EndMessage);
                    sock.SendTo(EndMessageBytes, EndMessageBytes.Length, SocketFlags.None, remoteEndpoint);
                    Console.WriteLine("Send End\n\n");
                    msgtracker = msgcounter;

                    // Stop resending after End
                    sessionEnded = true;
                    pendingReplies.Clear();

                }
                else
                {
                    Console.WriteLine($"SocketException occurred: {ex.Message}");
                }
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

        }
    }
    private static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        if (domain.Length < 3 || domain.Length > 253)
            return false;

        var parts = domain.Split('.');
        if (parts.Length < 2)
            return false;

        if (parts.Length == 3 && parts[0].ToLower() != "www")
            return false;

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || part.Length > 63)
                return false;

            if (part.StartsWith("-") || part.EndsWith("-"))
                return false;

            foreach (char c in part)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        return true;
    }


}