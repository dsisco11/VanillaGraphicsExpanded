using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Feeds a terrain mapping into production voxel capture and samples the selected boundary material.</summary>
internal static class TerrainBoundaryCaptureFixture
{
    #region Selected surface capture
    /// <summary>Adjacent chunks have distinct face materials, so selecting the outward neighbor cannot pass.</summary>
    public static void AssertSelectedSurface(ICoreClientAPI api, uint[] mapping, int face, int boundary)
    {
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(api, out var owner, out string log), log);
        using var shader = owner!;
        using var depth = Texture3D.Create(4, 4, 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray);
        using var material = Texture3D.Create(4, 4, 1, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray);
        using var occupancy = Texture3D.Create(64, 64, 64, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D);
        using var palette = Texture2D.Create(64, 1, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest);
        int axis = face >> 1;
        var voxels = new uint[64 * 64 * 64];
        for (int z = 0; z < 64; z++)
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            int coordinate = axis == 0 ? x : axis == 1 ? y : z;
            voxels[(z * 64 + y) * 64 + x] = LumonSceneOccupancyPacking.Pack(0, 0, 0, coordinate < 32 ? 5u : 6u);
        }
        occupancy.UploadDataImmediate(voxels, 0, 0, 0, 64, 64, 64, 0);
        uint[] entries = new uint[64 * 4];
        for (int i = 0; i < 3; i++)
        {
            entries[5 * 4 + i] = 9u | (9u << 16);
            entries[6 * 4 + i] = 17u | (17u << 16);
        }
        palette.UploadDataImmediate(entries);
        int[] origin = [0, 0, 0]; origin[axis] = boundary - 32;
        using var scene = new SharedSurfaceInputFixture(occupancy, palette, new(origin[0], origin[1], origin[2]));
        // Populate the same signed toroidal window that produced the terrain slot.
        var slots = new int[125 * 4];
        for (int y = -2; y <= 2; y++)
        for (int z = -2; z <= 2; z++)
        for (int x = -2; x <= 2; x++)
        {
            int slot = (((y + 4) % 5) * 5 + (z + 5) % 5) * 5 + (x + 3) % 5;
            slots[slot * 4] = x << 5; slots[slot * 4 + 1] = y << 5; slots[slot * 4 + 2] = z << 5;
            slots[slot * 4 + 3] = 1000 + slot;
        }
        using var work = Buffer<LumonSceneCaptureWorkGpu>([new(1, mapping[0], mapping[1], 0)]);
        using var metadata = Buffer<LumonScenePatchMetadataGpu>(new LumonScenePatchMetadataGpu[2]);
        using var slotInfo = Buffer<int>(slots);
        using var use = shader.UseScope();
        shader.BindCaptureWorkSsbo(work); shader.BindPatchMetaSsbo(metadata); shader.BindChunkSlotInfoSsbo(slotInfo);
        shader.BindDepthAtlasImage(depth); shader.BindMaterialAtlasImage(material); shader.BindSharedGeometry(scene.Scene);
        shader.SetAtlasLayout(4, 1, 1, 0);
        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
        byte[] pixels = new byte[4 * 4 * 4];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, material.TextureId))
            GL.GetTexImage(TextureTarget.Texture2DArray, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        int u = Math.Clamp((int)(BitConverter.UInt32BitsToSingle(mapping[4]) * 4), 0, 3);
        int v = Math.Clamp((int)(BitConverter.UInt32BitsToSingle(mapping[5]) * 4), 0, 3);
        Assert.Equal(face % 2 == 0 ? (byte)9 : (byte)17, pixels[(v * 4 + u) * 4 + 2]);
    }
    #endregion

    #region GPU input storage
    /// <summary>Uploads tightly packed work records through the production buffer owner.</summary>
    private static GpuShaderStorageBuffer Buffer<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        var buffer = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
        int bytes = values.Length * Marshal.SizeOf<T>();
        buffer.EnsureCapacity(bytes, false); buffer.UploadSubData(values, 0, bytes);
        return buffer;
    }
    #endregion
}
