using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Executes one bounded lighting operation with disjoint previous and next radiance storage.</summary>
internal sealed class SurfaceLightingDispatch : IDisposable
{
    private readonly GpuComputePipeline pipeline;
    private readonly TraceGeometryComputeBindings geometry = new();
    private readonly GpuUniformBuffer parameters = GpuUniformBuffer.Create(debugName: "SurfaceLighting.Parameters");
    private readonly byte[] bytes = new byte[96];
    private readonly GpuShaderStorageBuffer disabledFallback = GpuShaderStorageBuffer.Create(debugName: "SurfaceLighting.DisabledFallback");
    public SurfaceWorkDiagnostics Diagnostics { get; } = new();

    #region Execution
    /// <summary>Loads the packaged SPIR-V producer through its shader-owned contract.</summary>
    public SurfaceLightingDispatch(ICoreAPI api)
    {
        if (!GpuComputePipeline.TryCreateFromAssets(api, LumonSceneSurfaceLightingShader.Contract.Identity,
            out var created, out _, out string log, preferSpirv: true))
            throw new InvalidOperationException(log);
        pipeline = created!;
        disabledFallback.EnsureCapacity(32, growExponentially:false);
        disabledFallback.UploadSubData<uint>(new uint[8],0,32);
    }

    /// <summary>Binds a coherent input snapshot and writes only the explicitly selected page batches.</summary>
    public void Run(TraceGeometryGpuScene scene, in SurfaceLightingSnapshot input, GpuTexture destination,
        GpuShaderStorageBuffer work, int count, uint operation, uint texels, uint rays, uint steps, uint frame, bool emission, int maxFramesAccumulated = 4,
        GpuShaderStorageBuffer? fallbackRequests = null, GpuShaderStorageBuffer? fallbackCommits = null)
    {
        if (input.OutgoingRadiance.TextureId == destination.TextureId)
            throw new ArgumentException("Surface lighting requires distinct outgoing generations.");
        var stage = operation switch { 0 => SurfaceWorkStage.Seed, 1 or 5 => SurfaceWorkStage.Indirect,
            2 => SurfaceWorkStage.Combine, 3 => SurfaceWorkStage.Reset, _ => SurfaceWorkStage.Direct };
        bool measured = Diagnostics.Begin(stage, count);
        try
        {
            UboPacking.WriteUVec4(bytes, 0, (uint)input.TileSize, (uint)input.TilesPerAxis, (uint)input.TilesPerAtlas, operation);
            UboPacking.WriteUVec4(bytes, 16, texels, rays, steps, frame);
            UboPacking.WriteIVec4(bytes, 32, input.Origin.X, input.Origin.Y, input.Origin.Z, 0);
            UboPacking.WriteIVec4(bytes, 48, input.Dimensions.X, input.Dimensions.Y, input.Dimensions.Z, 0);
            UboPacking.WriteIVec4(bytes, 64, input.Ring.X, input.Ring.Y, input.Ring.Z, 0);
            // Use geometry's authoritative boundary, never the moving local coverage height.
            UboPacking.WriteUVec4(bytes, 80, emission ? 1u : 0u, (uint)Math.Max(0, scene.Coverage?.WorldHeight ?? 0), (uint)Math.Clamp(maxFramesAccumulated, 1, 255), measured ? 1u : 0u);
            using var program = pipeline.UseScope();
            parameters.UploadOrResize(bytes, growExponentially: false);
            parameters.BindBase(GpuBindingRegistry.Ubo.Lights);
            geometry.Bind(scene);
            work.BindBase(0); input.Patches.BindBase(1); input.Slots.BindBase(2); input.Readiness.BindBase(3);
            (fallbackRequests ?? disabledFallback).BindBase(5);
            (fallbackCommits ?? disabledFallback).BindBase(6);
            input.Material.Bind(16); input.OutgoingRadiance.Bind(17);
            scene.LightColors.Bind(3); scene.BlockLevels.Bind(4); scene.SunLevels.Bind(5);
            input.PageTable.Bind(18); scene.Surfaces.Bind(7);
            for (int unit = 16; unit <= 18; unit++) GpuSamplers.NearestClamp.Bind(unit);
            for (int unit = 1; unit <= 7; unit++) GpuSamplers.NearestClamp.Bind(unit);
            input.IndirectIrradiance.BindImageUnit(0, TextureAccess.ReadWrite, layered: true, format: SizedInternalFormat.Rgba16f);
            input.DirectIrradiance.BindImageUnit(1, TextureAccess.ReadWrite, layered: true, format: SizedInternalFormat.Rgba16f);
            destination.BindImageUnit(2, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((input.TileSize + 7) / 8, (input.TileSize + 7) / 8, count);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit |
                MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
        }
        finally { Diagnostics.End(); }
    }
    #endregion

    #region Lifetime
    /// <summary>Releases the program and per-dispatch parameters on the owning render context.</summary>
    public void Dispose() { Diagnostics.Dispose(); pipeline.Dispose(); parameters.Dispose(); geometry.Dispose(); disabledFallback.Dispose(); }
    #endregion
}
