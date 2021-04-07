using System;
using System.Net;
using System.Net.Sockets;

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
        static readonly IPAddress ListenHost = IPAddress.Any;
        const int Port = 65530;

        static GameManager pendingGame = new GameManager();

        static void Main()
        {
            var server = new TcpListener(ListenHost, Port);
            server.Start();
            Console.WriteLine($"Listening on {server.LocalEndpoint}");

            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                Console.WriteLine($"New connection from {client.Client.RemoteEndPoint}");
                PlacePlayer(new GamePlayer(client));
            }
        }

        public static void PlacePlayer(GamePlayer player)
        {
            lock (pendingGame)
            {
                if (pendingGame.State != GameState.WaitingForPlayers)
                {
                    // TODO: re-place other players?
                    pendingGame = new GameManager();
                }

                var gameNowFull = pendingGame.AddPlayer(player);
                Console.WriteLine($"Placed player {player} in game {pendingGame}");

                if (gameNowFull)
                    pendingGame = new GameManager();
            }
        }
    }
}
