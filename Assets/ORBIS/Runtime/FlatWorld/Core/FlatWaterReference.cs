using System;
using System.IO;
using System.Threading;

namespace Orbis.Flat
{
    // Optional authoring input. The simulated water array remains authoritative
    // in saved games. The original bed determines whether a sub-grid passage
    // still exists after a terrain edit.
    public sealed class FlatWaterReference
    {
        internal const short Dry = short.MinValue, Marine = Dry + 1, Passage = Dry + 2;
        readonly int width;
        readonly byte[] northFirstBed;
        readonly short[] survey;

        FlatWaterReference(int width, byte[] originalBed)
        {
            this.width = width;
            northFirstBed = (byte[])originalBed.Clone();
            survey = new short[width * (width / 2)];
        }

        int Source(int i) { return (width / 2 - 1 - i / width) * width + i % width; }
        internal short At(int tile) { return survey[Source(tile)]; }
        internal short OriginalBed(int tile)
        {
            int p = Source(tile) * 2;
            return unchecked((short)(northFirstBed[p] | northFirstBed[p + 1] << 8));
        }
        internal bool HasSeaway(int tile, short bed)
        {
            short source = At(tile);
            return (source == Marine || source == Passage) && OriginalBed(tile) == bed;
        }

        public static FlatWaterReference Read(int width, byte[] originalNorthFirstHeights, byte[] bytes)
        {
            if (width < 64 || width > WorldData.MaxWidth || width % 8 != 0 ||
                originalNorthFirstHeights == null || originalNorthFirstHeights.Length != (long)width * width ||
                bytes == null || bytes.Length < 12)
                throw new InvalidDataException("Geographic water reference dimensions or data missing.");
            var result = new FlatWaterReference(width, originalNorthFirstHeights);
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadUInt32() != 0x3152574f || reader.ReadInt32() != width)
                    throw new InvalidDataException("Geographic water reference signature or width mismatch.");
                int runs = reader.ReadInt32(), p = 0;
                if (runs <= 0 || 12L + 6L * runs != bytes.Length)
                    throw new InvalidDataException("Invalid geographic water span count.");
                for (int r = 0; r < runs; r++)
                {
                    short value = reader.ReadInt16();
                    int length = reader.ReadInt32();
                    if (length < 1 || length > result.survey.Length - p ||
                        value > HeightBands.MaximumBed || (value < HeightBands.MinimumBed && value > Passage))
                        throw new InvalidDataException("Invalid geographic water span.");
                    for (int end = p + length; p < end; p++) result.survey[p] = value;
                }
                if (p != result.survey.Length) throw new InvalidDataException("Incomplete geographic water reference.");
            }
            return result;
        }
    }

    internal static class FlatOcean
    {
        internal static bool[] Find(WorldData w, CancellationToken cancel)
        {
            int count = w.Count;
            var seen = new bool[count];
            var queue = new int[count];
            var ocean = new bool[count];
            var rowArea = new double[w.Height];
            for (int row = 0; row < w.Height; row++) rowArea[row] = FlatRiverDischarge.RelativeArea(row, w.Height);
            var reference = w.WaterReference;
            double bestArea = 0;
            int best = -1;
            bool CanPass(int i)
            {
                short bed = w.Elevation[i];
                if (reference == null) return bed < 0;
                return reference.HasSeaway(i, bed) || (bed < 0 && bed != reference.OriginalBed(i));
            }
            int Flood(int start)
            {
                int tail = 1;
                queue[0] = start;
                seen[start] = true;
                for (int head = 0; head < tail; head++)
                {
                    if ((head & 4095) == 0) cancel.ThrowIfCancellationRequested();
                    int cell = queue[head];
                    for (int d = 0; d < 4; d++)
                    {
                        int n = w.SphereNeighbor(cell, d);
                        if (seen[n] || !CanPass(n)) continue;
                        seen[n] = true;
                        queue[tail++] = n;
                    }
                }
                return tail;
            }
            for (int i = 0; i < count; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (seen[i] || !CanPass(i)) continue;
                int length = Flood(i);
                double area = 0;
                bool submerged = false, sourced = false;
                for (int k = 0; k < length; k++)
                {
                    int cell = queue[k];
                    if (w.Elevation[cell] > 0) continue;
                    area += rowArea[cell / w.Width];
                    submerged |= w.Elevation[cell] < 0;
                    sourced |= reference != null && reference.At(cell) == FlatWaterReference.Marine;
                }
                // An unlabelled height grid cannot distinguish a small inland
                // hollow from a small ocean. Use the largest marine-scale body
                // (1/32 of the spherical surface) unless marine provenance exists.
                if (submerged && (reference != null ? sourced : area >= count / 32.0) && area > bestArea)
                { best = i; bestArea = area; }
            }
            if (best < 0) return ocean;
            Array.Clear(seen, 0, count);
            int size = Flood(best);
            for (int k = 0; k < size; k++)
            {
                int i = queue[k];
                ocean[i] = w.Elevation[i] <= 0 &&
                    (reference == null || reference.At(i) == FlatWaterReference.Marine ||
                     w.Elevation[i] != reference.OriginalBed(i));
            }
            if (reference != null)
            {
                // A deliberate breach admits seawater into the original dry
                // basin behind it. Start at the cut, so an unrelated edit
                // cannot erase a surveyed levee elsewhere on the coast.
                int tail = 0;
                for (int i = 0; i < count; i++)
                    if (ocean[i] && w.Elevation[i] < 0 && reference.OriginalBed(i) >= 0)
                        queue[tail++] = i;
                for (int head = 0; head < tail; head++)
                {
                    if ((head & 4095) == 0) cancel.ThrowIfCancellationRequested();
                    for (int d = 0; d < 4; d++)
                    {
                        int n = w.SphereNeighbor(queue[head], d);
                        if (ocean[n] || w.Elevation[n] >= 0) continue;
                        ocean[n] = true;
                        queue[tail++] = n;
                    }
                }
            }
            return ocean;
        }
    }
}
