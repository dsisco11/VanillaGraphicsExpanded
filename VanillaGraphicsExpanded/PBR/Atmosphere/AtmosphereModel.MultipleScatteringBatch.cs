using System;
using System.Buffers;
using System.Numerics;
using System.Numerics.Tensors;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Batches isotropic transport and its nested solar integrals through width-independent tensor operations.</summary>
internal static partial class AtmosphereModel
{
    #region Batched isotropic transport
    /// <summary>Evaluates independent rays with bounded pooled scratch and preserves per-ray integration order.</summary>
    internal static void MultipleScatteringTransferBatch(ReadOnlySpan<Vector3> directions, Vector3 sun,
        float altitude, float aerosol, float groundAlbedo, int samples, int lightSamples,
        Span<Vector3> sources, Span<Vector3> feedback, CancellationToken cancellationToken = default)
    {
        if (sources.Length < directions.Length || feedback.Length < directions.Length)
            throw new ArgumentException("Transport output spans must cover all directions.");
        if (samples < 2 || samples > 256) throw new ArgumentOutOfRangeException(nameof(samples));
        if (lightSamples < 2 || lightSamples > 96) throw new ArgumentOutOfRangeException(nameof(lightSamples));
        for (int offset = 0; offset < directions.Length; offset += BatchCapacity)
        {
            int count = Math.Min(BatchCapacity, directions.Length - offset);
            IntegrateMultipleScatteringBatch(directions.Slice(offset, count), sun, altitude, aerosol,
                groundAlbedo, samples, lightSamples, sources.Slice(offset, count), feedback.Slice(offset, count), cancellationToken);
        }
    }

