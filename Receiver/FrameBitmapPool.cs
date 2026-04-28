using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace ExtentDesktop.Receiver
{
    internal sealed class FrameBitmapPool : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Stack<Bitmap> _available = new Stack<Bitmap>();
        private int _width;
        private int _height;
        private const int Capacity = 4;
        private bool _disposed;

        public Bitmap Take(int width, int height)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return new Bitmap(width, height, PixelFormat.Format32bppRgb);
                }

                if (width != _width || height != _height)
                {
                    while (_available.Count > 0)
                    {
                        _available.Pop().Dispose();
                    }
                    _width = width;
                    _height = height;
                }

                if (_available.Count > 0)
                {
                    return _available.Pop();
                }
            }

            return new Bitmap(width, height, PixelFormat.Format32bppRgb);
        }

        public void Return(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return;
            }

            lock (_sync)
            {
                if (!_disposed && bitmap.Width == _width && bitmap.Height == _height && _available.Count < Capacity)
                {
                    _available.Push(bitmap);
                    return;
                }
            }

            bitmap.Dispose();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                while (_available.Count > 0)
                {
                    _available.Pop().Dispose();
                }
            }
        }
    }
}
