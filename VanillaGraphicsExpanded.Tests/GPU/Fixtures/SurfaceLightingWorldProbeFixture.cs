using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the production world-probe atlas and its actual SPIR-V upload programs.</summary>
internal sealed class SurfaceLightingWorldProbeFixture : IDisposable
{
    private readonly EngineShaderPlatformScope platform = new();
    private readonly BinaryShaderApiFixture assets = new();
    private readonly LumOnWorldProbeClipmapResolveShaderProgram metadata = new();
    private readonly LumOnWorldProbeRadianceTileResolveShaderProgram radiance = new();
    private readonly LumOnWorldProbeClipmapGpuUploader uploader;
    public LumOnWorldProbeClipmapGpuResources Resources { get; }

    #region Resource ownership
    /// <summary>Provides shader lookup at the engine boundary while retaining the production upload implementation.</summary>
    public SurfaceLightingWorldProbeFixture(int resolution = 1)
    {
        Resources = new(assets.Api, resolution, 1, 8);
        // The uploader discovers retained owners through the production declaration library.
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(assets.Api, metadata);
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(assets.Api, radiance);
        Assert.True(metadata.EnsureReady(), string.Join('\n', assets.Logs));
        Assert.True(radiance.EnsureReady(), string.Join('\n', assets.Logs));
        uploader = new(assets.Api);
    }

    /// <summary>Publishes the actual resolved result; unresolved batches must be rejected by the caller.</summary>
    public float[] Upload(in LumOnWorldProbeTraceResult result)
    {
        Assert.True(result.Success);
        Assert.True(uploader.Upload(Resources, new[] { result }, 65536) > 0);
        return Resources.ProbeRadianceAtlas.ReadPixels();
    }

    /// <summary>Attempts an atomic publication with an explicit budget and exposes its admission count.</summary>
    public int TryUpload(in LumOnWorldProbeTraceResult result, int budget)
        => uploader.Upload(Resources, new[] { result }, budget);

    /// <summary>Publishes a matched group of admissions through one production uploader call.</summary>
    public int TryUpload(LumOnWorldProbeTraceResult[] results, int budget)
        => uploader.Upload(Resources, results, budget);

    /// <summary>Invalidates both retained executable owners so the next upload must prepare replacement pipelines.</summary>
    public void InvalidatePrograms()
    {
        metadata.InvalidateAssets();
        radiance.InvalidateAssets();
    }

    /// <summary>Retires uploads before deleting programs and their atlas resources.</summary>
    public void Dispose() { uploader.Dispose(); Resources.Dispose(); metadata.Dispose(); radiance.Dispose(); assets.Dispose(); platform.Dispose(); }
    #endregion
}
