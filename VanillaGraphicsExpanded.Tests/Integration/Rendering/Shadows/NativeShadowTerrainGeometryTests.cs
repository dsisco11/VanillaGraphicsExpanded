using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises packaged native terrain materials and borrowed engine buffers through real OpenGL draws.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowTerrainGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares integer comparison executables used to verify complete GPU shadow tiles.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs) =>
        programs.Prepare("compare", 2, 0);

    /// <summary>Ordinary vertices and packed face SSBOs both write the expected center-plane virtual depth.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NativeOrdinaryAndStorageGeometryRespectAtlasCoverage(bool storage, bool opaqueAtlas)
    {
        // Arrange: allocate one writable virtual page, its immutable view record, and production native terrain binaries.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4,
            "The context cannot execute the four-plane native terrain shadow fixture.");
        Assert.SkipWhen(storage && GpuSupport.Graphics.MaxVertexShaderStorageBlocks < 1,
            "The context has no vertex-stage storage block for native packed terrain.");
        using var api = new BinaryShaderApiFixture();
        var (poolOwner, physical) = CreateWritablePage(api.Api);
        using var pool = poolOwner;
        var view = CreateView(physical);
        using var views = new ShadowViewBuffer([view], pool, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 64
        });
        using var native = new NativeTerrainStorage(storage);
        using var geometry = new NativeShadowTerrainGeometry(native.Mesh, 4, 6, storage);
        using var selectors = VertexData([0u, 0u], "Tests.Shadows.NativeTerrainSelectors");
        var inputs = geometry.WithViews(new(new GpuBufferRef(selectors), 0, 2 * sizeof(uint)));

        var contract = GpuShaderContracts.Registry.FindProgram("shadows/terrain");
        var options = new Dictionary<string, string?>
        {
            ["VGE_TERRAIN_PACKED_INPUT"] = storage ? "1" : "0",
            ["VGE_VERTEX_DEFORMATION_ENABLED"] = "0"
        };
        using var shader = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(contract, options)));
        using var atlas = Atlas([255, 255, 255, opaqueAtlas ? (byte)255 : (byte)0]);
        using var sampler = RHICommandContext.Current.CreateSampler(new RHISamplerDescription
        {
            MinFilter = SamplerFilter.Nearest, MagFilter = SamplerFilter.Nearest,
            AddressS = SamplerAddressMode.ClampToEdge, AddressT = SamplerAddressMode.ClampToEdge
        }).RequireResource();
        using var material = MaterialBuffer(view);
        var commands = RHICommandContext.Current;
        var pass = views.DescribePass(0);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader, inputs.Layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        var bindings = TerrainBindings(shader, views, pool, atlas, sampler, material,
            storage ? geometry.Faces : default);

        // Act: submit the selected native layout through the packaged material, view projection, and atomic page writer.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.NativeTerrain", true,
                   graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(bindings);
                commands.DrawIndexed(inputs, new(0, 6));
                commands.EndRenderPass();
                commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }

        // Assert: accepted z=0 fragments write mid-depth; native alpha-discarded fragments leave the integer tile clear.
        var region = TileRegion(physical, pool.PageSize);
        var clear = ImageAssertions.Comparisons.CompareUnsignedConstant(pool.DepthBank(physical.Bank),
            ShadowDepthEncoding.Clear, region);
        uint written = checked((uint)(pool.PageSize * pool.PageSize) - clear.ConstantMatches);
        if (opaqueAtlas)
        {
            Assert.True(written > 0);
            var exact = ImageAssertions.Comparisons.CompareUnsignedConstant(pool.DepthBank(physical.Bank),
                ShadowDepthEncoding.Encode(0.5f), region);
            Assert.Equal(written, exact.ConstantMatches);
        }
        else Assert.Equal(0u, written);
        pool.SealBatch();
        views.Seal();
    }

    /// <summary>Resizing borrowed face data withdraws the exact SSBO interval and preserves native owners.</summary>
    [Fact]
    public void FaceStorageBorrowUsesCurrentSharedIndicesAndWithdrawsOnResize()
    {
        // Arrange: prepare a packed four-vertex face allocation beside a distinct saved VAO element buffer.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxVertexShaderStorageBlocks < 1,
            "The context has no vertex-stage storage block for native packed terrain.");
        using var native = new NativeTerrainStorage(storage: true);
        using var geometry = new NativeShadowTerrainGeometry(native.Mesh, 4, 6, storage: true);
        using var selectors = VertexData([0u, 0u], "Tests.Shadows.NativeTerrainBorrowSelectors");
        var selection = geometry.WithViews(new(new GpuBufferRef(selectors), 0, 2 * sizeof(uint)));
        var face = geometry.Faces;
        Assert.True(geometry.IsPrepared);
        Assert.Equal((0, 64), (face.OffsetBytes, face.SizeBytes));
        Assert.True(face.Buffer.IsValid);
        Assert.True(face.Buffer.TryGetMetadata(out var faceMetadata));
        Assert.Equal((nint)64, faceMetadata.Capacity);
        Assert.Equal((nint)native.SharedIndexId, selection.Indices!.Value.Range.Buffer.RequireResource().ResourceId);
        Assert.NotEqual(native.Mesh.vboIdIndex, selection.Indices!.Value.Range.Buffer.RequireResource().ResourceId);
        Assert.Equal((nint)24, selection.Indices.Value.Range.Length);
        // Assert actual borrowed engine names remain live across native range publication; no RHI foreign-name query exists.
        Assert.True(GL.IsBuffer(native.FaceData.BufferId));

        // Act: the engine owner replaces the borrowed face-data allocation while the adapter retains its old certificate.
        native.FaceData.Allocate(128);

        // Assert: consumers cannot reuse the captured face range after resize, and disposing the borrow keeps engine storage alive.
        Assert.False(geometry.IsPrepared);
        Assert.Throws<InvalidOperationException>(() => geometry.WithViews(
            new(new GpuBufferRef(selectors), 0, 2 * sizeof(uint))));
        geometry.Dispose();
        Assert.True(GL.IsBuffer(native.FaceData.BufferId));
        Assert.True(GL.IsBuffer(native.SharedIndexId));
        Assert.True(GL.IsVertexArray(native.Mesh.VaoId));
    }
    #endregion

    #region Private
    /// <summary>Allocates, selectively clears, and freezes one exact page for a geometry draw.</summary>
    private static (ShadowPagePool Pool, PhysicalPage Physical) CreateWritablePage(Vintagestory.API.Common.ICoreAPI api)
    {
        var pool = ShadowPagePool.Create(api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, BankCapacity = 1,
            ProbeLimit = 1, OperationsPerBatch = 16
        });
        var source = new ShadowSourceKey(81, 0, ShadowSourceKind.Sun, 4, 12, 0);
        var bounds = new ShadowBounds(-16, -16, -16, 16, 16, 16);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            bounds, baseTexelWidth: 1, levels: 1, pageSize: 8);
        var key = new ShadowPageKey(source, 1, 0, 0, 0);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate, key,
            bounds, 1, 1, 0, Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        try
        {
            if (!pool.BeginBatch() || !pool.Execute(request)) throw new InvalidOperationException("Native terrain page allocation failed.");
            var result = Read(pool.Results, 8);
            if (result[0] != 0) throw new InvalidOperationException("Native terrain page allocation was rejected.");
            int slot = checked((int)result[1]);
            if (slot >= pool.Capacity) throw new InvalidOperationException("Native terrain page allocation returned no resident slot.");
            PhysicalPage physical;
            using (var entry = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read))
            {
                if (!entry.IsMapped) throw new InvalidOperationException("Native terrain page assignment could not be read.");
                physical = new(checked((int)entry.Span[0]), checked((int)entry.Span[1]), checked((int)entry.Span[2]));
            }
            if (!pool.ClearReplacement() || !pool.FreezeWritableAssignments())
                throw new InvalidOperationException("Native terrain page was not frozen for writing.");
            return (pool, physical);
        }
        catch { pool.Dispose(); throw; }
    }

    /// <summary>Uses a symmetric directional source so a world-space z of zero has independently known depth one-half.</summary>
    private static ShadowDrawView CreateView(PhysicalPage physical)
    {
        var source = new ShadowSourceKey(81, 0, ShadowSourceKind.Sun, 4, 12, 0);
        var bounds = new ShadowBounds(-16, -16, -16, 16, 16, 16);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            bounds, baseTexelWidth: 1, levels: 1, pageSize: 8);
        var region = new ShadowRasterRegion(0, 0, 8, 8);
        return ShadowDrawView.Directional(projection, 0, region, region, Vector3d.Zero);
    }

    /// <summary>Creates the complete retained binding table for one packaged native terrain draw.</summary>
    private static RHIResourceBindings TerrainBindings(RHIShader shader, ShadowViewBuffer views, ShadowPagePool pool,
        GpuTexture atlas, GpuSampler sampler, GpuUniformBuffer material, GpuStorageBufferBinding face)
    {
        var buffers = new List<RHIBufferBinding>
        {
            new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "ShadowWriteEntries"), BufferRole.ShaderStorage, pool.Entries),
            new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "ShadowWritableAssignments"), BufferRole.ShaderStorage, pool.WritableAssignments),
            new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeShadowMaterial"), BufferRole.Uniform,
                material, 0, 208, Whole: false)
        };
        if (!face.Buffer.IsEmpty)
            buffers.Add(new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "VgeShadowFaces"), BufferRole.ShaderStorage,
                face.Buffer, face.OffsetBytes, face.SizeBytes, Whole: false));
        return new(shader,
            samplers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "shadowViews"), views.Texture),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_shadowAtlas"), atlas, Sampler: sampler)
            ],
            images:
            [
                Image("shadowBank0", pool.DepthBank(0)), Image("shadowBank1", pool.DepthBank(0)),
                Image("shadowBank2", pool.DepthBank(0)), Image("shadowBank3", pool.DepthBank(0))
            ],
            buffers: buffers);
    }

    /// <summary>Captures the exact 208-byte native-material block with identity matrices and zero warp controls.</summary>
    private static GpuUniformBuffer MaterialBuffer(ShadowDrawView view)
    {
        byte[] bytes = new byte[208];
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        System.Buffer.BlockCopy(view.Matrix.ToArray(), 0, bytes, 0, 64);
        System.Buffer.BlockCopy(identity, 0, bytes, 144, 64);
        var buffer = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.DynamicDraw, Label = "Tests.Shadows.NativeTerrainMaterial"
        }).RequireResource();
        buffer.UploadData<byte>(bytes);
        return buffer;
    }

    /// <summary>Creates a one-texel atlas with caller-selected alpha using normal texture upload and nearest sampling.</summary>
    private static GpuTexture Atlas(byte[] color)
    {
        var commands = RHICommandContext.Current;
        var texture = commands.CreateTexture(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
            Width = 1, Height = 1, Depth = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.NativeTerrainAtlas"
        }).RequireResource();
        var encoding = commands.GetTextureEncoding(texture, PixelComponentType.UnsignedByte);
        commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 1, 1), encoding, color);
        return texture;
    }

    /// <summary>Creates one storage-free selector buffer for the compact object/view instance record.</summary>
    private static GpuVbo VertexData(uint[] words, string label)
    {
        var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();
        buffer.UploadData(words);
        return buffer;
    }

    /// <summary>Builds the selected physical tile's source-coordinate region in its depth bank.</summary>
    private static RHITextureRegion TileRegion(PhysicalPage page, int pageSize) =>
        new(new(0), page.TileX * pageSize, page.TileY * pageSize, 0, pageSize, pageSize);

    /// <summary>Builds one explicit read/write image view required by atomic minimum.</summary>
    private static RHIImageInput Image(string name, GpuTexture texture) =>
        new(GpuBindingEntry.Identity(ShaderBindingKind.Image, name),
            new RHIImageBinding(texture, ImageAccess.ReadWrite, Format: TextureFormat.R32ui));

    /// <summary>Reads an allocator result prefix for the slot's bank and physical page coordinates.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Captures a shader-selected physical page location without treating its slot as a texture coordinate.</summary>
    private readonly record struct PhysicalPage(int Bank, int TileX, int TileY);

    /// <summary>Owns test allocations while presenting an engine-shaped mesh whose VAO and buffers remain externally owned.</summary>
    private sealed class NativeTerrainStorage : IDisposable
    {
        private readonly int previousSharedIndex;
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = VertexBuffer("Tests.Shadows.NativeTerrainPosition");
        private readonly GpuVbo uv = VertexBuffer("Tests.Shadows.NativeTerrainUv");
        private readonly GpuVbo colors = VertexBuffer("Tests.Shadows.NativeTerrainColor");
        private readonly GpuVbo flags = VertexBuffer("Tests.Shadows.NativeTerrainFlags");
        private readonly GpuEbo savedElements = IndexBuffer("Tests.Shadows.NativeTerrainSavedIndices");
        private readonly GpuEbo sharedElements = IndexBuffer("Tests.Shadows.NativeTerrainSharedIndices");
        internal GpuShaderStorageBuffer FaceData { get; }
        internal VAO Mesh { get; }
        internal int SharedIndexId => sharedElements.BufferId;

        /// <summary>Uploads matching native quad streams and lets SSBO geometry select a distinct live shared index buffer.</summary>
        internal NativeTerrainStorage(bool storage)
        {
            previousSharedIndex = ClientPlatformAbstract.singleIndexBufferId;
            positions.UploadData<float>([0, 0, 0, 16, 0, 0, 0, 16, 0, 16, 16, 0]);
            uv.UploadData<float>(new float[8]);
            colors.UploadData<byte>(Enumerable.Repeat((byte)255, 16).ToArray());
            flags.UploadData<uint>(new uint[4]);
            uint[] indices = [0, 1, 2, 2, 1, 3];
            savedElements.UploadIndices(indices);
            sharedElements.UploadIndices(indices);
            FaceData = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
            {
                Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0,
                Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.NativeTerrainFaces"
            }).RequireResource();
            FaceData.UploadData<byte>(PackFaceData());
            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId;
            Mesh.vboIdIndex = savedElements.BufferId;
            Mesh.drawMode = PrimitiveType.Triangles;
            Mesh.xyzVboId = storage ? FaceData.BufferId : positions.BufferId;
            Mesh.uvVboId = uv.BufferId;
            Mesh.rgbaVboId = colors.BufferId;
            Mesh.flagsVboId = flags.BufferId;
            if (storage) ClientPlatformAbstract.singleIndexBufferId = sharedElements.BufferId;
        }

        /// <summary>Leaves no native alias alive after the engine-shaped borrower has retired.</summary>
        public void Dispose()
        {
            ClientPlatformAbstract.singleIndexBufferId = previousSharedIndex;
            FaceData.Dispose(); sharedElements.Dispose(); savedElements.Dispose();
            flags.Dispose(); colors.Dispose(); uv.Dispose(); positions.Dispose(); vao.Dispose();
        }

        /// <summary>Creates an initialized RHI vertex buffer for one native stream.</summary>
        private static GpuVbo VertexBuffer(string label) => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Creates an initialized engine-shaped element allocation.</summary>
        private static GpuEbo IndexBuffer(string label) => RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Packs a 64-byte std430 face record with eight-unit half-edges and no deformation flags.</summary>
        private static byte[] PackFaceData()
        {
            byte[] bytes = new byte[64];
            WriteVec3(bytes, 0, 0, 0, 0);
            WriteVec3(bytes, 16, 8, 0, 0);
            WriteVec3(bytes, 48, 0, 8, 0);
            return bytes;
        }

        /// <summary>Writes one tightly packed three-float component at its explicitly aligned face-record offset.</summary>
        private static void WriteVec3(byte[] bytes, int offset, float x, float y, float z)
        {
            System.Buffer.BlockCopy(new[] { x, y, z }, 0, bytes, offset, 3 * sizeof(float));
        }
    }
    #endregion
}
