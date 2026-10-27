using System.Diagnostics;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production GLSL generation and validates packed storage, complete mips and deterministic resource publication.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CelestialGalaxyGenerationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>World loading has no ordinary frame ring; the generation uses and closes a bounded private epoch.</summary>
    [Fact]
    public void LoadingWithoutFrameAllocatorPublishesCompleteResources()
    {
        EnsureShaderTestAvailable();
        Assert.True(GpuUniformRingSystem.TryGetCurrent(out var previous));
        GpuUniformRingSystem.ClearCurrent();
        try
        {
            var source = CelestialSource.Create(new(3, 32, 4096, 7));
            using var result = Generate(source);
            Assert.Same(source, result.Source);
            Assert.False(GpuUniformRingSystem.TryGetCurrent(out _));
            Assert.All(Read(result.Galaxy, 0, 0), value => Assert.True(float.IsFinite(value) && value > 0));
        }
        finally { GpuUniformRingSystem.SetCurrent(previous); }
    }

    /// <summary>Exercises production load callbacks, unchanged frames, failed reload retention and world disposal.</summary>
    [Fact]
    public void LifetimeRetainsOnlyCompleteSourceOnFailedReload()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var platform = new EngineShaderPlatformScope();
        GpuShaderProgramLibraries.Declare(assets.Api, CelestialGenerationProgram.Contract.Identity, () => new CelestialGenerationProgram());
        var lifetime = new CelestialSourceLifetime(assets.Api);
        try
        {
            lifetime.StartLoading(0); Assert.Null(lifetime.Completed);
            lifetime.FinishLoading(0); AwaitLifetime(lifetime, () => lifetime.Ready is not null);
            Assert.Null(lifetime.Completed);
            var ready = lifetime.Ready!;
            for (int i = 0; i < 10; i++) lifetime.Update(0);
            Assert.Same(ready, lifetime.Ready);
            Assert.Throws<InvalidOperationException>(() => lifetime.Commit(ready.Source with { GalaxyAmplitude = 0 }));
            Assert.Null(lifetime.Completed);
            lifetime.Commit(ready.Source);
            Assert.Null(lifetime.Ready);
            var previous = lifetime.Completed!;
            int reads = assets.Reads.Count;
            for (int i = 0; i < 10; i++) lifetime.Update(0);
            Assert.Same(previous, lifetime.Completed); Assert.Equal(reads, assets.Reads.Count);
            foreach (string path in assets.Reads.Where(p => p.Contains("celestial_generate"))) assets.MissingAssets.Add(path);
            var registry = GpuShaderProgramLibraries.Require(assets.Api);
            registry.BeginReload(); lifetime.Reload(0); registry.CompleteReload();
            Assert.Same(previous, lifetime.Completed);
            AwaitLifetime(lifetime, () => assets.Logs.Any(log => log.Contains("Celestial source generation failed")));
            Assert.Same(previous, lifetime.Completed); Assert.True(previous.Galaxy.IsValid);
            int failedReads = assets.Reads.Count;
            for (int i = 0; i < 10; i++) lifetime.Update(0);
            Assert.Equal(failedReads, assets.Reads.Count);
             lifetime.Dispose(); Assert.Null(lifetime.Completed); Assert.False(previous.Galaxy.IsValid);
        }
        finally { lifetime.Dispose(); }
    }
    /// <summary>Uses a small complete-catalog fixture to qualify actual packed conversion and every mip without a full-size routine workload.</summary>
    [Fact]
    public void PackedCubePreservesSourceAndMipEnergy()
    {
        EnsureShaderTestAvailable();
        var source = CelestialSource.Create(new(3, 64, 4096, 7));
        using var first = Generate(source);
        using var second = Generate(source);
        Assert.Equal(PixelInternalFormat.R11fG11fB10f, first.Galaxy.InternalFormat);
        Vector3 reference = Vector3.Zero, stored = Vector3.Zero;
        float maximum = 0;
        for (int face = 0; face < 6; face++)
        {
            float[] actual = Read(first.Galaxy, face, 0);
            Assert.Equal(actual, Read(second.Galaxy, face, 0));
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                Vector3 d = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / 64 - 1, 2f * (y + .5f) / 64 - 1);
                Vector3 expected = CelestialProceduralField.Evaluate(d, 7, 2f / 64) * source.GalaxyAmplitude;
                int index = (y * 64 + x) * 3;
                Vector3 value = new(actual[index], actual[index + 1], actual[index + 2]); value /= source.StorageScale;
                AssertFinite(value, source.MaximumNits);
                Assert.InRange(MathF.Abs(value.X / expected.X - 1), 0, .0101f);
                Assert.InRange(MathF.Abs(value.Y / expected.Y - 1), 0, .0101f);
                Assert.InRange(MathF.Abs(value.Z / expected.Z - 1), 0, .0201f);
                float area = CelestialCubeGeometry.SolidAngle(x, y, 64);
                reference += expected * area; stored += value * area; maximum = Math.Max(maximum, Math.Max(value.X, Math.Max(value.Y, value.Z)));
            }
        }
        AssertEnergy(reference, stored, .01f);
        for (int mip = 1; mip < first.Galaxy.StorageMipLevels; mip++)
        {
            int size = Math.Max(1, 64 >> mip); Vector3 integrated = Vector3.Zero;
            for (int face = 0; face < 6; face++)
            {
                float[] data = Read(first.Galaxy, face, mip);
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    int index = (y * size + x) * 3;
                    Vector3 value = new(data[index], data[index + 1], data[index + 2]); value /= source.StorageScale;
                    AssertFinite(value, maximum * 1.02f);
                    integrated += value * CelestialCubeGeometry.SolidAngle(x, y, size);
                }
            }
            AssertEnergy(stored, integrated, .01f);
        }
    }

    /// <summary>Qualifies selected dimensions, omitted-star flux and loading cost only when explicitly requested.</summary>
    [Fact]
    public void FullQualityGenerationQualification()
    {
        if (Environment.GetEnvironmentVariable("VGE_RUN_CELESTIAL_QUALIFICATION") != "1")
            Assert.Skip("Set VGE_RUN_CELESTIAL_QUALIFICATION=1 for full-size celestial generation qualification.");
        EnsureShaderTestAvailable();
        output.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}, GL: {GL.GetString(StringName.Version)}");
        for (int quality = 0; quality < 4; quality++)
        {
            var clock = Stopwatch.StartNew();
            var source = CelestialSource.Create(CelestialQuality.FromLevel(quality));
            using var timer = GpuTimerQuery.Create(); timer.Begin();
            using var result = Generate(source); timer.End();
            double device = timer.GetResultNanoseconds() / 1e6;
            output.WriteLine($"Quality {quality}: {source.Quality.FaceSize}px, GPU {device:F2} ms, wall {clock.Elapsed.TotalMilliseconds:F2} ms, map {MapBytes(result.Galaxy) / 1048576.0:F2} MiB");
            double[] integratedChannels = new double[3];
            for (int face = 0; face < 6; face++)
            {
                float[] data = Read(result.Galaxy, face, 0); int size = result.Galaxy.Width;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    int i = (y * size + x) * 3; Vector3 value = new(data[i], data[i + 1], data[i + 2]); value /= source.StorageScale;
                    AssertFinite(value, source.MaximumNits * 1.02f);
                    AddEnergy(integratedChannels, value, CelestialCubeGeometry.SolidAngle(x, y, size));
                }
            }
            Vector3 expected = source.Quadrature.Aggregate(Vector3.Zero, (sum, s) => sum + s.Radiance * s.SolidAngle);
            for (int i = source.Quality.StarCount; i < source.Stars.Length; i++) expected += source.Stars[i].Color * source.Stars[i].FluxLux;
            Vector3 integrated = new((float)integratedChannels[0], (float)integratedChannels[1], (float)integratedChannels[2]);
            AssertEnergy(expected, integrated, .01f);
            // Check the real non-power-of-two mip chain as well as final map normalization.
            for (int mip = 1; mip < result.Galaxy.StorageMipLevels; mip++)
            {
                int size = Math.Max(1, result.Galaxy.Width >> mip); double[] mipChannels = new double[3];
                for (int face = 0; face < 6; face++)
                {
                    float[] data = Read(result.Galaxy, face, mip);
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    {
                        int i = (y * size + x) * 3; Vector3 value = new(data[i], data[i + 1], data[i + 2]);
                        value /= source.StorageScale; AssertFinite(value, source.MaximumNits * 1.02f);
                        AddEnergy(mipChannels, value, CelestialCubeGeometry.SolidAngle(x, y, size));
                    }
                }
                AssertEnergy(integrated, new((float)mipChannels[0], (float)mipChannels[1], (float)mipChannels[2]), .01f);
            }
            if (quality == 3) Assert.InRange(device, 0, 1000);
        }
    }
    #endregion

    #region Private
    /// <summary>Polls the runtime lifecycle at a controlled cadence without adding a production spin-wait.</summary>
    private static void AwaitLifetime(CelestialSourceLifetime lifetime, Func<bool> completed)
    {
        var clock = Stopwatch.StartNew();
        while (!completed() && clock.Elapsed < TimeSpan.FromSeconds(30)) { lifetime.Update(0); Thread.Sleep(1); }
        Assert.True(completed(), "Celestial lifecycle did not reach its expected publication/failure.");
    }
    /// <summary>Uses double accumulation so multi-million-texel qualification does not introduce FP32 summation drift.</summary>
    private static void AddEnergy(double[] channels, Vector3 value, double area)
    {
        channels[0] += value.X * area; channels[1] += value.Y * area; channels[2] += value.Z * area;
    }
    /// <summary>Submits the production tiled build and waits between single polls only in this headless test.</summary>
    private CelestialResourceSet Generate(CelestialSource source)
    {
        var program = Programs.Create<CelestialGenerationProgram>();
        using var generation = new CelestialGalaxyGeneration(program, source);
        generation.Submit();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            var ready = generation.TryComplete(); if (ready is not null) return ready;
            Thread.Sleep(1);
        }
        throw new TimeoutException("Celestial generation did not complete.");
    }

    /// <summary>Reads one actual packed face/mip through a preserved binding for numeric qualification.</summary>
    private static float[] Read(DynamicTextureCube cube, int face, int mip)
    {
        int size = Math.Max(1, cube.Width >> mip); float[] data = new float[size * size * 3];
        using var binding = StateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap, 0, cube.TextureId);
        GL.GetTexImage(TextureTarget.TextureCubeMapPositiveX + face, mip, PixelFormat.Rgb, PixelType.Float, data);
        return data;
    }
    /// <summary>Checks each physical channel rather than accepting a finite value produced by a hidden safety clamp.</summary>
    private static void AssertFinite(Vector3 value, float maximum)
    {
        Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z));
        Assert.InRange(value.X, 0, maximum); Assert.InRange(value.Y, 0, maximum); Assert.InRange(value.Z, 0, maximum);
    }
    /// <summary>Checks solid-angle RGB energy independently for each channel.</summary>
    private static void AssertEnergy(Vector3 reference, Vector3 actual, float tolerance)
    {
        Assert.InRange(MathF.Abs(actual.X / reference.X - 1), 0, tolerance);
        Assert.InRange(MathF.Abs(actual.Y / reference.Y - 1), 0, tolerance);
        Assert.InRange(MathF.Abs(actual.Z / reference.Z - 1), 0, tolerance);
    }
    /// <summary>Accounts for every allocated face/mip including the non-power-of-two chain.</summary>
    private static long MapBytes(DynamicTextureCube cube)
    {
        long bytes = 0;
        for (int mip = 0; mip < cube.StorageMipLevels; mip++) { long size = Math.Max(1, cube.Width >> mip); bytes += size * size * 6 * 4; }
        return bytes;
    }
    #endregion
}
