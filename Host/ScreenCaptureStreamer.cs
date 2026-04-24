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
            using (var capturer = new GdiCaptureSession(3840, 58L, captureBoundsProvider))
            {
                var frameDelay = Math.Max(30, 1000 / Math.Max(1, fps));

                while (!token.IsCancellationRequested)
                {
                    CapturedFrame frame;
                    if (capturer.TryCapture(out frame))
                    {
                        Protocol.SendMessage(stream, writeSync, MessageType.Frame, delegate(BinaryWriter writer)
                        {
                            writer.Write(frame.SourceWidth);
                            writer.Write(frame.SourceHeight);
                            writer.Write(frame.JpegBytes.Length);
                            writer.Write(frame.JpegBytes);
                        });
                    }

                    if (token.WaitHandle.WaitOne(frameDelay))
                    {
                        return;
                    }
                }
            }
        }

        private sealed class CapturedFrame
        {
            public int SourceWidth;
            public int SourceHeight;
            public byte[] JpegBytes;
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

            public bool TryCapture(out CapturedFrame frame)
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

                frame = new CapturedFrame
                {
                    SourceWidth = bounds.Width,
                    SourceHeight = bounds.Height,
                    JpegBytes = _jpegStream.ToArray()
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
                    _scaledGraphics.CompositingQuality = CompositingQuality.HighSpeed;
                    _scaledGraphics.InterpolationMode = InterpolationMode.Low;
                    _scaledGraphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
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
