using System.Diagnostics;
using System.Numerics;
using System.Collections.Immutable;
using System.Reflection;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks real atmospheric compute transport against the retained CPU numerical model.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereGpuComputationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Numerical publication
    /// <summary>Atmosphere's internal dispatches preserve the enclosing compute owner's storage binding.</summary>
    [Fact]
    public void CompletePublicationRestoresEnclosingComputeStorage()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var loaded, out string log), log);
        using var outer = loaded!;
        using var texture = Texture3D.Create(1, 1, 1, PixelInternalFormat.R32ui, textureTarget: TextureTarget.Texture2DArray);
        using var requests = GpuShaderStorageBuffer.Create();
        requests.EnsureCapacity(64, growExponentially: false);
        using var counter = new ComponentAtomicCounters(0, 1);
        outer.BindPageUsageStamp(texture.TextureId);
        outer.BindPageTableMip0(texture.TextureId);
        outer.BindRequestsSsbo(requests);
        outer.BindRequestCounter(counter.Buffer);
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        using var scope = outer.UseScope();
        Complete(gpu, Vector3.UnitY, 0, 0);
        // Observe the driver after the whole producer finishes, including its cleanup path.
        Assert.Equal(outer.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding, 0, out int restored);
        Assert.Equal(requests.BufferId, restored);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Every published sky sample and shared integral uses the same admitted physical inputs.</summary>
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(.3f, 1f, 2f)]
    [InlineData(-.05f, .2f, 0f)]
    [InlineData(.05f, 0f, 99f)]
    [InlineData(-.002f, 0f, .001f)]
    [InlineData(.002f, 0f, .001f)]
    public void CompletePublicationMatchesCpu(float sunY, float clouds, float altitude)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        var sun = Vector3.Normalize(new Vector3(MathF.Sqrt(1 - sunY * sunY), sunY, 0));
        var clock = Stopwatch.StartNew();
        var actual = Complete(gpu, sun, altitude, clouds);
        double gpuMilliseconds = clock.Elapsed.TotalMilliseconds;
        var cpu = new AtmosphereLookup();
        clock.Restart();
        cpu.Update(sun, altitude, clouds);
        output.WriteLine($"Complete default lookup including source table: GPU {gpuMilliseconds:F2} ms, CPU {clock.Elapsed.TotalMilliseconds:F2} ms (single diagnostic sample, not a benchmark).");
        var expected = cpu.Current!;
        Compare(expected.Solar, actual.Solar); Compare(expected.Environment, actual.Environment);
        Compare(expected.Horizon, actual.Horizon); Compare(expected.Extinction, actual.Extinction);
        Assert.Equal(expected.Sky.Length, actual.Sky.Length);
        for (int i = 0; i < expected.Sky.Length; i++) Close(expected.Sky[i], actual.Sky[i], $"sky[{i}]");
        Assert.Equal(expected.SkyMie.Length, actual.SkyMie.Length);
        for (int i = 0; i < expected.SkyMie.Length; i++) Close(expected.SkyMie[i], actual.SkyMie[i], $"sky Mie[{i}]");
        Assert.Equal(expected.AerialMie.Length, actual.AerialMie.Length);
        for (int i = 0; i < expected.AerialMie.Length; i++) Close(expected.AerialMie[i], actual.AerialMie[i], $"aerial Mie[{i}]");
        Assert.Equal(expected.Altitude, actual.Altitude);
        Assert.Equal(expected.AerialRadiance.Length, actual.AerialRadiance.Length);
        Assert.Equal(expected.AerialAttenuation.Length, actual.AerialAttenuation.Length);
        Assert.Equal(expected.Width * expected.Height * AtmosphereAerialPerspective.Depth * 4, actual.AerialRadiance.Length);
        for (int i = 0; i < expected.AerialRadiance.Length; i++)
        {
            Close(expected.AerialRadiance[i], actual.AerialRadiance[i], $"aerial radiance[{i}]");
            Close(expected.AerialAttenuation[i], actual.AerialAttenuation[i], $"aerial attenuation[{i}]");
        }
        Assert.Null(gpu.Update(sun, altitude, clouds, 32, 24));
    }

    /// <summary>Seasonal ground reflectance changes finish the admitted generation and match CPU sky and shared lighting.</summary>
    [Fact]
    public void SeasonalReflectancePreservesGenerationAndMatchesCpu()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        Assert.Null(gpu.Update(Vector3.UnitY, 0, 0, 32, 24, groundAlbedo: .1f));
        var bare = Complete(gpu, Vector3.UnitY, 0, 0, groundAlbedo: .8f);
        var snowy = Complete(gpu, Vector3.UnitY, 0, 0, groundAlbedo: .8f);
        var cpu = new AtmosphereLookup();
        cpu.Update(Vector3.UnitY, 0, 0, groundAlbedo: .1f);
        Compare(cpu.Current!.Environment, bare.Environment);
        cpu.Update(Vector3.UnitY, 0, 0, groundAlbedo: .8f);
        Compare(cpu.Current!.Environment, snowy.Environment);
        Compare(cpu.Current.Horizon, snowy.Horizon);
        Compare(cpu.Current.Solar, snowy.Solar);
        Compare(cpu.Current.Extinction, snowy.Extinction);
        for (int i = 0; i < snowy.Sky.Length; i++) Close(cpu.Current.Sky[i], snowy.Sky[i], $"snow sky[{i}]");
        Assert.True(snowy.Environment.Length() > bare.Environment.Length());
        Assert.Null(gpu.Update(Vector3.UnitY, 0, 0, 32, 24, groundAlbedo: .801f));
    }
    /// <summary>Changed sun and weather publish their complete generation after an admitted generation finishes.</summary>
    [Fact]
    public void ChangedInputsAndDisposalKeepGenerationOwnership()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var gpu = AtmosphereGpuComputation.Create(assets.Api);
        Assert.Null(gpu.Update(Vector3.UnitY, 0, 0, 32, 24));
        var first = Complete(gpu, Vector3.UnitX, 2, 1);
        Assert.Equal(Vector3.UnitY, first.Sun);
        var second = Complete(gpu, Vector3.UnitX, 2, 1);
        Assert.Equal(Vector3.UnitX, second.Sun);
        gpu.Update(Vector3.UnitY, 1, .5f, 32, 24);
        gpu.Dispose();
        Assert.Throws<ObjectDisposedException>(() => gpu.Update(Vector3.UnitY, 0, 0, 32, 24));
        using var replacement = AtmosphereGpuComputation.Create(assets.Api);
        Assert.Equal(Vector3.UnitY, Complete(replacement, Vector3.UnitY, 0, 0).Sun);
    }

    /// <summary>Missing compute assets select and complete the retained asynchronous CPU implementation once.</summary>
    [Fact]
    public void MissingComputeAssetsFallBackToCpu()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        assets.BeforeRead = path => { if (path.EndsWith(".spv", StringComparison.Ordinal)) throw new IOException("Intentional missing compute asset"); };
        using var backend = new AtmosphereBackend(assets.Api);
        Assert.Null(backend.Update(Vector3.UnitY, 0, 0, 32, 24, 0));
        var pending = Assert.IsAssignableFrom<Task<AtmosphereLighting>>(backend.PendingCpu);
        // This task contains CPU work only: blocking here preserves the owning GL context thread.
        var expected = pending.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        Assert.Same(expected, backend.Update(Vector3.UnitY, 0, 0, 32, 24, 0));
        Assert.Null(backend.PendingCpu);
        Assert.Single(assets.Logs.Where(log => log.Contains("using CPU transport")));
    }

    /// <summary>Rejects each insufficient capability independently, including bounded output storage.</summary>
    [Fact]
    public void UnsupportedCapabilitiesRejectGpuAdmission()
    {
        Assert.True(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(false, true, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, false, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, false, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 63, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 63, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 191, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 192, 2, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes - 1));
    }

    /// <summary>Solar motion reuses the medium table, while a changed weather bucket rebuilds it.</summary>
    [Fact]
    public void SunReusesSourceAndWeatherInvalidatesIt()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        Complete(gpu, Vector3.UnitY, 0, 0);
        Assert.Null(gpu.Update(Vector3.UnitX, 0, .01f, 32, 24));
        using (var fence = GpuFence.Insert()) fence.Wait(TimeSpan.FromSeconds(5));
        Assert.NotNull(gpu.Update(Vector3.UnitX, 0, .01f, 32, 24));
        Assert.Null(gpu.Update(Vector3.UnitX, 0, .1f, 32, 24));
        using (var fence = GpuFence.Insert()) fence.Wait(TimeSpan.FromSeconds(5));
        Assert.Null(gpu.Update(Vector3.UnitX, 0, .1f, 32, 24));
        Assert.Equal(Vector3.UnitX, Complete(gpu, Vector3.UnitX, 0, .1f).Sun);
    }

    /// <summary>A pending generation finishes coherently before a new quality and odd output extent are admitted.</summary>
    [Fact]
    public void PendingQualityChangePublishesWholeOddSizedLookup()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        Assert.Null(gpu.Update(Vector3.UnitY, 0, 0, 32, 24));
        var old = Complete(gpu, Vector3.UnitX, 1, .2f, 17, 9, 1);
        Assert.Equal(32, old.Width); Assert.Equal(Vector3.UnitY, old.Sun);
        var current = Complete(gpu, Vector3.UnitX, 1, .2f, 17, 9, 1);
        Assert.Equal(17, current.Width); Assert.Equal(9, current.Height);
        Assert.Equal(17 * 9 * 4, current.Sky.Length); Assert.Equal(Vector3.UnitX, current.Sun);
        for (int i = 0; i < current.Sky.Length; i++) Assert.True(float.IsFinite(current.Sky[i]));
        for (int i = 3; i < current.Sky.Length; i += 4) Assert.Equal(1f, current.Sky[i]);
    }

    /// <summary>Actual reload preserves the published texture and world reset retires it alongside pending transport.</summary>
    [Fact]
    public void ModSystemReloadAndWorldResetRespectPublishedOwnership()
    {
        EnsureContextValid();
        output.WriteLine($"Renderer: {fixture.GLRenderer}; OpenGL: {fixture.GLVersion}; runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        using var assets = new BinaryShaderApiFixture();
        var system = new AtmosphereModSystem();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var apiField = typeof(AtmosphereModSystem).GetField("api", flags)!;
        var backendField = typeof(AtmosphereModSystem).GetField("computation", flags)!;
        var backend = new AtmosphereBackend(assets.Api);
        apiField.SetValue(system, assets.Api); backendField.SetValue(system, backend);
        try
        {
            var display = new AtmosphereLighting(Vector3.UnitY, Vector3.One, Vector3.One, Vector3.One, Vector3.Zero,
                ImmutableArray.Create(.1f, .2f, .3f, 1f)) { Width = 1, Height = 1 };
            system.Publish(display);
            int texture = AtmosphereModSystem.SkyTextureId;
            Assert.NotEqual(0, texture);
            backend.Update(Vector3.UnitY, 0, 0, 32, 24, 0);
            Assert.Equal(true, typeof(AtmosphereModSystem).GetMethod("Reload", flags)!.Invoke(system, null));
            Assert.Same(display, AtmosphereModSystem.Lighting); Assert.Equal(texture, AtmosphereModSystem.SkyTextureId);
            Assert.Throws<ObjectDisposedException>(() => backend.Update(Vector3.UnitY, 0, 0, 32, 24, 0));
            var seasonField = typeof(AtmosphereModSystem).GetField("seasonInputs", flags)!;
            var previousSeasonOwner = seasonField.GetValue(system);
            typeof(AtmosphereModSystem).GetMethod("Reset", flags)!.Invoke(system, null);
            Assert.NotSame(previousSeasonOwner, seasonField.GetValue(system));
            Assert.Null(AtmosphereModSystem.Lighting); Assert.Equal(0, AtmosphereModSystem.SkyTextureId);
        }
        finally
        {
            // No event subscriptions were installed; avoid asking the fixture to unregister them.
            apiField.SetValue(system, null); system.Dispose();
        }
    }
    #endregion

    #region Completion and comparisons
    /// <summary>Waits on submitted GPU dependencies rather than adding unconditional timing delays.</summary>
    private static AtmosphereLighting Complete(AtmosphereGpuComputation gpu, Vector3 sun, float altitude, float clouds, int width = 32, int height = 24, int quality = 0, float groundAlbedo = .1f)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = gpu.Update(sun, altitude, clouds, width, height, quality, groundAlbedo);
            if (result is not null) return result;
            using var fence = GpuFence.Insert();
            Assert.Contains(fence.Wait(TimeSpan.FromSeconds(5)), new[] { WaitSyncStatus.AlreadySignaled, WaitSyncStatus.ConditionSatisfied });
        }
        throw new TimeoutException("Atmosphere GPU publication did not complete.");
    }

    /// <summary>Compares the complete shared RGB lighting domain.</summary>
    private static void Compare(Vector3 expected, Vector3 actual)
    {
        Close(expected.X, actual.X, "R"); Close(expected.Y, actual.Y, "G"); Close(expected.Z, actual.Z, "B");
    }

    /// <summary>Allows floating-point transcendental and parallel reduction differences near thin atmospheric paths.</summary>
    private static void Close(float expected, float actual, string name) => Assert.True(float.IsFinite(actual)
        && MathF.Abs(expected - actual) <= 1e-4f + .01f * MathF.Abs(expected), $"{name}: expected {expected:G9}, actual {actual:G9}");
    #endregion
}
