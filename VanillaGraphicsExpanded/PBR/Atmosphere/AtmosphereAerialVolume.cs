using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using VanillaGraphicsExpanded.Collections;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Builds worker-owned aerial volumes using the same admitted direction grid as the sky.</summary>
internal static class AtmosphereAerialVolume
{
    #region Construction
    /// <summary>Produces z-major RGBA volumes and anchors their terminal radiance to the published sky quadrature.</summary>
    internal static (ImmutableArray<float> Radiance, ImmutableArray<float> Attenuation) Build(
        Vector3 sun, float altitude, float aerosol, int width, int height, ReadOnlySpan<float> sky,
        AtmosphereMultipleScattering scattering, CancellationToken cancellation)
    {
        int count = width * height;
        float[] radiance = new float[count * AtmosphereAerialPerspective.Depth * 4];
        float[] attenuation = new float[radiance.Length];
        const int capacity = 128;
        // Bounded scratch belongs to this worker build and is reused across direction batches.
        using var rayOwner = PooledArray<Vector4>.Rent(capacity * AtmosphereAerialPerspective.Depth);
        using var transOwner = PooledArray<Vector4>.Rent(rayOwner.Length);
        Span<Vector4> ray = rayOwner.Span, trans = transOwner.Span;
        Span<Vector3> directions = stackalloc Vector3[capacity];
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        for (int first = 0; first < count; first += capacity)
        {
            cancellation.ThrowIfCancellationRequested();
            int lanes = Math.Min(capacity, count - first);
            for (int lane = 0; lane < lanes; lane++)
            {
                int index = first + lane;
                float elevation = AtmosphereSkyMapping.Elevation((float)(index / width) / (height - 1), horizon);
                float azimuth = ((index % width) + .5f) / width * (2f * MathF.PI);
                directions[lane] = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            }
            AtmosphereModel.AerialBatch(directions[..lanes], sun, altitude, aerosol, scattering, ray, trans);
            for (int lane = 0; lane < lanes; lane++)
            {
                int index = first + lane, last = (AtmosphereAerialPerspective.Depth - 1) * lanes + lane;
                // The cumulative grid and sky use different quadrature nodes. Match
                // their terminal source to avoid a terrain/sky seam without altering extinction.
                Vector3 terminal = new(ray[last].X, ray[last].Y, ray[last].Z);
                Vector3 scale = new Vector3(sky[index * 4], sky[index * 4 + 1], sky[index * 4 + 2])
                    / Vector3.Max(terminal, new Vector3(1e-20f));
                for (int slice = 0; slice < AtmosphereAerialPerspective.Depth; slice++)
                {
                    int offset = (slice * count + index) * 4, source = slice * lanes + lane;
                    radiance[offset] = ray[source].X * scale.X; radiance[offset + 1] = ray[source].Y * scale.Y;
                    radiance[offset + 2] = ray[source].Z * scale.Z; radiance[offset + 3] = 1;
                    // Store loss rather than transmission so a black neutral volume is identity transport.
                    attenuation[offset] = 1f - trans[source].X; attenuation[offset + 1] = 1f - trans[source].Y;
                    attenuation[offset + 2] = 1f - trans[source].Z; attenuation[offset + 3] = 1;
                }
            }
        }
        // These dedicated arrays are no longer mutated; transfer ownership without a second full-volume copy.
        return (ImmutableCollectionsMarshal.AsImmutableArray(radiance), ImmutableCollectionsMarshal.AsImmutableArray(attenuation));
    }
    #endregion
}
