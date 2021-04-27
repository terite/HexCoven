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
        public Message(MessageType type, string payload)
        {
            Type = type;
            Payload = System.Text.Encoding.UTF8.GetBytes(payload);
        }

        public static bool TryRead(ReadOnlySpan<byte> data, out Message message)
        {
            if (data.Length < HeaderLength)
            {
                message = default;
                return false;
            }
            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != i + 1)
                {
                    throw new ArgumentException("Invalid message signature");
                }
             }

            ushort payloadLength = BitConverter.ToUInt16(data.Slice(Signature.Length));
            if (data.Length < (HeaderLength + payloadLength))
            {
                message = default;
                return false;
            }

            byte type = data[Signature.Length + 2];
            ReadOnlySpan<byte> payload = payloadLength > 0 ? data.Slice(HeaderLength, payloadLength) : ReadOnlySpan<byte>.Empty;
            message = new Message((MessageType)type, payload);
            return true;
        }

        public void WriteTo (Span<byte> target)
        {
            Signature.CopyTo(target);
            if (!BitConverter.TryWriteBytes(target.Slice(Signature.Length), (ushort)Payload.Length))
                throw new InvalidOperationException("Failed to write signature bytes");

            target[Signature.Length + sizeof(ushort)] = (byte)Type;

            if (Payload.Length > 0)
                Payload.CopyTo(target.Slice(Signature.Length + sizeof(ushort) + sizeof(byte)));
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
