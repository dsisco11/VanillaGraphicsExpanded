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
    internal static (ImmutableArray<float> Radiance, ImmutableArray<float> Attenuation, ImmutableArray<float> Mie) Build(
        Vector3 sun, float altitude, float aerosol, int width, int height, ReadOnlySpan<float> sky,
        AtmosphereMultipleScattering scattering, CancellationToken cancellation, ReadOnlySpan<float> skyMie)
    {
        int count = width * height;
        if (sky.Length != count * 4 || skyMie.Length != sky.Length)
            throw new ArgumentException("Sky and Mie transport dimensions differ.");
        float[] radiance = new float[count * AtmosphereAerialPerspective.Depth * 4];
        float[] attenuation = new float[radiance.Length];
        float[] mie = new float[radiance.Length];
        const int capacity = 128;
        // Bounded scratch belongs to this worker build and is reused across direction batches.
        using var rayOwner = PooledArray<Vector4>.Rent(capacity * AtmosphereAerialPerspective.Depth);
        using var transOwner = PooledArray<Vector4>.Rent(rayOwner.Length);
        using var mieOwner = PooledArray<Vector4>.Rent(rayOwner.Length);
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
            AtmosphereModel.AerialBatch(directions[..lanes], sun, altitude, aerosol, scattering, ray, trans, mieOwner.Span);
            for (int lane = 0; lane < lanes; lane++)
            {
                int index = first + lane, last = (AtmosphereAerialPerspective.Depth - 1) * lanes + lane;
                // The cumulative grid and sky use different quadrature nodes. Match
                // their terminal source to avoid a terrain/sky seam without altering extinction.
                Vector3 terminal = new(ray[last].X, ray[last].Y, ray[last].Z);
                float angular = AtmosphereMieTransport.Factor(Vector3.Dot(Vector3.Normalize(directions[lane]), sun));
                Vector3 terminalMie = new(mieOwner.Span[last].X, mieOwner.Span[last].Y, mieOwner.Span[last].Z);
                // Match each smooth term independently; total-radiance normalization would reintroduce the angular lobe.
                Vector3 targetMie = new(skyMie[index * 4], skyMie[index * 4 + 1], skyMie[index * 4 + 2]);
                Vector3 targetTotal = new(sky[index * 4], sky[index * 4 + 1], sky[index * 4 + 2]);
                Vector3 mieScale = targetMie / Vector3.Max(terminalMie, new(1e-20f));
                Vector3 scale = Vector3.Max(Vector3.Zero, targetTotal - targetMie * angular)
                    / Vector3.Max(terminal - terminalMie * angular, new(1e-20f));
                for (int slice = 0; slice < AtmosphereAerialPerspective.Depth; slice++)
                {
                    int offset = (slice * count + index) * 4, source = slice * lanes + lane;
                    Vector3 rawMie = new(mieOwner.Span[source].X, mieOwner.Span[source].Y, mieOwner.Span[source].Z);
                    Vector3 rawTotal = new(ray[source].X, ray[source].Y, ray[source].Z);
                    Vector3 scaledMie = rawMie * mieScale;
                    Vector3 total = Vector3.Max(Vector3.Zero, rawTotal - rawMie * angular) * scale + scaledMie * angular;
                    radiance[offset] = total.X; radiance[offset + 1] = total.Y;
                    radiance[offset + 2] = total.Z; radiance[offset + 3] = 1;
                    mie[offset] = scaledMie.X; mie[offset + 1] = scaledMie.Y; mie[offset + 2] = scaledMie.Z; mie[offset + 3] = 1;
                    // Store loss rather than transmission so a black neutral volume is identity transport.
                    attenuation[offset] = 1f - trans[source].X; attenuation[offset + 1] = 1f - trans[source].Y;
                    attenuation[offset + 2] = 1f - trans[source].Z; attenuation[offset + 3] = 1;
                }
            }
        }
        // These dedicated arrays are no longer mutated; transfer ownership without a second full-volume copy.
        return (ImmutableCollectionsMarshal.AsImmutableArray(radiance), ImmutableCollectionsMarshal.AsImmutableArray(attenuation),
            ImmutableCollectionsMarshal.AsImmutableArray(mie));
    }
    #endregion
}
