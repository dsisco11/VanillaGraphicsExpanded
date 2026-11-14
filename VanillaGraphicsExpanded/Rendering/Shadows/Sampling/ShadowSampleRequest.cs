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
    internal ShadowPageRequest PageRequest { get; private set; }
    private Vector3d Origin { get; }
    #region Public API
    /// <summary>Builds a directional query at one explicit level; adjacent levels use the same world receiver.</summary>
    internal static ShadowSampleRequest Directional(DirectionalShadowProjection projection, int level, Vector3d receiver,
        Vector3d normal, long worldRevision, long sceneEpoch, long sceneSequence, ShadowPageSettings settings)
    {
        double width = projection.TexelWidth(level);
        var snapshot = ShadowProjectionSnapshot.Directional(projection, level);
        var point = new ShadowBounds(receiver.X, receiver.Y, receiver.Z, receiver.X, receiver.Y, receiver.Z);
        var query = new ShadowSampleRequest(projection.Key(receiver, level), receiver, normal, width, worldRevision, sceneEpoch, sceneSequence,
            settings, ShadowCoordinates.SamplingOrigin(snapshot, point));
        snapshot.WriteTo(query.Words); query.PageRequest = query.PageRequest with { Projection = snapshot };
        ShadowCoordinates.WriteSamplingFrame(query.Words, snapshot, query.Footprint);
        new ShadowTemporalQuery(snapshot, 0, new() { MaxStaleAgeSeconds = 0 }).WriteTo(query.Words, snapshot);
        return query;
    }
    /// <summary>Builds a point query on a radial receiver shell; taps are remapped to their destination face.</summary>
    internal static ShadowSampleRequest Point(PointShadowProjection projection, Vector3d receiver, Vector3d normal,
        long worldRevision, long sceneEpoch, long sceneSequence, ShadowPageSettings settings)
    {
        double distance = Math.Sqrt(Vector3d.Dot(receiver - projection.Position, receiver - projection.Position));
        double width = 2 * distance / projection.FaceResolution;
        var query = new ShadowSampleRequest(projection.Key(receiver), receiver, normal, width, worldRevision, sceneEpoch, sceneSequence,
            settings, projection.Position, 3 * (1 + 8.0 / projection.FaceResolution));
        var snapshot = ShadowProjectionSnapshot.Point(projection, query.PageRequest.Key.FaceOrLevel);
        snapshot.WriteTo(query.Words); query.PageRequest = query.PageRequest with { Projection = snapshot };
        ShadowCoordinates.WriteSamplingFrame(query.Words, snapshot, query.Footprint);
        new ShadowTemporalQuery(snapshot, 0, new() { MaxStaleAgeSeconds = 0 }).WriteTo(query.Words, snapshot);
        return query;
    }

    /// <summary>Checks current source identity while retaining the exact rendered projection and current receiver footprint.</summary>
    internal ShadowSampleRequest WithTemporal(ShadowTemporalQuery temporal)
    {
        ArgumentNullException.ThrowIfNull(temporal);
        if (temporal.Current.Source != PageRequest.Key.Source || temporal.Current.IsPoint != (Words[64] == 1))
            throw new ArgumentException("Current illumination and retained depth must share source lifetime and projection kind.", nameof(temporal));
        var result = new ShadowSampleRequest(this);
        temporal.WriteTo(result.Words, PageRequest.Projection!);
        return result;
    }
    #endregion
    #region Private
    /// <summary>Constructs a conservative enclosure for all nine taps and the bounded receiver normal shift.</summary>
    private ShadowSampleRequest(ShadowPageKey key, Vector3d receiver, Vector3d normal, double texelWidth,
        long worldRevision, long sceneEpoch, long sceneSequence, ShadowPageSettings settings, Vector3d samplingOrigin, double filterRadius = 3)
    {
        if (!ShadowProjectionMath.Finite(receiver) || !ShadowProjectionMath.Finite(normal) || !double.IsFinite(texelWidth) || texelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(receiver));
        var policy = settings.Freeze(); normal = ShadowProjectionMath.Unit(normal);
        // Three widths cover normalized cube-plane perturbation (including a half-texel center) at any face edge.
        var local = receiver - samplingOrigin;
        double arithmetic = Math.ScaleB(Math.Max(1, Math.Max(Math.Abs(local.X), Math.Max(Math.Abs(local.Y), Math.Abs(local.Z)))), -20);
        double radius = (filterRadius + policy.NormalBiasTexels) * texelWidth;
        var filtered = new ShadowBounds(receiver.X - radius, receiver.Y - radius, receiver.Z - radius,
            receiver.X + radius, receiver.Y + radius, receiver.Z + radius);
        radius += ShadowCoordinates.DemandPadding(filtered) + arithmetic;
        Footprint = ShadowCoverageBounds.Canonicalize(new(receiver.X - radius, receiver.Y - radius, receiver.Z - radius,
            receiver.X + radius, receiver.Y + radius, receiver.Z + radius));
        PageRequest = new(ShadowPageOperation.Lookup, key, Footprint, worldRevision, sceneEpoch, sceneSequence);
        Words = new uint[ShadowCoordinates.SamplingWords]; PageRequest.Pack().CopyTo(Words, 0);
        // Validate native range and make this standalone query's receiver its own translated origin.
        ValidateOrigin(receiver.X); ValidateOrigin(receiver.Y); ValidateOrigin(receiver.Z);
        Origin = receiver;
        ShadowCoordinates.Vector(Words, 98, System.Numerics.Vector3.Zero);
        ShadowCoordinates.Vector(Words, 101, new System.Numerics.Vector3((float)normal.X, (float)normal.Y, (float)normal.Z));
        ShadowCoordinates.Split(Origin, out var high, out var low);
        ShadowCoordinates.Vector(Words, 104, high); ShadowCoordinates.Vector(Words, 107, low);
        ShadowCoordinates.Float(Words, 123, (float)ShadowCoordinates.OriginError(Origin));
        ShadowCoordinates.Float(Words, 110, (float)policy.DepthBiasTexels);
        ShadowCoordinates.Float(Words, 111, (float)policy.NormalBiasTexels);
        ShadowCoordinates.Float(Words, 112, (float)policy.SlopeBiasTexels);
    }
    /// <summary>Copies request storage so temporal policy changes cannot mutate an already submitted query.</summary>
    private ShadowSampleRequest(ShadowSampleRequest source)
    { Words = (uint[])source.Words.Clone(); Footprint = source.Footprint; PageRequest = source.PageRequest; Origin = source.Origin; }
    /// <summary>Rejects world origins outside native signed chunk coordinates instead of overflowing their identity.</summary>
    private static void ValidateOrigin(double world)
    {
        double cell = Math.Floor(world / 32);
        if (cell < int.MinValue || cell > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(world));
    }
    #endregion
}
