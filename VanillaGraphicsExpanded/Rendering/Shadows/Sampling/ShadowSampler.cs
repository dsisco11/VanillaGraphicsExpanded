using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

/// <summary>Dispatches the complete shared filter without optional capabilities or production result readback.</summary>
internal sealed class ShadowSampler : IDisposable
{
    private readonly ShadowSamplingShader shader;
    private readonly RHIShaderRef executable;
    private readonly GpuShaderStorageBuffer inputs;
    private bool disposed;
    /// <summary>Visibility, failure mask, tap count, stale-tap mask and nine face/page/reference-depth records.</summary>
    internal GpuShaderStorageBuffer Results { get; }
    /// <summary>Charges all owned query/output storage to its scheduling owner.</summary>
    internal long AllocatedBytes => 640 + 160;
    #region Public API
    /// <summary>Creates the bounded query/output buffers through RHI.</summary>
    internal ShadowSampler(ICoreAPI api)
    {
        shader = ShadowSamplingShader.Create(api); executable = shader.Shader;
        try
        {
            inputs = Buffer(640, "Inputs"); Results = Buffer(160, "Results");
        }
        catch { shader.Dispose(); inputs?.Dispose(); Results?.Dispose(); throw; }
    }
    /// <summary>Samples all nine taps; any invalid tap yields diagnosed unshadowed visibility.</summary>
    internal void Sample(ShadowPagePool pool, ShadowSampleRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!pool.CanSubmit || pool.ArrayLayers < 1 || request.Words[65] != pool.PageSize)
            throw new InvalidOperationException("Sampler and page storage are unavailable or incompatible.");
        if (!shader.EnsureReady() || shader.Shader != executable)
        { pool.Withdraw(); throw new InvalidOperationException("Sampling shader replacement requires a fresh sampler and withdrawn readiness."); }
        uint[] words = (uint[])request.Words.Clone(); words[116] = (uint)pool.Capacity; words[117] = (uint)pool.ProbeLimit;
        words[118] = (uint)pool.TilesPerLayer; words[119] = (uint)pool.ArrayLayers;
        inputs.UploadSubData<uint>(words, 0);
        var binding = (IShadowSamplingBindings)shader; binding.Inputs = inputs; binding.Entries = pool.Entries; binding.Results = Results;
        binding.Depth = pool.DepthArray;
        var rhi = RHICommandContext.Current;
        using var boundary = rhi.EnterBoundary(new("Shadows.Sample", true, compute: [shader.Pipeline]));
        boundary.Run(() => { shader.Dispatch(1); rhi.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate)); });
    }
    /// <summary>Releases only query resources; the depth pool remains owned by its coordinator.</summary>
    public void Dispose() { if (disposed) return; disposed = true; shader.Dispose(); inputs.Dispose(); Results.Dispose(); }
    #endregion
    #region Private
    /// <summary>Creates fixed-capacity mutable storage for one bounded query.</summary>
    private static GpuShaderStorageBuffer Buffer(int bytes, string name) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = bytes,
            Usage = BufferUsage.DynamicDraw, Label = "Shadows.Sample." + name }).RequireResource();
    #endregion
}
