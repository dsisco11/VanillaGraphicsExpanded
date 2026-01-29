using System;
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
public sealed class LumonSceneMeshCardCaptureComputeTests : RenderTestBase
{
    public LumonSceneMeshCardCaptureComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void Capture_PlanarQuad_WritesZeroDepthAndNormal()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_capture_meshcard.csh");

        const int tileSize = 16;
        using var depthAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        // One physical page, one work item.
        var work = new LumonSceneMeshCardCaptureWorkGpu(physicalPageId: 1, triangleOffset: 0, triangleCount: 2);
        Span<LumonSceneMeshCardCaptureWorkGpu> oneWork = stackalloc LumonSceneMeshCardCaptureWorkGpu[1];
        oneWork[0] = work;
        using var workSsbo = CreateSsbo<LumonSceneMeshCardCaptureWorkGpu>("Test_WorkSSBO", oneWork);

        // Patch metadata indexed by physicalPageId (1-based). Allocate [0..1].
        LumonScenePatchMetadataGpu[] meta = new LumonScenePatchMetadataGpu[2];
        meta[1] = new LumonScenePatchMetadataGpu
        {
            OriginWS = new Vector4(0, 0, 0, 0),
            AxisUWS = new Vector4(1, 0, 0, 0),
            AxisVWS = new Vector4(0, 1, 0, 0),
            NormalWS = new Vector4(0, 0, 1, 0),
            VirtualBasePageX = 0,
            VirtualBasePageY = 0,
            VirtualSizePagesX = 1,
            VirtualSizePagesY = 1,
            ChunkSlot = 0,
            PatchId = 123,
        };
        using var metaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_MetaSSBO", meta);

        // Two triangles cover [0,1]^2 at z=0.
        var tri0 = new LumonSceneMeshCardTriangleGpu(
            p0: new Vector4(0, 0, 0, 0),
            p1: new Vector4(1, 0, 0, 0),
            p2: new Vector4(1, 1, 0, 0),
            n0: new Vector4(0, 0, 1, 0));
        var tri1 = new LumonSceneMeshCardTriangleGpu(
            p0: new Vector4(0, 0, 0, 0),
            p1: new Vector4(1, 1, 0, 0),
            p2: new Vector4(0, 1, 0, 0),
            n0: new Vector4(0, 0, 1, 0));
        Span<LumonSceneMeshCardTriangleGpu> twoTri = stackalloc LumonSceneMeshCardTriangleGpu[2];
        twoTri[0] = tri0;
        twoTri[1] = tri1;
        using var triSsbo = CreateSsbo<LumonSceneMeshCardTriangleGpu>("Test_TriSSBO", twoTri);

        GL.UseProgram(program);

        // SSBO bindings match the shader:
        // binding=0 work, binding=1 patch metadata, binding=2 triangles
        workSsbo.BindBase(bindingIndex: 0);
        metaSsbo.BindBase(bindingIndex: 1);
        triSsbo.BindBase(bindingIndex: 2);

