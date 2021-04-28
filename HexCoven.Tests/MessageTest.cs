using HexCoven;
using System;
using Xunit;

namespace HexCoven.Tests
{
    public class MessageTest
    {
        [Fact]
        public void TestReadEmptySpan()
        {
            var data = ReadOnlySpan<byte>.Empty;
            var readResult = Message.TryRead(data, out Message message);

            Assert.False(readResult);
            Assert.Equal(MessageType.None, message.Type);
        }

        [Fact]
        public void TestReadPingOnly()
        {
            var pingMsg = Message.Ping();
            var pingBuffer = new byte[pingMsg.TotalLength];
            pingMsg.WriteTo(pingBuffer);

            var readResult = Message.TryRead(pingBuffer, out Message message);

            Assert.True(readResult);
            Assert.Equal(MessageType.Ping, message.Type);

            Assert.Equal(pingMsg.TotalLength, message.TotalLength);
        }
        
        [Fact]
        public void TestReadUpdateName()
        {
            string newName = "Lance 💪";
            var sentMsg = Message.UpdateName(newName);
            var data = new byte[sentMsg.TotalLength];
            sentMsg.WriteTo(data);

            var readResult = Message.TryRead(data, out Message recMsg);

            Assert.True(readResult);
            Assert.Equal(MessageType.UpdateName, recMsg.Type);

            Assert.Equal(sentMsg.TotalLength, recMsg.TotalLength);
            Assert.Equal(newName, System.Text.Encoding.UTF8.GetString(recMsg.Payload));
        }
    }
}
