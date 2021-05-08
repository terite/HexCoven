using System;
using System.Net.Sockets;
using System.Threading;

namespace HexCoven
{
    class Program
    {
        public static Game pendingGame = new Game();

        readonly static TcpListener server = TcpListener.Create(65530);

        static ManualResetEvent doneListening = new ManualResetEvent(false);
        static bool listening = false;

        public static int NumInitializedPlayers = 0;
        public static int NumActiveGames = 0;

        static DateTime lastExitCheck = default;

        static void Main(string[] args)
        {
            bool debug = false;
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "--debug":
                        debug = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {arg}");
                        Environment.Exit(1);
                        break;
                }
            }

            Settings.LogCloseCalls = debug;
            Settings.LogOutbound = debug;
            Settings.LogOutboundPing = debug;
            Settings.LogInbound = debug;
            Settings.LogInboundPing = debug;
            Settings.LogNameUpdates = debug;

            Console.CancelKeyPress += Console_CancelKeyPress;
            Console.WriteLine("Press CTRL-C to exit");

            listening = true;
            server.Start();
            StartAccept();

            Console.WriteLine($"Listening to {server.LocalEndpoint}");

            doneListening.WaitOne();

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
                else if (NumInitializedPlayers > 0)
                {
                    Console.WriteLine($"There are still {NumInitializedPlayers} connected players");
                    Console.WriteLine("Press CTRL-C again to quit the server");
                    lastExitCheck = DateTime.UtcNow;
                    return;
                }

                Console.WriteLine($"Exiting with {NumInitializedPlayers} player(s) still connected");
                listening = false;
                doneListening.Set();
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
                    pendingGame = new Game();
                }

                var gameNowFull = pendingGame.AddPlayer(player);
                Console.WriteLine($"Placed player {player} in game {pendingGame}");

                if (gameNowFull)
                    pendingGame = new Game();
            }
        }

        static void StartAccept()
        {
            server.BeginAcceptSocket(ProcessAccept, null);
        }

        static private void ProcessAccept(IAsyncResult ar)
        {
            Socket socket = server.EndAcceptSocket(ar);
            var player = new GamePlayer(socket);

            Console.WriteLine($"{socket.RemoteEndPoint} connected as player {player}");
            player.OnInitialized += Player_OnInitialized;
            player.OnDisconnect += Player_OnDisconnect;
            player.Listen();

            StartAccept();
        }

        private static void PrintStatus()
        {
            Console.WriteLine($">> Players: {NumInitializedPlayers,3} | Active Games: {NumActiveGames,3} <<");
        }

        private static void Player_OnInitialized(GamePlayer player)
        {
            Interlocked.Increment(ref NumInitializedPlayers);
            PlacePlayer(player);
            PrintStatus();
        }
        private static void Player_OnDisconnect(GamePlayer player)
        {
            if (player.IsInitialized)
                Interlocked.Decrement(ref NumInitializedPlayers);

            player.OnInitialized -= Player_OnInitialized;
            player.OnDisconnect -= Player_OnDisconnect;
            PrintStatus();
        }

    }
}