        // Image bindings match the shader layout(binding=...).
        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniformLocal(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniformLocal(program, "vge_tilesPerAxis", 1u);
        SetUniformLocal(program, "vge_tilesPerAtlas", 1u);
        _ = TrySetUniformLocal(program, "vge_borderTexels", 0u); // may be optimized out
        SetUniformLocal(program, "vge_captureDepthRange", 1f);

        GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        float[] depth = ReadTexImageR32f(depthAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        Assert.Equal(tileSize * tileSize, depth.Length);

        // All depths should be ~0 (planar on the card plane).
        (float min, float max) = MinMax(depth);
        Assert.InRange(min, -0.02f, 0.02f);
        Assert.InRange(max, -0.02f, 0.02f);

        byte[] material = ReadTexImageRgba8(materialAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        Assert.Equal(tileSize * tileSize * 4, material.Length);

        // Spot check center pixel normal ~= (0,0,1) encoded to (128,128,255), alpha=255.
        int cx = tileSize / 2;
        int cy = tileSize / 2;
        int idx = (cy * tileSize + cx) * 4;
        Assert.InRange(material[idx + 0], (byte)120, (byte)136);
        Assert.InRange(material[idx + 1], (byte)120, (byte)136);
        Assert.InRange(material[idx + 2], (byte)250, (byte)255);
        Assert.Equal((byte)255, material[idx + 3]);

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Capture_OffsetQuad_WritesExpectedSignedDepth()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_capture_meshcard.csh");

        const int tileSize = 16;
        using var depthAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        var work = new LumonSceneMeshCardCaptureWorkGpu(physicalPageId: 1, triangleOffset: 0, triangleCount: 2);
        Span<LumonSceneMeshCardCaptureWorkGpu> oneWork = stackalloc LumonSceneMeshCardCaptureWorkGpu[1];
        oneWork[0] = work;
        using var workSsbo = CreateSsbo<LumonSceneMeshCardCaptureWorkGpu>("Test_WorkSSBO", oneWork);

        LumonScenePatchMetadataGpu[] meta = new LumonScenePatchMetadataGpu[2];
        meta[1] = new LumonScenePatchMetadataGpu
        {
            OriginWS = new Vector4(0, 0, 0, 0),
            AxisUWS = new Vector4(1, 0, 0, 0),
            AxisVWS = new Vector4(0, 1, 0, 0),
            NormalWS = new Vector4(0, 0, 1, 0),
            VirtualBasePageX = 0,
            VirtualBasePageY = 0,
            VirtualSizePagesX = 1,
            VirtualSizePagesY = 1,
            ChunkSlot = 0,
            PatchId = 123,
        };
        using var metaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_MetaSSBO", meta);

        const float dz = 0.25f;
        var tri0 = new LumonSceneMeshCardTriangleGpu(
            p0: new Vector4(0, 0, dz, 0),
            p1: new Vector4(1, 0, dz, 0),
            p2: new Vector4(1, 1, dz, 0),
            n0: new Vector4(0, 0, 1, 0));
        var tri1 = new LumonSceneMeshCardTriangleGpu(
            p0: new Vector4(0, 0, dz, 0),
            p1: new Vector4(1, 1, dz, 0),
            p2: new Vector4(0, 1, dz, 0),
            n0: new Vector4(0, 0, 1, 0));
        Span<LumonSceneMeshCardTriangleGpu> twoTri = stackalloc LumonSceneMeshCardTriangleGpu[2];
        twoTri[0] = tri0;
        twoTri[1] = tri1;
        using var triSsbo = CreateSsbo<LumonSceneMeshCardTriangleGpu>("Test_TriSSBO", twoTri);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        metaSsbo.BindBase(bindingIndex: 1);
        triSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniformLocal(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniformLocal(program, "vge_tilesPerAxis", 1u);
        SetUniformLocal(program, "vge_tilesPerAtlas", 1u);
        _ = TrySetUniformLocal(program, "vge_borderTexels", 0u); // may be optimized out
        SetUniformLocal(program, "vge_captureDepthRange", 1f);

        GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        float[] depth = ReadTexImageR32f(depthAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        (float min, float max) = MinMax(depth);

        Assert.InRange(min, dz - 0.03f, dz + 0.03f);
        Assert.InRange(max, dz - 0.03f, dz + 0.03f);

        GL.DeleteProgram(program);
    }

    [Fact]
    public void Capture_RotatedQuad_WritesZeroDepth_AndExpectedNormal()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_capture_meshcard.csh");

        const int tileSize = 16;
        using var depthAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_DepthAtlas");
        using var materialAtlas = Texture3D.Create(tileSize, tileSize, depth: 1, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "Test_MaterialAtlas");

        Span<LumonSceneMeshCardCaptureWorkGpu> oneWork = stackalloc LumonSceneMeshCardCaptureWorkGpu[1];
        oneWork[0] = new LumonSceneMeshCardCaptureWorkGpu(physicalPageId: 1, triangleOffset: 0, triangleCount: 2);
        using var workSsbo = CreateSsbo<LumonSceneMeshCardCaptureWorkGpu>("Test_WorkSSBO", oneWork);

        // Rotate the canonical card (XY plane, +Z normal) by +45 degrees around Y.
        float c = 0.70710678f;
        Vector3 axisU = new(c, 0f, -c);
        Vector3 axisV = new(0f, 1f, 0f);
        Vector3 n = Vector3.Normalize(Vector3.Cross(axisU, axisV));

        LumonScenePatchMetadataGpu[] meta = new LumonScenePatchMetadataGpu[2];
        meta[1] = new LumonScenePatchMetadataGpu
        {
            OriginWS = new Vector4(0, 0, 0, 0),
            AxisUWS = new Vector4(axisU, 0),
            AxisVWS = new Vector4(axisV, 0),
            NormalWS = new Vector4(n, 0),
            VirtualBasePageX = 0,
            VirtualBasePageY = 0,
            VirtualSizePagesX = 1,
            VirtualSizePagesY = 1,
            ChunkSlot = 0,
            PatchId = 123,
        };
        using var metaSsbo = CreateSsbo<LumonScenePatchMetadataGpu>("Test_MetaSSBO", meta);

        Vector3 p0 = Vector3.Zero;
        Vector3 p1 = axisU;
        Vector3 p2 = axisU + axisV;
        Vector3 p3 = axisV;

        Span<LumonSceneMeshCardTriangleGpu> twoTri = stackalloc LumonSceneMeshCardTriangleGpu[2];
        twoTri[0] = new LumonSceneMeshCardTriangleGpu(new Vector4(p0, 0), new Vector4(p1, 0), new Vector4(p2, 0), new Vector4(n, 0));
        twoTri[1] = new LumonSceneMeshCardTriangleGpu(new Vector4(p0, 0), new Vector4(p2, 0), new Vector4(p3, 0), new Vector4(n, 0));
        using var triSsbo = CreateSsbo<LumonSceneMeshCardTriangleGpu>("Test_TriSSBO", twoTri);

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        metaSsbo.BindBase(bindingIndex: 1);
        triSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        SetUniformLocal(program, "vge_tileSizeTexels", (uint)tileSize);
        SetUniformLocal(program, "vge_tilesPerAxis", 1u);
        SetUniformLocal(program, "vge_tilesPerAtlas", 1u);
        _ = TrySetUniformLocal(program, "vge_borderTexels", 0u);
        SetUniformLocal(program, "vge_captureDepthRange", 1f);

        GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        float[] depth = ReadTexImageR32f(depthAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        (float min, float max) = MinMax(depth);
        Assert.InRange(min, -0.03f, 0.03f);
        Assert.InRange(max, -0.03f, 0.03f);

        byte[] material = ReadTexImageRgba8(materialAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        int cx = tileSize / 2;
        int cy = tileSize / 2;
        int idx = (cy * tileSize + cx) * 4;
        Assert.Equal((byte)255, material[idx + 3]); // valid

        Vector3 n01 = new(material[idx + 0] / 255f, material[idx + 1] / 255f, material[idx + 2] / 255f);
        Vector3 decoded = Vector3.Normalize(n01 * 2f - Vector3.One);
        Assert.True(Vector3.Dot(decoded, n) > 0.99f, $"Captured normal dot expected too low: {Vector3.Dot(decoded, n)}");

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

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    private static void SetUniformLocal(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniformLocal(int program, string name, float value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static bool TrySetUniformLocal(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        if (loc < 0)
        {
            return false;
        }
        GL.Uniform1(loc, value);
        return true;
    }

    private static float[] ReadTexImageR32f(int textureId, TextureTarget target, int width, int height)
    {
        float[] data = new float[checked(width * height)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(target, textureId);
        GL.GetTexImage(target, level: 0, PixelFormat.Red, PixelType.Float, data);
        GL.BindTexture(target, 0);
        return data;
    }

    private static byte[] ReadTexImageRgba8(int textureId, TextureTarget target, int width, int height)
    {
        byte[] data = new byte[checked(width * height * 4)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(target, textureId);
        GL.GetTexImage(target, level: 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        GL.BindTexture(target, 0);
        return data;
    }

    private static (float Min, float Max) MinMax(ReadOnlySpan<float> v)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < v.Length; i++)
        {
            float x = v[i];
            if (x < min) min = x;
            if (x > max) max = x;
        }
        return (min, max);
    }
}
