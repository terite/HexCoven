using System;
using System.Net.Sockets;
using System.Text;

namespace HexCoven
{
    public class GameManager
    {
        static int lastGameId = 0;

        static readonly byte[][] PendingNames = {
            Encoding.UTF8.GetBytes("Matching (●   )"),
            Encoding.UTF8.GetBytes("Matching ( ●  )"),
            Encoding.UTF8.GetBytes("Matching (  ● )"),
            Encoding.UTF8.GetBytes("Matching (   ●)"),
            Encoding.UTF8.GetBytes("Matching (  ● )"),
            Encoding.UTF8.GetBytes("Matching ( ●  )"),
        };

        GamePlayer? player1;
        GamePlayer? player2;

        readonly int gameId;
        readonly float TimerDuration = Settings.TimerDuration;
        readonly bool ShowClock = Settings.ShowClock;

        public GameState State { get; private set; } = GameState.WaitingForPlayers;

        public GameManager()
        {
            gameId = ++lastGameId;
            if (Settings.MatchingIntervalMs > 0)
            {
                var tickTimer = new System.Timers.Timer(Settings.MatchingIntervalMs);
                tickTimer.Elapsed += TickTimer_Elapsed;
                tickTimer.Start();
            }
        }

        int pendingNameIndex = 0;
        private void TickTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (State != GameState.WaitingForPlayers) return;
            var p1 = this.player1;
            var p2 = this.player2;

            if (p1 != null && p2 != null) return;

            var pendingName = PendingNames[pendingNameIndex++];
            pendingNameIndex %= PendingNames.Length; ;

            if (p1 != null)
                p1.Send(new Message(MessageType.UpdateName, pendingName));

            if (p2 != null)
                p2.Send(new Message(MessageType.UpdateName, pendingName));
        }

        /// <summary>
        /// Add a player to this game
        /// </summary>
        /// <param name="tcpClient">connection representing the player to be added</param>
        /// <returns>true if the game is now full, otherwise false.</returns>
        public bool AddPlayer(GamePlayer player)
        {
            if (State != GameState.WaitingForPlayers)
                throw new Exception($"Cannot add player to game whose state is {State}");

            if (player1 == null)
            {
                player1 = SetupPlayer(player);
            }
            else if (player2 == null)
            {
                player2 = SetupPlayer(player);
            }
            else
            {
                player.Close("Game full");
                throw new Exception("Game is full");
            }

            return player1 != null && player2 != null;
        }

        GamePlayer SetupPlayer(GamePlayer player)
        {
            player.OnMessage += Player_OnMessage;
            player.OnInitialized += Player_OnInitialized;
            player.OnDisconnect += Player_OnDisconnect;

            return player;
        }

        private void Player_OnInitialized(GamePlayer player)
        {
            var otherPlayer = GetOtherPlayer(player);
            player.Send(new Message(MessageType.Connect, Encoding.UTF8.GetBytes(otherPlayer?.PlayerName ?? "")));

            if (otherPlayer != null)
            {
                if (otherPlayer.Team == player.Team)
                    player.SwapTeam();

                otherPlayer.Send(new Message(MessageType.UpdateName, Encoding.UTF8.GetBytes(player.PlayerName)));
            }

        }

        private void Player_OnDisconnect(GamePlayer sender)
        {
            sender.OnMessage -= Player_OnMessage;
            sender.OnInitialized -= Player_OnInitialized;
            sender.OnDisconnect -= Player_OnDisconnect;

            GamePlayer? otherPlayer = null;
            if (sender == player1)
                otherPlayer = player2;
            else if (sender == player2)
                otherPlayer = player1;
            else
            {
                Console.Error.WriteLine($"Game {this} got message from unknown player {sender}");
                return;
            }

            if (otherPlayer != null)
            {
                otherPlayer.OnMessage -= Player_OnMessage;
                otherPlayer.OnDisconnect -= Player_OnDisconnect;
            }

            switch (State)
            {
                case GameState.WaitingForPlayers:
                    if (otherPlayer != null)
                        Program.PlacePlayer(otherPlayer);
                    break;
                case GameState.Playing:
                    if (otherPlayer != null && !sender.SentSurrender)
                    {
                        Console.WriteLine($"Sending missing surrender message!");
                        sender.SentSurrender = true;
                        otherPlayer.Send(new Message(MessageType.Surrender));
                    }
                    State = GameState.Complete;
                    break;
                case GameState.Complete:
                    break;
                default:
                    Console.Error.WriteLine($"Unknown game state: {State}");
                    break;
            }

            State = GameState.Complete;
            player1 = null;
            player2 = null;
        }

