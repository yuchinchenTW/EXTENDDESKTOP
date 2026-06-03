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
        private H264HwDecoder _hwDecoder;
        private FrameBitmapPool _bitmapPool;
        private System.Threading.Timer _keepWarmTimer;
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

            _keepWarmTimer = new System.Threading.Timer(KeepWarmTick, null, 3000, 3000);

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

            if (_hwDecoder != null)
            {
                try { _hwDecoder.Dispose(); } catch { }
                _hwDecoder = null;
            }
            if (_decoder != null)
            {
                try { _decoder.Dispose(); } catch { }
                _decoder = null;
            }

            if (_keepWarmTimer != null)
            {
                try { _keepWarmTimer.Dispose(); } catch { }
                _keepWarmTimer = null;
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

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadTimes(IntPtr hThread, out long creation, out long exit, out long kernel, out long user);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(IntPtr hProcess, out long creation, out long exit, out long kernel, out long user);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private long _lastCpuTicks;
        private long _lastProcCpuTicks;

        private long GetThreadCpuMs()
        {
            long c, e, k, u;
            if (!GetThreadTimes(GetCurrentThread(), out c, out e, out k, out u))
            {
                return -1;
            }
            long total = k + u;
            long delta = total - _lastCpuTicks;
            _lastCpuTicks = total;
            return delta / 10000L;
        }

        private void KeepWarmTick(object state)
        {
            try
            {
                var pool = _bitmapPool;
                if (pool != null) pool.TouchAll();
            }
            catch
            {
            }
        }

        private long GetProcessCpuMs()
        {
            long c, e, k, u;
            if (!GetProcessTimes(GetCurrentProcess(), out c, out e, out k, out u))
            {
                return -1;
            }
            long total = k + u;
            long delta = total - _lastProcCpuTicks;
            _lastProcCpuTicks = total;
            return delta / 10000L;
        }

        private void DecodeLoop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            GetThreadCpuMs();
            while (_running)
            {
                FrameData frame;
                if (!_latestFrame.WaitAndTake(out frame))
                {
                    return;
                }

                GetThreadCpuMs();
                GetProcessCpuMs();
                long t0 = sw.ElapsedTicks;

                try
                {
                    if (_decoder == null && _hwDecoder == null)
                    {
                        _bitmapPool = new FrameBitmapPool();
                        try
                        {
                            _hwDecoder = new H264HwDecoder(frame.Width, frame.Height, _bitmapPool);
                            ExtentDesktop.Shared.MFHelpers.Log("Using H.264 HW decoder path");
                        }
                        catch (Exception hwEx)
                        {
                            ExtentDesktop.Shared.MFHelpers.Log("HW decoder init failed, using SW: " + hwEx.Message);
                            try
                            {
                                _decoder = new H264Decoder(frame.Width, frame.Height, _bitmapPool);
                            }
                            catch (Exception ex)
                            {
                                _statusCallback("H.264 init failed: " + ex.Message);
                                return;
                            }
                        }
                        // First-frame init time is not representative; reset t0 after init
                        t0 = sw.ElapsedTicks;
                    }

                    long accumProcOut = 0;
                    long accumConv = 0;
                    int drainIters = 0;
                    Bitmap decoded;
                    long tSubmit;

                    if (_hwDecoder != null)
                    {
                        _hwDecoder.AccumulatedProcessOutputTicks = 0;
                        _hwDecoder.AccumulatedConvertTicks = 0;
                        _hwDecoder.Submit(frame.PayloadBuffer, frame.PayloadLength);
                        _latestFrame.ReturnBuffer(frame.PayloadBuffer);
                        tSubmit = sw.ElapsedTicks;
                        while (_hwDecoder.TryDrainBitmap(out decoded))
                        {
                            drainIters++;
                            if (decoded != null) _frameCallback(decoded, frame.Width, frame.Height);
                        }
                        accumProcOut = _hwDecoder.AccumulatedProcessOutputTicks;
                        accumConv = _hwDecoder.AccumulatedConvertTicks;
                    }
                    else
                    {
                        _decoder.AccumulatedProcessOutputTicks = 0;
                        _decoder.AccumulatedConvertTicks = 0;
                        _decoder.Submit(frame.PayloadBuffer, frame.PayloadLength);
                        _latestFrame.ReturnBuffer(frame.PayloadBuffer);
                        tSubmit = sw.ElapsedTicks;
                        while (_decoder.TryDrainBitmap(out decoded))
                        {
                            drainIters++;
                            if (decoded != null) _frameCallback(decoded, frame.Width, frame.Height);
                        }
                        accumProcOut = _decoder.AccumulatedProcessOutputTicks;
                        accumConv = _decoder.AccumulatedConvertTicks;
                    }

                    long tDrain = sw.ElapsedTicks;

                    long totalMs = (tDrain - t0) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                    if (totalMs > 35)
                    {
                        long subMs = (tSubmit - t0) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long drainMs = (tDrain - tSubmit) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long procMs = accumProcOut * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long convMs = accumConv * 1000L / System.Diagnostics.Stopwatch.Frequency;
                        long cpuMs = GetThreadCpuMs();
                        long procMsCpu = GetProcessCpuMs();
                        ExtentDesktop.Shared.MFHelpers.Log("SLOW decode total=" + totalMs + "ms submit=" + subMs + " drain=" + drainMs + " iters=" + drainIters + " procOutSum=" + procMs + " convSum=" + convMs + " thrCpu=" + cpuMs + " procCpu=" + procMsCpu);
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

                        ReturnBufferLocked(poolBuf);
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
                    ReturnBufferLocked(buffer);
                }
            }

            public void Complete()
            {
                _completed = true;
                _available.Set();
                _slotFreed.Set();
            }

            private void ReturnBufferLocked(byte[] buffer)
            {
                if (buffer != null && _bufferPool.Count < BufferPoolCapacity)
                {
                    _bufferPool.Push(buffer);
                }
            }
        }
    }
}
