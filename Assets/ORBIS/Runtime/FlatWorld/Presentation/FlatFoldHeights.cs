using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Flat
{
    // The folding disk moves over the globe. Its vertices cannot resolve individual DEM
    // cells on large worlds. Reconstruct the elevation they *can* resolve in geographic
    // space once per published terrain revision. The close chart still uses the native DEM.
    internal sealed class FlatFoldHeights : IDisposable
    {
        internal const int Columns = 256;
        const int Rows = Columns / 2, Subdivisions = 4;
        const int Width = Columns * Subdivisions, Height = Rows * Subdivisions;
        readonly float[] metres = new float[Width * Height];
        internal Texture2D Texture { get; private set; }

        internal void Rebuild(WorldData world)
        {
            var knots = new float[Columns * Rows];
            var horizontal = new float[Width * Rows];
            var workers = FlatWorkerPolicy.Options(default);
            Parallel.For(0, Rows, workers, row =>
            {
                int south = row * world.Height / Rows, north = (row + 1) * world.Height / Rows;
                for (int column = 0; column < Columns; column++)
                {
                    int west = column * world.Width / Columns, east = (column + 1) * world.Width / Columns;
                    double sum = 0;
                    for (int y = south; y < north; y++)
                        for (int x = west; x < east; x++)
                            sum += FlatGlobeGeometry.SurfaceHeightAt(world, y * world.Width + x);
                    knots[row * Columns + column] = (float)(sum / ((north - south) * (east - west)));
                }
                ReconstructLine(knots, row * Columns, 1, Columns, true, horizontal, row * Width, 1);
            });
            Parallel.For(0, Width, workers, x =>
                ReconstructLine(horizontal, x, Width, Rows, false, metres, x, Width));

            if (!Texture)
            {
                var format = SystemInfo.SupportsTextureFormat(TextureFormat.RFloat) ? TextureFormat.RFloat : TextureFormat.RGBAHalf;
                Texture = new Texture2D(Width, Height, format, false, true) {
                    name = "Geographic fold elevations", filterMode = FilterMode.Bilinear,
                    wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
                };
            }
            if (Texture.format == TextureFormat.RFloat) Texture.SetPixelData(metres, 0);
            else
            {
                var pixels = new Color[metres.Length];
                for (int i = 0; i < pixels.Length; i++) pixels[i].r = metres[i];
                Texture.SetPixels(pixels);
            }
            Texture.Apply(false, false);
        }

        // A cubic B-spline has continuous slopes across the fixed sample cells. Baking
        // its two separable passes leaves a single bilinear lookup per moving vertex.
        static void ReconstructLine(float[] source, int start, int stride, int count, bool wrap,
            float[] destination, int offset, int step)
        {
            int Index(int i) => start + stride * (wrap ? FMath.Wrap(i, count) : Math.Max(0, Math.Min(i, count - 1)));
            for (int i = 0; i < count * Subdivisions; i++)
            {
                float position = (i + .5f) / Subdivisions - .5f;
                int j = (int)Math.Floor(position);
                float t = position - j, s = 1 - t;
                float a = s * s * s, d = t * t * t;
                float b = 4 - 6 * t * t + 3 * d, c = 6 - a - b - d;
                destination[offset + i * step] = (a * source[Index(j - 1)] + b * source[Index(j)]
                    + c * source[Index(j + 1)] + d * source[Index(j + 2)]) / 6;
            }
        }

        internal float Sample(Vector2 uv)
        {
            float x = uv.x * Width - .5f, y = uv.y * Height - .5f;
            int col = Mathf.FloorToInt(x), row = Mathf.FloorToInt(y);
            int west = FMath.Wrap(col, Width), east = (west + 1) % Width;
            int south = Mathf.Clamp(row, 0, Height - 1) * Width, north = Mathf.Clamp(row + 1, 0, Height - 1) * Width;
            return Mathf.Lerp(Mathf.Lerp(metres[south + west], metres[south + east], x - col),
                Mathf.Lerp(metres[north + west], metres[north + east], x - col), y - row);
        }

        public void Dispose() => FlatMapRenderer.ReleaseTexture(Texture);
    }
}
