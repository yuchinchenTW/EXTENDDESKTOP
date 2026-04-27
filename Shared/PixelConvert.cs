using System;

namespace ExtentDesktop.Shared
{
    internal static class PixelConvert
    {
        public static int Nv12Size(int width, int height)
        {
            return width * height + (width * height) / 2;
        }

        public static unsafe void Bgra32ToNv12(IntPtr bgra, int bgraStride, IntPtr nv12, int width, int height)
        {
            byte* src = (byte*)bgra;
            byte* yPlane = (byte*)nv12;
            byte* uvPlane = yPlane + width * height;

            for (int y = 0; y < height; y++)
            {
                byte* row = src + y * bgraStride;
                byte* yRow = yPlane + y * width;

                for (int x = 0; x < width; x++)
                {
                    byte b = row[(x << 2)];
                    byte g = row[(x << 2) + 1];
                    byte r = row[(x << 2) + 2];

                    int yVal = (66 * r + 129 * g + 25 * b + 128) >> 8;
                    yRow[x] = (byte)(yVal + 16);
                }
            }

            int chromaWidth = width / 2;
            int chromaHeight = height / 2;
            for (int cy = 0; cy < chromaHeight; cy++)
            {
                byte* row0 = src + (cy * 2) * bgraStride;
                byte* row1 = src + (cy * 2 + 1) * bgraStride;
                byte* uvRow = uvPlane + cy * width;

                for (int cx = 0; cx < chromaWidth; cx++)
                {
                    int xb = cx * 2 * 4;

                    int b = row0[xb] + row0[xb + 4] + row1[xb] + row1[xb + 4];
                    int g = row0[xb + 1] + row0[xb + 5] + row1[xb + 1] + row1[xb + 5];
                    int r = row0[xb + 2] + row0[xb + 6] + row1[xb + 2] + row1[xb + 6];

                    b >>= 2;
                    g >>= 2;
                    r >>= 2;

                    int u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                    int v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

                    if (u < 0) u = 0; else if (u > 255) u = 255;
                    if (v < 0) v = 0; else if (v > 255) v = 255;

                    uvRow[cx * 2] = (byte)u;
                    uvRow[cx * 2 + 1] = (byte)v;
                }
            }
        }

        public static unsafe void Nv12ToBgra32(IntPtr nv12, int nv12Stride, IntPtr bgra, int bgraStride, int width, int height)
        {
            byte* yPlane = (byte*)nv12;
            byte* uvPlane = yPlane + nv12Stride * height;
            byte* dst = (byte*)bgra;

            for (int y = 0; y < height; y++)
            {
                byte* yRow = yPlane + y * nv12Stride;
                byte* uvRow = uvPlane + (y >> 1) * nv12Stride;
                byte* dstRow = dst + y * bgraStride;

                for (int x = 0; x < width; x++)
                {
                    int yVal = yRow[x] - 16;
                    int u = uvRow[(x & ~1)] - 128;
                    int v = uvRow[(x & ~1) + 1] - 128;

                    int c = 298 * yVal;
                    int rOut = (c + 409 * v + 128) >> 8;
                    int gOut = (c - 100 * u - 208 * v + 128) >> 8;
                    int bOut = (c + 516 * u + 128) >> 8;

                    if (rOut < 0) rOut = 0; else if (rOut > 255) rOut = 255;
                    if (gOut < 0) gOut = 0; else if (gOut > 255) gOut = 255;
                    if (bOut < 0) bOut = 0; else if (bOut > 255) bOut = 255;

                    int xb = x << 2;
                    dstRow[xb] = (byte)bOut;
                    dstRow[xb + 1] = (byte)gOut;
                    dstRow[xb + 2] = (byte)rOut;
                    dstRow[xb + 3] = 255;
                }
            }
        }
    }
}
