using System;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Immutable table copy owned by one publication batch, never exposed to workers.</summary>
internal sealed class TraceGeometryTables
{
    public const long MaximumUploadBytes = 16384L * 16 + 16384L * 48 + 65536L * 16 + 256L * 4;
    private readonly uint[] faces, surfaces;
    private readonly byte[] colors;
    private readonly float[] lights;
    public long Revision { get; }
    public ReadOnlySpan<uint> Faces => faces;
    public ReadOnlySpan<uint> Surfaces => surfaces;
    public ReadOnlySpan<byte> Colors => colors;
    public ReadOnlySpan<float> Lights => lights;

    /// <summary>Detaches every table buffer from its caller before crossing publication boundaries.</summary>
    public TraceGeometryTables(long revision, ReadOnlySpan<uint> faces, ReadOnlySpan<byte> colors,
        ReadOnlySpan<uint> surfaces, ReadOnlySpan<float> lights)
    {
        Revision = revision; this.faces = faces.ToArray(); this.colors = colors.ToArray();
        this.surfaces = surfaces.ToArray(); this.lights = lights.ToArray();
    }
}

