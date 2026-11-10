using System;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Pairs one complete receiver enclosure with its exact current projection and candidate dependency request.</summary>
internal sealed class ShadowDrawPage
{
    internal ShadowPageRequest Request { get; }
    internal ShadowCandidateHandle Candidate { get; }
    internal ShadowCandidateCurrent Current { get; }
    private readonly float[] matrix;
    private readonly Vector3d origin;
    private readonly float deformationPadding;

    #region Public API
    /// <summary>Freezes directional raster metadata using the same basis and page identity as sampling.</summary>
    internal static ShadowDrawPage Directional(ShadowPageRequest request, ShadowCandidateHandle candidate,
        ShadowCandidateCurrent current, DirectionalShadowProjection projection, Vector3d origin, float deformationPadding)
    {
        if (current.PositionOrDirection != -projection.Forward)
            throw new ArgumentException("Candidate and directional raster directions differ.");
        return new(request, candidate, current, ShadowRasterProjection.Directional(projection, request.Key, origin), origin, deformationPadding);
    }

    /// <summary>Freezes point raster metadata without losing homogeneous near/source crossing behavior.</summary>
    internal static ShadowDrawPage Point(ShadowPageRequest request, ShadowCandidateHandle candidate,
        ShadowCandidateCurrent current, PointShadowProjection projection, Vector3d origin, float deformationPadding)
    {
        if (current.PositionOrDirection != projection.Position)
            throw new ArgumentException("Candidate and point raster positions differ.");
        return new(request, candidate, current, ShadowRasterProjection.Point(projection, request.Key, origin), origin, deformationPadding);
    }

    /// <summary>Packs fixed 384-byte requests independently of GPU-selected physical assignments.</summary>
    internal void Pack(Span<uint> output)
    {
        if (output.Length != 96) throw new ArgumentException("Draw page requires 96 words.");
        output.Clear(); Request.Pack().CopyTo(output);
        for (int i = 0; i < 16; i++) output[64 + i] = BitConverter.SingleToUInt32Bits(matrix[i]);
        output[80] = (uint)Candidate.Slot; ShadowPageRequest.Put(output, 81, Candidate.Generation);
        ShadowPageRequest.Put(output, 84, BitConverter.DoubleToInt64Bits(origin.X));
        ShadowPageRequest.Put(output, 86, BitConverter.DoubleToInt64Bits(origin.Y));
        ShadowPageRequest.Put(output, 88, BitConverter.DoubleToInt64Bits(origin.Z));
        output[90] = BitConverter.SingleToUInt32Bits(deformationPadding);
    }
    #endregion

    #region Private
    /// <summary>Rejects mismatched dependencies before any GPU work or page clear is admitted.</summary>
    private ShadowDrawPage(ShadowPageRequest request, ShadowCandidateHandle candidate, ShadowCandidateCurrent current,
        float[] matrix, Vector3d origin, float deformationPadding)
    {
        _ = request.Pack();
        if (request.Key.Source != current.Source || request.Key.Projection != current.ProjectionGeneration
            || !current.Receiver.Contains(request.Region) || !ShadowProjectionMath.Finite(origin)
            || !float.IsFinite(deformationPadding) || deformationPadding < 0)
            throw new ArgumentException("Draw page coverage or projection dependencies disagree.");
        Request = request; Candidate = candidate; Current = current;
        this.matrix = matrix; this.origin = origin; this.deformationPadding = deformationPadding;
    }
    #endregion
}
