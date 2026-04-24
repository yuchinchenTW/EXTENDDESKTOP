using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Receiver
{
    internal sealed class DisplayReceiverClient : IDisposable
    {
        private readonly Action<string> _statusCallback;
        private readonly Action<Bitmap, int, int> _frameCallback;
        private readonly object _writeSync = new object();
        private readonly LatestFrameStore _latestFrame = new LatestFrameStore();

        private TcpClient _client;
        private Thread _receiveThread;
        private Thread _decodeThread;
        private volatile bool _running;

        public DisplayReceiverClient(Action<string> statusCallback, Action<Bitmap, int, int> frameCallback)
        {
            _statusCallback = statusCallback;
            _frameCallback = frameCallback;
        }

        public void Connect(string host, int port, string password)
        {
            if (_running)
            {
                return;
            }

            _client = new TcpClient();
            _client.NoDelay = true;
            _client.Connect(host, port);

            var stream = _client.GetStream();
            Protocol.SendMessage(stream, _writeSync, MessageType.AuthRequest, delegate(BinaryWriter writer)
            {
                writer.Write(password ?? string.Empty);
            });

            var response = Protocol.ReceiveMessage(stream);
            if (response.Type != MessageType.AuthResponse)
            {
                throw new InvalidDataException("Unexpected auth response.");
            }

            using (var reader = Protocol.CreateReader(response.Payload))
            {
                var success = reader.ReadBoolean();
                var message = reader.ReadString();

                if (!success)
                {
                    throw new InvalidDataException(message);
                }
            }

            _running = true;
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();

            _decodeThread = new Thread(DecodeLoop);
            _decodeThread.IsBackground = true;
            _decodeThread.Start();

            _statusCallback("Connected.");
        }

        public void Dispose()
        {
            var wasRunning = _running;
            _running = false;
            _latestFrame.Complete();

            if (_client != null)
            {
                try
                {
                    _client.Close();
                }
                catch
                {
                }

                _client = null;
            }

            if (_receiveThread != null && _receiveThread != Thread.CurrentThread)
            {
                _receiveThread.Join(500);
                _receiveThread = null;
            }

            if (_decodeThread != null && _decodeThread != Thread.CurrentThread)
            {
                _decodeThread.Join(500);
                _decodeThread = null;
            }

            if (wasRunning)
            {
                _statusCallback("Disconnected.");
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                var stream = _client.GetStream();

                while (_running)
                {
                    var message = Protocol.ReceiveMessage(stream);
                    if (message.Type != MessageType.Frame)
                    {
                        continue;
                    }

                    using (var reader = Protocol.CreateReader(message.Payload))
                    {
                        var width = reader.ReadInt32();
                        var height = reader.ReadInt32();
                        var imageLength = reader.ReadInt32();
                        var imageBytes = reader.ReadBytes(imageLength);
                        _latestFrame.Update(width, height, imageBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    _statusCallback("Disconnected: " + ex.Message);
                }
            }
            finally
            {
                Dispose();
            }
        }

        private void DecodeLoop()
        {
            while (_running)
            {
                FrameData frame;
                if (!_latestFrame.WaitAndTake(out frame))
                {
                    return;
                }

                try
                {
                    using (var imageStream = new MemoryStream(frame.JpegBytes))
                    using (var image = Image.FromStream(imageStream))
                    {
                        _frameCallback(new Bitmap(image), frame.Width, frame.Height);
                    }
                }
                catch
                {
                }
            }
        }

        private sealed class FrameData
        {
            public int Width;
            public int Height;
            public byte[] JpegBytes;
        }

        private sealed class LatestFrameStore
        {
            private readonly object _sync = new object();
            private readonly AutoResetEvent _available = new AutoResetEvent(false);
            private volatile bool _completed;
            private FrameData _pending;

            public void Update(int width, int height, byte[] jpegBytes)
            {
                if (_completed)
                {
                    return;
                }

                lock (_sync)
                {
                    _pending = new FrameData
                    {
                        Width = width,
                        Height = height,
                        JpegBytes = jpegBytes
                    };
                }

                _available.Set();
            }

            public bool WaitAndTake(out FrameData frame)
            {
                frame = null;

                while (true)
                {
                    lock (_sync)
                    {
                        if (_pending != null)
                        {
                            frame = _pending;
                            _pending = null;
                            return true;
                        }

                        if (_completed)
                        {
                            return false;
                        }
                    }

                    _available.WaitOne();
                }
            }

            public void Complete()
            {
                _completed = true;
                _available.Set();
            }
        }
    }
}
