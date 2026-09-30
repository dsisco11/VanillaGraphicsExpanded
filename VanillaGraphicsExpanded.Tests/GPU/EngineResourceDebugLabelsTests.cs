using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Diagnostics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Reads driver labels on borrowed engine resource handles without transferring their ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineResourceDebugLabelsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Installs each allocation hook against the engine assembly shipped with the test environment.</summary>
    [Fact]
    public void AllocationHooksMatchInstalledEngineSignatures()
    {
        var harmony = new Harmony("VGE.Tests.EngineResourceLabels");
        try
        {
            foreach (var type in new[] { typeof(EngineFramebufferDebugLabelsHook), typeof(EngineMeshDebugLabelsHook), typeof(EngineOitDebugLabelsHook), typeof(EngineMeshPoolDebugLabelsHook) })
            {
                var patched = harmony.CreateClassProcessor(type).Patch();
#if DEBUG
                Assert.NotEmpty(patched);
#else
                Assert.True(patched is null || patched.Count == 0);
#endif
            }
#if DEBUG
            Assert.Equal(7, harmony.GetPatchedMethods().Count());
#else
            Assert.Empty(harmony.GetPatchedMethods());
#endif
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>Default-table labeling retains the original shared-depth owner and follows replacement handles.</summary>
    [Fact]
    public void DefaultTablePreservesSharedDepthOwnershipAcrossRebuilds()
    {
        EnsureContextValid();
        using var first = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);
        using var second = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);
        using var depth = DynamicTexture2D.Create(2, 2, PixelInternalFormat.R32f);
        using var replacement = DynamicTexture2D.Create(2, 2, PixelInternalFormat.R32f);
        var frames = new[] { Borrow(first), Borrow(second) };
        frames[0].DepthTextureId = frames[1].DepthTextureId = depth.TextureId;
        EngineFramebufferDebugLabels.ApplyDefaults(frames);
#if DEBUG
        string expected = (0 == (int)EnumFrameBuffer.Transparent ? "VS.OIT" : $"VS.{(EnumFrameBuffer)0}") + ".Depth";
        Assert.Equal(expected, Label(ObjectLabelIdentifier.Texture, depth.TextureId));
#endif
        frames[0].DepthTextureId = frames[1].DepthTextureId = replacement.TextureId;
        EngineFramebufferDebugLabels.ApplyDefaults(frames);
#if DEBUG
        Assert.Equal(expected, Label(ObjectLabelIdentifier.Texture, replacement.TextureId));
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Names replacement OIT resources, including its layered accumulation texture, without rebinding state.</summary>
    [Fact]
    public void OitReplacementObjectsAreLabeledWithoutChangingBindings()
    {
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);
        using var reveal = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        using var buckets = Texture3D.Create(2, 2, 3, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture2DArray);
        using var replacement = Texture3D.Create(2, 2, 3, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture2DArray);
        var frame = Borrow(target);
        using var binding = GlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        int activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        EngineFramebufferDebugLabels.ApplyOit(frame, reveal.TextureId, buckets.TextureId);
#if DEBUG
        Assert.Equal("VS.OIT.Framebuffer", Label(ObjectLabelIdentifier.Framebuffer, target.FboId));
        Assert.Equal("VS.OIT.BucketRevealage", Label(ObjectLabelIdentifier.Texture, reveal.TextureId));
        Assert.Equal("VS.OIT.AccumulationBuckets", Label(ObjectLabelIdentifier.Texture, buckets.TextureId));
        Assert.Equal("VS.OIT.Revealage", Label(ObjectLabelIdentifier.Texture, target[1].TextureId));
        Assert.Equal("VS.OIT.Glow", Label(ObjectLabelIdentifier.Texture, target[2].TextureId));
#endif
        EngineFramebufferDebugLabels.ApplyOit(frame, reveal.TextureId, replacement.TextureId);
#if DEBUG
        Assert.Equal("VS.OIT.AccumulationBuckets", Label(ObjectLabelIdentifier.Texture, replacement.TextureId));
#endif
        Assert.Equal(target.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(target.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Equal(activeTexture, GL.GetInteger(GetPName.ActiveTexture));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Names each mesh storage role without binding its VAO or changing the current array buffer.</summary>
    [Fact]
    public void MeshObjectsReceiveRoleNamesWithoutChangingBindings()
    {
        EnsureContextValid();
        using var vao = GpuVao.Create();
        using var vertices = GpuVbo.Create();
        using var flags = GpuVbo.Create();
        using var indices = GpuEbo.Create();
        vertices.UploadData(new float[] { 0, 0, 0 });
        flags.UploadData(new int[] { 0 });
        indices.UploadIndices(new uint[] { 0 });
        using var vaoBinding = vao.BindScope();
        using var bufferBinding = vertices.BindScope();
        var mesh = new VAO { VaoId = vao.VertexArrayId, xyzVboId = vertices.BufferId, flagsVboId = flags.BufferId, vboIdIndex = indices.BufferId };
        // This shell borrows GPU handles; its engine finalizer must not run ownership diagnostics.
        GC.SuppressFinalize(mesh);
        EngineMeshDebugLabels.Apply(mesh, "VS.TestMesh");
#if DEBUG
        Assert.Equal("VS.TestMesh.VAO", Label(ObjectLabelIdentifier.VertexArray, vao.VertexArrayId));
        Assert.Equal("VS.TestMesh.Positions", Label(ObjectLabelIdentifier.Buffer, vertices.BufferId));
        Assert.Equal("VS.TestMesh.Flags", Label(ObjectLabelIdentifier.Buffer, flags.BufferId));
        Assert.Equal("VS.TestMesh.Indices", Label(ObjectLabelIdentifier.Buffer, indices.BufferId));
#endif
        int previousSharedIndex = ClientPlatformAbstract.singleIndexBufferId;
        try
        {
            // SSBO meshes borrow one global index allocation; successive pool labels must preserve that role.
            ClientPlatformAbstract.singleIndexBufferId = indices.BufferId;
            EngineMeshDebugLabels.Apply(mesh, "VS.FirstPool");
            EngineMeshDebugLabels.Apply(mesh, "VS.SecondPool");
#if DEBUG
            Assert.Equal("VS.Mesh.SharedIndices", Label(ObjectLabelIdentifier.Buffer, indices.BufferId));
#endif
        }
        finally
        {
            ClientPlatformAbstract.singleIndexBufferId = previousSharedIndex;
        }
        Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(vertices.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Labels appended pools with pass and atlas context, while existing allocations require no relabeling.</summary>
    [Fact]
    public void PoolGrowthReceivesSemanticLabelsOnlyForNewAllocations()
    {
        EnsureContextValid();
        using var vao = GpuVao.Create();
        using var vao2 = GpuVao.Create();
        // A generated VAO name becomes an object only after its first bind, as in engine allocation.
        using (vao.BindScope()) { }
        using (vao2.BindScope()) { }
        var first = new VAO { VaoId = vao.VertexArrayId };
        var second = new VAO { VaoId = vao2.VertexArrayId };
        GC.SuppressFinalize(first);
        GC.SuppressFinalize(second);
        var manager = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
        var pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
        var pool2 = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
        var pools = new List<MeshDataPool> { pool };
        AccessTools.Field(typeof(MeshDataPoolManager), "pools").SetValue(manager, pools);
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(pool, first);
        AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(pool2, second);
        int pass = (int)EnumChunkRenderPass.Liquid;
        var tables = new MeshDataPoolManager[pass + 1][];
        tables[pass] = [manager];
        EngineMeshPoolDebugLabels.Register(tables);
#if DEBUG
        Assert.Equal("VS.MeshPool.Liquid.Atlas0.Pool0.VAO", Label(ObjectLabelIdentifier.VertexArray, vao.VertexArrayId));
#endif
        EngineMeshDebugLabels.Apply(first, "Test.Unchanged");
        pools.Add(pool2);
        EngineMeshPoolDebugLabels.LabelAdded(manager, pools, 1);
        EngineMeshPoolDebugLabels.LabelAdded(manager, pools, pools.Count);
#if DEBUG
        Assert.Equal("Test.Unchanged.VAO", Label(ObjectLabelIdentifier.VertexArray, vao.VertexArrayId));
        Assert.Equal("VS.MeshPool.Liquid.Atlas0.Pool1.VAO", Label(ObjectLabelIdentifier.VertexArray, vao2.VertexArrayId));
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Builds engine metadata referencing resources whose lifetime remains with the GPU fixture.</summary>
    private static FrameBufferRef Borrow(GpuFramebuffer framebuffer) => new()
    {
        FboId = framebuffer.FboId,
        Width = 2,
        Height = 2,
        ColorTextureIds = [framebuffer[0].TextureId, framebuffer[1].TextureId, framebuffer[2].TextureId]
    };

    /// <summary>Queries the actual driver label rather than the managed debug name.</summary>
    private static string Label(ObjectLabelIdentifier kind, int id)
    {
        GL.GetObjectLabel(kind, id, 512, out _, out string label);
        return label;
    }
    #endregion
}
