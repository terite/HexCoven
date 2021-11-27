using System;
using System.Text;
using System.Threading;

namespace HexCoven
{
    public class Game
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
        bool previewMovesOn;

        private GameState _state = GameState.WaitingForPlayers;
        public GameState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    switch ((_state, value))
                    {
                        case (GameState.WaitingForPlayers, GameState.Playing):
                            Interlocked.Increment(ref Program.NumActiveGames);
                            break;
                        case (GameState.Playing, GameState.Complete):
                            Interlocked.Decrement(ref Program.NumActiveGames);
                            break;
                        case (GameState.WaitingForPlayers, GameState.Complete):
                            // Players left a lobby without starting the game
                            break;
                        default:
                            throw new ArgumentException($"Invalid state transition: {_state} to {value}");
                    }
                    Console.WriteLine($"Game {gameId} changing from {_state} to {value}, now at {Program.NumActiveGames} active games");
                    _state = value;
                }
            }
        }

        public Game()
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
        byte[] PendingName => PendingNames[pendingNameIndex];

        private void TickTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (State != GameState.WaitingForPlayers) return;
            var p1 = this.player1;
            var p2 = this.player2;

            if (p1 != null && p2 != null) return;

            pendingNameIndex = (++pendingNameIndex) % PendingNames.Length;

            if (p1 != null)
                p1.Send(Message.UpdateName(PendingName));

            if (p2 != null)
                p2.Send(Message.UpdateName(PendingName));
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

            if (!player.IsInitialized)
                throw new ArgumentException("player must already be initialized");

            if (player1 == null)
            {
                player1 = player;
            }
            else if (player2 == null)
            {
                player2 = player;
            }
            else
            {
                player.Close("Game full");
                throw new Exception("Game is full");
            }

            player.OnMessage += Player_OnMessage;
            player.OnDisconnect += Player_OnDisconnect;

            var otherPlayer = GetOtherPlayer(player);
            if (otherPlayer != null)
            {
                player.SetOtherName(NiceName(otherPlayer));
                player.SetPreviewMovesOn(previewMovesOn);
                if (otherPlayer.Team == player.Team)
                    player.SwapTeam();

                otherPlayer.SetOtherName(NiceName(player));
                player.Send(Message.OpponentFound());
                otherPlayer.Send(Message.OpponentFound());

                if (player.IsReady)
                    otherPlayer.Send(Message.Ready());
                else
                    otherPlayer.Send(Message.Unready());

                if (otherPlayer.IsReady)
                    player.Send(Message.Ready());
                else
                    player.Send(Message.Unready());
            }
            else
            {
                player.SetOtherName(PendingName);
                previewMovesOn = player.PreviewMovesOn;
                player.Send(Message.OpponentSearching());
                player.Send(Message.Unready());
            }

            return player1 != null && player2 != null;
        }

        string NiceName(GamePlayer player)
        {
            if (Settings.ShowReadyInName && State == GameState.WaitingForPlayers)
                return player.IsReady ? $"{player.PlayerName} (+)" : $"{player.PlayerName} (-)";
            else
                return player.PlayerName;
        }

        private void Player_OnDisconnect(GamePlayer sender)
        {
            sender.OnMessage -= Player_OnMessage;
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
                        otherPlayer.Send(Message.Surrender(0));
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

            bool shouldLog = message.Type switch
            {
                MessageType.Ping => Settings.LogInboundPing,
                MessageType.Pong => Settings.LogInboundPing,
                MessageType.UpdateName => Settings.LogNameUpdates,
                _ => Settings.LogInbound,
            };
            if (shouldLog)
                Console.WriteLine($"<- {prefix} -- {message.ToString()}");

            switch (message.Type)
            {
                // Forward or ignore
                case MessageType.None:
                case MessageType.Pong:
                case MessageType.ProposeTeamChange:
                case MessageType.DenyTeamChange:

                // Forward or warn
                case MessageType.Promotion:
                case MessageType.BoardState:
                case MessageType.OfferDraw:
                case MessageType.DenyDraw:
                    ForwardMessage(in message, otherPlayer, warnIfMissing: true);
                    break;

                case MessageType.AcceptDraw:
                case MessageType.FlagFall:
                case MessageType.Checkmate:
                case MessageType.Stalemate:
                    State = GameState.Complete;
                    ForwardMessage(in message, otherPlayer, warnIfMissing: true);
                    break;

                // Messages that need to be handled
                case MessageType.Ping:
                    sender.Send(Message.Pong());
                    break;
                case MessageType.PreviewMovesOn:
                    previewMovesOn = true;
                    ForwardMessage(in message, otherPlayer);
                    break;
                case MessageType.PreviewMovesOff:
                    previewMovesOn = false;
                    ForwardMessage(in message, otherPlayer);
                    break;

                case MessageType.UpdateName:
                    if (otherPlayer != null)
                        otherPlayer.SetOtherName(NiceName(sender));
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
                    ForwardMessage(in message, otherPlayer);
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
            var p1 = player1;
            var p2 = player2;
            if (p1 == null || p2 == null)
                return;

            if (p1.IsReady && p2.IsReady)
            {
                State = GameState.Playing;
                p1.SetOtherName(NiceName(p2));
                p2.SetOtherName(NiceName(p1));
                p1.Send(Message.StartMatch(new GameParams(p1.Team, previewMovesOn, TimerDuration, ShowClock)));
                p2.Send(Message.StartMatch(new GameParams(p2.Team, previewMovesOn, TimerDuration, ShowClock)));
                Console.WriteLine($"Starting game {this}");
            }
            else
            {
                p1.SetOtherName(NiceName(p2));
                p2.SetOtherName(NiceName(p1));
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
                    Program.pendingGame = new Game();
            }
        }

        public override string ToString()
        {
            return $"Game#{gameId}(p1={player1}, p2={player2})";
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
