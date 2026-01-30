using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonSceneVoxelCaptureComputeTests : RenderTestBase
{
    public LumonSceneVoxelCaptureComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void CaptureVoxel_SingleWorkItem_WritesDepthZero_AndExpectedNormal()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");

        const int tileSize = 16;
        const int tilesPerAxis = 1;
        const int tilesPerAtlas = 1;
        const int atlasCount = 1;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        ClearR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, atlasCount, r: 0, g: 0, b: 0, a: 0);

        // physicalPageId=1 maps to tile (0,0) in atlas layer 0.
        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[1];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u);
        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);

        // Bind output images to match shader layout(binding=...).
        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(program, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(program, "vge_borderTexels", 0u);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        float[] depth = ReadTexImageR32f_2DArray(depthAtlas.TextureId, tileSize, tileSize, atlasCount);
        (float min, float max) = MinMax(depth);
        Assert.InRange(min, -0.02f, 0.02f);
        Assert.InRange(max, -0.02f, 0.02f);

        byte[] material = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, atlasCount);
        (Vector3 n, byte a) = ReadNormalAndAlphaAt(material, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)255, a);
        Assert.True(Vector3.Dot(n, Vector3.UnitX) > 0.99f, $"Captured normal dot expected too low: {Vector3.Dot(n, Vector3.UnitX)}");

        GL.DeleteProgram(program);
    }

    [Fact]
    public void CaptureVoxel_MultiplePages_AddressingDoesNotOverlap_AndRespectsAtlasLayer()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");

        const int tileSize = 8;
        const int tilesPerAxis = 2;
        const int tilesPerAtlas = tilesPerAxis * tilesPerAxis; // 4
        const int atlasCount = 2;

        int w = tileSize * tilesPerAxis;
        int h = tileSize * tilesPerAxis;

        using var depthAtlas = Texture3D.Create(w, h, atlasCount, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(w, h, atlasCount, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        ClearR16f2DArray(depthAtlas.TextureId, w, h, atlasCount, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, w, h, atlasCount, r: 0, g: 0, b: 0, a: 0);

        // Work:
        // - pid=1 -> pageIndex=0 -> atlas0 tile(0,0)
        // - pid=4 -> pageIndex=3 -> atlas0 tile(1,1)
        // - pid=5 -> pageIndex=4 -> atlas1 tile(0,0)
        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[3];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 1u, virtualPageIndex: 0u); // +X
        work[1] = new LumonSceneCaptureWorkGpu(physicalPageId: 4u, chunkSlot: 0u, patchId: 2u, virtualPageIndex: 0u); // -X
        work[2] = new LumonSceneCaptureWorkGpu(physicalPageId: 5u, chunkSlot: 0u, patchId: 3u, virtualPageIndex: 0u); // +Y
        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", (uint)tilesPerAxis);
        SetUniform(program, "vge_tilesPerAtlas", (uint)tilesPerAtlas);
        _ = TrySetUniform(program, "vge_borderTexels", 0u);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 3);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Tile centers:
        // atlas0 tile(0,0): center at (tileSize/2, tileSize/2)
        // atlas0 tile(1,1): center at (tileSize + tileSize/2, tileSize + tileSize/2)
        // atlas1 tile(0,0): center at (tileSize/2, tileSize/2) in layer 1
        byte[] material = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, w, h, atlasCount);

        (Vector3 n00, byte a00) = ReadNormalAndAlphaAt(material, w, h, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)255, a00);
        Assert.True(Vector3.Dot(n00, Vector3.UnitX) > 0.99f);

        (Vector3 n11, byte a11) = ReadNormalAndAlphaAt(material, w, h, layer: 0, x: tileSize + tileSize / 2, y: tileSize + tileSize / 2);
        Assert.Equal((byte)255, a11);
        Assert.True(Vector3.Dot(n11, -Vector3.UnitX) > 0.99f);

        (Vector3 nLayer1, byte aLayer1) = ReadNormalAndAlphaAt(material, w, h, layer: 1, x: tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)255, aLayer1);
        Assert.True(Vector3.Dot(nLayer1, Vector3.UnitY) > 0.99f);

        // Unwritten tile atlas0 tile(1,0) center should remain alpha=0 and depth=1.
        (Vector3 _, byte aUnwritten) = ReadNormalAndAlphaAt(material, w, h, layer: 0, x: tileSize + tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)0, aUnwritten);

        float[] depth = ReadTexImageR32f_2DArray(depthAtlas.TextureId, w, h, atlasCount);
        float dUnwritten = depth[LinearIndex(w, h, layer: 0, x: tileSize + tileSize / 2, y: tileSize / 2)];
        Assert.InRange(dUnwritten, 0.98f, 1.02f);

        GL.DeleteProgram(program);
    }

    [Fact]
    public void CaptureVoxel_BorderTexelsNonZero_DoesNotBreakWrites()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_capture_voxel.csh");

        const int tileSize = 16;

        using var depthAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        ClearR16f2DArray(depthAtlas.TextureId, tileSize, tileSize, depth: 1, value: 1f);
        ClearRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, depth: 1, r: 0, g: 0, b: 0, a: 0);

        Span<LumonSceneCaptureWorkGpu> work = stackalloc LumonSceneCaptureWorkGpu[1];
        work[0] = new LumonSceneCaptureWorkGpu(physicalPageId: 1u, chunkSlot: 0u, patchId: 6u, virtualPageIndex: 0u); // -Z
        using var workSsbo = CreateSsbo<LumonSceneCaptureWorkGpu>("Test_WorkSSBO", work);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniform(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniform(program, "vge_tilesPerAxis", 1u);
        SetUniform(program, "vge_tilesPerAtlas", 1u);
        _ = TrySetUniform(program, "vge_borderTexels", 2u);

        int gx = (tileSize + 7) / 8;
        int gy = (tileSize + 7) / 8;
        GL.DispatchCompute(gx, gy, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        byte[] material = ReadTexImageRgba8_2DArray(materialAtlas.TextureId, tileSize, tileSize, depth: 1);
        (Vector3 n, byte a) = ReadNormalAndAlphaAt(material, tileSize, tileSize, layer: 0, x: tileSize / 2, y: tileSize / 2);
        Assert.Equal((byte)255, a);
        Assert.True(Vector3.Dot(n, -Vector3.UnitZ) > 0.99f);

        GL.DeleteProgram(program);
    }

    private static ShaderTestHelper CreateShaderHelperOrSkip()
    {
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.Skip("Shader assets not available - test output content may be missing");
        }

        return new ShaderTestHelper(shaderPath, includePath);
    }

    private static int CompileAndLinkCompute(ShaderTestHelper helper, string computeShaderFile)
    {
        var cs = helper.CompileShader(computeShaderFile, ShaderType.ComputeShader);
        Assert.True(cs.IsSuccess, cs.ErrorMessage);

        int program = GL.CreateProgram();
        GL.AttachShader(program, cs.ShaderId);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        string log = GL.GetProgramInfoLog(program) ?? string.Empty;
        Assert.True(ok != 0, $"Compute program link failed:\n{log}");

        return program;
    }

    private static void SetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static bool TrySetUniform(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0)
        {
            return false;
        }
        GL.Uniform1(loc, value);
        return true;
    }

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    private static void ClearR16f2DArray(int textureId, int width, int height, int depth, float value)
    {
        float[] data = new float[checked(width * height * depth)];
        Array.Fill(data, value);

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static void ClearRgba8_2DArray(int textureId, int width, int height, int depth, byte r, byte g, byte b, byte a)
    {
        byte[] data = new byte[checked(width * height * depth * 4)];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i + 0] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }

        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, width, height, depth, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
    }

    private static float[] ReadTexImageR32f_2DArray(int textureId, int width, int height, int depth)
    {
        float[] data = new float[checked(width * height * depth)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, level: 0, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        return data;
    }

    private static byte[] ReadTexImageRgba8_2DArray(int textureId, int width, int height, int depth)
    {
        byte[] data = new byte[checked(width * height * depth * 4)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture2DArray, textureId);
        GL.GetTexImage(TextureTarget.Texture2DArray, level: 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        return data;
    }

    private static int LinearIndex(int width, int height, int layer, int x, int y)
        => ((layer * height + y) * width) + x;

    private static (Vector3 Normal, byte Alpha) ReadNormalAndAlphaAt(byte[] rgba, int width, int height, int layer, int x, int y)
    {
        int idx = ((layer * height + y) * width + x) * 4;
        byte r = rgba[idx + 0];
        byte g = rgba[idx + 1];
        byte b = rgba[idx + 2];
        byte a = rgba[idx + 3];

        Vector3 n01 = new(r / 255f, g / 255f, b / 255f);
        Vector3 n = Vector3.Normalize(n01 * 2f - Vector3.One);
        return (n, a);
    }

    private static (float Min, float Max) MinMax(ReadOnlySpan<float> v)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < v.Length; i++)
        {
            float f = v[i];
            min = Math.Min(min, f);
            max = Math.Max(max, f);
        }
        return (min, max);
    }
}
