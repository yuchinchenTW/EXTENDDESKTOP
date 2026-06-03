using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ExtentDesktop.Shared;

namespace ExtentDesktop.Host
{
    internal static class ScreenCaptureStreamer
    {
        public static void StreamFrames(NetworkStream stream, object writeSync, CancellationToken token, int fps, Func<Rectangle> captureBoundsProvider, int maxDimension)
        {
            var bounds = ResolveStartBounds(captureBoundsProvider);
            int encodedWidth;
            int encodedHeight;
            ResolveEncodedSize(bounds, maxDimension, out encodedWidth, out encodedHeight);
            MFHelpers.Log("TCP stream size " + encodedWidth + "x" + encodedHeight + " from source " + bounds.Width + "x" + bounds.Height + " maxDim=" + maxDimension);

            var bitrate = ChooseBitrate(encodedWidth, encodedHeight, fps);

            H264HwEncoder hwEncoder = null;
            H264Encoder swEncoder = null;

            try
            {
                try
                {
                    hwEncoder = new H264HwEncoder(encodedWidth, encodedHeight, fps, bitrate);
                    MFHelpers.Log("Using H.264 HW encoder path");
                }
                catch (Exception ex)
                {
                    MFHelpers.Log("HW encoder init failed, using SW: " + ex.Message);
                    swEncoder = new H264Encoder(encodedWidth, encodedHeight, fps, bitrate);
                }

                using (var capturer = new GdiCaptureSession(captureBoundsProvider, encodedWidth, encodedHeight))
                {
                    var targetFrameTicks = (long)(System.Diagnostics.Stopwatch.Frequency / (double)Math.Max(1, fps));
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var nextFrameTicks = watch.ElapsedTicks;

                    while (!token.IsCancellationRequested)
                    {
                        long t0 = watch.ElapsedTicks;
                        BitmapData locked;
                        long tCap = t0;
                        long tEnc = t0;
                        if (capturer.TryCaptureLocked(out locked))
                        {
                            tCap = watch.ElapsedTicks;
                            try
                            {
                                if (hwEncoder != null)
                                {
                                    hwEncoder.Submit(locked.Scan0, locked.Stride);
                                }
                                else
                                {
                                    swEncoder.Submit(locked.Scan0, locked.Stride);
                                }
                            }
                            finally
                            {
                                capturer.UnlockCaptured();
                            }
                            tEnc = watch.ElapsedTicks;

                            byte[] outputBytes;
                            int outputLen;
                            bool isKeyframe;
                            while (TryDrainEncoderOutput(hwEncoder, swEncoder, out outputBytes, out outputLen, out isKeyframe))
                            {
                                int len = outputLen;
                                byte[] buf = outputBytes;
                                try
                                {
                                    Protocol.SendMessage(stream, writeSync, MessageType.Frame, delegate(BinaryWriter writer)
                                    {
                                        writer.Write(encodedWidth);
                                        writer.Write(encodedHeight);
                                        writer.Write(len);
                                        writer.Write(buf, 0, len);
                                    });
                                }
                                catch
                                {
                                    return;
                                }
                            }

                            long tSend = watch.ElapsedTicks;
                            long totalMs = (tSend - t0) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                            if (totalMs > 50)
                            {
                                long capMs = (tCap - t0) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                                long encMs = (tEnc - tCap) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                                long sendMs = (tSend - tEnc) * 1000L / System.Diagnostics.Stopwatch.Frequency;
                                MFHelpers.Log("SLOW frame total=" + totalMs + "ms cap=" + capMs + " enc=" + encMs + " send=" + sendMs);
                            }
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
                if (hwEncoder != null)
                {
                    try { hwEncoder.Dispose(); } catch { }
                }
                if (swEncoder != null)
                {
                    try { swEncoder.Dispose(); } catch { }
                }
            }
        }

        private static bool TryDrainEncoderOutput(H264HwEncoder hwEncoder, H264Encoder swEncoder, out byte[] buffer, out int length, out bool isKeyframe)
        {
            if (hwEncoder != null)
            {
                return hwEncoder.TryDrainOutput(out buffer, out length, out isKeyframe);
            }

            return swEncoder.TryDrainOutput(out buffer, out length, out isKeyframe);
        }

        private static Rectangle ResolveStartBounds(Func<Rectangle> provider)
        {
            var bounds = provider != null ? provider() : SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                bounds = SystemInformation.VirtualScreen;
            }
            return bounds;
        }

        private static void ResolveEncodedSize(Rectangle bounds, int maxDimension, out int encodedWidth, out int encodedHeight)
        {
            if (maxDimension <= 0 || bounds.Width <= maxDimension && bounds.Height <= maxDimension)
            {
                encodedWidth = bounds.Width & ~1;
                encodedHeight = bounds.Height & ~1;
            }
            else
            {
                double scale = Math.Min((double)maxDimension / bounds.Width, (double)maxDimension / bounds.Height);
                encodedWidth = ((int)Math.Round(bounds.Width * scale)) & ~1;
                encodedHeight = ((int)Math.Round(bounds.Height * scale)) & ~1;
            }

            if (encodedWidth < 2) encodedWidth = 2;
            if (encodedHeight < 2) encodedHeight = 2;
        }

        private static int ChooseBitrate(int width, int height, int fps)
        {
            long pixelsPerSecond = (long)width * height * fps;
            double bppFactor = 0.10;
            int bitrate = (int)(pixelsPerSecond * bppFactor);
            if (bitrate < 1500000) bitrate = 1500000;
            if (bitrate > 25000000) bitrate = 25000000;
            return bitrate;
        }

        private sealed class GdiCaptureSession : IDisposable
        {
            private readonly Func<Rectangle> _captureBoundsProvider;
            private readonly int _captureWidth;
            private readonly int _captureHeight;

            private Bitmap _captureBitmap;
            private Graphics _captureGraphics;
            private BitmapData _lockedData;

            public GdiCaptureSession(Func<Rectangle> captureBoundsProvider, int width, int height)
            {
                _captureBoundsProvider = captureBoundsProvider;
                _captureWidth = width;
                _captureHeight = height;

                _captureBitmap = new Bitmap(_captureWidth, _captureHeight, PixelFormat.Format32bppPArgb);
                _captureGraphics = Graphics.FromImage(_captureBitmap);
            }

            public bool TryCaptureLocked(out BitmapData data)
            {
                data = null;
                var bounds = _captureBoundsProvider != null ? _captureBoundsProvider() : SystemInformation.VirtualScreen;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    bounds = SystemInformation.VirtualScreen;
                }

                CaptureDesktop(bounds);

                _lockedData = _captureBitmap.LockBits(
                    new Rectangle(0, 0, _captureWidth, _captureHeight),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppPArgb);

                data = _lockedData;
                return true;
            }

            public void UnlockCaptured()
            {
                if (_lockedData != null)
                {
                    _captureBitmap.UnlockBits(_lockedData);
                    _lockedData = null;
                }
            }

            public void Dispose()
            {
                if (_lockedData != null)
                {
                    try { _captureBitmap.UnlockBits(_lockedData); } catch { }
                    _lockedData = null;
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
                    if (bounds.Width == _captureWidth && bounds.Height == _captureHeight)
                    {
                        if (!BitBlt(targetDc, 0, 0, _captureWidth, _captureHeight, screenDc, bounds.Left, bounds.Top, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt))
                        {
                            throw new InvalidOperationException("BitBlt screen capture failed.");
                        }
                    }
                    else
                    {
                        SetStretchBltMode(targetDc, COLORONCOLOR);
                        if (!StretchBlt(targetDc, 0, 0, _captureWidth, _captureHeight, screenDc, bounds.Left, bounds.Top, bounds.Width, bounds.Height, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt))
                        {
                            throw new InvalidOperationException("StretchBlt screen capture failed.");
                        }
                    }

                    DrawCursor(targetDc, bounds, _captureWidth, _captureHeight);
                }
                finally
                {
                    if (targetDc != IntPtr.Zero)
                    {
                        _captureGraphics.ReleaseHdc(targetDc);
                    }

                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            private static void DrawCursor(IntPtr targetDc, Rectangle bounds, int targetWidth, int targetHeight)
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
                    var sourceX = ci.ptScreenPos.x - ii.xHotspot - bounds.Left;
                    var sourceY = ci.ptScreenPos.y - ii.yHotspot - bounds.Top;
                    var x = (int)((long)sourceX * targetWidth / bounds.Width);
                    var y = (int)((long)sourceY * targetHeight / bounds.Height);
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
            private const int COLORONCOLOR = 3;

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

            [DllImport("gdi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool StretchBlt(
                IntPtr hdcDest,
                int nXDest,
                int nYDest,
                int nWidth,
                int nHeight,
                IntPtr hdcSrc,
                int nXSrc,
                int nYSrc,
                int nSrcWidth,
                int nSrcHeight,
                CopyPixelOperation dwRop);

            [DllImport("gdi32.dll")]
            private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        }
    }
}