    /// <summary>Compacts unoccluded solar paths and vectorizes density and attenuation while keeping analytic segment integration.</summary>
    private static void IntegrateMultipleScatteringBatch(ReadOnlySpan<Vector3> directions, Vector3 sun,
        float altitude, float aerosol, float groundAlbedo, int samples, int lightSamples,
        Span<Vector3> sources, Span<Vector3> feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = directions.Length, capacity = count * (lightSamples + 1);
        float[] scratch = ArrayPool<float>.Shared.Rent(capacity * 5 + count * 6);
        try
        {
            Span<float> radii = scratch.AsSpan(0, capacity), heights = scratch.AsSpan(capacity, capacity);
            Span<float> molecular = scratch.AsSpan(capacity * 2, capacity), aerosols = scratch.AsSpan(capacity * 3, capacity);
            Span<float> weights = scratch.AsSpan(capacity * 4, capacity), attenuation = scratch.AsSpan(capacity * 5, count * 6);
            Span<float> distances = stackalloc float[count], grounds = stackalloc float[count], steps = stackalloc float[count];
            Span<int> offsets = stackalloc int[count];
            Span<Vector3> throughput = stackalloc Vector3[count], extinction = stackalloc Vector3[count];
            Vector3 origin = new(0, GroundRadius + altitude, 0);
            throughput.Fill(Vector3.One); sources.Clear(); feedback.Clear();
            for (int lane = 0; lane < count; lane++)
            {
                grounds[lane] = GroundDistance(origin, directions[lane]);
                distances[lane] = grounds[lane] > 0 ? grounds[lane] : Boundary(origin, directions[lane], TopRadius);
            }
            for (int step = 0; step < samples; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int occupied = count;
                for (int lane = 0; lane < count; lane++)
                {
                    float start = distances[lane] * step * step / (samples * samples);
                    float end = distances[lane] * (step + 1) * (step + 1) / (samples * samples);
                    steps[lane] = end - start;
                    Vector3 point = origin + directions[lane] * ((start + end) * .5f);
                    radii[lane] = point.LengthSquared(); offsets[lane] = -1;
                    if (GroundDistance(point, sun) > 0) continue;
                    offsets[lane] = occupied;
                    float distance = Boundary(point, sun, TopRadius);
                    for (int light = 0; light < lightSamples; light++, occupied++)
                    {
                        float a = distance * light * light / (lightSamples * lightSamples);
                        float b = distance * (light + 1) * (light + 1) / (lightSamples * lightSamples);
                        radii[occupied] = (point + sun * ((a + b) * .5f)).LengthSquared();
                        weights[occupied] = b - a;
                    }
                }
                // View slots retain unit weights; nested solar slots integrate density columns.
                // TensorPrimitives selects SIMD width and handles arbitrary final batch sizes.
                var h = heights[..occupied];
                TensorPrimitives.Sqrt(radii[..occupied], h);
                TensorPrimitives.Subtract(h, GroundRadius, h);
                TensorPrimitives.Clamp(h, 0f, float.MaxValue, h);
                TensorPrimitives.Divide(h, -8f, molecular[..occupied]);
                TensorPrimitives.Exp(molecular[..occupied], molecular[..occupied]);
                TensorPrimitives.Divide(h, -1.2f, aerosols[..occupied]);
                TensorPrimitives.Exp(aerosols[..occupied], aerosols[..occupied]);
                TensorPrimitives.Subtract(h, 25f, h);
                TensorPrimitives.Abs(h, h);
                TensorPrimitives.Divide(h, 15f, h);
                TensorPrimitives.Multiply(h, -1f, h);
                TensorPrimitives.Add(h, 1f, h);
                TensorPrimitives.Clamp(h, 0f, 1f, h);
                weights[..count].Fill(1f);
                TensorPrimitives.Multiply(molecular[..occupied], weights[..occupied], molecular[..occupied]);
                TensorPrimitives.Multiply(aerosols[..occupied], weights[..occupied], aerosols[..occupied]);
                TensorPrimitives.Multiply(h, weights[..occupied], h);
                for (int lane = 0; lane < count; lane++)
                {
                    extinction[lane] = Extinction(new(molecular[lane], aerosols[lane], h[lane]), aerosol);
                    Vector3 column = Vector3.Zero;
                    if (offsets[lane] >= 0)
                        for (int light = offsets[lane]; light < offsets[lane] + lightSamples; light++)
                            column += new Vector3(molecular[light], aerosols[light], h[light]);
                    Vector3 optical = Extinction(column, aerosol);
                    Vector3 segment = extinction[lane] * steps[lane];
                    int index = lane * 3, solar = count * 3 + index;
                    attenuation[index] = -segment.X; attenuation[index + 1] = -segment.Y; attenuation[index + 2] = -segment.Z;
                    attenuation[solar] = -optical.X; attenuation[solar + 1] = -optical.Y; attenuation[solar + 2] = -optical.Z;
                }
                TensorPrimitives.Exp(attenuation, attenuation);
                for (int lane = 0; lane < count; lane++)
                {
                    int index = lane * 3, solar = count * 3 + index;
                    Vector3 segment = new(attenuation[index], attenuation[index + 1], attenuation[index + 2]);
                    Vector3 integral = new(SegmentIntegral(extinction[lane].X, steps[lane], segment.X),
                        SegmentIntegral(extinction[lane].Y, steps[lane], segment.Y), SegmentIntegral(extinction[lane].Z, steps[lane], segment.Z));
                    Vector3 scattering = Rayleigh * molecular[lane] + new Vector3(.003996f * aerosol * aerosols[lane]);
                    Vector3 response = throughput[lane] * scattering * integral;
                    feedback[lane] += response;
                    if (offsets[lane] >= 0)
                        sources[lane] += response * new Vector3(attenuation[solar], attenuation[solar + 1], attenuation[solar + 2]) / (4f * MathF.PI);
                    throughput[lane] *= segment;
                }
            }
            // Ground is evaluated only once per ray, retaining the scalar boundary contract.
            for (int lane = 0; lane < count; lane++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (grounds[lane] <= 0) continue;
                Vector3 normal = Vector3.Normalize(origin + directions[lane] * grounds[lane]);
                Vector3 reflected = throughput[lane] * groundAlbedo;
                sources[lane] += reflected * Transmittance(normal * (GroundRadius + .001f), sun, aerosol, lightSamples)
                    * (MathF.Max(0, Vector3.Dot(normal, sun)) / MathF.PI);
                feedback[lane] += reflected;
            }
        }
        finally { ArrayPool<float>.Shared.Return(scratch); }
    }

    /// <summary>Uses precomputed tensor attenuation with the same stable thin-segment limit as the scalar reference.</summary>
    private static float SegmentIntegral(float extinction, float distance, float transmission)
    {
        float optical = extinction * distance;
        return optical < .001f ? distance * (1f - optical * .5f + optical * optical / 6f)
            : (1f - transmission) / extinction;
    }
    #endregion
}
