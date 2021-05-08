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

        byte[] receiveBuffer = new byte[ushort.MaxValue];
        int receiveBufferStart = 0;
        int receiveBufferEnd = 0;

        Timer? initializeTimer;

        public string PlayerName { get; private set; }
        public ChessTeam Team { get; set; } = ChessTeam.Black;
        public bool IsReady { get; set; }
        public bool PreviewMovesOn { get; set; } = true;

        public bool SentSurrender { get; set; }
        public bool SentDisconnect { get; set; }
        public bool ReceivedConnect { get; set; }

        public bool IsInitialized { get; private set; } = false;

        public GamePlayer(Socket socket)
        {
            playerId = ++lastPlayerId;
            PlayerName = $"Opponent";

            ReadArg = new SocketAsyncEventArgs();
            ReadArg.Completed += IO_Completed;

            Socket = socket;

            initializeTimer = new Timer(1000);
            initializeTimer.Elapsed += InitializeTimer_Elapsed;
            initializeTimer.AutoReset = false;
            initializeTimer.Start();
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

        private void InitializeTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            initializeTimer?.Dispose();
            initializeTimer = null;
            if (!IsInitialized)
            {
                Close("Failed to initialize");
            }
        }

        void BeginReceive()
        {
            while (true)
            {
                if (closedReason != null)
                    break;
                ReadArg.SetBuffer(receiveBuffer, receiveBufferEnd, receiveBuffer.Length - receiveBufferEnd);
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
            if (e.SocketError != SocketError.Success)
            {
                Close($"Socket error: {e.SocketError}");
                return;
            }

            // check if the remote host closed the connection
            if (e.BytesTransferred == 0)
            {
                Close("No bytes transferred");
                return;
            }

            receiveBufferEnd += e.BytesTransferred;

            try
            {
                ReadMessages();
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
            Send(Message.ApproveTeamChange());
            Team = Team == ChessTeam.Black ? ChessTeam.White : ChessTeam.Black;
        }

        void ReadMessages()
        {
            while (receiveBufferStart < receiveBufferEnd)
            {
                var mem = new ReadOnlySpan<byte>(receiveBuffer, receiveBufferStart, receiveBufferEnd - receiveBufferStart);
                var result = Message.TryRead(mem, out Message message);
                if (!result)
                    break;

                HandleReceiveMessage(in message);
                receiveBufferStart += message.TotalLength;
            }

            int unreadBytes = receiveBufferEnd - receiveBufferStart;
            if (unreadBytes == 0)
            {
                // Buffer is empty
                receiveBufferStart = 0;
                receiveBufferEnd = 0;
            }
            else if (unreadBytes > 0)
            {
                // Move remaining bytes to the beginning of the buffer
                Array.Copy(receiveBuffer, receiveBufferStart, receiveBuffer, 0, unreadBytes);
                receiveBufferStart = 0;
                receiveBufferEnd = unreadBytes;
            }
            else
            {
                throw new Exception($"Buffer end ({receiveBufferEnd}) is beyond start ({receiveBufferStart})");
            }
        }

        public void SetPreviewMovesOn(bool previewMovesOn)
        {
            if (previewMovesOn)
                Send(Message.PreviewMovesOn());
            else
                Send(Message.PreviewMovesOff());
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
            if (closedReason != null)
            {
                Console.Error.WriteLine($"Ignoring write to disconnected client");
                return;
            }

            bool shouldLog = message.Type switch
            {
                MessageType.Ping => Settings.LogOutboundPing,
                MessageType.Pong => Settings.LogOutboundPing,
                MessageType.UpdateName => Settings.LogNameUpdates,
                _ => Settings.LogOutbound,
            };
            if (shouldLog)
                Console.WriteLine($"-> {Socket.RemoteEndPoint} -- {message.ToString()}");

            byte[] writeBuffer = ArrayPool<byte>.Shared.Rent(message.TotalLength);
            var writeSpan = new Span<byte>(writeBuffer, 0, message.TotalLength);
            try
            {
                message.WriteTo(writeSpan);
                Socket.Send(writeSpan);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error while sending to {this}: {ex}");
                Close("Error calling WriteTo");
                return;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(writeBuffer);
            }

            switch (message.Type)
            {
                case MessageType.Connect:
                    ReceivedConnect = true;
                    break;
                case MessageType.PreviewMovesOff:
                    PreviewMovesOn = false;
                    break;
                case MessageType.PreviewMovesOn:
                    PreviewMovesOn = true;
                    break;
            }
        }

        public void SetOtherName(byte[] otherPlayerName)
        {
            if (!ReceivedConnect)
                Send(Message.Connect(otherPlayerName));
            else
                Send(Message.UpdateName(otherPlayerName));
        }

        public void SetOtherName(string otherPlayerName)
        {
            SetOtherName(Encoding.UTF8.GetBytes(otherPlayerName));
        }

        public void Close(string reason)
        {
            if (Settings.LogCloseCalls)
                Console.WriteLine($"{this} closed: {reason}");

            if (initializeTimer != null)
            {
                initializeTimer.Dispose();
                initializeTimer = null;
            }

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
            return $"GamePlayer#{playerId}";
        }

        public string PlayerDescription()
        {
            string remoteEp;
            try
            {
                remoteEp = Socket.RemoteEndPoint?.ToString() ?? "this will never happen";
            }
            catch (ObjectDisposedException)
            {
                remoteEp = "<disconnected>";
            }
            return $"GamePlayer({remoteEp}, {PlayerName}, {Team})";
        }
    }
}
