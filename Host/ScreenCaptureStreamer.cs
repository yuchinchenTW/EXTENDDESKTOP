using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal static class ScreenCaptureStreamer
    {
        private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        public static void StreamFrames(NetworkStream stream, object writeSync, CancellationToken token, int fps, Func<Rectangle> captureBoundsProvider)
        {
            var pipe = new EncodedFramePipe();
            var senderThread = new Thread(delegate()
            {
                SenderLoop(pipe, stream, writeSync, token);
            });
            senderThread.IsBackground = true;
            senderThread.Name = "FrameSender";

            try
            {
                senderThread.Start();

                using (var capturer = new GdiCaptureSession(1920, 82L, captureBoundsProvider))
                {
                    var targetFrameTicks = (long)(System.Diagnostics.Stopwatch.Frequency / (double)Math.Max(1, fps));
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var nextFrameTicks = watch.ElapsedTicks;

                    while (!token.IsCancellationRequested)
                    {
                        EncodedFrame encoded;
                        if (capturer.TryCapture(out encoded))
                        {
                            pipe.Submit(encoded);
                        }

                        nextFrameTicks += targetFrameTicks;
                        var remainingTicks = nextFrameTicks - watch.ElapsedTicks;
                        if (remainingTicks > 0)
                        {
                            var remainingMs = (int)(remainingTicks * 1000L / System.Diagnostics.Stopwatch.Frequency);
                            if (remainingMs > 0 && token.WaitHandle.WaitOne(remainingMs))
                            {
                                return;
                            }
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
                pipe.Complete();
                try
                {
                    senderThread.Join(1000);
                }
                catch
                {
                }
            }
        }

        private static void SenderLoop(EncodedFramePipe pipe, NetworkStream stream, object writeSync, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                EncodedFrame frame;
                if (!pipe.TakeNext(out frame))
                {
                    return;
                }

                try
                {
                    var localFrame = frame;
                    Protocol.SendMessage(stream, writeSync, MessageType.Frame, delegate(BinaryWriter writer)
                    {
                        writer.Write(localFrame.Width);
                        writer.Write(localFrame.Height);
                        writer.Write(localFrame.Length);
                        writer.Write(localFrame.Buffer, 0, localFrame.Length);
                    });
                }
                catch
                {
                    return;
                }
            }
        }

        private sealed class EncodedFrame
        {
            public int Width;
            public int Height;
            public byte[] Buffer;
            public int Length;
        }

        private sealed class EncodedFramePipe
        {
            private readonly object _sync = new object();
            private readonly AutoResetEvent _available = new AutoResetEvent(false);
            private EncodedFrame _pending;
            private volatile bool _completed;

            public void Submit(EncodedFrame frame)
            {
                if (_completed)
                {
                    return;
                }

                lock (_sync)
                {
                    _pending = frame;
                }

                _available.Set();
            }

            public bool TakeNext(out EncodedFrame frame)
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

        private sealed class GdiCaptureSession : IDisposable
        {
            private readonly int _maxDimension;
            private readonly EncoderParameters _encoderParameters;
            private readonly Func<Rectangle> _captureBoundsProvider;

            private Rectangle _sourceBounds;
            private Bitmap _captureBitmap;
            private Graphics _captureGraphics;
            private Bitmap _scaledBitmap;
            private Graphics _scaledGraphics;
            private MemoryStream _jpegStream;

            public GdiCaptureSession(int maxDimension, long jpegQuality, Func<Rectangle> captureBoundsProvider)
            {
                _maxDimension = maxDimension;
                _captureBoundsProvider = captureBoundsProvider;
                _encoderParameters = new EncoderParameters(1);
                _encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);
            }

            public bool TryCapture(out EncodedFrame frame)
            {
                var bounds = _captureBoundsProvider != null ? _captureBoundsProvider() : SystemInformation.VirtualScreen;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    bounds = SystemInformation.VirtualScreen;
                }

                EnsureBuffers(bounds);
                CaptureDesktop(bounds);

                var imageToEncode = _scaledBitmap ?? _captureBitmap;
                _jpegStream.SetLength(0);

                if (JpegCodec != null)
                {
                    imageToEncode.Save(_jpegStream, JpegCodec, _encoderParameters);
                }
                else
                {
                    imageToEncode.Save(_jpegStream, ImageFormat.Jpeg);
                }

                var length = (int)_jpegStream.Length;
                var snapshot = new byte[length];
                Buffer.BlockCopy(_jpegStream.GetBuffer(), 0, snapshot, 0, length);

                frame = new EncodedFrame
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Buffer = snapshot,
                    Length = length
                };
                return true;
            }

            public void Dispose()
            {
                _encoderParameters.Dispose();

                if (_scaledGraphics != null)
                {
                    _scaledGraphics.Dispose();
                }

                if (_scaledBitmap != null)
                {
                    _scaledBitmap.Dispose();
                }

                if (_captureGraphics != null)
                {
                    _captureGraphics.Dispose();
                }

                if (_captureBitmap != null)
                {
                    _captureBitmap.Dispose();
                }

                if (_jpegStream != null)
                {
                    _jpegStream.Dispose();
                }
            }

            private void EnsureBuffers(Rectangle bounds)
            {
                if (_captureBitmap != null && bounds == _sourceBounds)
                {
                    return;
                }

                DisposeBuffers();
                _sourceBounds = bounds;

                _captureBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
                _captureGraphics = Graphics.FromImage(_captureBitmap);

                var scale = Math.Min(1.0, Math.Min((double)_maxDimension / bounds.Width, (double)_maxDimension / bounds.Height));
                if (scale < 0.999)
                {
                    var scaledWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
                    var scaledHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
                    _scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format24bppRgb);
                    _scaledGraphics = Graphics.FromImage(_scaledBitmap);
                    _scaledGraphics.CompositingMode = CompositingMode.SourceCopy;
                    _scaledGraphics.CompositingQuality = CompositingQuality.HighQuality;
                    _scaledGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    _scaledGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    _scaledGraphics.SmoothingMode = SmoothingMode.None;
                }

                _jpegStream = new MemoryStream(Math.Max(1024, bounds.Width * bounds.Height / 4));
            }

            private void DisposeBuffers()
            {
                if (_scaledGraphics != null)
                {
                    _scaledGraphics.Dispose();
                    _scaledGraphics = null;
                }

                if (_scaledBitmap != null)
                {
                    _scaledBitmap.Dispose();
                    _scaledBitmap = null;
                }

                if (_captureGraphics != null)
                {
                    _captureGraphics.Dispose();
                    _captureGraphics = null;
                }

                if (_captureBitmap != null)
                {
                    _captureBitmap.Dispose();
                    _captureBitmap = null;
                }

                if (_jpegStream != null)
                {
                    _jpegStream.Dispose();
                    _jpegStream = null;
                }
            }

            private void CaptureDesktop(Rectangle bounds)
            {
                var screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to access screen device context.");
                }

                var targetDc = IntPtr.Zero;

                try
                {
                    targetDc = _captureGraphics.GetHdc();
                    if (!BitBlt(targetDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.Left, bounds.Top, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt))
                    {
                        throw new InvalidOperationException("BitBlt screen capture failed.");
                    }

                    DrawCursor(targetDc, bounds);
                }
                finally
                {
                    if (targetDc != IntPtr.Zero)
                    {
                        _captureGraphics.ReleaseHdc(targetDc);
                    }

                    ReleaseDC(IntPtr.Zero, screenDc);
                }

                if (_scaledGraphics != null)
                {
                    _scaledGraphics.DrawImage(_captureBitmap, new Rectangle(Point.Empty, _scaledBitmap.Size));
                }
            }

            private static void DrawCursor(IntPtr targetDc, Rectangle bounds)
            {
                var ci = new CURSORINFO();
                ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));

                if (!GetCursorInfo(ref ci) || ci.flags != CURSOR_SHOWING || ci.hCursor == IntPtr.Zero)
                {
                    return;
                }

                ICONINFO ii;
                if (!GetIconInfo(ci.hCursor, out ii))
                {
                    return;
                }

                try
                {
                    var x = ci.ptScreenPos.x - ii.xHotspot - bounds.Left;
                    var y = ci.ptScreenPos.y - ii.yHotspot - bounds.Top;
                    DrawIconEx(targetDc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                }
                finally
                {
                    if (ii.hbmMask != IntPtr.Zero)
                    {
                        DeleteObject(ii.hbmMask);
                    }

                    if (ii.hbmColor != IntPtr.Zero)
                    {
                        DeleteObject(ii.hbmColor);
                    }
                }
            }

            private const int CURSOR_SHOWING = 0x00000001;
            private const int DI_NORMAL = 0x0003;

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT
            {
                public int x;
                public int y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct CURSORINFO
            {
                public int cbSize;
                public int flags;
                public IntPtr hCursor;
                public POINT ptScreenPos;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct ICONINFO
            {
                public bool fIcon;
                public int xHotspot;
                public int yHotspot;
                public IntPtr hbmMask;
                public IntPtr hbmColor;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetCursorInfo(ref CURSORINFO pci);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool DeleteObject(IntPtr hObject);

            [DllImport("user32.dll")]
            private static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

            [DllImport("gdi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool BitBlt(
                IntPtr hdcDest,
                int nXDest,
                int nYDest,
                int nWidth,
                int nHeight,
                IntPtr hdcSrc,
                int nXSrc,
                int nYSrc,
                CopyPixelOperation dwRop);
        }
    }
}
