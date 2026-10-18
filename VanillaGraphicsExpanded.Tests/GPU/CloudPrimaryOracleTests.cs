using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Integrates independently enumerated native intervals with a fixed fine OIT schedule.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudPrimaryOracleTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks midpoint transport against a stable detailed-density oracle without changing its bucket target.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseReferenceMatchesIndependentFixedBucketIntegration(bool detail)
    {
        EnsureShaderTestAvailable();
        using var scene = new CloudReconstructionScene(8, 8, Programs.Create<CloudVolumeProgram>(),
            Programs.Create<CloudTraceProgram>(), Programs.Create<CloudTemporalProgram>(),
            Programs.Create<CloudResolveProgram>(), false, 4);
        using var native = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba32f);
        using var volume = new CloudVolumeParameters();
        Vector4[] columns = Enumerable.Repeat(new Vector4(2, 0, -.5f, .5f), 16).ToArray();
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
        volume.CaptureMap(4, new CloudPlacement(0, 0, 0, 0, 0, 0, 0, 0, new(0, 0, -50), 0),
            1, CloudVolumeSettings.Default with { Detail = detail });
        int shader = BuiltShaderFixture.LoadFixture("tests/cloud-traversal.csh", ShaderType.ComputeShader);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            scene.SetReferenceSamples(384);
            scene.Capture(0, 2, 10, 1, 0, detail ? "detail" : "static");
            scene.RenderReference(); float[][] coarse = scene.ReadReference();
            scene.SetReferenceSamples(768);
            scene.Capture(0, 2, 10, 1, 0, detail ? "detail" : "static");
            scene.RenderReference(); float[][] fine = scene.ReadReference();
            double oracleStability = 0, referenceStability = 0, worstTransmission = 0, worstPayload = 0;
            var targets = new double[64][];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                Vector3 direction = Vector3.Normalize(new((2 * (x + .5f) / 8 - 1) / 4,
                    (2 * (y + .5f) / 8 - 1) / 4, -1));
                double[] a = Integrate(program, native, volume, columns, direction, 384);
                double[] b = Integrate(program, native, volume, columns, direction, 768);
                targets[y * 8 + x] = b;
                double adaptive = AdaptiveOpticalDepth(program, native, volume, columns, direction);
                Assert.InRange(Math.Abs(Math.Exp(-adaptive) - b[3]), 0, .002);
                int pixel = y * 8 + x;
                for (int channel = 0; channel < 4; channel++)
                {
                    oracleStability = Math.Max(oracleStability, Math.Abs(a[channel] - b[channel]));
                    int layer = channel == 3 ? 1 : 0;
                    int index = channel == 3 ? pixel : pixel * 3 + channel;
                    referenceStability = Math.Max(referenceStability, Math.Abs(coarse[layer][index] - fine[layer][index]));
                    worstTransmission = Math.Max(worstTransmission, Math.Abs(fine[layer][index] - b[channel]));
                }
                double alpha = 1 - b[3];
                // Native Glow blends its authored opacity with source-alpha once more.
                worstPayload = Math.Max(worstPayload, Math.Abs(fine[2][pixel * 4 + 3] - alpha * alpha));
                for (int bucket = 0; bucket < 3; bucket++) for (int c = 0; c < 4; c++)
                {
                    double expected = alpha * (1 - b[bucket]) * (c == 3 ? 1 : 10);
                    double residual = Math.Abs(fine[3 + bucket][pixel * 4 + c] - expected) / (c == 3 ? 1 : 10);
                    worstPayload = Math.Max(worstPayload, residual);
                }
            }
            output.WriteLine($"Independent fixed bins: oracle384/768={oracleStability:G9}, reference384/768={referenceStability:G9}, transmission={worstTransmission:G9}, normalizedPayload={worstPayload:G9}");
            Assert.InRange(oracleStability, 0, .002);
            Assert.InRange(referenceStability, 0, .002);
            Assert.InRange(worstTransmission, 0, .01);
            Assert.InRange(worstPayload, 0, .01);
            var means = Enumerable.Range(0, 16).Select(_ => new double[20]).ToArray();
            for (uint frame = 0; frame < 128; frame++)
            {
                scene.Capture(frame, 2, 10, 1, 0, detail ? "detail" : "static");
                scene.RenderTrace(frame, fixedPhase: true);
                float[][] payload = scene.ReadTrace();
                float[][] physical = scene.ReadTraceRecord();
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    for (int c = 0; c < 3; c++) means[pixel][c] += physical[0][pixel * 4 + c] / 128.0;
                    means[pixel][3] += payload[1][pixel] / 128.0;
                    means[pixel][19] += payload[2][pixel * 4 + 3] / 128.0;
                    for (int bucket = 0; bucket < 3; bucket++)
                    {
                        means[pixel][4 + bucket] += payload[0][pixel * 3 + bucket] / 128.0;
                        for (int c = 0; c < 4; c++)
                            means[pixel][7 + bucket * 4 + c] += payload[3 + bucket][pixel * 4 + c] / 128.0;
                    }
                    Assert.All(payload.SelectMany(layer => layer), value => Assert.True(float.IsFinite(value) && value >= 0));
                }
            }
            double meanAttenuation = 0, meanRgb = 0, meanAlpha = 0;
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                double[] target = targets[y * 2 * 8 + x * 2], mean = means[y * 4 + x];
                double alpha = 1 - target[3];
                meanAttenuation = Math.Max(meanAttenuation, Math.Abs(mean[3] - target[3]));
                meanAlpha = Math.Max(meanAlpha, Math.Abs(mean[19] - alpha));
                for (int c = 0; c < 3; c++) meanRgb = Math.Max(meanRgb, Math.Abs(mean[c] / 10 - alpha));
                for (int bucket = 0; bucket < 3; bucket++)
                {
                    meanAttenuation = Math.Max(meanAttenuation, Math.Abs(mean[4 + bucket] - target[bucket]));
                    for (int c = 0; c < 4; c++)
                    {
                        double error = Math.Abs(mean[7 + bucket * 4 + c] / (c == 3 ? 1 : 10) - alpha * (1 - target[bucket]));
                        if (c == 3) meanAlpha = Math.Max(meanAlpha, error);
                        else meanRgb = Math.Max(meanRgb, error);
                    }
                }
            }
            output.WriteLine($"128 frames / eight masks: independent-target mean attenuation={meanAttenuation:G9}, normalizedRGB={meanRgb:G9}, alpha={meanAlpha:G9}");
            Assert.InRange(meanAttenuation, 0, .01);
            Assert.InRange(meanRgb, 0, .02);
            Assert.InRange(meanAlpha, 0, .01);
        }
        finally
        {
            GL.UseProgram(0); GL.DeleteProgram(program); GL.DeleteShader(shader);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    #region Independent integration
    /// <summary>Adapts Simpson subdivisions inside native boxes using independently requested density values and local error estimates.</summary>
    private static double AdaptiveOpticalDepth(int program, DynamicTexture2D native, CloudVolumeParameters volume,
        Vector4[] columns, Vector3 direction)
    {
        Vector3 origin = new(100, 0, 150);
        var active = new List<(double A, double B, double Fa, double Fm, double Fb, double Tolerance)>();
        foreach (var box in CloudTraversalOracle.Intersections(columns, 4, origin, direction, 2000))
        {
            // Use one-sided boundary values: adjacent native boxes can have jumps at a measure-zero face.
            double epsilon = Math.Min(1e-4, (box.Far - box.Near) * 1e-5);
            float[] values = Query(program, native, volume, origin, direction,
                [(float)(box.Near + epsilon), (float)((box.Near + box.Far) / 2), (float)(box.Far - epsilon)]);
            active.Add((box.Near, box.Far, values[0], values[1], values[2], 1e-6));
        }
        double sum = 0;
        for (int depth = 0; active.Count > 0; depth++)
        {
            Assert.True(depth < 18, "Independent adaptive density integral did not converge.");
            float[] positions = active.SelectMany(node => new[] {
                (float)((3 * node.A + node.B) / 4), (float)((node.A + 3 * node.B) / 4) }).ToArray();
            float[] samples = Query(program, native, volume, origin, direction, positions);
            var next = new List<(double A, double B, double Fa, double Fm, double Fb, double Tolerance)>();
            for (int i = 0; i < active.Count; i++)
            {
                var n = active[i]; double h = n.B - n.A, midpoint = (n.A + n.B) / 2;
                double left = h * (n.Fa + 4 * samples[2 * i] + n.Fm) / 12;
                double right = h * (n.Fm + 4 * samples[2 * i + 1] + n.Fb) / 12;
                double whole = h * (n.Fa + 4 * n.Fm + n.Fb) / 6;
                double difference = left + right - whole;
                if (Math.Abs(difference) <= 15 * n.Tolerance) sum += left + right + difference / 15;
                else
                {
                    next.Add((n.A, midpoint, n.Fa, samples[2 * i], n.Fm, n.Tolerance / 2));
                    next.Add((midpoint, n.B, n.Fm, samples[2 * i + 1], n.Fb, n.Tolerance / 2));
                }
            }
            active = next;
        }
        return sum;
    }

    /// <summary>Refines density inside independently enumerated native boxes while retaining 8192 fixed bucket intervals.</summary>
    private static double[] Integrate(int program, DynamicTexture2D native, CloudVolumeParameters volume,
        Vector4[] columns, Vector3 direction, int subdivisions)
    {
        Vector3 origin = new(100, 0, 150);
        var intervals = CloudTraversalOracle.Intersections(columns, 4, origin, direction, 2000);
        var curves = new List<(double Near, double Far, float[] Density)>();
        foreach (var interval in intervals)
        {
            float[] positions = Enumerable.Range(0, subdivisions + 1)
                .Select(i => (float)(interval.Near + (interval.Far - interval.Near) * i / subdivisions)).ToArray();
            curves.Add((interval.Near, interval.Far, Query(program, native, volume, origin, direction, positions)));
        }
        double end = intervals.Count == 0 ? 0 : intervals.Max(interval => interval.Far);
        double[] tau = new double[4];
        const int fixedBins = 8192;
        double ds = end / fixedBins;
        for (int i = 0; i < fixedBins; i++)
        {
            double t = (i + .5) * ds, sigma = 0;
            foreach (var curve in curves)
            {
                if (t < curve.Near || t >= curve.Far) continue;
                double u = (t - curve.Near) / (curve.Far - curve.Near) * subdivisions;
                int j = Math.Min(subdivisions - 1, (int)u);
                sigma = curve.Density[j] + (curve.Density[j + 1] - curve.Density[j]) * (u - j);
                break;
            }
            double depth = t > 500 ? t - 250 : t * t * t * (1 - t / 1000) / 250000;
            double bin = Math.Log(depth / 30 + 1);
            for (int bucket = 0; bucket < 3; bucket++)
                tau[bucket] += sigma * ds * (bucket == 2 && bin > 2 ? 1 : Math.Exp(-Math.Pow((bin - bucket) / .832, 2)));
            tau[3] += sigma * ds;
        }
        return tau.Select(value => Math.Exp(-value)).ToArray();
    }
    #endregion

    #region Density queries
    /// <summary>Queries the unchanged GPU density definition at CPU-selected positions without using primary quadrature.</summary>
    private static float[] Query(int program, DynamicTexture2D native, CloudVolumeParameters volume,
        Vector3 origin, Vector3 direction, float[] positions)
    {
        var result = new float[positions.Length];
        using var ray = new PackedUniformBuffer(32);
        using var samples = GpuShaderStorageBuffer.Create();
        for (int start = 0; start < positions.Length; start += 402)
        {
            int count = Math.Min(402, positions.Length - start);
            var data = new Vector4[403];
            for (int i = 0; i < count; i++) data[i].X = positions[start + i];
            samples.UploadData(MemoryMarshal.Cast<Vector4, float>(data.AsSpan()).ToArray());
            float[] inputs = [origin.X, origin.Y, origin.Z, count, direction.X, direction.Y, direction.Z, 6];
            ray.SetBytes(MemoryMarshal.AsBytes(inputs.AsSpan()));
            GL.UseProgram(program); native.Bind(0);
            Assert.True(volume.TryBindToSlot(28)); Assert.True(ray.TryBindToSlot(16)); samples.BindBase(0);
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
            using (StateCache.Current.BindBufferScope(BufferTarget.ShaderStorageBuffer, samples.BufferId))
                GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, data.Length * 16, data);
            for (int i = 0; i < count; i++) result[start + i] = data[i].Z;
        }
        GL.UseProgram(0); StateCache.Current.InvalidateAll();
        return result;
    }
    #endregion
    #endregion
}
