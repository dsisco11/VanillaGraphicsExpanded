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
    /// <summary>Evaluates independent sky directions with bounded scratch storage and scalar-compatible integration order.</summary>
    internal static void RadianceBatch(ReadOnlySpan<Vector3> directions, Vector3 sun, float altitudeKm,
        float aerosol, Span<Vector3> destination)
    {
        if (destination.Length < directions.Length) throw new ArgumentException("Destination is too short.", nameof(destination));
        sun = Vector3.Normalize(sun);
        aerosol = Math.Clamp(aerosol, .1f, 8f);
        var origin = new Vector3(0, GroundRadius + Math.Clamp(altitudeKm, .001f, 99f), 0);
        for (int offset = 0; offset < directions.Length; offset += BatchCapacity)
        {
            int count = Math.Min(BatchCapacity, directions.Length - offset);
            IntegrateBatch(directions.Slice(offset, count), sun, origin, aerosol, destination.Slice(offset, count));
        }
    }

    /// <summary>Compacts visible sunlight samples, vectorizes their transcendental math, then accumulates each ray in order.</summary>
    private static void IntegrateBatch(ReadOnlySpan<Vector3> directions, Vector3 sun, Vector3 origin,
        float aerosol, Span<Vector3> result)
    {
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
            Span<Vector3> extinction = stackalloc Vector3[count];
            Span<float> distances = stackalloc float[count];
            Span<float> steps = stackalloc float[count];
            Span<float> rayleighPhase = stackalloc float[count];
            Span<float> miePhase = stackalloc float[count];
            Span<int> lightOffsets = stackalloc int[count];
            optical.Clear(); result.Clear();
            for (int lane = 0; lane < count; lane++)
            {
                rays[lane] = Vector3.Normalize(directions[lane]);
                float distance = Boundary(origin, rays[lane], TopRadius);
                float ground = GroundDistance(origin, rays[lane]);
                distances[lane] = ground > 0 ? MathF.Min(distance, ground) : distance;
                float cosine = Math.Clamp(Vector3.Dot(rays[lane], sun), -1f, 1f);
                rayleighPhase[lane] = 3f * (1f + cosine * cosine) / (16f * MathF.PI);
                const float g = .76f;
                miePhase[lane] = (1f - g * g) / (4f * MathF.PI * MathF.Pow(1f + g * g - 2f * g * cosine, 1.5f));
            }
            for (int view = 0; view < ViewSamples; view++)
            {
                int samples = count;
                for (int lane = 0; lane < count; lane++)
                {
                    float start = distances[lane] * view * view / (ViewSamples * ViewSamples);
                    float end = distances[lane] * (view + 1) * (view + 1) / (ViewSamples * ViewSamples);
                    steps[lane] = end - start;
                    Vector3 point = origin + rays[lane] * ((start + end) * .5f);
                    radii[lane] = point.LengthSquared();
                    lightOffsets[lane] = -1;
                    if (GroundDistance(point, sun) > 0) continue;
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
                    Vector3 viewOptical = optical[lane] + extinction[lane] * (.5f * steps[lane]);
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
                    result[lane] += transmittance * scattering * steps[lane];
                    optical[lane] += extinction[lane] * steps[lane];
                }
            }
            for (int lane = 0; lane < count; lane++) result[lane] = Vector3.Max(Vector3.Zero, result[lane] * Solar);
        }
        finally { ArrayPool<float>.Shared.Return(rented); }
    }
    #endregion
}
