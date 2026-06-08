using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal sealed class WebStreamHost : IDisposable
    {
        private readonly Action<string> _statusCallback;
        private readonly Action<string> _clientCallback;

        private TcpListener _listener;
        private Thread _acceptThread;
        private CancellationTokenSource _sessionTokenSource;
        private volatile bool _running;
        private int _port;
        private byte[] _indexHtml;
        private Func<Rectangle> _captureBoundsProvider;
        private Func<string> _captureLabelProvider;
        private Func<int> _streamMaxDimensionProvider;

        public WebStreamHost(Action<string> statusCallback, Action<string> clientCallback)
        {
            _statusCallback = statusCallback;
            _clientCallback = clientCallback;
        }

        public void Start(int port, byte[] indexHtml, Func<Rectangle> captureBoundsProvider, Func<int> streamMaxDimensionProvider, Func<string> captureLabelProvider)
        {
            if (_running) return;

            _port = port;
            _indexHtml = indexHtml;
            _captureBoundsProvider = captureBoundsProvider;
            _streamMaxDimensionProvider = streamMaxDimensionProvider;
            _captureLabelProvider = captureLabelProvider;

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _running = true;

            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Name = "WebStreamAccept";
            _acceptThread.Start();

            _statusCallback("Web stream listening on http://<host>:" + port + "/");
        }

        public void Dispose()
        {
            _running = false;
            if (_sessionTokenSource != null) { try { _sessionTokenSource.Cancel(); } catch { } }
            if (_listener != null) { try { _listener.Stop(); } catch { } }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException) { if (!_running) return; continue; }
                catch (ObjectDisposedException) { return; }

                ThreadPool.QueueUserWorkItem(state => HandleHttpClient((TcpClient)state), client);
            }
        }

        private void HandleHttpClient(TcpClient client)
        {
            try
            {
                client.NoDelay = true;
                try { client.SendBufferSize = 1024 * 1024; } catch { }
                var stream = client.GetStream();
                stream.ReadTimeout = 5000;

                MFHelpers.Log("WebStream: HTTP client " + client.Client.RemoteEndPoint);
                var requestLine = ReadHttpLine(stream);
                MFHelpers.Log("WebStream: request='" + requestLine + "'");
                if (string.IsNullOrEmpty(requestLine))
                {
                    client.Close();
                    return;
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var line = ReadHttpLine(stream);
                    if (string.IsNullOrEmpty(line)) break;
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        var key = line.Substring(0, colon).Trim();
                        var value = line.Substring(colon + 1).Trim();
                        headers[key] = value;
                    }
                }

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) { client.Close(); return; }
                var path = parts[1];

                string upgrade;
                bool isWebSocket = headers.TryGetValue("Upgrade", out upgrade) && string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase);

                if (isWebSocket && path == "/stream")
                {
                    HandleWebSocketStream(client, stream, headers);
                }
                else if (path == "/" || path == "/index.html")
                {
                    SendHttpResponse(stream, "200 OK", "text/html; charset=utf-8", _indexHtml);
                    client.Close();
                }
                else
                {
                    SendHttpResponse(stream, "404 Not Found", "text/plain", Encoding.UTF8.GetBytes("Not Found"));
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                MFHelpers.Log("WebStreamHost client error: " + ex.Message);
                try { client.Close(); } catch { }
            }
        }

        private static string ReadHttpLine(NetworkStream stream)
        {
            var sb = new StringBuilder();
            int prev = -1;
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                if (b == '\n')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] == '\r') sb.Length--;
                    return sb.ToString();
                }
                sb.Append((char)b);
                prev = b;
                if (sb.Length > 8192) throw new InvalidDataException("HTTP line too long");
            }
        }

        private static void SendHttpResponse(NetworkStream stream, string status, string contentType, byte[] body)
        {
            var header = "HTTP/1.1 " + status + "\r\n" +
                         "Content-Type: " + contentType + "\r\n" +
                         "Content-Length: " + body.Length + "\r\n" +
                         "Cache-Control: no-cache\r\n" +
                         "Connection: close\r\n" +
                         "\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private void HandleWebSocketStream(TcpClient client, NetworkStream stream, Dictionary<string, string> headers)
        {
            string key;
            if (!headers.TryGetValue("Sec-WebSocket-Key", out key))
            {
                client.Close();
                return;
            }

            var acceptKey = ComputeAcceptKey(key);
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           "Sec-WebSocket-Accept: " + acceptKey + "\r\n" +
                           "\r\n";
            var responseBytes = Encoding.ASCII.GetBytes(response);
            stream.Write(responseBytes, 0, responseBytes.Length);
            stream.Flush();

            stream.ReadTimeout = System.Threading.Timeout.Infinite;
            _clientCallback("Web client connected from " + client.Client.RemoteEndPoint + ".");
            _statusCallback("Streaming H.264 to " + client.Client.RemoteEndPoint + ".");
            MFHelpers.Log("WebStream: WS upgraded for " + client.Client.RemoteEndPoint);

            try
            {
                var bounds = _captureBoundsProvider != null ? _captureBoundsProvider() : SystemInformation.VirtualScreen;
                if (bounds.Width <= 0 || bounds.Height <= 0) bounds = SystemInformation.VirtualScreen;

                int encodedWidth;
                int encodedHeight;
                int maxDim = GetStreamMaxDimension();
                ResolveEncodedSize(bounds, maxDim, out encodedWidth, out encodedHeight);

                var configMsg = "{\"type\":\"config\",\"width\":" + encodedWidth + ",\"height\":" + encodedHeight + "}";
                SendWebSocketText(stream, configMsg);
                MFHelpers.Log("WebStream: config sent " + encodedWidth + "x" + encodedHeight + " maxDim=" + maxDim);

                var sessionCts = new CancellationTokenSource();
                _sessionTokenSource = sessionCts;
                var token = sessionCts.Token;
                var writeSync = new object();

                StartReaderThread(stream, sessionCts, writeSync);

                WebSocketStreamFrames(stream, token, 60, encodedWidth, encodedHeight, writeSync);
            }
            catch (Exception ex)
            {
                MFHelpers.Log("WebStream session ended: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { client.Close(); } catch { }
                _clientCallback("No web client connected.");
                if (_running) _statusCallback("Web stream listening on http://<host>:" + _port + "/");
            }
        }

        private void StartReaderThread(NetworkStream stream, CancellationTokenSource sessionCts, object writeSync)
        {
            var t = new Thread(() =>
            {
                try
                {
                    while (!sessionCts.IsCancellationRequested)
                    {
                        if (!ReadAndHandleWebSocketFrame(stream, writeSync)) break;
                    }
                }
                catch { }
                finally
                {
                    try { sessionCts.Cancel(); } catch { }
                }
            });
            t.IsBackground = true;
            t.Name = "WebSocketReader";
            t.Start();
        }

        private static int _frameLogCount = 0;
        private static bool ReadAndHandleWebSocketFrame(NetworkStream stream, object writeSync)
        {
            byte[] header = new byte[2];
            if (!ReadFull(stream, header, 0, 2)) return false;

            bool fin = (header[0] & 0x80) != 0;
            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (_frameLogCount++ < 8)
            {
                MFHelpers.Log("WebStream incoming: opcode=0x" + opcode.ToString("X") + " masked=" + masked + " len=" + payloadLen);
            }

            if (payloadLen == 126)
            {
                byte[] ext = new byte[2];
                if (!ReadFull(stream, ext, 0, 2)) return false;
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                byte[] ext = new byte[8];
                if (!ReadFull(stream, ext, 0, 8)) return false;
                payloadLen = 0;
                for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
            }

            byte[] mask = null;
            if (masked)
            {
                mask = new byte[4];
                if (!ReadFull(stream, mask, 0, 4)) return false;
            }

            byte[] payload = null;
            if (payloadLen > 0)
            {
                if (payloadLen > 16 * 1024 * 1024) return false;
                payload = new byte[payloadLen];
                if (!ReadFull(stream, payload, 0, (int)payloadLen)) return false;
                if (masked)
                {
                    for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
                }
            }

            if (opcode == 0x8)
            {
                lock (writeSync)
                {
                    SendWebSocketFrame(stream, 0x8, payload ?? new byte[0], 0, payload != null ? payload.Length : 0);
                }
                return false;
            }
            if (opcode == 0x9)
            {
                lock (writeSync)
                {
                    SendWebSocketFrame(stream, 0xA, payload ?? new byte[0], 0, payload != null ? payload.Length : 0);
                }
                return true;
            }

            return true;
        }

        private static bool ReadFull(NetworkStream stream, byte[] buf, int offset, int length)
        {
            int got = 0;
            while (got < length)
            {
                int n = stream.Read(buf, offset + got, length - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        private void WebSocketStreamFrames(NetworkStream stream, CancellationToken token, int fps, int encodedWidth, int encodedHeight, object writeSync)
        {
            var bitrate = ChooseBitrate(encodedWidth, encodedHeight, fps);

            H264HwEncoder hwEnc = null;
            H264Encoder swEnc = null;
            try
            {
                hwEnc = new H264HwEncoder(encodedWidth, encodedHeight, fps, bitrate);
                MFHelpers.Log("WebStream: using HW H.264 encoder");
            }
            catch (Exception ex)
            {
                MFHelpers.Log("WebStream: HW encoder unavailable (" + ex.Message + "), falling back to SW");
                swEnc = new H264Encoder(encodedWidth, encodedHeight, fps, bitrate);
            }

            try
            {
                using (var capturer = new GdiBgraCapturer(_captureBoundsProvider, encodedWidth, encodedHeight))
                {
                    var targetFrameTicks = (long)(System.Diagnostics.Stopwatch.Frequency / (double)Math.Max(1, fps));
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var nextFrameTicks = watch.ElapsedTicks;
                    int sentCount = 0;
                    long freq = System.Diagnostics.Stopwatch.Frequency;
                    int slowLogCount = 0;
                    long statsStartTicks = watch.ElapsedTicks;
                    int statsLogCount = 0;
                    int statsCapCount = 0;
                    int statsSentCount = 0;
                    long statsCapTicks = 0;
                    long statsEncTicks = 0;
                    long statsSendTicks = 0;
                    string encoderName = hwEnc != null ? "HW" : "SW";

                    while (!token.IsCancellationRequested)
                    {
                        long t0 = watch.ElapsedTicks;
                        System.Drawing.Imaging.BitmapData locked;
                        long tCap = t0;
                        long tEnc = t0;
                        if (capturer.TryCaptureLocked(out locked))
                        {
                            tCap = watch.ElapsedTicks;
                            try
                            {
                                if (hwEnc != null) hwEnc.Submit(locked.Scan0, locked.Stride);
                                else swEnc.Submit(locked.Scan0, locked.Stride);
                            }
                            finally
                            {
                                capturer.UnlockCaptured();
                            }
                            tEnc = watch.ElapsedTicks;
                            statsCapCount++;
                            statsCapTicks += tCap - t0;
                            statsEncTicks += tEnc - tCap;

                            byte[] outputBytes;
                            int outputLen;
                            bool isKeyframe;
                            bool drained;
                            do
                            {
                                drained = hwEnc != null
                                    ? hwEnc.TryDrainOutput(out outputBytes, out outputLen, out isKeyframe)
                                    : swEnc.TryDrainOutput(out outputBytes, out outputLen, out isKeyframe);
                                if (!drained) break;

                                try
                                {
                                    lock (writeSync)
                                    {
                                        SendWebSocketBinary(stream, outputBytes, 0, outputLen);
                                    }
                                    statsSentCount++;
                                    if (sentCount < 5)
                                    {
                                        sentCount++;
                                        MFHelpers.Log("WebStream sent frame #" + sentCount + " bytes=" + outputLen + " key=" + isKeyframe);
                                    }
                                }
                                catch (Exception sendEx)
                                {
                                    MFHelpers.Log("WebStream send failed: " + sendEx.Message);
                                    return;
                                }
                            } while (true);

                            long tSend = watch.ElapsedTicks;
                            statsSendTicks += tSend - tEnc;
                            long totalMs = (tSend - t0) * 1000L / freq;
                            if (totalMs > 25 && slowLogCount < 30)
                            {
                                slowLogCount++;
                                long capMs = (tCap - t0) * 1000L / freq;
                                long encMs = (tEnc - tCap) * 1000L / freq;
                                long sendMs = (tSend - tEnc) * 1000L / freq;
                                MFHelpers.Log("WebStream SLOW total=" + totalMs + " cap=" + capMs + " enc=" + encMs + " send=" + sendMs);
                            }
                        }

                        long statsNowTicks = watch.ElapsedTicks;
                        if (statsLogCount < 60 && statsNowTicks - statsStartTicks >= freq)
                        {
                            double elapsedSec = (statsNowTicks - statsStartTicks) / (double)freq;
                            double capFps = statsCapCount / elapsedSec;
                            double sentFps = statsSentCount / elapsedSec;
                            double avgCapMs = statsCapCount > 0 ? statsCapTicks * 1000.0 / freq / statsCapCount : 0.0;
                            double avgEncMs = statsCapCount > 0 ? statsEncTicks * 1000.0 / freq / statsCapCount : 0.0;
                            double avgSendMs = statsCapCount > 0 ? statsSendTicks * 1000.0 / freq / statsCapCount : 0.0;

                            statsLogCount++;
                            MFHelpers.Log("WebStream fps cap=" + FormatOne(capFps) +
                                " sent=" + FormatOne(sentFps) +
                                " avgCap=" + FormatOne(avgCapMs) + "ms" +
                                " avgEnc=" + FormatOne(avgEncMs) + "ms" +
                                " avgSend=" + FormatOne(avgSendMs) + "ms" +
                                " size=" + encodedWidth + "x" + encodedHeight +
                                " enc=" + encoderName);

                            statsStartTicks = statsNowTicks;
                            statsCapCount = 0;
                            statsSentCount = 0;
                            statsCapTicks = 0;
                            statsEncTicks = 0;
                            statsSendTicks = 0;
                        }

                        nextFrameTicks += targetFrameTicks;
                        var remainingTicks = nextFrameTicks - watch.ElapsedTicks;
                        if (remainingTicks > 0)
                        {
                            var remainingMs = (int)(remainingTicks * 1000L / System.Diagnostics.Stopwatch.Frequency);
                            if (remainingMs > 0 && token.WaitHandle.WaitOne(remainingMs)) return;
                        }
                        else
                        {
                            nextFrameTicks = watch.ElapsedTicks;
                        }
                    }
                }
            }
            finally
            {
                if (hwEnc != null) try { hwEnc.Dispose(); } catch { }
                if (swEnc != null) try { swEnc.Dispose(); } catch { }
            }
        }

        private int GetStreamMaxDimension()
        {
            int maxDim = _streamMaxDimensionProvider != null ? _streamMaxDimensionProvider() : 1280;
            return maxDim < 0 ? 0 : maxDim;
        }

        private static void ResolveEncodedSize(Rectangle bounds, int maxDim, out int encodedWidth, out int encodedHeight)
        {
            if (maxDim > 0 && (bounds.Width > maxDim || bounds.Height > maxDim))
            {
                double scale = Math.Min((double)maxDim / bounds.Width, (double)maxDim / bounds.Height);
                encodedWidth = EvenAtLeastTwo((int)Math.Round(bounds.Width * scale));
                encodedHeight = EvenAtLeastTwo((int)Math.Round(bounds.Height * scale));
                return;
            }

            encodedWidth = EvenAtLeastTwo(bounds.Width);
            encodedHeight = EvenAtLeastTwo(bounds.Height);
        }

        private static int EvenAtLeastTwo(int value)
        {
            value &= ~1;
            return value < 2 ? 2 : value;
        }

        private static string FormatOne(double value)
        {
            return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int ChooseBitrate(int width, int height, int fps)
        {
            long pixelsPerSecond = (long)width * height * fps;
            int bitrate = (int)(pixelsPerSecond * 0.06);
            if (bitrate < 1500000) bitrate = 1500000;
            if (bitrate > 12000000) bitrate = 12000000;
            return bitrate;
        }

        private static string ComputeAcceptKey(string key)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key + magic));
                return Convert.ToBase64String(hash);
            }
        }

        private static void SendWebSocketText(NetworkStream stream, string text)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            SendWebSocketFrame(stream, 0x1, payload, 0, payload.Length);
        }

        private static void SendWebSocketBinary(NetworkStream stream, byte[] data, int offset, int length)
        {
            SendWebSocketFrame(stream, 0x2, data, offset, length);
        }

        private static void SendWebSocketFrame(NetworkStream stream, byte opcode, byte[] data, int offset, int length)
        {
            byte[] header;
            if (length < 126)
            {
                header = new byte[] { (byte)(0x80 | opcode), (byte)length };
            }
            else if (length < 65536)
            {
                header = new byte[] { (byte)(0x80 | opcode), 126, (byte)(length >> 8), (byte)(length & 0xFF) };
            }
            else
            {
                header = new byte[10];
                header[0] = (byte)(0x80 | opcode);
                header[1] = 127;
                long len64 = length;
                for (int i = 0; i < 8; i++)
                {
                    header[2 + i] = (byte)((len64 >> (56 - i * 8)) & 0xFF);
                }
            }

            stream.Write(header, 0, header.Length);
            stream.Write(data, offset, length);
            stream.Flush();
        }

        private sealed class GdiBgraCapturer : IDisposable
        {
            private readonly Func<Rectangle> _boundsProvider;
            private readonly int _width;
            private readonly int _height;
            private Bitmap _bitmap;
            private Graphics _graphics;
            private System.Drawing.Imaging.BitmapData _locked;

            public GdiBgraCapturer(Func<Rectangle> boundsProvider, int width, int height)
            {
                _boundsProvider = boundsProvider;
                _width = width;
                _height = height;
                _bitmap = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                _graphics = Graphics.FromImage(_bitmap);
            }

            public bool TryCaptureLocked(out System.Drawing.Imaging.BitmapData data)
            {
                data = null;
                var bounds = _boundsProvider != null ? _boundsProvider() : SystemInformation.VirtualScreen;
                if (bounds.Width <= 0 || bounds.Height <= 0) bounds = SystemInformation.VirtualScreen;

                var hSrc = GetDC(IntPtr.Zero);
                var hDst = _graphics.GetHdc();
                try
                {
                    if (bounds.Width == _width && bounds.Height == _height)
                    {
                        BitBlt(hDst, 0, 0, _width, _height, hSrc, bounds.Left, bounds.Top, 0x00CC0020);
                    }
                    else
                    {
                        SetStretchBltMode(hDst, 3);
                        StretchBlt(hDst, 0, 0, _width, _height, hSrc, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x00CC0020);
                    }
                    DrawCursor(hDst, bounds, _width, _height);
                }
                finally
                {
                    _graphics.ReleaseHdc(hDst);
                    ReleaseDC(IntPtr.Zero, hSrc);
                }

                _locked = _bitmap.LockBits(new Rectangle(0, 0, _width, _height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                data = _locked;
                return true;
            }

            private static void DrawCursor(IntPtr targetDc, Rectangle bounds, int targetW, int targetH)
            {
                var ci = new CURSORINFO();
                ci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CURSORINFO));
                if (!GetCursorInfo(ref ci) || ci.flags != 0x00000001 || ci.hCursor == IntPtr.Zero) return;

                ICONINFO ii;
                if (!GetIconInfo(ci.hCursor, out ii)) return;

                try
                {
                    int srcX = ci.ptScreenPos.x - ii.xHotspot - bounds.Left;
                    int srcY = ci.ptScreenPos.y - ii.yHotspot - bounds.Top;
                    int destX = (int)((long)srcX * targetW / bounds.Width);
                    int destY = (int)((long)srcY * targetH / bounds.Height);
                    DrawIconEx(targetDc, destX, destY, ci.hCursor, 0, 0, 0, IntPtr.Zero, 0x0003);
                }
                finally
                {
                    if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                }
            }

            [System.Runtime.InteropServices.DllImport("gdi32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int dwRop);

            [System.Runtime.InteropServices.DllImport("gdi32.dll")]
            private static extern int SetStretchBltMode(IntPtr hdc, int mode);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct POINT { public int x; public int y; }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct CURSORINFO
            {
                public int cbSize;
                public int flags;
                public IntPtr hCursor;
                public POINT ptScreenPos;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct ICONINFO
            {
                public bool fIcon;
                public int xHotspot;
                public int yHotspot;
                public IntPtr hbmMask;
                public IntPtr hbmColor;
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool GetCursorInfo(ref CURSORINFO pci);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

            [System.Runtime.InteropServices.DllImport("gdi32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool DeleteObject(IntPtr hObject);

            public void UnlockCaptured()
            {
                if (_locked != null)
                {
                    _bitmap.UnlockBits(_locked);
                    _locked = null;
                }
            }

            public void Dispose()
            {
                if (_locked != null) { try { _bitmap.UnlockBits(_locked); } catch { } _locked = null; }
                if (_graphics != null) { _graphics.Dispose(); _graphics = null; }
                if (_bitmap != null) { _bitmap.Dispose(); _bitmap = null; }
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr GetDC(IntPtr hWnd);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

            [System.Runtime.InteropServices.DllImport("gdi32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
        }
    }
}
