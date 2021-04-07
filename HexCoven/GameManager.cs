using System;
using System.Net.Sockets;

namespace HexCoven
{
    public class GameManager
    {
        static int lastGameId = 0;

        int gameId;

        GamePlayer? player1;
        GamePlayer? player2;

        public GameState State { get; private set; } = GameState.WaitingForPlayers;

        public GameManager()
        {
            gameId = ++lastGameId;
            var tickSwapTimer = new System.Timers.Timer(250);
            tickSwapTimer.Elapsed += TickSwapTeams;
            tickSwapTimer.Start();
        }

        private void TickSwapTeams(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (State != GameState.WaitingForPlayers) return;
            var p1 = this.player1;
            var p2 = this.player2;

            if (p1 != null && p2 != null) return;

            if (p1 != null)
                p1.SwapTeam();

            if (p2 != null)
                p2.SwapTeam();
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

            bool addedPlayer = false;
            if (player1 == null)
            {
                player1 = SetupPlayer(player, player2);
                addedPlayer = true;
            }
            else if (player2 == null)
            {
                player2 = SetupPlayer(player, player1);
                addedPlayer = true;
            }

            if (!addedPlayer)
                throw new Exception("Game is full");

            return player1 != null && player2 != null;
        }

        GamePlayer SetupPlayer(GamePlayer player, GamePlayer? otherPlayer)
        {
            player.OnMessage += Player_OnMessage;
            player.OnDisconnect += Player_OnDisconnect;

            if (otherPlayer?.Team == player.Team)
                player.SwapTeam();

            return player;
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
                        otherPlayer.Send(new Message(MessageType.Surrender));
                    }
                    /*
                    if (otherPlayer != null && !sender.SentDisconnect)
                    {
                        Console.WriteLine($"Sending missing disconnect message!");
                        sender.SentDisconnect = true;
                        otherPlayer.Send(new Message(MessageType.Disconnect));
                    }
                    */
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
            string prefix;
            GamePlayer? otherPlayer;
            if (sender == this.player1)
            {
                prefix = "p1";
                otherPlayer = player2;
            }
            else if (sender == this.player2)
            {
                prefix = "p2";
                otherPlayer = player1;
            }
            else
            {
                Console.Error.WriteLine($"Received message from neither p1 or p2");
                return;
            }

            if (message.Type != MessageType.Ping && message.Type != MessageType.Pong)
                Console.WriteLine($"{prefix} -> {message.ToString()}");

            switch (message.Type)
            {
                // Forward or ignore
                case MessageType.None:
                case MessageType.Pong:
                case MessageType.ProposeTeamChange:
                case MessageType.DenyTeamChange:
                case MessageType.PreviewMovesOn:
                case MessageType.PreviewMovesOff:
                    if (otherPlayer != null)
                        otherPlayer.Send(message);
                    break;

                // Forward or warn
                case MessageType.Promotion:
                case MessageType.BoardState:
                    if (otherPlayer != null)
                        otherPlayer.Send(message);
                    else
                        Console.Error.WriteLine($"Cannot forward message, no other player! {message.ToString()}");
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
                    sender.Close();
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
                    sender.IsReady = true;
                    Player_OnReadyChange();
                    break;
                case MessageType.Unready:
                    sender.IsReady = false;
                    Player_OnReadyChange();
                    break;

                // Should never receive
                case MessageType.StartMatch:
                    Console.Error.WriteLine($"Received unexpected message {message.ToString()}");
                    break;

                default:
                    Console.Error.WriteLine($"Received unknown message {message.ToString()}");
                    break;
            }
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
                State = GameState.Playing;
                player1?.Send(new Message(MessageType.StartMatch));
                player2?.Send(new Message(MessageType.StartMatch));
            }
        }

        void ForceEndGame(string reason)
        {
            State = GameState.Complete;
            Console.Error.WriteLine($"Force ending game: {reason}");
            player1?.Close();
            player1 = null;
            player2?.Close();
            player2 = null;

            lock (Program.pendingGame)
            {
                if (Program.pendingGame == this)
                    Program.pendingGame = new GameManager();
            }
        }

        public override string ToString()
        {
            return $"GameManager(id={gameId})";
        }
    }
}
