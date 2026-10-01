using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.HitLighting;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Executes one bounded lighting operation with disjoint previous and next radiance storage.</summary>
internal sealed class SurfaceLightingDispatch : IDisposable
{
    private readonly LumonSceneSurfaceLightingShader pipeline;

    private readonly PackedUniformBuffer parameters = new(144);
    private readonly byte[] bytes = new byte[144];
    private readonly GpuShaderStorageBuffer disabledFallback = GpuShaderStorageBuffer.Create(debugName: "SurfaceLighting.DisabledFallback");
    public SurfaceWorkDiagnostics Diagnostics { get; } = new();

    #region Execution
    /// <summary>Loads the packaged SPIR-V producer through its shader-owned contract.</summary>
    public SurfaceLightingDispatch(ICoreAPI api)
    {
        if (!GpuComputePipeline.TryCreateFromAssets(api, LumonSceneSurfaceLightingShader.Contract.Identity,
            out var created, out _, out string log, preferSpirv: true))
            throw new InvalidOperationException(log);
        pipeline = new(created!);
        disabledFallback.EnsureCapacity(SurfaceHitCaptureCodec.HeaderBytes, growExponentially:false);
        disabledFallback.UploadSubData<uint>(new uint[SurfaceHitCaptureCodec.HeaderBytes >> 2],0,SurfaceHitCaptureCodec.HeaderBytes);
    }

    /// <summary>Binds a coherent input snapshot and writes only the explicitly selected page batches.</summary>
    public void Run(TraceGeometryGpuScene scene, in SurfaceLightingSnapshot input, GpuTexture destination,
        GpuShaderStorageBuffer work, int count, uint operation, uint texels, uint rays, uint steps, uint frame, bool emission, int maxFramesAccumulated = 4,
        GpuShaderStorageBuffer? fallbackRequests = null, GpuShaderStorageBuffer? fallbackCommits = null, int maxTraceDistance = 512,
        GpuShaderStorageBuffer? hitRetries = null, bool captureHitRetries = false)
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
            UboPacking.WriteIVec4(bytes, 32, input.Origin.X, input.Origin.Y, input.Origin.Z, Math.Clamp(maxTraceDistance, 1, 512));
            UboPacking.WriteIVec4(bytes, 48, input.Dimensions.X, input.Dimensions.Y, input.Dimensions.Z, captureHitRetries ? 1 : 0);
            UboPacking.WriteIVec4(bytes, 64, input.Ring.X, input.Ring.Y, input.Ring.Z, 0);
            // Use geometry's authoritative boundary, never the moving local coverage height.
            UboPacking.WriteUVec4(bytes, 80, emission ? 1u : 0u, (uint)Math.Max(0, scene.Coverage?.WorldHeight ?? 0), (uint)Math.Clamp(maxFramesAccumulated, 1, 255), measured ? 1u : 0u);
            var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
            var environment = atmosphere?.Environment ?? new System.Numerics.Vector3(32f / MathF.PI);
            var solar = atmosphere?.Solar ?? System.Numerics.Vector3.Zero;
            var sun = atmosphere?.Sun ?? System.Numerics.Vector3.UnitY;
            UboPacking.WriteVec4(bytes, 96, environment.X, environment.Y, environment.Z, 0f);
            UboPacking.WriteVec4(bytes, 112, solar.X, solar.Y, solar.Z, 0f);
            UboPacking.WriteVec4(bytes, 128, sun.X, sun.Y, sun.Z, 0f);
            parameters.SetBytes(bytes);
            pipeline.SurfaceLightingParameters = parameters;
            pipeline.BindSharedGeometry(scene);
            pipeline.SurfaceWork = work;
            pipeline.SurfacePatches = input.Patches; pipeline.SurfaceSlots = input.Slots; pipeline.SurfaceReady = input.Readiness;
            pipeline.SurfaceFallbackRequests = fallbackRequests ?? disabledFallback;
            pipeline.SurfaceFallbackCommits = fallbackCommits ?? disabledFallback;
            pipeline.SurfaceHitRetries = hitRetries ?? disabledFallback;
            pipeline.CapturedMaterial = input.Material; pipeline.PreviousOutgoing = input.OutgoingRadiance;
            pipeline.SurfacePages = input.PageTable;
            pipeline.LightColors = scene.LightColors; pipeline.BlockLevels = scene.BlockLevels;
            pipeline.SunLevels = scene.SunLevels; pipeline.Surfaces = scene.Surfaces;
            pipeline.IndirectIrradiance = new(input.IndirectIrradiance, TextureAccess.ReadWrite, Layered: true, Format: SizedInternalFormat.Rgba16f);
            pipeline.DirectIrradiance = new(input.DirectIrradiance, TextureAccess.ReadWrite, Layered: true, Format: SizedInternalFormat.Rgba16f);
            pipeline.NextOutgoing = new(destination, TextureAccess.WriteOnly, Layered: true, Format: SizedInternalFormat.Rgba16f);
            pipeline.DiagnosticCounters = Diagnostics.ActiveBuffer;
            pipeline.Dispatch((input.TileSize + 7) / 8, (input.TileSize + 7) / 8, count);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit |
                MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
        }
        finally { Diagnostics.End(); }
    }
    #endregion

    #region Lifetime
    /// <summary>Releases the program and per-dispatch parameters on the owning render context.</summary>
    public void Dispose() { Diagnostics.Dispose(); pipeline.Dispose(); parameters.Dispose(); disabledFallback.Dispose(); }
    #endregion
}
