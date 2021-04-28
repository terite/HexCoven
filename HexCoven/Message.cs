using System;
using System.Net.Sockets;
using System.Text;

namespace HexCoven
{
    public readonly ref struct Message
    {
        public enum TryReadResult
        {
            TooShort,
            InvalidSignature,
            Success,
        }

        static byte[] Signature = { 1, 2, 3, 4, 5 };
        static ushort HeaderLength = 5 + 2 + 1;

        public readonly MessageType Type;
        public readonly ReadOnlySpan<byte> Payload;

        public readonly ushort TotalLength { get => (ushort)(HeaderLength + Payload.Length); }

        private Message(MessageType type)
        {
            Type = type;
            Payload = ReadOnlySpan<byte>.Empty;
        }

        private Message(MessageType type, ReadOnlySpan<byte> payload)
        {
            Type = type;
            Payload = payload;
        }
        private Message(MessageType type, string payload)
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

        public void WriteTo(Span<byte> target)
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

        #region Type-safe constructors
        public static Message UpdateName(string newName)
        {
            return new Message(MessageType.UpdateName, Encoding.UTF8.GetBytes(newName));
        }
        public static Message UpdateName(byte[] newName)
        {
            return new Message(MessageType.UpdateName, newName);
        }
        public static Message Surrender(float when)
        {
            return new Message(MessageType.Surrender, System.Text.Json.JsonSerializer.Serialize(when));
        }

        public static Message Connect(byte[] otherPlayerName)
        {
            return new Message(MessageType.Connect, otherPlayerName);
        }
        public static Message Ping()
        {
            return new Message(MessageType.Ping);
        }
        public static Message Pong()
        {
            return new Message(MessageType.Pong);
        }
        public static Message ApproveTeamChange()
        {
            return new Message(MessageType.ApproveTeamChange);
        }
        public static Message StartMatch(GameParams gameParams)
        {
            return new Message(MessageType.StartMatch, gameParams.Serialize());
        }
        public static Message PreviewMovesOn()
        {
            return new Message(MessageType.PreviewMovesOn);
        }
        public static Message PreviewMovesOff()
        {
            return new Message(MessageType.PreviewMovesOff);
        }

        #endregion
    }
}
