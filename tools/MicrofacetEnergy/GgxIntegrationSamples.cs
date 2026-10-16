/// <summary>Precomputes the view-independent visible-normal sample sequence shared by every material node.</summary>
internal sealed class GgxIntegrationSamples
{
    private readonly double[] x, y, capY;

    #region Public API
    /// <summary>Exposes disk coordinates without allowing integrators to mutate the shared sequence.</summary>
    internal ReadOnlySpan<double> X => x;
    /// <summary>Exposes the second disk coordinate before its view-dependent warp.</summary>
    internal ReadOnlySpan<double> Y => y;
    /// <summary>Exposes the fixed hemisphere term used by each view-dependent warp.</summary>
    internal ReadOnlySpan<double> CapY => capY;
    /// <summary>Reports the number of independently integrated directions per material node.</summary>
    internal int Count => x.Length;

    /// <summary>Computes deterministic trigonometry, disk radii and fixed hemisphere terms once for the whole table.</summary>
    internal GgxIntegrationSamples(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        x = new double[count]; y = new double[count]; capY = new double[count];
        // These terms depend only on sample identity, not material roughness or view.
        // Retain the original scalar evaluation order so precomputation does not change the sequence.
        for (uint i = 0; i < count; i++)
        {
            double radius = Math.Sqrt((i + .5) / count), angle = 2 * Math.PI * RadicalInverse(i);
            x[i] = radius * Math.Cos(angle);
            y[i] = radius * Math.Sin(angle);
            capY[i] = Math.Sqrt(Math.Max(0, 1 - x[i] * x[i]));
        }
    }
    #endregion

    #region Private
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
