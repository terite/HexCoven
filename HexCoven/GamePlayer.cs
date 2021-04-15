using System;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace HexCoven
{
    public class GamePlayer
    {
        public delegate void MessageEvent(GamePlayer sender, in Message message);
        public event MessageEvent? OnMessage;
        public event Action<GamePlayer>? OnDisconnect;

        readonly Socket Socket;
        readonly SocketAsyncEventArgs ReadArg; 
        string? closedReason = null;

        byte[] ReceiveBuffer = new byte[ushort.MaxValue];
        ushort ReceiveBufferLen = 0;

        public string PlayerName { get; private set; } = "Unknown player";
        public ChessTeam Team { get; set; } = ChessTeam.Black;
        public bool IsReady { get; set; }
        public bool PreviewMovesOn { get; set; }

        public bool SentSurrender { get; set; }
        public bool SentDisconnect { get; set; }

        public bool NeedsConnect { get; set; } = true;

        public GamePlayer(Socket socket)
        {
            var readArg = new SocketAsyncEventArgs();
            readArg.Completed += IO_Completed;
            readArg.SetBuffer(new byte[10 * 1024]);
            this.ReadArg = readArg;

            Socket = socket;
            BeginReceive();

            var timer = new Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = false;
            timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (NeedsConnect)
            {
                Send(new Message(MessageType.Connect, ReadOnlySpan<byte>.Empty));
                NeedsConnect = false;
            }
        }

        void BeginReceive()
        {
            if (closedReason == null)
                if (!Socket.ReceiveAsync(ReadArg))
                    ProcessReceive(ReadArg);
        }

        void IO_Completed(object? sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            // check if the remote host closed the connection
            if (e.BytesTransferred == 0) {
                Close("No bytes transferred");
                return;
            }
            if (e.SocketError != SocketError.Success)
            {
                Close($"Socket error: {e.SocketError}");
                return;
            }

            try
            {
                var received = e.MemoryBuffer.Slice(e.Offset, e.BytesTransferred);
                ReceiveBytes(received);
            }
            catch (Exception ex)
            {
                Close($"Error processing data: {ex.Message}");
                return;
            }
            BeginReceive();
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // read the next block of data send from the client
                bool willRaiseEvent = Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                    ProcessReceive(e);
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        internal void SwapTeam()
        {
            Send(new Message(MessageType.ApproveTeamChange));
            Team = Team == ChessTeam.Black ? ChessTeam.White : ChessTeam.Black;
        }

        void ReceiveBytes(in ReadOnlyMemory<byte> received)
        {
            received.CopyTo(new Memory<byte>(ReceiveBuffer, ReceiveBufferLen, ReceiveBuffer.Length - ReceiveBufferLen));
            ReceiveBufferLen += (ushort)received.Length;

            ushort unhandledIndex = 0;

            while (unhandledIndex < ReceiveBufferLen)
            {
                var result = Message.TryRead(new ReadOnlySpan<byte>(ReceiveBuffer, unhandledIndex, ReceiveBufferLen), out Message message);

                if (result == Message.TryReadResult.TooShort)
                    break;

                if (result == Message.TryReadResult.InvalidSignature)
                {
                    Close("Because of invalid signature");
                    throw new Exception("Invalid signature!");
                }

                if (result != Message.TryReadResult.Success)
                {
                    Close("Odd result");
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

                case MessageType.Ready:
                    IsReady = true;
                    break;
                case MessageType.Unready:
                    IsReady = false;
                    break;

                case MessageType.PreviewMovesOn:
                    PreviewMovesOn = true;
                    break;
                case MessageType.PreviewMovesOff:
                    PreviewMovesOn = false;
                    break;

                case MessageType.UpdateName:
                    PlayerName = Encoding.UTF8.GetString(message.Payload);
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
            if (closedReason == null)
            {
                try
                {
                    message.WriteTo(Socket);
                } catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while sending to {this}: {ex}");
                    Close("Error calling WriteTo");
                }

            }
            else
                Console.Error.WriteLine($"Ignoring write to disconnected client");
        }

        public void Close(string reason)
        {
            if (Settings.LogCloseCalls)
                Console.WriteLine($"GamePlayer.Close() call: {reason}");

            CloseClientSocket(ReadArg);

            if (closedReason == null)
            {
                closedReason = reason;
                OnDisconnect?.Invoke(this);
            }
        }

        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            // close the socket associated with the client
            try
            {
                Socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch (Exception) { }
            Socket.Close();

            ReadArg.Dispose();
        }

        public override string ToString()
        {
            string remoteEp;
            try
            {
                remoteEp = Socket.RemoteEndPoint?.ToString() ?? "this will never happen";
            } catch (ObjectDisposedException)
            {
                remoteEp = "<disconnected>";
            }
            return $"GamePlayer({remoteEp}, {PlayerName}, {Team})";
        }
    }
}
