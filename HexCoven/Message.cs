using System;
using System.Net.Sockets;

namespace HexCoven
{
    public readonly ref struct Message
    {
        public enum TryReadResult {
            TooShort,
            InvalidSignature,
            Success,
        }

        static byte[] Signature = { 1, 2, 3, 4, 5 };
        static ushort HeaderLength = 5 + 2 + 1;

        public readonly MessageType Type;
        public readonly ReadOnlySpan<byte> Payload;

        public readonly ushort TotalLength { get => (ushort)(HeaderLength + Payload.Length); }

        public Message(MessageType type)
        {
            Type = type;
            Payload = ReadOnlySpan<byte>.Empty;
        }

        public Message(MessageType type, ReadOnlySpan<byte> payload)
        {
            Type = type;
            Payload = payload;
        }

        public static TryReadResult TryRead(ReadOnlySpan<byte> data, out Message message)
        {
            if (data.Length < HeaderLength)
            {
                message = default;
                return TryReadResult.TooShort;
            }
            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != i + 1)
                {
                    message = default;
                    return TryReadResult.InvalidSignature;
                }
             }

            ushort payloadLength = BitConverter.ToUInt16(data.Slice(Signature.Length));
            if (data.Length < (HeaderLength + payloadLength))
            {
                message = default;
                return TryReadResult.TooShort;
            }

            byte type = data[Signature.Length + 2];
            ReadOnlySpan<byte> payload = payloadLength > 0 ? data.Slice(HeaderLength, payloadLength) : ReadOnlySpan<byte>.Empty;
            message = new Message((MessageType)type, payload);
            return TryReadResult.Success;
        }

        public void WriteTo (Socket socket)
        {
            if (Settings.LogOutbound)
                if (Settings.LogOutboundPing || (Type != MessageType.Ping && Type != MessageType.Pong))
                    Console.WriteLine($"-> {socket.RemoteEndPoint} -- {this.ToString()}");

            socket.Send(Signature);
            socket.Send(BitConverter.GetBytes((ushort)Payload.Length));
            socket.Send(new byte[] { (byte)this.Type });
            if (Payload.Length > 0)
                socket.Send(Payload);
        }

        public override string ToString()
        {
            string payloadStr;
            if (Payload.Length == 0)
                payloadStr = String.Empty;
            else if (Type == MessageType.BoardState || Type == MessageType.Connect || Type == MessageType.UpdateName)
            {
                payloadStr = System.Text.Encoding.UTF8.GetString(Payload);
            }
            else
            {
                payloadStr = Payload.Length > 0 ? $"{Payload.Length}:{BitConverter.ToString(Payload.ToArray())}" : String.Empty;
            }

            if (!String.IsNullOrEmpty(payloadStr))
                payloadStr = $", payload={payloadStr}";

            return $"Message(type={Type}{payloadStr})";
        }
    }
}
