using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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

/// <summary>Exercises packaged adaptive terrain shadow variants through native layouts and actual atomic page writes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowAdaptiveTerrainTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares integer comparison and inspection executables used to verify full GPU shadow tiles.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        programs.Prepare("compare", 2, 0);
        programs.Prepare("inspection", 2, 0);
    }

    /// <summary>Ordinary and SSBO tessellated materials retain alpha rejection and apply the captured height displacement.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PackagedAdaptiveTerrainWritesDisplacedDepthAfterNativeCutout(bool storage, bool opaqueAtlas)
    {
        // Arrange: freeze one writable page and load the exact packaged adaptive shader for the native mesh layout.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4,
            "The context cannot execute the four-plane adaptive terrain shadow fixture.");
        Assert.SkipWhen(GpuSupport.Graphics.MaxTessGenLevel < 3,
            "The context cannot generate the adaptive terrain patch subdivision used by this fixture.");
        Assert.SkipWhen(storage && GpuSupport.Graphics.MaxVertexShaderStorageBlocks < 1,
            "The context has no vertex-stage storage block for native packed terrain.");
        using var api = new BinaryShaderApiFixture();
        var (poolOwner, page) = CreateWritablePage(api.Api);
        using var pool = poolOwner;
        var view = CreateView();
        using var views = new ShadowViewBuffer([view], pool, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 64
        });
        using var native = new NativeTerrainStorage(storage);
        using var geometry = new NativeShadowTerrainGeometry(native.Mesh, 4, 6, storage);
        using var selectors = VertexData([0u, 0u], "Tests.Shadows.NativeAdaptiveSelectors");
        var inputs = geometry.WithViews(new(new GpuBufferRef(selectors), 0, 2 * sizeof(uint)));
        using var shader = ShadowDisplacedTerrainShader.Create(api.Api, storage, waving: false);
        using var atlas = Texture(1, 1, [255, 255, 255, opaqueAtlas ? (byte)255 : (byte)0], "NativeAdaptiveAtlas");
        using var indices = IndexTexture();
        using var records = RecordTexture();
        using var height = HeightTexture();
        CaptureMaterial(shader, view);
        shader.CaptureSubdivision(1, 5, 64, 128, 8, true,
            indices, records, height);
        CaptureVirtualBindings(shader, views, pool, geometry, atlas);

        var commands = RHICommandContext.Current;
        var pass = views.DescribePass(0);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader.Shader, inputs.Layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveTopology.Patches, PatchVertices = 3 },
            label: "Tests.Shadows.NativeAdaptiveTerrain")).RequireResource();

        // Act: draw triangle patches through packaged vertex, control, evaluation and native cutout fragment stages.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.NativeAdaptiveTerrain", true,
                   graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(shader.CaptureBindings());
                commands.DrawIndexed(inputs, new(0, 6));
                commands.EndRenderPass();
                commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }

        // Assert: native alpha rejects before writes, while accepted fragments encode the known positive height offset.
        var region = TileRegion(page, pool.PageSize);
        var clear = ImageAssertions.Comparisons.CompareUnsignedConstant(pool.DepthArray,
            ShadowDepthEncoding.Clear, region);
        uint written = checked((uint)(pool.PageSize * pool.PageSize) - clear.ConstantMatches);
        if (!opaqueAtlas) Assert.Equal(0u, written);
        else
        {
            Assert.True(written > 0);
            // The neutral counterfactual is exactly 0.5. Positive +Z displacement reduces depth;
            // the amplitude cap of .047 over the 34-unit fitted depth span bounds the full effect.
            var range = ImageAssertions.Statistics.Inspect(pool.DepthArray,
                ShadowDepthEncoding.Encode(0.4985f), ShadowDepthEncoding.Encode(0.5f), region);
            Assert.Equal(written, range.InsideRange);
            var displaced = ImageAssertions.Statistics.Inspect(pool.DepthArray, 0,
                ShadowDepthEncoding.Encode(0.5f) - 1d, region);
            Assert.True(displaced.InsideRange > 0);
        }
        pool.SealBatch();
        views.Seal();
    }
    #endregion

    #region Private
    /// <summary>Builds the selected physical tile's source-coordinate region in its depth layer.</summary>
    private static RHITextureRegion TileRegion(PhysicalPage page, int pageSize) =>
        new(new(0, page.Layer), page.TileX * pageSize, page.TileY * pageSize, 0, pageSize, pageSize);
    /// <summary>Captures identity native transforms so final displacement remains the only known depth offset.</summary>
    private static void CaptureMaterial(ShadowDisplacedTerrainShader shader, ShadowDrawView view)
    {
        byte[] bytes = new byte[208];
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        System.Buffer.BlockCopy(view.Matrix.ToArray(), 0, bytes, 0, 64);
        System.Buffer.BlockCopy(identity, 0, bytes, 144, 64);
        shader.CaptureMaterial(bytes);
    }

    /// <summary>Supplies exact native atlas, view, face-data, writable-page and integer-image bindings.</summary>
    private static void CaptureVirtualBindings(ShadowDisplacedTerrainShader shader, ShadowViewBuffer views,
        ShadowPagePool pool, NativeShadowTerrainGeometry geometry, GpuTexture atlas)
    {
        var terrain = (IShadowTerrainBindings)shader;
        terrain.Views = views.Texture;
        terrain.Atlas = atlas;
        terrain.Faces = geometry.Faces;
        terrain.Entries = pool.Entries;
        terrain.Writable = pool.WritableAssignments;
        terrain.Depth = IntegerImage(pool.DepthArray);
    }

    /// <summary>Builds one page and captures its allocated physical coordinates before later commands overwrite results.</summary>
    private static (ShadowPagePool Pool, PhysicalPage Page) CreateWritablePage(Vintagestory.API.Common.ICoreAPI api)
    {
        var pool = ShadowPagePool.Create(api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, ArrayLayers = 1,
            ProbeLimit = 1, OperationsPerBatch = 16
        });
        var source = new ShadowSourceKey(93, 0, ShadowSourceKind.Sun, 3, 7, 0);
        var bounds = new ShadowBounds(-16, -16, -16, 16, 16, 16);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            bounds, baseTexelWidth: 1, levels: 1, pageSize: 8);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate,
            new ShadowPageKey(source, 1, 0, 0, 0), bounds, 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        try
        {
            if (!pool.BeginBatch() || !pool.Execute(request)) throw new InvalidOperationException("Adaptive terrain page allocation failed.");
            var result = Read(pool.Results, 8);
            if (result[0] != 0 || result[1] >= pool.Capacity) throw new InvalidOperationException("Adaptive terrain page was rejected.");
            int slot = checked((int)result[1]);
            PhysicalPage page;
            using (var entry = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read))
            {
                if (!entry.IsMapped) throw new InvalidOperationException("Adaptive terrain page assignment could not be read.");
                page = new(checked((int)entry.Span[0]), checked((int)entry.Span[1]), checked((int)entry.Span[2]));
            }
            if (!pool.ClearReplacement() || !pool.FreezeWritableAssignments())
                throw new InvalidOperationException("Adaptive terrain page was not frozen for raster writes.");
            return (pool, page);
        }
        catch { pool.Dispose(); throw; }
    }

    /// <summary>Creates a directional view with a known depth of one-half before positive surface displacement.</summary>
    private static ShadowDrawView CreateView()
    {
        var source = new ShadowSourceKey(93, 0, ShadowSourceKind.Sun, 3, 7, 0);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            new ShadowBounds(-16, -16, -16, 16, 16, 16), baseTexelWidth: 1, levels: 1, pageSize: 8);
        var region = new ShadowRasterRegion(0, 0, 8, 8);
        return ShadowDrawView.Directional(projection, 0, region, region, Vector3d.Zero);
    }

    /// <summary>Creates immutable nearest-sampled RGBA8 data for the installed shader's authored material contract.</summary>
    private static GpuTexture Texture(int width, int height, byte[] bytes, string label)
    {
        var commands = RHICommandContext.Current;
        var texture = commands.CreateTexture(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
            Width = width, Height = height, Depth = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            BaseLevel = 0, MaxLevel = 0, Label = label
        }).RequireResource();
        var encoding = commands.GetTextureEncoding(texture, PixelComponentType.UnsignedByte);
        commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, width, height), encoding, bytes);
        return texture;
    }

    /// <summary>Encodes one valid displacement record index across the native atlas UV region.</summary>
    private static GpuTexture IndexTexture()
    {
        byte[] bytes = new byte[8 * 8 * 4];
        for (int i = 0; i < bytes.Length; i += 4) bytes[i] = 255;
        return Texture(8, 8, bytes, "Tests.Shadows.NativeAdaptiveIndices");
    }

    /// <summary>Stores a half-tile UV rectangle and an exact bounded native displacement amplitude.</summary>
    private static GpuTexture RecordTexture()
    {
        var commands = RHICommandContext.Current;
        var texture = commands.CreateTexture(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f,
            Width = 2, Height = 1, Depth = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.NativeAdaptiveRecords"
        }).RequireResource();
        float[] values = [0, 0, .5f, .5f, .047f, 0, 0, 1];
        byte[] bytes = new byte[values.Length * sizeof(float)];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var encoding = commands.GetTextureEncoding(texture, PixelComponentType.Float);
        commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 2, 1), encoding, bytes);
        return texture;
    }

    /// <summary>Provides positive initialized native height in the alpha channel for every sampled texel.</summary>
    private static GpuTexture HeightTexture()
    {
        byte[] bytes = new byte[8 * 8 * 4];
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        return Texture(8, 8, bytes, "Tests.Shadows.NativeAdaptiveHeight");
    }

    /// <summary>Builds an exact writable integer image view for atomic page accumulation.</summary>
    private static RHIImageBinding IntegerImage(GpuTexture texture) =>
        new(texture, ImageAccess.ReadWrite, Layered: true, Format: TextureFormat.R32ui);

    /// <summary>Creates the native object/view selector stream required by the generated vertex interface.</summary>
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

    /// <summary>Reads a bounded GPU allocator result prefix for the current replacement slot.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Retains the array layer and tile returned by the allocator for exact post-draw inspection.</summary>
    private readonly record struct PhysicalPage(int Layer, int TileX, int TileY);

    /// <summary>Owns native-shaped mesh buffers while the geometry adapter borrows their current names.</summary>
    private sealed class NativeTerrainStorage : IDisposable
    {
        private readonly int previousSharedIndex;
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = VertexBuffer("Tests.Shadows.NativeAdaptivePosition");
        private readonly GpuVbo uv = VertexBuffer("Tests.Shadows.NativeAdaptiveUv");
        private readonly GpuVbo colors = VertexBuffer("Tests.Shadows.NativeAdaptiveColor");
        private readonly GpuVbo flags = VertexBuffer("Tests.Shadows.NativeAdaptiveFlags");
        private readonly GpuEbo savedElements = IndexBuffer("Tests.Shadows.NativeAdaptiveSavedIndices");
        private readonly GpuEbo sharedElements = IndexBuffer("Tests.Shadows.NativeAdaptiveSharedIndices");
        internal GpuShaderStorageBuffer FaceData { get; }
        internal VAO Mesh { get; }

        /// <summary>Uploads a four-vertex quad and an engine-compatible packed face for ordinary or SSBO selection.</summary>
        internal NativeTerrainStorage(bool storage)
        {
            previousSharedIndex = ClientPlatformAbstract.singleIndexBufferId;
            positions.UploadData<float>([-8, -8, 0, 8, -8, 0, -8, 8, 0, 8, 8, 0]);
        uv.UploadData<float>([0, 0, .5f, 0, 0, .5f, .5f, .5f]);
            colors.UploadData<byte>(Enumerable.Repeat((byte)255, 16).ToArray());
            uint upwardNormal = 7u << 22;
            flags.UploadData<uint>(Enumerable.Repeat(upwardNormal, 4).ToArray());
            uint[] indices = [0, 1, 2, 2, 1, 3];
            savedElements.UploadIndices(indices);
            sharedElements.UploadIndices(indices);
            FaceData = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
            {
                Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0,
                Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.NativeAdaptiveFaces"
            }).RequireResource();
            FaceData.UploadData<byte>(PackFaceData());
            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = savedElements.BufferId;
            Mesh.drawMode = PrimitiveType.Triangles;
            Mesh.xyzVboId = storage ? FaceData.BufferId : positions.BufferId;
            Mesh.uvVboId = uv.BufferId; Mesh.rgbaVboId = colors.BufferId; Mesh.flagsVboId = flags.BufferId;
            if (storage) ClientPlatformAbstract.singleIndexBufferId = sharedElements.BufferId;
        }

        /// <summary>Restores the shared native index name and retires only test-owned allocations.</summary>
        public void Dispose()
        {
            ClientPlatformAbstract.singleIndexBufferId = previousSharedIndex;
            FaceData.Dispose(); sharedElements.Dispose(); savedElements.Dispose();
            flags.Dispose(); colors.Dispose(); uv.Dispose(); positions.Dispose(); vao.Dispose();
        }

        /// <summary>Allocates one native-compatible vertex stream.</summary>
        private static GpuVbo VertexBuffer(string label) => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Allocates one native-compatible index stream.</summary>
        private static GpuEbo IndexBuffer(string label) => RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Packs one face record with two eight-unit half-edges and identical upward normals.</summary>
        private static byte[] PackFaceData()
        {
            byte[] bytes = new byte[64];
            WriteVec3(bytes, 0, -8, -8, 0); WriteVec3(bytes, 16, 8, 0, 0);
            System.Buffer.BlockCopy(BitConverter.GetBytes(0), 0, bytes, 12, sizeof(int));
            // Native UV extents are signed 15-bit values; 0x3fff encodes a positive half-tile extent.
            uint packedUvExtent = 0x3fff3fffu;
            System.Buffer.BlockCopy(BitConverter.GetBytes(packedUvExtent), 0, bytes, 28, sizeof(uint));
            uint upwardNormal = 7u << 22;
            System.Buffer.BlockCopy(Enumerable.Repeat(upwardNormal, 4).ToArray(), 0, bytes, 32, 4 * sizeof(uint));
            WriteVec3(bytes, 48, 0, 8, 0);
            return bytes;
        }

        /// <summary>Writes one aligned three-float native vector member.</summary>
        private static void WriteVec3(byte[] bytes, int offset, float x, float y, float z) =>
            System.Buffer.BlockCopy(new[] { x, y, z }, 0, bytes, offset, 3 * sizeof(float));
    }
    #endregion
}
