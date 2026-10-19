using System.Security.Cryptography;

/// <summary>Reproducibly bakes only the periodic perturbation fields, independently of weather and rendering.</summary>
internal static class Program
{
    #region Public API
    /// <summary>Writes the versioned little-endian interleaved RGBA8 volume and prints its complete asset digest.</summary>
    public static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Specify the output asset path.");
        const int side = 64;
        using (var output = new BinaryWriter(File.Create(args[0])))
        {
            output.Write(0x4E434756u); output.Write(1); output.Write(side); output.Write(4);
            output.Write(0x434C4F55u); output.Write(8); output.Write(1); output.Write(0);
            // All channels occupy the same spatial coordinates; periodic corner wrapping makes
            // hardware interpolation across opposite volume faces part of the declared field.
            for (int z = 0; z < side; z++)
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            for (int octave = 0; octave < 4; octave++)
            {
                int frequency = 8 << octave;
                double value = Value(x * frequency / 64.0, y * frequency / 64.0,
                    z * frequency / 64.0, frequency, 0x434C4F55u + (uint)octave);
                output.Write((byte)Math.Clamp((int)Math.Floor(value * 255 + .5), 0, 255));
            }
        }
        Console.WriteLine(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))));
    }
    #endregion

    #region Private
    /// <summary>Interpolates a wrapped lattice using deterministic double arithmetic before UNORM8 rounding.</summary>
    private static double Value(double x, double y, double z, int period, uint seed)
    {
        int ix = (int)x, iy = (int)y, iz = (int)z;
        double fx = x - ix, fy = y - iy, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        double value = 0;
        for (int dz = 0; dz < 2; dz++)
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
            value += Hash((ix + dx) % period, (iy + dy) % period, (iz + dz) % period, seed)
                * (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy) * (dz == 0 ? 1 - fz : fz);
        return value;
    }
    /// <summary>Maps integer lattice coordinates and an independent octave seed to a bounded exact 24-bit value.</summary>
    private static double Hash(int x, int y, int z, uint seed)
    {
        unchecked
        {
            uint h = seed ^ (uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu ^ (uint)z * 0xC2B2AE35u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h >> 8) / 16777215.0;
        }
    }
    #endregion
}
