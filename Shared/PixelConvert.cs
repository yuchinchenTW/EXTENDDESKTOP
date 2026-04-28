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
            int rowDwords = bgraStride >> 2;

            for (int y = 0; y < height; y++)
            {
                uint* row = (uint*)src + y * rowDwords;
                byte* yRow = yPlane + y * width;

                for (int x = 0; x < width; x++)
                {
                    uint p = row[x];
                    int b = (int)(p & 0xFF);
                    int g = (int)((p >> 8) & 0xFF);
                    int r = (int)((p >> 16) & 0xFF);

                    int yVal = (66 * r + 129 * g + 25 * b + 128) >> 8;
                    yRow[x] = (byte)(yVal + 16);
                }
            }

            int chromaWidth = width >> 1;
            int chromaHeight = height >> 1;
            for (int cy = 0; cy < chromaHeight; cy++)
            {
                uint* row0 = (uint*)src + (cy * 2) * rowDwords;
                uint* row1 = (uint*)src + (cy * 2 + 1) * rowDwords;
                byte* uvRow = uvPlane + cy * width;

                for (int cx = 0; cx < chromaWidth; cx++)
                {
                    int xp = cx * 2;
                    uint p00 = row0[xp];
                    uint p01 = row0[xp + 1];
                    uint p10 = row1[xp];
                    uint p11 = row1[xp + 1];

                    int b = (int)((p00 & 0xFF) + (p01 & 0xFF) + (p10 & 0xFF) + (p11 & 0xFF));
                    int g = (int)(((p00 >> 8) & 0xFF) + ((p01 >> 8) & 0xFF) + ((p10 >> 8) & 0xFF) + ((p11 >> 8) & 0xFF));
                    int r = (int)(((p00 >> 16) & 0xFF) + ((p01 >> 16) & 0xFF) + ((p10 >> 16) & 0xFF) + ((p11 >> 16) & 0xFF));

                    b >>= 2; g >>= 2; r >>= 2;

                    int u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                    int v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

                    if (u < 0) u = 0; else if (u > 255) u = 255;
                    if (v < 0) v = 0; else if (v > 255) v = 255;

                    uvRow[xp] = (byte)u;
                    uvRow[xp + 1] = (byte)v;
                }
            }
        }

        public static unsafe void Nv12ToBgra32(IntPtr nv12, int nv12Stride, IntPtr bgra, int bgraStride, int width, int height)
        {
            byte* yPlane = (byte*)nv12;
            byte* uvPlane = yPlane + nv12Stride * height;
            byte* dst = (byte*)bgra;
            int dstDwordStride = bgraStride >> 2;

            for (int y = 0; y < height; y++)
            {
                byte* yRow = yPlane + y * nv12Stride;
                byte* uvRow = uvPlane + (y >> 1) * nv12Stride;
                uint* dstRow = (uint*)dst + y * dstDwordStride;

                for (int x = 0; x < width; x += 2)
                {
                    int u = uvRow[x] - 128;
                    int v = uvRow[x + 1] - 128;
                    int uG = -100 * u - 208 * v + 128;
                    int uB = 516 * u + 128;
                    int uR = 409 * v + 128;

                    int y0 = (yRow[x] - 16) * 298;
                    int b0 = (y0 + uB) >> 8;
                    int g0 = (y0 + uG) >> 8;
                    int r0 = (y0 + uR) >> 8;
                    if (b0 < 0) b0 = 0; else if (b0 > 255) b0 = 255;
                    if (g0 < 0) g0 = 0; else if (g0 > 255) g0 = 255;
                    if (r0 < 0) r0 = 0; else if (r0 > 255) r0 = 255;
                    dstRow[x] = 0xFF000000u | ((uint)r0 << 16) | ((uint)g0 << 8) | (uint)b0;

                    int y1 = (yRow[x + 1] - 16) * 298;
                    int b1 = (y1 + uB) >> 8;
                    int g1 = (y1 + uG) >> 8;
                    int r1 = (y1 + uR) >> 8;
                    if (b1 < 0) b1 = 0; else if (b1 > 255) b1 = 255;
                    if (g1 < 0) g1 = 0; else if (g1 > 255) g1 = 255;
                    if (r1 < 0) r1 = 0; else if (r1 > 255) r1 = 255;
                    dstRow[x + 1] = 0xFF000000u | ((uint)r1 << 16) | ((uint)g1 << 8) | (uint)b1;
                }
            }
        }
    }
}
