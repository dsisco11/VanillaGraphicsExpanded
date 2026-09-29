using System;
using System.Buffers;
using System.Numerics;
using System.Numerics.Tensors;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Batches expensive density and transmission functions through width-independent tensor primitives.</summary>
internal static partial class AtmosphereModel
{
    private const int BatchCapacity = 128;

    #region Batched integration
    /// <summary>Builds one bounded batch of finite-path columns using the same tensor density and sunlight integration kernels.</summary>
    internal static void AerialBatch(ReadOnlySpan<Vector3> directions, Vector3 sun, float altitude,
        float aerosol, AtmosphereMultipleScattering? scattering, Span<Vector4> radiances, Span<Vector4> transmissions,
        Span<Vector4> mieTransport = default)
    {
        if (directions.Length > BatchCapacity || radiances.Length < directions.Length * AtmosphereAerialPerspective.Depth
            || transmissions.Length < directions.Length * AtmosphereAerialPerspective.Depth
            || (!mieTransport.IsEmpty && mieTransport.Length < directions.Length * AtmosphereAerialPerspective.Depth))
            throw new ArgumentException("Invalid aerial batch dimensions.");
        Span<Vector3> result = stackalloc Vector3[directions.Length];
        IntegrateBatch(directions, Vector3.Normalize(sun), new(0, GroundRadius + Math.Clamp(altitude, .001f, 99f), 0),
            Math.Clamp(aerosol, .1f, 8f), result, scattering, radiances, transmissions, aerialMie: mieTransport);
    }

    /// <summary>Evaluates independent sky directions with bounded scratch storage and scalar-compatible integration order.</summary>
    internal static void RadianceBatch(ReadOnlySpan<Vector3> directions, Vector3 sun, float altitudeKm,
        float aerosol, Span<Vector3> destination, AtmosphereMultipleScattering? multipleScattering = null,
        Span<Vector3> mieTransport = default)
    {
        if (destination.Length < directions.Length) throw new ArgumentException("Destination is too short.", nameof(destination));
        if (!mieTransport.IsEmpty && mieTransport.Length < directions.Length)
            throw new ArgumentException("Mie transport destination is too short.", nameof(mieTransport));
        sun = Vector3.Normalize(sun);
        aerosol = Math.Clamp(aerosol, .1f, 8f);
        var origin = new Vector3(0, GroundRadius + Math.Clamp(altitudeKm, .001f, 99f), 0);
        for (int offset = 0; offset < directions.Length; offset += BatchCapacity)
        {
            int count = Math.Min(BatchCapacity, directions.Length - offset);
            IntegrateBatch(directions.Slice(offset, count), sun, origin, aerosol, destination.Slice(offset, count), multipleScattering,
                skyMie: mieTransport.IsEmpty ? default : mieTransport.Slice(offset, count));
        }
    }

