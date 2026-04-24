using System;
using System.IO;
using System.Text;

namespace ExtentDesktop.Shared
{
    public enum MessageType : byte
    {
        AuthRequest = 1,
        AuthResponse = 2,
        Frame = 3
    }

    public sealed class Message
    {
        public Message(MessageType type, byte[] payload)
        {
            Type = type;
            Payload = payload;
        }

        public MessageType Type { get; private set; }
        public byte[] Payload { get; private set; }
    }

    public static class Protocol
    {
        public static void SendMessage(Stream stream, object syncRoot, MessageType type, Action<BinaryWriter> writePayload)
        {
            byte[] body;

            using (var bodyStream = new MemoryStream())
            using (var writer = new BinaryWriter(bodyStream, Encoding.UTF8))
            {
                writer.Write((byte)type);
                writePayload(writer);
                writer.Flush();
                body = bodyStream.ToArray();
            }

            var length = BitConverter.GetBytes(body.Length);

            lock (syncRoot)
            {
                stream.Write(length, 0, length.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
        }

        public static Message ReceiveMessage(Stream stream)
        {
            var lengthBytes = ReadExact(stream, 4);
            var length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("Invalid message length.");
            }

            var body = ReadExact(stream, length);
            var type = (MessageType)body[0];
            var payload = new byte[length - 1];
            if (payload.Length > 0)
            {
                Buffer.BlockCopy(body, 1, payload, 0, payload.Length);
            }

            return new Message(type, payload);
        }

        public static BinaryReader CreateReader(byte[] payload)
        {
            return new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            var buffer = new byte[length];
            var offset = 0;

            while (offset < length)
            {
                var read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Remote side disconnected.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}
