using System;
using System.Net.Sockets;
using System.Threading;

namespace HexCoven
{
    public enum GameState
    {
        WaitingForPlayers,
        Playing,
        Complete,
    }

    class Program
    {
        public static GameManager pendingGame = new GameManager();

        readonly static TcpListener server = TcpListener.Create(65530);

        static bool listening = true;

        public static int NumConnectedPlayers = 0;
        public static int NumActiveGames = 0;

        static DateTime lastExitCheck = default;

        static void Main()
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            Console.WriteLine("Press CTRL-C to exit");

            server.Start();
            StartAccept();

            Console.WriteLine($"Listening to {server.LocalEndpoint}");

            while (listening)
                Thread.Yield();

            Console.WriteLine("No longer listening!");
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            if (listening)
            {
                e.Cancel = true;

                if ((DateTime.UtcNow - lastExitCheck).TotalSeconds < 5)
                {
                    // 2nd CTRL-C within 5 seconds, close anyway
                }
                else if (NumConnectedPlayers > 0)
                {
                    Console.WriteLine($"There are still {NumConnectedPlayers} connected players");
                    Console.WriteLine("Press CTRL-C again to quit the server");
                    lastExitCheck = DateTime.UtcNow;
                    return;
                }

                Console.WriteLine($"Exiting with {NumConnectedPlayers} player(s) still connected");
                listening = false;
                server.Stop();
            }
            else
            {
                e.Cancel = false;
                Console.WriteLine("Got CTRL-C AGAIN");
                Environment.Exit(1);
            }
        }

        public static void PlacePlayer(GamePlayer player)
        {
            lock (pendingGame)
            {
                if (pendingGame.State != GameState.WaitingForPlayers)
                {
                    pendingGame = new GameManager();
                }

                var gameNowFull = pendingGame.AddPlayer(player);
                Console.WriteLine($"Placed player {player} in game {pendingGame}");

                if (gameNowFull)
                    pendingGame = new GameManager();
            }
        }

        static void StartAccept()
        {
            server.BeginAcceptSocket(ProcessAccept, null);
        }

        static private void ProcessAccept(IAsyncResult ar)
        {
            Socket socket = server.EndAcceptSocket(ar);
            Console.WriteLine($"New connection from {socket.RemoteEndPoint}");

            var player = new GamePlayer(socket);
            player.OnInitialized += Player_OnInitialized;
            player.Listen();

            StartAccept();
        }

        private static void Player_OnInitialized(GamePlayer player)
        {
            player.OnDisconnect += Player_OnDisconnect;
            Interlocked.Increment(ref NumConnectedPlayers);
            Console.WriteLine($"We now have {NumConnectedPlayers} connected players");
            PlacePlayer(player);
        }
        private static void Player_OnDisconnect(GamePlayer player)
        {
            Interlocked.Decrement(ref NumConnectedPlayers);
            Console.WriteLine($"We now have {NumConnectedPlayers} connected players");
            player.OnInitialized -= Player_OnInitialized;
            player.OnDisconnect -= Player_OnDisconnect;
        }

    }
}
