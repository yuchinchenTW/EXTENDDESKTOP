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
        [ThreadStatic]
        private static MemoryStream _sendBuffer;

        [ThreadStatic]
        private static BinaryWriter _sendWriter;

        [ThreadStatic]
        private static byte[] _sendLengthBuf;

        public static void SendMessage(Stream stream, object syncRoot, MessageType type, Action<BinaryWriter> writePayload)
        {
            var bodyStream = _sendBuffer;
            if (bodyStream == null)
            {
                bodyStream = new MemoryStream(64 * 1024);
                _sendBuffer = bodyStream;
                _sendWriter = new BinaryWriter(bodyStream, Encoding.UTF8);
                _sendLengthBuf = new byte[4];
            }

            bodyStream.Position = 0;
            bodyStream.SetLength(0);

            var writer = _sendWriter;
            writer.Write((byte)type);
            writePayload(writer);
            writer.Flush();

            int bodyLen = (int)bodyStream.Length;
            var lengthBuf = _sendLengthBuf;
            lengthBuf[0] = (byte)(bodyLen & 0xFF);
            lengthBuf[1] = (byte)((bodyLen >> 8) & 0xFF);
            lengthBuf[2] = (byte)((bodyLen >> 16) & 0xFF);
            lengthBuf[3] = (byte)((bodyLen >> 24) & 0xFF);

            lock (syncRoot)
            {
                stream.Write(lengthBuf, 0, 4);
                stream.Write(bodyStream.GetBuffer(), 0, bodyLen);
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

        public static int ReceiveMessageInto(Stream stream, ref byte[] buffer)
        {
            var lengthBytes = ReadExact(stream, 4);
            var length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("Invalid message length.");
            }

            if (buffer == null || buffer.Length < length)
            {
                buffer = new byte[Math.Max(length, 256 * 1024)];
            }

            int read = 0;
            while (read < length)
            {
                int n = stream.Read(buffer, read, length - read);
                if (n <= 0)
                {
                    throw new EndOfStreamException("Remote side disconnected.");
                }
                read += n;
            }

            return length;
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