    /// <summary>Compacts visible sunlight samples, vectorizes their transcendental math, then accumulates each ray in order.</summary>
    private static void IntegrateBatch(ReadOnlySpan<Vector3> directions, Vector3 sun, Vector3 origin,
        float aerosol, Span<Vector3> result, AtmosphereMultipleScattering? multipleScattering,
        Span<Vector4> aerialRadiance = default, Span<Vector4> aerialTransmission = default,
        Span<Vector3> skyMie = default, Span<Vector4> aerialMie = default)
    {
        bool finite = !aerialRadiance.IsEmpty;
        bool collectMie = !skyMie.IsEmpty || !aerialMie.IsEmpty;
        int count = directions.Length, capacity = count * (LightSamples + 1);
        float[] rented = ArrayPool<float>.Shared.Rent(capacity * 5 + count * 6);
        try
        {
            Span<float> radii = rented.AsSpan(0, capacity);
            Span<float> heights = rented.AsSpan(capacity, capacity);
            Span<float> molecular = rented.AsSpan(capacity * 2, capacity);
            Span<float> aerosols = rented.AsSpan(capacity * 3, capacity);
            Span<float> lightSteps = rented.AsSpan(capacity * 4, capacity);
            Span<float> transmission = rented.AsSpan(capacity * 5, count * 6);
            Span<Vector3> rays = stackalloc Vector3[count];
            Span<Vector3> optical = stackalloc Vector3[count];
            Span<Vector3> mieIntegral = stackalloc Vector3[count];
            mieIntegral.Clear();
            Span<Vector3> extinction = stackalloc Vector3[count];
            Span<Vector3> indirect = stackalloc Vector3[count];
            Span<float> distances = stackalloc float[count];
            Span<float> steps = stackalloc float[count];
            Span<float> rayleighPhase = stackalloc float[count];
            Span<float> miePhase = stackalloc float[count];
            Span<int> lightOffsets = stackalloc int[count];
            optical.Clear(); result.Clear();
            for (int lane = 0; lane < count; lane++)
            {
                if (finite) { aerialRadiance[lane] = new(0, 0, 0, 1); aerialTransmission[lane] = Vector4.One; }
                if (!aerialMie.IsEmpty) aerialMie[lane] = new(0, 0, 0, 1);
                rays[lane] = Vector3.Normalize(directions[lane]);
                float distance = Boundary(origin, rays[lane], TopRadius);
                float ground = GroundDistance(origin, rays[lane]);
                distances[lane] = ground > 0 ? MathF.Min(distance, ground) : distance;
                float cosine = Math.Clamp(Vector3.Dot(rays[lane], sun), -1f, 1f);
                rayleighPhase[lane] = 3f * (1f + cosine * cosine) / (16f * MathF.PI);
                miePhase[lane] = AtmosphereMieTransport.Factor(cosine);
            }
            int viewCount = finite ? (AtmosphereAerialPerspective.Depth - 1) * 2 : ViewSamples;
            for (int view = 0; view < viewCount; view++)
            {
                int samples = count;
                for (int lane = 0; lane < count; lane++)
                {
                    float start = distances[lane] * view * view / (ViewSamples * ViewSamples);
                    float end = distances[lane] * (view + 1) * (view + 1) / (ViewSamples * ViewSamples);
                    if (finite)
                    {
                        int slice = view >> 1;
                        float lower = AtmosphereAerialPerspective.Distance(slice, distances[lane]);
                        float upper = AtmosphereAerialPerspective.Distance(slice + 1, distances[lane]);
                        float half = (upper - lower) * .5f;
                        start = lower + (view & 1) * half;
                        end = start + half;
                    }
                    steps[lane] = end - start;
                    Vector3 point = origin + rays[lane] * ((start + end) * .5f);
                    radii[lane] = point.LengthSquared();
                    if (multipleScattering is not null)
                    {
                        float radius = MathF.Sqrt(radii[lane]);
                        indirect[lane] = multipleScattering.Sample(radius - GroundRadius, Vector3.Dot(point, sun) / radius);
                    }
                    lightOffsets[lane] = -1;
                    if (steps[lane] <= 0 || GroundDistance(point, sun) > 0) continue;
                    lightOffsets[lane] = samples;
                    float distance = Boundary(point, sun, TopRadius);
                    for (int light = 0; light < LightSamples; light++, samples++)
                    {
                        float lightStart = distance * light * light / (LightSamples * LightSamples);
                        float lightEnd = distance * (light + 1) * (light + 1) / (LightSamples * LightSamples);
                        radii[samples] = (point + sun * ((lightStart + lightEnd) * .5f)).LengthSquared();
                        lightSteps[samples] = lightEnd - lightStart;
                    }
                }
                // TensorPrimitives selects the available SIMD width and handles tails itself.
                // Keep division and exponentiation separate to match the scalar density formula.
                var h = heights[..samples];
                TensorPrimitives.Sqrt(radii[..samples], h);
                TensorPrimitives.Subtract(h, GroundRadius, h);
                TensorPrimitives.Clamp(h, 0f, float.MaxValue, h);
                TensorPrimitives.Divide(h, -8f, molecular[..samples]);
                TensorPrimitives.Exp(molecular[..samples], molecular[..samples]);
                TensorPrimitives.Divide(h, -1.2f, aerosols[..samples]);
                TensorPrimitives.Exp(aerosols[..samples], aerosols[..samples]);
                // Extinction is linear in the three medium densities. Integrate their
                // columns first, then apply RGB coefficients once per sunlight ray.
                // View samples keep unit weights; sunlight samples use their segment lengths.
                lightSteps[..count].Fill(1f);
                TensorPrimitives.Subtract(h, 25f, h);
                TensorPrimitives.Abs(h, h);
                TensorPrimitives.Divide(h, 15f, h);
                TensorPrimitives.Multiply(h, -1f, h);
                TensorPrimitives.Add(h, 1f, h);
                TensorPrimitives.Clamp(h, 0f, 1f, h);
                TensorPrimitives.Multiply(molecular[..samples], lightSteps[..samples], molecular[..samples]);
                TensorPrimitives.Multiply(aerosols[..samples], lightSteps[..samples], aerosols[..samples]);
                TensorPrimitives.Multiply(h, lightSteps[..samples], h);
                for (int lane = 0; lane < count; lane++)
                {
                    extinction[lane] = Extinction(new(molecular[lane], aerosols[lane], h[lane]), aerosol);
                    Vector3 column = Vector3.Zero;
                    int first = lightOffsets[lane];
                    if (first >= 0)
                        for (int sample = first; sample < first + LightSamples; sample++)
                            column += new Vector3(molecular[sample], aerosols[sample], h[sample]);
                    Vector3 lightOptical = Extinction(column, aerosol);
                    Vector3 viewOptical = finite ? optical[lane] : optical[lane] + extinction[lane] * (.5f * steps[lane]);
                    int index = lane * 3, lightIndex = count * 3 + index;
                    transmission[index] = -viewOptical.X;
                    transmission[index + 1] = -viewOptical.Y;
                    transmission[index + 2] = -viewOptical.Z;
                    transmission[lightIndex] = -lightOptical.X;
                    transmission[lightIndex + 1] = -lightOptical.Y;
                    transmission[lightIndex + 2] = -lightOptical.Z;
                }
                TensorPrimitives.Exp(transmission, transmission);
                for (int lane = 0; lane < count; lane++)
                {
                    int index = lane * 3, lightIndex = count * 3 + index;
                    Vector3 transmittance = lightOffsets[lane] < 0 ? Vector3.Zero :
                        new Vector3(transmission[index], transmission[index + 1], transmission[index + 2])
                        * new Vector3(transmission[lightIndex], transmission[lightIndex + 1], transmission[lightIndex + 2]);
                    Vector3 scattering = Rayleigh * (molecular[lane] * rayleighPhase[lane])
                        + new Vector3(.003996f * aerosol * aerosols[lane] * miePhase[lane]);
                    Vector3 integrated = finite
                        ? new(SegmentIntegral(extinction[lane].X, steps[lane]), SegmentIntegral(extinction[lane].Y, steps[lane]), SegmentIntegral(extinction[lane].Z, steps[lane]))
                        : new(steps[lane]);
                    result[lane] += transmittance * scattering * integrated;
                    // Retain smooth transport without the concentrated angular factor.
                    if (collectMie) mieIntegral[lane] += transmittance * (.003996f * aerosol * aerosols[lane]) * integrated;
                    if (multipleScattering is not null)
                    {
                        // Isotropic incoming radiance already includes angular normalization.
                        // Do not apply the direct-sun phase function or planet shadow to it again.
                        Vector3 viewTransmission = new(transmission[index], transmission[index + 1], transmission[index + 2]);
                        Vector3 totalScattering = Rayleigh * molecular[lane] + new Vector3(.003996f * aerosol * aerosols[lane]);
                        result[lane] += viewTransmission * totalScattering * indirect[lane] * integrated;
                    }
                    optical[lane] += extinction[lane] * steps[lane];
                    if (finite && (view & 1) != 0)
                    {
                        int target = ((view >> 1) + 1) * count + lane;
                        aerialRadiance[target] = new(Vector3.Max(Vector3.Zero, result[lane] * Solar), 1);
                        if (!aerialMie.IsEmpty) aerialMie[target] = new(mieIntegral[lane] * Solar, 1);
                        aerialTransmission[target] = new(ExpNegative(optical[lane]), 1);
                    }
                }
            }
            for (int lane = 0; lane < count; lane++)
            {
                result[lane] = Vector3.Max(Vector3.Zero, result[lane] * Solar);
                if (!skyMie.IsEmpty) skyMie[lane] = mieIntegral[lane] * Solar;
            }
        }
        finally { ArrayPool<float>.Shared.Return(rented); }
    }
    #endregion
}
