/// <summary>Generates the immutable GGX directional-energy asset independently of runtime shader execution.</summary>
internal static class Program
{
    #region Public API
    /// <summary>Writes a versioned little-endian table; all integration is offline, never in the render loop.</summary>
    public static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Specify the output asset path.");
        const int size = 128, samples = 16384;
        var sequence = new GgxIntegrationSamples(samples);
        var entries = new (float E, float Ef)[size * size];
        Parallel.For(0, size, y =>
        {
            // Warped axes resolve smooth materials and the tangent boundary without extra storage.
            double nv = Math.Pow((double)y / (size - 1), 4);
            for (int x = 0; x < size; x++)
            {
                double roughness = .04 + .96 * Math.Pow((double)x / (size - 1), 2);
                var energy = GgxEnergyIntegrator.Integrate(roughness, nv, sequence);
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

}