        private void Player_OnMessage(GamePlayer sender, in Message message)
        {
            string prefix = sender.ToString();
            GamePlayer? otherPlayer;
            if (sender == this.player1)
            {
                otherPlayer = player2;
            }
            else if (sender == this.player2)
            {
                otherPlayer = player1;
            }
            else
            {
                Console.Error.WriteLine($"Received message from neither p1 or p2");
                return;
            }

            if (Settings.LogInbound)
                if (Settings.LogInboundPing || (message.Type != MessageType.Ping && message.Type != MessageType.Pong))
                    Console.WriteLine($"<- {prefix} -- {message.ToString()}");

            switch (message.Type)
            {
                // Forward or ignore
                case MessageType.None:
                case MessageType.Pong:
                case MessageType.ProposeTeamChange:
                case MessageType.DenyTeamChange:
                case MessageType.PreviewMovesOn:
                case MessageType.PreviewMovesOff:
                case MessageType.UpdateName:
                    ForwardMessage(in message, otherPlayer);
                    break;

                // Forward or warn
                case MessageType.Promotion:
                case MessageType.BoardState:
                case MessageType.OfferDraw:
                case MessageType.DenyDraw:
                    ForwardMessage(in message, otherPlayer, warnIfMissing: true);
                    break;

                case MessageType.AcceptDraw:
                case MessageType.FlagFall:
                    State = GameState.Complete;
                    ForwardMessage(in message, otherPlayer, warnIfMissing: true);
                    break;

                // Need to be handled
                case MessageType.Ping:
                    if (otherPlayer != null)
                        otherPlayer.Send(in message);
                    else
                        sender.Send(new Message(MessageType.Pong));
                    break;

                case MessageType.Disconnect:
                    if (State == GameState.Playing)
                    {
                        otherPlayer?.Send(in message);
                    }
                    sender.Close("Disconnect message");
                    break;
                case MessageType.ApproveTeamChange:
                    if (otherPlayer != null)
                    {
                        otherPlayer.Team = otherPlayer.Team == ChessTeam.White ? ChessTeam.Black : ChessTeam.White;
                        otherPlayer.Send(in message);
                    }
                    break;

                case MessageType.Surrender:
                    HandleSurrender(sender, in message, otherPlayer);
                    break;

                case MessageType.Ready:
                case MessageType.Unready:
                    Player_OnReadyChange();
                    break;

                // Should never receive
                case MessageType.Connect:
                case MessageType.StartMatch:
                    Console.Error.WriteLine($"Received unexpected message {message.ToString()}");
                    break;

                default:
                    Console.Error.WriteLine($"Forwarding unknown message {message.ToString()}");
                    ForwardMessage(in message, otherPlayer);
                    break;
            }
        }

        void ForwardMessage(in Message message, GamePlayer? recipient, bool warnIfMissing = false)
        {
            if (recipient != null)
                recipient.Send(in message);
            else if (warnIfMissing)
                Console.Error.WriteLine($"Cannot forward message, no other player! {message.ToString()}");
        }

        private void HandleSurrender(GamePlayer sender, in Message message, GamePlayer? otherPlayer)
        {
            if (sender.SentSurrender)
                Console.Error.WriteLine($"Sender {sender} surrendered twice!");

            if (State == GameState.WaitingForPlayers)
            {
                ForceEndGame("Player surrendered while in lobby?");
                return;
            }

            if (State == GameState.Complete)
            {
                ForceEndGame("Player surrendered after game was complete");
                return;
            }

            sender.SentSurrender = true;
            if (otherPlayer != null)
            {
                State = GameState.Complete;
                otherPlayer.Send(message);
            }
            else
                Console.Error.WriteLine($"Cannot surrender!, no other player! {message.ToString()}");
        }

        private void Player_OnReadyChange()
        {
            if (player1?.IsReady == true && player2?.IsReady == true)
            {
                var p1 = player1!;
                var p2 = player2!;
                State = GameState.Playing;
                p1.Send(new Message(MessageType.StartMatch, new GameParams(p1.Team, p1.PreviewMovesOn, TimerDuration, ShowClock).Serialize()));
                p2.Send(new Message(MessageType.StartMatch, new GameParams(p2.Team, p2.PreviewMovesOn, TimerDuration, ShowClock).Serialize()));
                Console.WriteLine($"Starting game {this}");
            }
        }

        void ForceEndGame(string reason)
        {
            var rstr = $"Force ending game: {reason}";
            State = GameState.Complete;
            Console.Error.WriteLine(rstr);
            player1?.Close(rstr);
            player1 = null;
            player2?.Close(rstr);
            player2 = null;

            lock (Program.pendingGame)
            {
                if (Program.pendingGame == this)
                    Program.pendingGame = new GameManager();
            }
        }

        public override string ToString()
        {
            return $"GameManager(id={gameId}, p1={player1}, p2={player2})";
        }

        private GamePlayer? GetOtherPlayer(GamePlayer player)
        {
            if (player == player1)
                return player2;
            else if (player == player2)
                return player1;
            else
            {
                Console.Error.WriteLine($"Given player {player} was not in game {this}");
                return null;
            }
        }
    }
}
