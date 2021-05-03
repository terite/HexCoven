#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace HexCoven.Tests
{
    public class GamePlayerTest : IDisposable
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
            player.Send(Message.Ping());

            var readBuffer = new byte[100];
            serversClient.GetStream().Read(readBuffer, 0, readBuffer.Length);

            var readResult = Message.TryRead(readBuffer, out Message message);
            Assert.True(readResult);
            Assert.Equal(MessageType.Ping, message.Type);
        }
        [Fact]
        public void TestSendTwoMessages()
        {
            var server = StartServer();

            var client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, 65530));
            Assert.True(client.Connected);

            var serversClient = server.AcceptTcpClient();

            var player = new GamePlayer(client.Client);
            player.Send(Message.Ping());
            player.Send(Message.Pong());

            var readBuffer = new byte[100];
            int bytesRead = serversClient.GetStream().Read(readBuffer, 0, readBuffer.Length);

            var readSpan = new ReadOnlySpan<byte>(readBuffer, 0, bytesRead);

            Assert.Equal(16, bytesRead);

            var readResult1 = Message.TryRead(readSpan, out Message message1);
            Assert.True(readResult1);
            Assert.Equal(MessageType.Ping, message1.Type);

            var readResult2 = Message.TryRead(readSpan.Slice(message1.TotalLength), out Message message2);
            Assert.True(readResult2);
            Assert.Equal(MessageType.Pong, message2.Type);
        }

        [Fact]
        public async Task TestReceiveTwoMessages()
        {
            var server = StartServer();

            var client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, 65530));
            Assert.True(client.Connected);

            var serversClient = server.AcceptTcpClient();

            var player = new GamePlayer(client.Client);

            int numReceived = 0;

            player.OnMessage += (GamePlayer sender, in Message message) =>
            {
                // Console.WriteLine($"Got a test message! {message.ToString()}");
                numReceived++;
            };

            player.Listen();

            var writeBuffer = new byte[100];
            int pos = 0;
            for (int i = 0; i < 2; ++i)
            {
                Message.Pong().WriteTo(new Span<byte>(writeBuffer, pos, writeBuffer.Length - pos));
                pos += 8;
            }
            serversClient.Client.Send(new ReadOnlySpan<byte>(writeBuffer, 0, pos));

            await Task.Delay(20);
            Assert.Equal(2, numReceived);
        }

        [Fact]
        public async Task TestReceiveTwoMessagesOneIncomplete()
        {
            var server = StartServer();

            var client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, 65530));
            Assert.True(client.Connected);

            var serversClient = server.AcceptTcpClient();

            var player = new GamePlayer(client.Client);

            int numReceived = 0;

            player.OnMessage += (GamePlayer sender, in Message message) =>
            {
                // Console.WriteLine($"Got a test message! {message.ToString()}");
                numReceived++;
            };

            player.Listen();

            var writeBuffer = new byte[100];
            int pos = 0;
            for (int i = 0; i < 2; ++i)
            {
                Message.Pong().WriteTo(new Span<byte>(writeBuffer, pos, writeBuffer.Length - pos));
                pos += 8;
            }
            serversClient.Client.Send(new ReadOnlySpan<byte>(writeBuffer, 0, pos - 5));

            await Task.Delay(20);
            Assert.Equal(1, numReceived);

            serversClient.Client.Send(new ReadOnlySpan<byte>(writeBuffer, pos - 5, 5));

            await Task.Delay(20);
            Assert.Equal(2, numReceived);
        }

        public void Dispose()
        {
            server?.Stop();
            server = null;
        }
    }
}
