using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Packs completed trace results into CPU arrays matching the production GPU atlas layout.</summary>
internal sealed class WorldProbeAtlasData
{
    public int Resolution { get; }
    public int TileSize { get; }
    public int ScalarWidth => Resolution * Resolution;
    public int ScalarHeight => Resolution;
    public int Width => ScalarWidth * TileSize;
    public int Height => ScalarHeight * TileSize;
    public float[] Radiance { get; }
    public float[] Visibility { get; }
    public float[] Metadata { get; }

    #region Atlas Construction
    /// <summary>Allocates a single-level atlas with initially invalid probes.</summary>
    public WorldProbeAtlasData(int resolution, int tileSize)
    {
        if (resolution < 1 || tileSize < 1) throw new ArgumentOutOfRangeException(nameof(resolution));
        Resolution = resolution;
        TileSize = tileSize;
        Radiance = new float[Width * Height * 4];
        Visibility = new float[ScalarWidth * ScalarHeight * 4];
        Metadata = new float[ScalarWidth * ScalarHeight * 2];
    }

    /// <summary>Publishes a fully traced probe, rejecting incomplete or duplicate directional samples.</summary>
    public void SetProbe(LumOnWorldProbeTraceResult result)
    {
        var p = result.Request.StorageIndex;
        if (!result.Success || result.AtlasSamples.Length != TileSize * TileSize)
            throw new ArgumentException("Only successful complete probe tiles may be published.");
        if (result.Request.Level != 0 || p.X < 0 || p.X >= Resolution || p.Y < 0 || p.Y >= Resolution || p.Z < 0 || p.Z >= Resolution)
            throw new ArgumentOutOfRangeException(nameof(result));
        var seen = new HashSet<(int, int)>();
        int u = p.X + p.Z * Resolution;
        int v = p.Y;
        foreach (var sample in result.AtlasSamples)
        {
            if (sample.OctX < 0 || sample.OctX >= TileSize || sample.OctY < 0 || sample.OctY >= TileSize || !seen.Add((sample.OctX, sample.OctY)))
                throw new ArgumentException("Invalid or duplicate atlas direction.");
            int offset = ((v * TileSize + sample.OctY) * Width + u * TileSize + sample.OctX) * 4;
            Radiance[offset] = sample.RadianceRgb.X;
            Radiance[offset + 1] = sample.RadianceRgb.Y;
            Radiance[offset + 2] = sample.RadianceRgb.Z;
            Radiance[offset + 3] = sample.AlphaEncodedDistSigned;
        }
        int scalar = v * ScalarWidth + u;
        Visibility[scalar * 4] = 0.5f;
        Visibility[scalar * 4 + 1] = 0.5f;
        Visibility[scalar * 4 + 2] = result.SkyIntensity;
        Visibility[scalar * 4 + 3] = result.ShortRangeAoConfidence;
        Metadata[scalar * 2] = result.Confidence;
    }
    #endregion
}
