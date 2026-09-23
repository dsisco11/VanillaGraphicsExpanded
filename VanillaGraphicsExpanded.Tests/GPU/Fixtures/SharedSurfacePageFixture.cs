using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>One reusable surface page driven by the production capture, relight and shared binding contracts.</summary>
internal sealed class SharedSurfacePageFixture : IDisposable
{
    private const int Size = 8;
    private readonly BinaryShaderApiFixture assets = new();
    private readonly LumonSceneCaptureVoxelComputeShader capture;
    private readonly LumonSceneRelightVoxelDdaComputeShader relight;
    private readonly GpuShaderStorageBuffer captureWork, relightWork, metadata, slots;
    private readonly SurfaceAtlasTextures textures = new(Size,Size,1,"Tests.SurfacePage");
    private Texture3D depth => textures.Depth;
    private Texture3D material => textures.Material;
    private Texture3D irradiance => textures.Indirect;

    /// <summary>Exposes the actual history atlas to the production invalidation owner.</summary>
    internal GpuTexture IrradianceAtlas => irradiance;

    #region Setup and execution
    /// <summary>Creates a +X page with an exact integer chunk origin, including coordinates beyond float precision.</summary>
    public SharedSurfacePageFixture(int chunkX = 0)
    {
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(assets.Api, out var captureOwner, out string captureLog), captureLog);
        capture = captureOwner!;
        Assert.True(LumonSceneRelightVoxelDdaComputeShader.TryCreate(assets.Api, out var relightOwner, out string relightLog), relightLog);
        relight = relightOwner!;
        captureWork = Buffer<LumonSceneCaptureWorkGpu>([new(1, 0, 1, 0)]);
        relightWork = Buffer<LumonSceneRelightWorkGpu>([new(1, 0, 0, 0)]);
        metadata = Buffer<LumonScenePatchMetadataGpu>(new LumonScenePatchMetadataGpu[2]);
        slots = Buffer<int>([chunkX, 32, 0, 0]);
        ResetLighting();
    }

    /// <summary>Captures material data and returns whether all texels could resolve their source.</summary>
    public bool Capture(TraceGeometryGpuScene scene)
    {
        captureWork.UploadSubData<LumonSceneCaptureWorkGpu>([new(1, 0, 1, 0)], 0, 16);
        using var active = capture.UseScope();
        capture.BindSharedGeometry(scene);
        capture.BindCaptureWorkSsbo(captureWork); capture.BindPatchMetaSsbo(metadata); capture.BindChunkSlotInfoSsbo(slots);
        capture.BindDepthAtlasImage(depth);
        capture.BindMaterialAtlasImage(material);
        capture.SetAtlasLayout(Size, 1, 1, 0);
        Dispatch();
        using var read = captureWork.MapRange<LumonSceneCaptureWorkGpu>(0, 1, MapBufferAccessMask.MapReadBit);
        Assert.True(read.IsMapped);
        return (read.Span[0].VirtualPageIndex & 0x80000000u) == 0;
    }

    /// <summary>Runs the same capture resources through the production shader owner and binary loading path.</summary>
    public bool CaptureWithProductionOwner(Vintagestory.API.Common.ICoreAPI api, TraceGeometryGpuScene scene)
    {
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(api, out var owner, out string log, preferSpirv: true), log);
        using (owner)
        {
            captureWork.UploadSubData<LumonSceneCaptureWorkGpu>([new(1, 0, 1, 0)], 0, 16);
            using var program = owner!.UseScope();
            owner.BindSharedGeometry(scene);
            owner.BindCaptureWorkSsbo(captureWork);
            owner.BindPatchMetaSsbo(metadata);
            owner.BindChunkSlotInfoSsbo(slots);
            owner.BindDepthAtlasImage(depth);
            owner.BindMaterialAtlasImage(material);
            owner.SetAtlasLayout(Size, 1, 1, 0);
            Dispatch();
        }
        using var read = captureWork.MapRange<LumonSceneCaptureWorkGpu>(0, 1, MapBufferAccessMask.MapReadBit);
        Assert.True(read.IsMapped);
        return (read.Span[0].VirtualPageIndex & 0x80000000u) == 0;
    }

    /// <summary>Runs bounded relighting and returns the page completion marker written by the shader.</summary>
    public bool Relight(TraceGeometryGpuScene scene, uint steps = 256, uint rays = 1)
    {
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        relightWork.UploadSubData<LumonSceneRelightWorkGpu>([new(1, 0, 0, 0)], 0, 16);
        using var active = relight.UseScope();
        relight.BindSharedGeometry(scene);
        relight.BindRelightWorkSsbo(relightWork); relight.BindPatchMetaSsbo(metadata);
        relight.BindDepthAtlas(depth.TextureId); relight.BindMaterialAtlas(material.TextureId);
        relight.BindLightColorLut(scene.LightColors.TextureId);
        relight.BindBlockLevelScalarLut(scene.BlockLevels.TextureId);
        relight.BindSunLevelScalarLut(scene.SunLevels.TextureId);
        relight.BindSurfaceLut(scene.Surfaces.TextureId);
        relight.BindIrradianceAtlasImage(irradiance);
        relight.SetAtlasLayout(Size, 1, 1, 0);
        relight.SetRelightParams(0, Size * Size, rays, steps, false);
        relight.SetOccupancyMapping(0, 0, 0, 0, 0, 0, scene.Resolution);
        Dispatch();
        using var read = relightWork.MapRange<LumonSceneRelightWorkGpu>(0, 1, MapBufferAccessMask.MapReadBit);
        Assert.True(read.IsMapped);
        return (read.Span[0].VirtualPageIndex & 0x80000000u) == 0;
    }

    /// <summary>Clears temporal history using the production GL 4.3 reset shader.</summary>
    public void ResetLighting()
    {
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        // This packaged reset operation has a contract but no instance shader class.
        // Exercise the production compute pipeline and image owner directly.
        Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, "lumonscene_reset_irradiance",
            out var loaded, out _, out string log, preferSpirv: true), log);
        using var reset = loaded!;
        // Restore the prior live program before the temporary reset pipeline is disposed.
        using var program = reset.UseScope();
        irradiance.BindImageUnit(0, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba16f);
        Dispatch();
    }
    #endregion

    #region Readback and lifetime
    /// <summary>Reads all irradiance texels, including their accumulated sample weights.</summary>
    public float[] ReadLighting()
    {
        var data = new float[Size * Size * 4];
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, irradiance.TextureId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.Float, data);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return data;
    }

    /// <summary>Reads the captured face surface identities without substituting a CPU material lookup.</summary>
    public byte[] ReadMaterial()
    {
        var data = new byte[Size * Size * 4];
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, material.TextureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        return data;
    }

    /// <summary>Dispatches one page and makes both image results and completion flags available for inspection.</summary>
    private static void Dispatch()
    {
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GL.DispatchCompute(1, 1, 1);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
        GpuTestFence.WaitForGpuOrSkip("Shared surface page");
    }

    /// <summary>Allocates typed storage using the same buffer ownership used by runtime consumers.</summary>
    private static GpuShaderStorageBuffer Buffer<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        var buffer = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
        int bytes = values.Length * Marshal.SizeOf<T>();
        buffer.EnsureCapacity(bytes, growExponentially: false);
        buffer.UploadSubData(values, 0, bytes);
        return buffer;
    }

    /// <summary>Releases the page and its programs on the owning context.</summary>
    public void Dispose()
    {
        capture.Dispose(); relight.Dispose();
        captureWork.Dispose(); relightWork.Dispose(); metadata.Dispose(); slots.Dispose();
        textures.Dispose(); assets.Dispose();
    }
    #endregion
}
