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

    private const int CaptureMeshCardParamsUboSizeBytes = 32;

    [Fact]
    public void Capture_PlanarQuad_WritesZeroDepthAndNormal()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_meshcard", debugName: "Tests.MeshCardCapture.PlanarQuad");
        int program = computeProgram.ProgramId;

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

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneMeshCardCapture.PlanarQuad.ParamsUBO");

        GL.UseProgram(program);

        // SSBO bindings match the shader:
        // binding=0 work, binding=1 patch metadata, binding=2 triangles
        workSsbo.BindBase(bindingIndex: 0);
        metaSsbo.BindBase(bindingIndex: 1);
        triSsbo.BindBase(bindingIndex: 2);

        // Image bindings match the shader layout(binding=...).
        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        Span<byte> paramsBytes = stackalloc byte[CaptureMeshCardParamsUboSizeBytes];
        UboPacking.WriteUVec4(paramsBytes, byteOffset: 0, (uint)tileSize, 1u, 1u, 0u);
        UboPacking.WriteVec4(paramsBytes, byteOffset: 16, 1f, 0f, 0f, 0f);
        paramsUbo.UploadAndBind(paramsBytes);

        GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MeshCardCapture dispatch (case 1)");

        float[] depth = ReadTexImageR32f(depthAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        Assert.Equal(tileSize * tileSize, depth.Length);

        // All depths should be ~0 (planar on the card plane).
        (float min, float max) = MinMax(depth);
        Assert.InRange(min, -0.02f, 0.02f);
        Assert.InRange(max, -0.02f, 0.02f);

        byte[] material = ReadTexImageRgba8(materialAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        Assert.Equal(tileSize * tileSize * 4, material.Length);

        // Spot check center pixel:
        // MaterialAtlas packing: RG = oct-encoded normal, BA = 16-bit surfaceId (here 0).
        int cx = tileSize / 2;
        int cy = tileSize / 2;
        int idx = (cy * tileSize + cx) * 4;
        Assert.InRange(material[idx + 0], (byte)120, (byte)136); // ~0.5
        Assert.InRange(material[idx + 1], (byte)120, (byte)136); // ~0.5
        Assert.Equal((byte)0, material[idx + 2]); // surfaceId lo
        Assert.Equal((byte)0, material[idx + 3]); // surfaceId hi

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Capture_OffsetQuad_WritesExpectedSignedDepth()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_meshcard", debugName: "Tests.MeshCardCapture.Slanted");
        int program = computeProgram.ProgramId;

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

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneMeshCardCapture.OffsetQuad.ParamsUBO");

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        metaSsbo.BindBase(bindingIndex: 1);
        triSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        Span<byte> paramsBytes = stackalloc byte[CaptureMeshCardParamsUboSizeBytes];
        UboPacking.WriteUVec4(paramsBytes, byteOffset: 0, (uint)tileSize, 1u, 1u, 0u);
        UboPacking.WriteVec4(paramsBytes, byteOffset: 16, 1f, 0f, 0f, 0f);
        paramsUbo.UploadAndBind(paramsBytes);

        GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MeshCardCapture dispatch (case 2)");

        float[] depth = ReadTexImageR32f(depthAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        (float min, float max) = MinMax(depth);

        Assert.InRange(min, dz - 0.03f, dz + 0.03f);
        Assert.InRange(max, dz - 0.03f, dz + 0.03f);

        // Program disposed via ComputeProgram.
    }

    [Fact]
    public void Capture_RotatedQuad_WritesZeroDepth_AndExpectedNormal()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        using var computeProgram = ComputeProgram.Create(helper, "lumonscene_capture_meshcard", debugName: "Tests.MeshCardCapture.Coverage");
        int program = computeProgram.ProgramId;

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

        using var paramsUbo = new ObjectParamsUbo("Tests.LumonSceneMeshCardCapture.RotatedQuad.ParamsUBO");

        GL.UseProgram(program);
        workSsbo.BindBase(bindingIndex: 0);
        metaSsbo.BindBase(bindingIndex: 1);
        triSsbo.BindBase(bindingIndex: 2);

        GL.BindImageTexture(0, depthAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.R16f);
        GL.BindImageTexture(1, materialAtlas.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba8);

        Span<byte> paramsBytes = stackalloc byte[CaptureMeshCardParamsUboSizeBytes];
        UboPacking.WriteUVec4(paramsBytes, byteOffset: 0, (uint)tileSize, 1u, 1u, 0u);
        UboPacking.WriteVec4(paramsBytes, byteOffset: 16, 1f, 0f, 0f, 0f);
        paramsUbo.UploadAndBind(paramsBytes);

        GL.DispatchCompute((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MeshCardCapture dispatch (case 3)");

        float[] depth = ReadTexImageR32f(depthAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        (float min, float max) = MinMax(depth);
        Assert.InRange(min, -0.03f, 0.03f);
        Assert.InRange(max, -0.03f, 0.03f);

        byte[] material = ReadTexImageRgba8(materialAtlas.TextureId, TextureTarget.Texture2DArray, tileSize, tileSize);
        int cx = tileSize / 2;
        int cy = tileSize / 2;
        int idx = (cy * tileSize + cx) * 4;
        Assert.Equal((byte)0, material[idx + 2]);
        Assert.Equal((byte)0, material[idx + 3]);
        // RG encodes oct-normal; BA encodes surfaceId.
        Vector3 decoded = DecodeOctNormal01(new Vector2(material[idx + 0] / 255f, material[idx + 1] / 255f));
        Assert.True(Vector3.Dot(decoded, n) > 0.99f, $"Captured normal dot expected too low: {Vector3.Dot(decoded, n)}");

        // Program disposed via ComputeProgram.
    }

    private static Vector3 DecodeOctNormal01(Vector2 oct01)
    {
        Vector2 f = oct01 * 2f - Vector2.One; // [-1,1]
        Vector3 v = new(f.X, f.Y, 1f - MathF.Abs(f.X) - MathF.Abs(f.Y));
        if (v.Z < 0f)
        {
            float oldX = v.X;
            v.X = (1f - MathF.Abs(v.Y)) * MathF.Sign(oldX);
            v.Y = (1f - MathF.Abs(oldX)) * MathF.Sign(v.Y);
        }

        return Vector3.Normalize(v);
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

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: name);
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
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
