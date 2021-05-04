using System;
using System.Net;
using System.Net.Sockets;

namespace HexCoven.PerfTool
{
    class Program
    {
        readonly static byte[] pingBuf = new byte[8];

        static NetworkStream stream;

        static int Main(string[] args)
        {
            var ping = Message.Ping();
            ping.WriteTo(pingBuf);

            string host;
            int port = 65530;
            if (args.Length == 1)
            {
                host = args[0];
            }
            else if (args.Length == 2)
            {
                host = args[0];
                port = int.Parse(args[1]);
            }
            else if (args.Length == 0)
            {
                host = "127.0.0.1";
                port = 65530;
            }
            else
            {
                Console.Error.WriteLine("Usage: perftool <host> [port]");
                return 1; 
            }

            IPAddress ip;
            if (IPAddress.TryParse(host, out IPAddress? parsedAddress))
            {
                ip = parsedAddress!;
            }
            else
            {
                var hostEntry = Dns.GetHostEntry(host);
                Console.WriteLine($"HostEntry {hostEntry}");
                Console.WriteLine($"HostEntry.addresslist {hostEntry.AddressList}");
                ip = hostEntry.AddressList[0];
            }

            var client = new TcpClient(ip.AddressFamily);
            client.Connect(ip, port);
            stream = client.GetStream();

            byte[] buffer = new byte[2048];
            int bufReadStart = 0;
            int bufWriteStart = 0;

            var pingTimer = new System.Timers.Timer();
            pingTimer.Interval = 100;
            pingTimer.AutoReset = true;
            pingTimer.Elapsed += PingTimer_Tick;
            pingTimer.Start();

            var screenTimer = new System.Timers.Timer();
            screenTimer.Interval = 100;
            screenTimer.AutoReset = true;
            screenTimer.Elapsed += ScreenTimer_Tick;
            screenTimer.Start();

            var nameTimer = new System.Timers.Timer();
            nameTimer.Interval = 1000 / 30;
            nameTimer.AutoReset = true;
            nameTimer.Elapsed += NameTimer_Tick;
            nameTimer.Start();

            while (true)
            {
                int bytesRead = stream.Read(buffer, bufWriteStart, buffer.Length - bufWriteStart);
                if (bytesRead == 0)
                {
                    Console.Error.WriteLine($"Error reading: 0 bytes read");
                    break;
                }
                bufWriteStart += bytesRead;

                while (bufReadStart < bufWriteStart)
                {
                    var data = new ReadOnlySpan<byte>(buffer, bufReadStart, bufWriteStart - bufReadStart);
                    if (!Message.TryRead(data, out Message message))
                        break;

                    Dispatch(in message);
                    bufReadStart += message.TotalLength;
                }

                if (bufReadStart == bufWriteStart)
                {
                    bufReadStart = 0;
                    bufWriteStart = 0;
                }
            }

            pingTimer.Stop();
            screenTimer.Stop();

            return 0;
        }
        
        static void PingTimer_Tick(object source, System.Timers.ElapsedEventArgs e)
        {
            // lock (pingWatch)
            {
                if (pingWatch.IsRunning)
                    return; // Do not re-ping while already waiting

                pingWatch.Start();
                stream.Write(pingBuf, 0, pingBuf.Length);
            }
        }

        readonly static byte[][] names =
        {
            System.Text.Encoding.UTF8.GetBytes("A"),
            System.Text.Encoding.UTF8.GetBytes("B"),
            System.Text.Encoding.UTF8.GetBytes("C"),
            System.Text.Encoding.UTF8.GetBytes("D"),
            System.Text.Encoding.UTF8.GetBytes("E"),
            System.Text.Encoding.UTF8.GetBytes("F")
        };
        static int nameIndex = 0;

        static void NameTimer_Tick(object source, System.Timers.ElapsedEventArgs e)
        {
            var name = names[nameIndex++];
            nameIndex %= names.Length;

            var updateName = Message.UpdateName(name);
            var updateNameBuf = new byte[updateName.TotalLength];
            updateName.WriteTo(updateNameBuf);
            stream.Write(updateNameBuf);
        }

        static void ScreenTimer_Tick(object source, System.Timers.ElapsedEventArgs e)
        {
            var elapsed = latency;
            Console.Write($"latency={elapsed.Ticks,6} ticks ({elapsed.TotalMilliseconds,4:F1} ms) opponent={opponentName}\r");
        }

        static string opponentName = string.Empty;
        static TimeSpan latency;

        static System.Diagnostics.Stopwatch pingWatch = new System.Diagnostics.Stopwatch();

        private static void Dispatch(in Message message)
        {
            // Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r"); // clear line
            switch (message.Type)
            {
                case MessageType.Connect:
                case MessageType.UpdateName:
                    opponentName = System.Text.Encoding.UTF8.GetString(message.Payload);
                    break;
                case MessageType.Pong:
                    var maybeLatency = pingWatch.Elapsed;
                    // lock (pingWatch)
                    {
                        if (pingWatch.IsRunning)
                        {
                            latency = maybeLatency;
                            pingWatch.Reset();
                        }
                        else
                        {
                            Console.Error.WriteLine("Received pong without stopwatch running :(");
                        }
                    }
                    break;
                default:
                    Console.WriteLine($"Recieved message: {message.ToString()}");
                    return;
            }
        }
    }
}
