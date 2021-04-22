using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace HexCoven
{
    public class GamePlayer
    {
        static int lastPlayerId = 0;
        int playerId;
        public delegate void MessageEvent(GamePlayer sender, in Message message);
        public event MessageEvent? OnMessage;
        public event Action<GamePlayer>? OnInitialized;
        public event Action<GamePlayer>? OnDisconnect;

        readonly Socket Socket;
        readonly SocketAsyncEventArgs ReadArg; 
        string? closedReason = null;

        byte[] ReceiveBuffer = new byte[ushort.MaxValue];
        ushort ReceiveBufferLen = 0;

        public string PlayerName { get; private set; }
        public ChessTeam Team { get; set; } = ChessTeam.Black;
        public bool IsReady { get; set; }
        public bool PreviewMovesOn { get; set; }

        public bool SentSurrender { get; set; }
        public bool SentDisconnect { get; set; }

        public bool IsInitialized { get; private set; } = false;

        public GamePlayer(Socket socket)
        {
            playerId = ++lastPlayerId;
            PlayerName = $"Opponent";
            var readArg = new SocketAsyncEventArgs();
            readArg.Completed += IO_Completed;
            readArg.SetBuffer(new byte[10 * 1024]);
            this.ReadArg = readArg;

            Socket = socket;

            var timer = new Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = false;
            timer.Start();
        }

        bool listening = false;
        public void Listen()
        {
            if (!listening)
            {
                listening = true;
                BeginReceive();
            }

        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!IsInitialized)
            {
                OnInitialized?.Invoke(this);
                IsInitialized = true;
                Console.WriteLine($"{this} initialized due to timer");
            }
        }

        void BeginReceive()
        {
            while (true)
            {
                if (closedReason != null) break;
                if (!Socket.ReceiveAsync(ReadArg))
                    ProcessReceive(ReadArg, false);
                else
                    break;
            }
        }

        void IO_Completed(object? sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e, true);
                    break;
                default:
                    throw new ArgumentException($"Unhandled async socket operation: {e.LastOperation}");
            }
        }
        private void ProcessReceive(SocketAsyncEventArgs e, bool receiveAgain)
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

            if (receiveAgain)
                BeginReceive();
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

                case MessageType.Ping:
                    if (!IsInitialized)
                    {
                        IsInitialized = true;
                        OnInitialized?.Invoke(this);
                        Console.WriteLine($"{this} initialized due to ping");
                    }
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
                if (Settings.LogOutbound)
                    if (Settings.LogOutboundPing || (message.Type != MessageType.Ping && message.Type != MessageType.Pong))
                        Console.WriteLine($"-> {Socket.RemoteEndPoint} -- {message.ToString()}");

                byte[] writeBuffer = ArrayPool<byte>.Shared.Rent(message.TotalLength);
                var writeSpan = new Span<byte>(writeBuffer, 0, message.TotalLength);
                try
                {
                    message.WriteTo(writeSpan);
                    Socket.Send(writeSpan);
                } catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while sending to {this}: {ex}");
                    Close("Error calling WriteTo");
                } finally
                {
                    ArrayPool<byte>.Shared.Return(writeBuffer);
                }

            }
            else
                Console.Error.WriteLine($"Ignoring write to disconnected client");
        }

        public void Close(string reason)
        {
            if (Settings.LogCloseCalls)
                Console.WriteLine($"GamePlayer.Close() call: {reason}");

            CloseClientSocket();

            if (closedReason == null)
            {
                closedReason = reason;
                OnDisconnect?.Invoke(this);
            }
        }

        private void CloseClientSocket()
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
