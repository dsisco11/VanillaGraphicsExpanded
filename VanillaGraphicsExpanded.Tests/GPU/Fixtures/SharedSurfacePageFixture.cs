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
    private readonly ShaderTestHelper helper = new(Path.Combine(AppContext.BaseDirectory, "assets", "shaders"), Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes"));
    private readonly ComputeProgram capture, relight;
    private readonly TraceGeometryComputeBindings geometry = new();
    private readonly ObjectParamsUbo parameters = new("Tests.SharedSurfacePage");
    private readonly GpuShaderStorageBuffer captureWork, relightWork, metadata, slots;
    private readonly Texture3D depth = Texture3D.Create(Size, Size, 1, PixelInternalFormat.R16f, textureTarget: TextureTarget.Texture2DArray);
    private readonly Texture3D material = Texture3D.Create(Size, Size, 1, PixelInternalFormat.Rgba8, textureTarget: TextureTarget.Texture2DArray);
    private readonly Texture3D irradiance = Texture3D.Create(Size, Size, 1, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture2DArray);

    #region Setup and execution
    /// <summary>Creates a +X page with an exact integer chunk origin, including coordinates beyond float precision.</summary>
    public SharedSurfacePageFixture(int chunkX = 0)
    {
        capture = ComputeProgram.Create(helper, "lumonscene_capture_voxel", layout: new LumonSceneComputeProgramLayouts.CaptureVoxel());
        relight = ComputeProgram.Create(helper, "lumonscene_relight_voxel_dda", layout: new LumonSceneComputeProgramLayouts.RelightVoxelDda());
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
        GlStateCache.Current.UseProgram(capture.ProgramId);
        geometry.Bind(scene);
        captureWork.BindBase(0); metadata.BindBase(1); slots.BindBase(2);
        depth.BindImageUnit(0, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.R16f);
        material.BindImageUnit(1, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba8);
        LumonSceneCaptureVoxelParamsUbo.Bind(parameters, Size, 1, 1, 0, 0, 0, 0, 0, 0, 0, scene.Resolution);
        Dispatch();
        using var read = captureWork.MapRange<LumonSceneCaptureWorkGpu>(0, 1, MapBufferAccessMask.MapReadBit);
        Assert.True(read.IsMapped);
        return (read.Span[0].VirtualPageIndex & 0x80000000u) == 0;
    }

    /// <summary>Runs bounded relighting and returns the page completion marker written by the shader.</summary>
    public bool Relight(TraceGeometryGpuScene scene, uint steps = 256)
    {
        relightWork.UploadSubData<LumonSceneRelightWorkGpu>([new(1, 0, 0, 0)], 0, 16);
        GlStateCache.Current.UseProgram(relight.ProgramId);
        geometry.Bind(scene);
        relightWork.BindBase(0); metadata.BindBase(1);
        depth.Bind(0); material.Bind(1); scene.LightColors.Bind(3); scene.BlockLevels.Bind(4); scene.SunLevels.Bind(5); scene.Surfaces.Bind(7);
        foreach (int unit in new[] { 0, 1, 3, 4, 5, 7 }) GpuSamplers.NearestClamp.Bind(unit);
        irradiance.BindImageUnit(0, TextureAccess.ReadWrite, layered: true, format: SizedInternalFormat.Rgba16f);
        LumonSceneRelightParamsUbo.Bind(parameters, Size, 1, 1, 0, Size * Size, 1, steps, 0, 0, scene.Resolution, 0, 0, 0, 0, 0, 0);
        Dispatch();
        using var read = relightWork.MapRange<LumonSceneRelightWorkGpu>(0, 1, MapBufferAccessMask.MapReadBit);
        Assert.True(read.IsMapped);
        return (read.Span[0].VirtualPageIndex & 0x80000000u) == 0;
    }

    /// <summary>Clears temporal history using the production GL 4.3 reset shader.</summary>
    public void ResetLighting()
    {
        using var reset = ComputeProgram.Create(helper, "lumonscene_reset_irradiance");
        GlStateCache.Current.UseProgram(reset.ProgramId);
        irradiance.BindImageUnit(0, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba16f);
        Dispatch();
    }
    #endregion

    #region Readback and lifetime
    /// <summary>Reads all irradiance texels, including their accumulated sample weights.</summary>
    public float[] ReadLighting()
    {
        var data = new float[Size * Size * 4];
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, irradiance.TextureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.Float, data);
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
        GL.DispatchCompute(1, 1, 1);
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
        capture.Dispose(); relight.Dispose(); geometry.Dispose(); parameters.Dispose();
        captureWork.Dispose(); relightWork.Dispose(); metadata.Dispose(); slots.Dispose();
        depth.Dispose(); material.Dispose(); irradiance.Dispose(); helper.Dispose();
    }
    #endregion
}
