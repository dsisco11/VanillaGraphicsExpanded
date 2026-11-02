using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
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
[Trait("Category", "GpuIntegration")]
public sealed class LumonSceneMeshCardCaptureComputeTests : RenderTestBase
{


    public LumonSceneMeshCardCaptureComputeTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void Capture_PlanarQuad_WritesZeroDepthAndNormal()
    {
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneCaptureMeshCardComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;

        const int tileSize = 16;
        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();

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


        // SSBO bindings match the shader:
        // binding=0 work, binding=1 patch metadata, binding=2 triangles
        computeProgram.BindMeshCardCaptureWorkSsbo(workSsbo);
        computeProgram.BindPatchMetadataSsbo(metaSsbo);
        computeProgram.BindTrianglesSsbo(triSsbo);

        // Image bindings match the shader layout(binding=...).
        computeProgram.BindDepthAtlasImage(depthAtlas);
        computeProgram.BindMaterialAtlasImage(materialAtlas);

        computeProgram.SetAtlasLayout((uint)tileSize, 1u, 1u, 0u);
        computeProgram.CaptureDepthRange = 1f;

        computeProgram.Dispatch((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MeshCardCapture dispatch (case 1)");

        AssertDepthBounds(depthAtlas,-.02f,.02f);

        byte[] material=GpuLocalRead.Rgba8(materialAtlas,tileSize/2,tileSize/2);
        Assert.Equal(4,material.Length);

        // Spot check center pixel:
        // MaterialAtlas packing: RG = oct-encoded normal, BA = 16-bit surfaceId (here 0).
        int idx=0;
        Assert.InRange(material[idx + 0], (byte)120, (byte)136); // ~0.5
        Assert.InRange(material[idx + 1], (byte)120, (byte)136); // ~0.5
        Assert.Equal((byte)0, material[idx + 2]); // surfaceId lo
        Assert.Equal((byte)0, material[idx + 3]); // surfaceId hi

    }

    [Fact]
    public void Capture_OffsetQuad_WritesExpectedSignedDepth()
    {
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneCaptureMeshCardComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;

        const int tileSize = 16;
        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();

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

        computeProgram.BindMeshCardCaptureWorkSsbo(workSsbo);
        computeProgram.BindPatchMetadataSsbo(metaSsbo);
        computeProgram.BindTrianglesSsbo(triSsbo);

        computeProgram.BindDepthAtlasImage(depthAtlas);
        computeProgram.BindMaterialAtlasImage(materialAtlas);

        computeProgram.SetAtlasLayout((uint)tileSize, 1u, 1u, 0u);
        computeProgram.CaptureDepthRange = 1f;

        computeProgram.Dispatch((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MeshCardCapture dispatch (case 2)");

        AssertDepthBounds(depthAtlas,dz-.03f,dz+.03f);

    }

    [Fact]
    public void Capture_RotatedQuad_WritesZeroDepth_AndExpectedNormal()
    {
        EnsureContextValid();

        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneCaptureMeshCardComputeShader.TryCreate(assets.Api, out var computeProgramOwner, out string computeProgramLog), computeProgramLog);
        using var computeProgram = computeProgramOwner!;

        const int tileSize = 16;
        using var depthAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R16f, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_DepthAtlas" }).RequireResource();
        using var materialAtlas = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, tileSize), Height = System.Math.Max(1, tileSize), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_MaterialAtlas" }).RequireResource();

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

        computeProgram.BindMeshCardCaptureWorkSsbo(workSsbo);
        computeProgram.BindPatchMetadataSsbo(metaSsbo);
        computeProgram.BindTrianglesSsbo(triSsbo);

        computeProgram.BindDepthAtlasImage(depthAtlas);
        computeProgram.BindMaterialAtlasImage(materialAtlas);

        computeProgram.SetAtlasLayout((uint)tileSize, 1u, 1u, 0u);
        computeProgram.CaptureDepthRange = 1f;

        computeProgram.Dispatch((tileSize + 7) / 8, (tileSize + 7) / 8, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("MeshCardCapture dispatch (case 3)");

        AssertDepthBounds(depthAtlas,-.03f,.03f);

        byte[] material=GpuLocalRead.Rgba8(materialAtlas,tileSize/2,tileSize/2);
        int idx=0;
        Assert.Equal((byte)0, material[idx + 2]);
        Assert.Equal((byte)0, material[idx + 3]);
        // RG encodes oct-normal; BA encodes surfaceId.
        Vector3 decoded = DecodeOctNormal01(new Vector2(material[idx + 0] / 255f, material[idx + 1] / 255f));
        Assert.True(Vector3.Dot(decoded, n) > 0.99f, $"Captured normal dot expected too low: {Vector3.Dot(decoded, n)}");

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

    private static GpuShaderStorageBuffer CreateSsbo<T>(string name, ReadOnlySpan<T> data) where T : unmanaged
    {
        var ssbo = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = name }).RequireResource();
        int bytes = checked(data.Length * Marshal.SizeOf<T>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
        ssbo.UploadSubData(data, dstOffsetBytes: 0, byteCount: bytes);
        return ssbo;
    }

    /// <summary>Retains the original non-NaN extrema bounds and requires at least one represented depth over the full capture tile.</summary>
    private void AssertDepthBounds(GpuTexture depth,float minimum,float maximum)
    {
        var proof=ImageAssertions.Predicates.Predicate(depth, SurfacePredicates.LumonSceneMeshCardCaptureComputeTests_AssertDepthBounds,
            parameters:new(minimum,maximum,0,0));
        proof.AssertEqual();Assert.True(proof.MatchingTexels>0);
    }
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}
