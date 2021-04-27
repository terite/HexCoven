using System;
using System.Net;
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

        readonly static EndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 65530);
        readonly static Socket listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        static bool listening = true;

        static void Main()
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            Console.WriteLine("Press CTRL-C to exit");
            // create the socket which listens for incoming connections
            listenSocket.Bind(localEndPoint);

            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(100);

            StartAccept(null);

            Console.WriteLine($"Listening to {localEndPoint}");

            while (listening)
                Thread.Yield();

            Console.WriteLine("No longer listening!");
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            if (listening)
            {
                e.Cancel = true;
                Console.WriteLine("Got CTRL-C");
                listening = false;
                listenSocket.Close();
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
                ProcessAccept(acceptEventArg);
        }

        static void AcceptEventArg_Completed(object? sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    ProcessAccept(e);
                    break;
                default:
                    Console.Error.WriteLine($"Unexpected socket operation: {e.LastOperation}");
                    break;
            }
        }

        static private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                if (e.SocketError != SocketError.OperationAborted)
                    Console.Error.WriteLine($"Error while trying to accept: {e.SocketError}");
                return;
            }
            Console.WriteLine($"New connection from {e.AcceptSocket!.RemoteEndPoint}");

            var player = new GamePlayer(e.AcceptSocket);
            player.OnInitialized += Player_OnInitialized;
            player.OnDisconnect += Player_OnDisconnect;
            player.Listen();

            StartAccept(e);
        }

        private static void Player_OnInitialized(GamePlayer player)
        {
            PlacePlayer(player);
        }
        private static void Player_OnDisconnect(GamePlayer player)
        {
            player.OnInitialized -= Player_OnInitialized;
            player.OnDisconnect -= Player_OnDisconnect;
        }

    }
}
