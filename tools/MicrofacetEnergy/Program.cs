/// <summary>Generates the immutable GGX directional-energy asset independently of runtime shader execution.</summary>
internal static class Program
{
    #region Public API
    /// <summary>Writes a versioned little-endian table; all integration is offline, never in the render loop.</summary>
    public static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Specify the output asset path.");
        const int size = 128, samples = 16384;
        var entries = new (float E, float Ef)[size * size];
        Parallel.For(0, size, y =>
        {
            // Warped axes resolve smooth materials and the tangent boundary without extra storage.
            double nv = Math.Pow((double)y / (size - 1), 4);
            for (int x = 0; x < size; x++)
            {
                double roughness = .04 + .96 * Math.Pow((double)x / (size - 1), 2);
                var energy = Integrate(roughness, nv, samples);
                // Reject a changed/invalid integration domain instead of hiding it behind the runtime denominator guard.
                if (!double.IsFinite(energy.E) || !double.IsFinite(energy.Ef)
                    || energy.E < .25 || energy.E > 1 + 1e-12 || energy.Ef < 0 || energy.Ef > energy.E + 1e-12)
                    throw new InvalidDataException($"Invalid energy integral at material/view node {x},{y}.");
                entries[y * size + x] = ((float)Math.Min(energy.E, 1),
                    (float)Math.Clamp(energy.Ef, 0, Math.Min(energy.E, 1)));
            }
        });
        using var output = new BinaryWriter(File.Create(args[0]));
        output.Write(0x45475856u); output.Write(1); output.Write(size); output.Write(samples);
        output.Write(.04f); output.Write(2); output.Write(4); output.Write(2);
        foreach (var entry in entries) { output.Write(entry.E); output.Write(entry.Ef); }
        Console.WriteLine($"Generated {size}x{size} E/Ef nodes with {samples} samples each: {args[0]}");
    }
    #endregion

    #region Private
    /// <summary>Integrates correlated Smith GGX using visible-normal importance sampling and double precision.</summary>
    private static (double E, double Ef) Integrate(double roughness, double nv, int count)
    {
        double alpha = roughness * roughness, a2 = alpha * alpha;
        double vx = Math.Sqrt(Math.Max(0, 1 - nv * nv));
        double stretchedLength = Math.Sqrt(a2 * vx * vx + nv * nv);
        double sx = alpha * vx / stretchedLength, sz = nv / stretchedLength;
        double sqrtV = Math.Sqrt(a2 + (1 - a2) * nv * nv);
        double sum = 0, fresnelSum = 0;
        for (uint i = 0; i < count; i++)
        {
            double radius = Math.Sqrt((i + .5) / count), angle = 2 * Math.PI * RadicalInverse(i);
            double dx = radius * Math.Cos(angle), dy = radius * Math.Sin(angle);
            double blend = (1 + sz) * .5;
            dy = (1 - blend) * Math.Sqrt(Math.Max(0, 1 - dx * dx)) + blend * dy;
            double dz = Math.Sqrt(Math.Max(0, 1 - dx * dx - dy * dy));
            // Transform the warped disk through the stretched view frame, then undo stretch.
            double hx = alpha * (-sz * dy + sx * dz), hy = alpha * dx;
            double hz = Math.Max(0, sx * dy + sz * dz);
            double length = Math.Sqrt(hx * hx + hy * hy + hz * hz);
            hx /= length; hz /= length;
            double vh = vx * hx + nv * hz, nl = 2 * vh * hz - nv;
            if (vh <= 0 || nl <= 0) continue;
            double sqrtL = Math.Sqrt(a2 + (1 - a2) * nl * nl);
            // Cancel the visible-normal PDF analytically. This also defines the nv=0 limit.
            double weight = nl * (nv + sqrtV) / (nl * sqrtV + nv * sqrtL);
            sum += weight;
            fresnelSum += weight * Math.Pow(1 - Math.Clamp(vh, 0, 1), 5);
        }
        return (sum / count, fresnelSum / count);
    }

    /// <summary>Produces deterministic base-two low-discrepancy sample coordinates.</summary>
    private static double RadicalInverse(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xaaaaaaaau) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xccccccccu) >> 2);
        bits = ((bits & 0x0f0f0f0fu) << 4) | ((bits & 0xf0f0f0f0u) >> 4);
        bits = ((bits & 0x00ff00ffu) << 8) | ((bits & 0xff00ff00u) >> 8);
        return bits * (1.0 / 4294967296.0);
    }
    #endregion
}
