using System;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Collections;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns the render-thread compute commit for resident lighting and CPU-resolved fallback directions.</summary>
internal sealed class WorldProbeHybridCommit : IDisposable
{
    private readonly GpuComputePipeline pipeline;
    private readonly GpuShaderStorageBuffer staging = GpuShaderStorageBuffer.Create(BufferUsageHint.StreamDraw);

    /// <summary>Describes one probe's target and geometry metadata in three aligned vectors.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    { public int X, Y, TileSize, Count; public Vector4 Ao, Scalars; }

    /// <summary>Provides a resident ray index or CPU value for exactly one ready directional texel.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Sample
    { public int X, Y, Ray, Reserved; public Vector4 Value; }

    #region Resource lifetime
    /// <summary>Loads the commit program before any atlas write can be submitted.</summary>
    public WorldProbeHybridCommit(ICoreAPI api)
    {
        if (!GpuComputePipeline.TryCreateFromAssets(api, WorldProbeCommitShader.Contract.Identity,
            out var program, out _, out string log, preferSpirv: true))
        { staging.Dispose(); throw new InvalidOperationException(log); }
        pipeline = program!;
    }

    /// <summary>Releases owned staging and program resources.</summary>
    public void Dispose() { staging.Dispose(); pipeline.Dispose(); }
    #endregion

    #region Publication
    /// <summary>Validates the complete admission before writing directions and then metadata in one workgroup.</summary>
    public bool Commit(LumOnWorldProbeClipmapGpuResources resources, in LumOnWorldProbeTraceResult result)
    {
        var lease = result.GpuLease;
        if (!result.Success || result.AtlasSamples.IsDefaultOrEmpty || lease == null || !lease.IsCurrent) return false;
        var storage = result.Request.StorageIndex;
        int resolution = resources.Resolution, size = resources.WorldProbeTileSize;
        if ((uint)result.Request.Level >= (uint)resources.Levels || (uint)storage.X >= resolution ||
            (uint)storage.Y >= resolution || (uint)storage.Z >= resolution) return false;
        using var samples = PooledArray<Sample>.Rent(result.AtlasSamples.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = result.AtlasSamples[i];
            if (sample.SurfaceHit.HasValue || (uint)sample.OctX >= size || (uint)sample.OctY >= size ||
                (sample.GpuRayIndex >= 0 && (sample.GpuRayIndex < lease.First || sample.GpuRayIndex >= lease.First + lease.Count))) return false;
            samples.Span[i] = new Sample { X = sample.OctX, Y = sample.OctY, Ray = sample.GpuRayIndex,
                Value = new Vector4(sample.RadianceRgb, sample.AlphaEncodedDistSigned) };
        }
        uint flags = LumOnWorldProbeMetaFlags.Valid;
        if (result.MeanLogHitDistance <= 0 && result.ShortRangeAoConfidence > .99f) flags |= LumOnWorldProbeMetaFlags.SkyOnly;
        var header = new Header { X = storage.X + storage.Z * resolution, Y = storage.Y + result.Request.Level * resolution,
            TileSize = size, Count = samples.Length, Ao = new Vector4(result.ShortRangeAoDirWorld, result.ShortRangeAoConfidence),
            Scalars = new Vector4(result.SkyIntensity, result.Confidence, result.MeanLogHitDistance, BitConverter.UInt32BitsToSingle(flags)) };
        int bytes = 48 + (samples.Length << 5);
        staging.EnsureCapacity(bytes);
        staging.UploadSubData(MemoryMarshal.CreateReadOnlySpan(ref header, 1), 0, 48);
        staging.UploadSubData<Sample>(samples.Span, 48, samples.Length << 5);
        // Providers are render-thread-owned; recheck immediately before issuing the ordered commit.
        if (!lease.IsCurrent) return false;
        using (pipeline.UseScope())
        using (GpuImageUnitBinding.Bind(0, resources.ProbeRadianceAtlasTextureId, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f))
        using (GpuImageUnitBinding.Bind(1, resources.ProbeVis0TextureId, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f))
        using (GpuImageUnitBinding.Bind(2, resources.ProbeDist0TextureId, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rg16f))
        using (GpuImageUnitBinding.Bind(3, resources.ProbeMeta0TextureId, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rg32f))
        {
            lease.Answers.Bind(0); staging.BindRange(1, 0, bytes);
            GL.DispatchCompute(1, 1, 1);
        }
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit |
            MemoryBarrierFlags.FramebufferBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
        return true;
    }
    #endregion
}
