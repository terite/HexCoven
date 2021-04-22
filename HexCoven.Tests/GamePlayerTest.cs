#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace HexCoven.Tests
{
    public class GamePlayerTest: IDisposable
    {
        TcpListener? server = null;
        
        TcpListener StartServer()
        {
            if (server != null)
                throw new Exception("Already have a server");
            server = new TcpListener(IPAddress.Loopback, 65530);
            server.Start();
            return server;
        }

        [Fact]
        public void TestReceiveMessage()
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var player = new GamePlayer(socket);
            player.Listen();
        }

        [Fact]
        public void TestSendMessage()
        {
            var server = StartServer();

            var client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, 65530));
            Assert.True(client.Connected);

            var serversClient = server.AcceptTcpClient();

            var player = new GamePlayer(client.Client);
            player.Send(new Message(MessageType.Ping));

            var readBuffer = new byte[100];
            serversClient.GetStream().Read(readBuffer, 0, readBuffer.Length);

            var readResult = Message.TryRead(readBuffer, out Message message);
            Assert.Equal(Message.TryReadResult.Success, readResult);
            Assert.Equal(MessageType.Ping, message.Type);
        }

        public void Dispose()
        {
            server?.Stop();
            server = null;
        }
    }
}
