using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

/// <summary>Shared projection/filter inputs; receiving enclosures include all texel and normal-bias footprints.</summary>
internal sealed class ShadowSampleRequest
{
    internal uint[] Words { get; }
    internal ShadowBounds Footprint { get; }
    internal ShadowPageRequest PageRequest { get; }
    private Vector3d Origin { get; }
    #region Public API
    /// <summary>Builds a directional query at one explicit level; adjacent levels use the same world receiver.</summary>
    internal static ShadowSampleRequest Directional(DirectionalShadowProjection projection, int level, Vector3d receiver,
        Vector3d normal, long worldRevision, long sceneEpoch, long sceneSequence, ShadowPageSettings settings)
    {
        double width = projection.TexelWidth(level);
        var query = new ShadowSampleRequest(projection.Key(receiver, level), receiver, normal, width, worldRevision, sceneEpoch, sceneSequence, settings);
        query.Words[64] = 0; query.Words[65] = (uint)projection.PageSize;
        Vector(query.Words, 68, projection.Right); Vector(query.Words, 74, projection.Up); Vector(query.Words, 80, projection.Forward);
        Number(query.Words, 86, projection.Near); Number(query.Words, 88, projection.Far); Number(query.Words, 90, width);
        return query;
    }
    /// <summary>Builds a point query on a radial receiver shell; taps are remapped to their destination face.</summary>
    internal static ShadowSampleRequest Point(PointShadowProjection projection, Vector3d receiver, Vector3d normal,
        long worldRevision, long sceneEpoch, long sceneSequence, ShadowPageSettings settings)
    {
        double distance = Math.Sqrt(Vector3d.Dot(receiver - projection.Position, receiver - projection.Position));
        double width = 2 * distance / projection.FaceResolution;
        var query = new ShadowSampleRequest(projection.Key(receiver), receiver, normal, width, worldRevision, sceneEpoch, sceneSequence,
            settings, 3 * (1 + 8.0 / projection.FaceResolution));
        query.Words[64] = 1; query.Words[65] = (uint)projection.PageSize; query.Words[66] = (uint)projection.FaceResolution;
        Number(query.Words, 86, projection.Near); Number(query.Words, 88, projection.Far); Number(query.Words, 90, width);
        Vector(query.Words, 92, projection.Position - query.Origin); return query;
    }
    #endregion
    #region Private
    /// <summary>Constructs a conservative enclosure for all nine taps and the bounded receiver normal shift.</summary>
    private ShadowSampleRequest(ShadowPageKey key, Vector3d receiver, Vector3d normal, double texelWidth,
        long worldRevision, long sceneEpoch, long sceneSequence, ShadowPageSettings settings, double filterRadius = 3)
    {
        if (!ShadowProjectionMath.Finite(receiver) || !ShadowProjectionMath.Finite(normal) || !double.IsFinite(texelWidth) || texelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(receiver));
        var policy = settings.Freeze(); normal = ShadowProjectionMath.Unit(normal);
        // Three widths cover normalized cube-plane perturbation (including a half-texel center) at any face edge.
        double radius = (filterRadius + policy.NormalBiasTexels) * texelWidth;
        Footprint = new(receiver.X - radius, receiver.Y - radius, receiver.Z - radius,
            receiver.X + radius, receiver.Y + radius, receiver.Z + radius);
        PageRequest = new(ShadowPageOperation.Lookup, key, Footprint, worldRevision, sceneEpoch, sceneSequence);
        Words = new uint[128]; PageRequest.Pack().CopyTo(Words, 0);
        // Preserve native chunk-sized origin identity before packing bounded receiver remainders.
        int x = OriginCell(receiver.X), y = OriginCell(receiver.Y), z = OriginCell(receiver.Z);
        Origin = new(x * 32.0, y * 32.0, z * 32.0);
        Words[120] = (uint)x; Words[121] = (uint)y; Words[122] = (uint)z;
        Vector(Words, 98, receiver - Origin); Vector(Words, 104, normal);
        Number(Words, 110, policy.DepthBiasTexels); Number(Words, 112, policy.NormalBiasTexels); Number(Words, 114, policy.SlopeBiasTexels);
    }
    /// <summary>Writes a world-space vector as doubles so large origins are not rounded through float.</summary>
    private static void Vector(uint[] words, int offset, Vector3d v)
    { Number(words, offset, v.X); Number(words, offset + 2, v.Y); Number(words, offset + 4, v.Z); }
    /// <summary>Packs an IEEE double into the shared word layout.</summary>
    private static void Number(uint[] words, int offset, double value) => ShadowPageRequest.Put(words, offset, BitConverter.DoubleToInt64Bits(value));
    /// <summary>Rejects world origins outside native signed chunk coordinates instead of overflowing their identity.</summary>
    private static int OriginCell(double world)
    {
        double cell = Math.Floor(world / 32);
        if (cell < int.MinValue || cell > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(world));
        return (int)cell;
    }
    #endregion
}
