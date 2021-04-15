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
        public static GameManager pendingGame = new GameManager();

        readonly static EndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 65530);
        readonly static Socket listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        static void Main()
        {
            // create the socket which listens for incoming connections
            listenSocket.Bind(localEndPoint);

            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(100);

            StartAccept(null);

            Console.WriteLine($"Listening to {localEndPoint}");

            Console.WriteLine("Press any key to terminate the server process....");
            Console.ReadKey();
        }

        public static void PlacePlayer(GamePlayer player)
        {
            lock (pendingGame)
            {
                if (pendingGame.State != GameState.WaitingForPlayers)
                {
                    Console.Error.WriteLine($"pendingGame state was invalid! {pendingGame.State}");
                    pendingGame = new GameManager();
                }

                var gameNowFull = pendingGame.AddPlayer(player);
                Console.WriteLine($"Placed player {player} in game {pendingGame}");

                if (gameNowFull)
                    pendingGame = new GameManager();
            }
        }

        static void StartAccept(SocketAsyncEventArgs? acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        static void AcceptEventArg_Completed(object? sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }
        static private void ProcessAccept(SocketAsyncEventArgs e)
        {
            Console.WriteLine($"New connection from {e.AcceptSocket.RemoteEndPoint}");
            PlacePlayer(new GamePlayer(e.AcceptSocket));

            // Accept the next connection request
            StartAccept(e);
        }
    }
}
