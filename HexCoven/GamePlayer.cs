using System;
using System.Net.Sockets;
using System.Threading;

namespace HexCoven
{
    public class GamePlayer
    {
        public delegate void MessageEvent(GamePlayer sender, in Message message);
        public event MessageEvent? OnMessage;
        public event Action<GamePlayer>? OnDisconnect;

        TcpClient tcpClient;
        NetworkStream tcpStream;
        bool connected = true;
        CancellationTokenSource tokenSource;
        private Thread receiveThread;

        byte[] ReceiveBuffer = new byte[ushort.MaxValue];
        ushort ReceiveBufferLen = 0;

        public ChessTeam Team { get; set; } = ChessTeam.Black;
        public bool IsReady { get; set; }
        public bool SentSurrender { get; set; }
        public bool SentDisconnect { get; set; }

        public GamePlayer(TcpClient client)
        {
            tcpClient = client;
            tcpStream = client.GetStream();

            tokenSource = new CancellationTokenSource();
            receiveThread = new Thread(() =>
            {
                ReceiveThread(tokenSource.Token);
            });
            receiveThread.Start();
        }

        internal void SwapTeam()
        {
            Send(new Message(MessageType.ApproveTeamChange));
            Team = Team == ChessTeam.Black ? ChessTeam.White : ChessTeam.Black;
        }

        void ReceiveThread(CancellationToken cancellationToken)
        {
            var recBuffer = new byte[10 * 1024];
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine($"Connection cancelled via token");
                    break;
                }

                int i;
                try
                {
                    i = tcpStream.Read(recBuffer, 0, recBuffer.Length);
                } catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while reading from socket {ex}");
                    break;
                }

                if (i == 0)
                {
                    Console.Error.WriteLine($"Connection got 0 bytes");
                    break;
                }
                ReceiveBytes(new ArraySegment<byte>(recBuffer, 0, i));
            }
            Console.WriteLine($"Receive thread got break");
            Close();
        }

        void ReceiveBytes(ArraySegment<byte> received)
        {
            Array.Copy(received.Array!, received.Offset, ReceiveBuffer, ReceiveBufferLen, received.Count);
            ReceiveBufferLen += (ushort)received.Count;

            ushort unhandledIndex = 0;

            while (unhandledIndex < ReceiveBufferLen)
            {
                var result = Message.TryRead(new ReadOnlySpan<byte>(ReceiveBuffer, unhandledIndex, ReceiveBufferLen), out Message message);

                if (result == Message.TryReadResult.TooShort)
                    break;

                if (result == Message.TryReadResult.InvalidSignature)
                {
                    Close();
                    throw new Exception("Invalid signature!");
                }

                if (result != Message.TryReadResult.Success)
                {
                    Close();
                    throw new Exception($"wtf tryreadresult: {result}");
                }

                HandleReceiveMessage(in message);
                unhandledIndex += message.TotalLength;
            }

            ReceiveBufferLen -= unhandledIndex;

            // Move remaining bytes to the beginning of the buffer
            if (ReceiveBufferLen > 0)
            {
                Array.Copy(ReceiveBuffer, unhandledIndex, ReceiveBuffer, 0, ReceiveBufferLen);
            }
        }

        private void HandleReceiveMessage(in Message message)
        {
            switch (message.Type)
            {
                case MessageType.ApproveTeamChange:
                    Team = Team == ChessTeam.White ? ChessTeam.Black : ChessTeam.White;
                    break;
                case MessageType.Surrender:
                    SentSurrender = true;
                    break;
                case MessageType.Disconnect:
                    SentDisconnect = true;
                    break;
            }
            OnMessage?.Invoke(this, in message);
        }

        public void Send(in Message message)
        {
            if (connected)
            {
                try
                {
                    message.WriteTo(tcpStream);
                } catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while sending to {this}: {ex}");
                    Close();
                }

            }
            else
                Console.Error.WriteLine($"Ignoring write to disconnected client");
        }

        public void Close()
        {
            tokenSource.Cancel();
            tcpClient.Close();

            if (connected)
            {
                connected = false;
                OnDisconnect?.Invoke(this);
            }
        }

        public override string ToString()
        {
            string remoteEp;
            try
            {
                remoteEp = tcpClient.Client.RemoteEndPoint?.ToString() ?? "this will never happen";
            } catch (ObjectDisposedException)
            {
                remoteEp = "<disconnected>";
            }
            return $"GamePlayer({remoteEp}, {Team})";
        }
    }
}
