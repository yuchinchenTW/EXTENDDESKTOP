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
        private H264Decoder _decoder;
        private FrameBitmapPool _bitmapPool;
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
            _receiveThread.Priority = ThreadPriority.AboveNormal;
            _receiveThread.Start();

            _decodeThread = new Thread(DecodeLoop);
            _decodeThread.IsBackground = true;
            _decodeThread.Priority = ThreadPriority.Highest;
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

            if (_decoder != null)
            {
                try { _decoder.Dispose(); } catch { }
                _decoder = null;
            }

            if (_bitmapPool != null)
            {
                try { _bitmapPool.Dispose(); } catch { }
                _bitmapPool = null;
            }

            if (wasRunning)
            {
                _statusCallback("Disconnected.");
            }
        }

        private void ReceiveLoop()
        {
            byte[] receiveBuffer = null;
            try
            {
                var stream = _client.GetStream();

                while (_running)
                {
                    int totalLen = Protocol.ReceiveMessageInto(stream, ref receiveBuffer);
                    var msgType = (MessageType)receiveBuffer[0];
                    if (msgType != MessageType.Frame)
                    {
                        continue;
                    }

                    int width = BitConverter.ToInt32(receiveBuffer, 1);
                    int height = BitConverter.ToInt32(receiveBuffer, 5);
                    int imageLength = BitConverter.ToInt32(receiveBuffer, 9);
                    _latestFrame.Update(width, height, receiveBuffer, 13, imageLength);
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_running)
            {
                FrameData frame;
                if (!_latestFrame.WaitAndTake(out frame))
                {
                    return;
                }

                long t0 = sw.ElapsedTicks;

                try
                {
                    if (_decoder == null)
                    {
                        try
                        {
                            _bitmapPool = new FrameBitmapPool();
                            _decoder = new H264Decoder(frame.Width, frame.Height, _bitmapPool);
                        }
                        catch (Exception ex)
                        {
                            _statusCallback("H.264 init failed: " + ex.Message);
                            return;
                        }
                    }

                    _decoder.Submit(frame.PayloadBuffer, frame.PayloadLength);
                    _latestFrame.ReturnBuffer(frame.PayloadBuffer);
                    long tSubmit = sw.ElapsedTicks;

                    _decoder.AccumulatedProcessOutputTicks = 0;
                    _decoder.AccumulatedConvertTicks = 0;
                    int drainIters = 0;
                    Bitmap decoded;
                    while (_decoder.TryDrainBitmap(out decoded))
                    {
                        drainIters++;
                        if (decoded != null)
                        {
                            _frameCallback(decoded, frame.Width, frame.Height);
                        }
                    }
                    long tDrain = sw.ElapsedTicks;

                    long totalMs = (tDrain - t0) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                    if (totalMs > 35)
                    {
                        long subMs = (tSubmit - t0) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long drainMs = (tDrain - tSubmit) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long procMs = _decoder.AccumulatedProcessOutputTicks * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long convMs = _decoder.AccumulatedConvertTicks * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        ExtentDesktop.Shared.MFHelpers.Log("SLOW decode total=" + totalMs + "ms submit=" + subMs + " drain=" + drainMs + " iters=" + drainIters + " procOutSum=" + procMs + " convSum=" + convMs);
                    }
                }
                catch (Exception ex)
                {
                    ExtentDesktop.Shared.MFHelpers.Log("decode exception: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private sealed class FrameData
        {
            public int Width;
            public int Height;
            public byte[] PayloadBuffer;
            public int PayloadLength;
        }

        private sealed class LatestFrameStore
        {
            private readonly object _sync = new object();
            private readonly System.Collections.Generic.Queue<FrameData> _queue = new System.Collections.Generic.Queue<FrameData>();
            private readonly System.Collections.Generic.Stack<byte[]> _bufferPool = new System.Collections.Generic.Stack<byte[]>();
            private readonly System.Collections.Generic.Stack<FrameData> _frameDataPool = new System.Collections.Generic.Stack<FrameData>();
            private readonly AutoResetEvent _available = new AutoResetEvent(false);
            private readonly AutoResetEvent _slotFreed = new AutoResetEvent(false);
            private const int MaxDepth = 3;
            private const int BufferPoolCapacity = 6;
            private volatile bool _completed;

            public void Update(int width, int height, byte[] source, int offset, int length)
            {
                while (!_completed)
                {
                    byte[] poolBuf;
                    lock (_sync)
                    {
                        poolBuf = _bufferPool.Count > 0 ? _bufferPool.Pop() : null;
                    }

                    if (poolBuf == null || poolBuf.Length < length)
                    {
                        poolBuf = new byte[Math.Max(length, 256 * 1024)];
                    }

                    Buffer.BlockCopy(source, offset, poolBuf, 0, length);

                    lock (_sync)
                    {
                        if (_queue.Count < MaxDepth)
                        {
                            FrameData frame;
                            if (_frameDataPool.Count > 0)
                            {
                                frame = _frameDataPool.Pop();
                            }
                            else
                            {
                                frame = new FrameData();
                            }
                            frame.Width = width;
                            frame.Height = height;
                            frame.PayloadBuffer = poolBuf;
                            frame.PayloadLength = length;
                            _queue.Enqueue(frame);
                            _available.Set();
                            return;
                        }
                        else
                        {
                            // queue full, return our buffer back to pool then wait
                            if (_bufferPool.Count < BufferPoolCapacity)
                            {
                                _bufferPool.Push(poolBuf);
                            }
                        }
                    }
                    _slotFreed.WaitOne(50);
                }
            }

            public bool WaitAndTake(out FrameData frame)
            {
                frame = null;

                while (true)
                {
                    lock (_sync)
                    {
                        if (_queue.Count > 0)
                        {
                            frame = _queue.Dequeue();
                            _slotFreed.Set();
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

            public void ReturnBuffer(byte[] buffer)
            {
                if (buffer == null) return;
                lock (_sync)
                {
                    if (_bufferPool.Count < BufferPoolCapacity)
                    {
                        _bufferPool.Push(buffer);
                    }
                }
            }

            public void Complete()
            {
                _completed = true;
                _available.Set();
                _slotFreed.Set();
            }
        }
    }
}
