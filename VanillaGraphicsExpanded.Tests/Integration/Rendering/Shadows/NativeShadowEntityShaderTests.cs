using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
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

/// <summary>Runs packaged animated entity shadow materials against borrowed native streams and atomic page storage.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowEntityShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares integer comparison and inspection executables used to verify full GPU shadow tiles.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        programs.Prepare("compare", 2, 0);
        programs.Prepare("inspection", 2, 0);
    }

    /// <summary>Native cutout rejects transparent fragments after packaged skinning and global warp adjust caster depth.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PackagedEntityShaderWritesSkinnedWarpedDepthAfterNativeCutout(bool opaqueAtlas)
    {
        // Arrange: capture one writable page and a native shape whose joint-zero matrix and warp inputs are controlled.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4,
            "The context cannot execute the four-plane animated entity shadow fixture.");
        using var api = new BinaryShaderApiFixture();
        var (poolOwner, page) = CreateWritablePage(api.Api);
        using var pool = poolOwner;
        var view = CreateView();
        using var views = new ShadowViewBuffer([view], pool, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 64
        });
        using var native = new NativeEntityStorage();
        using var geometry = new NativeShadowEntityGeometry(native.Mesh);
        using var selectors = VertexData([0u, 0u], "Tests.Shadows.NativeEntitySelectors");
        var inputs = geometry.WithView(new(new GpuBufferRef(selectors), 0, 2 * sizeof(uint)));
        using var shader = ShadowEntityShader.Create(api.Api, waving: true);
        using var atlas = Atlas(opaqueAtlas ? (byte)255 : (byte)0);
        CaptureAnimationAndMaterial(shader);
        CaptureVirtualBindings(shader, views, pool, atlas);

        var commands = RHICommandContext.Current;
        var pass = views.DescribePass(0);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader.Shader, inputs.Layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
            rasterizer: new() { Cull = false }, label: "Tests.Shadows.NativeEntity")).RequireResource();

        // Act: execute packaged native joint transforms and global warping before the native atlas alpha rejection.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.NativeEntity", true,
                   graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(shader.CaptureBindings());
                commands.DrawIndexed(inputs, new(0, geometry.IndexCount));
                commands.EndRenderPass();
                commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }

        // Assert: cutout leaves the tile untouched; opaque fragments encode the known joint plus native-warp depth shift.
        uint written = CountWritten(pool.DepthArray, page, pool.PageSize);
        if (!opaqueAtlas) Assert.Equal(0u, written);
        else
        {
            uint lower = ShadowDepthEncoding.Encode(0.49f), upper = ShadowDepthEncoding.Encode(0.5f);
            var range = ImageAssertions.Statistics.Inspect(pool.DepthArray, lower, upper,
                TileRegion(page, pool.PageSize));
            Assert.Equal(written, range.InsideRange);
            var nearer = ImageAssertions.Statistics.Inspect(pool.DepthArray, 0,
                ShadowDepthEncoding.Encode(0.497f) - 1d, TileRegion(page, pool.PageSize));
            Assert.True(nearer.InsideRange > 0);
        }
        pool.SealBatch();
        views.Seal();
    }
    #endregion

    #region Private
    /// <summary>Builds the selected physical tile's source-coordinate region in its depth layer.</summary>
    private static RHITextureRegion TileRegion(PhysicalPage page, int pageSize) =>
        new(new(0, page.Layer), page.TileX * pageSize, page.TileY * pageSize, 0, pageSize, pageSize);

    /// <summary>Counts non-clear integer depths over one complete physical tile on the GPU.</summary>
    private uint CountWritten(GpuTexture layer, PhysicalPage page, int pageSize)
    {
        var clear = ImageAssertions.Comparisons.CompareUnsignedConstant(layer, ShadowDepthEncoding.Clear,
            TileRegion(page, pageSize));
        return checked((uint)(pageSize * pageSize) - clear.ConstantMatches);
    }

    /// <summary>Captures identity model/rebase matrices, a known joint translation, and deterministic native global warp.</summary>
    private static void CaptureAnimationAndMaterial(ShadowEntityShader shader)
    {
        byte[] material = new byte[208];
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        System.Buffer.BlockCopy(identity, 0, material, 0, 64);
        System.Buffer.BlockCopy(identity, 0, material, 144, 64);
        System.Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, material, 116, sizeof(float));
        float[] joint = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0.1f, 1];
        shader.CaptureMaterial(material, joint, renderFlags: 0);
    }

    /// <summary>Supplies native atlas, view, page authorization and integer image bindings for one entity draw.</summary>
    private static void CaptureVirtualBindings(ShadowEntityShader shader, ShadowViewBuffer views,
        ShadowPagePool pool, GpuTexture atlas)
    {
        var entity = (IShadowEntityBindings)shader;
        entity.Views = views.Texture;
        entity.Atlas = atlas;
        entity.Entries = pool.Entries;
        entity.Writable = pool.WritableAssignments;
        entity.Depth = IntegerImage(pool.DepthArray);
    }

    /// <summary>Creates one page and reads its assigned physical coordinates before later commands replace result words.</summary>
    private static (ShadowPagePool Pool, PhysicalPage Page) CreateWritablePage(Vintagestory.API.Common.ICoreAPI api)
    {
        var pool = ShadowPagePool.Create(api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, ArrayLayers = 1,
            ProbeLimit = 1, OperationsPerBatch = 16
        });
        var source = new ShadowSourceKey(111, 0, ShadowSourceKind.Sun, 8, 2, 0);
        var bounds = new ShadowBounds(-16, -16, -16, 16, 16, 16);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            bounds, baseTexelWidth: 1, levels: 1, pageSize: 8);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate,
            new ShadowPageKey(source, 1, 0, 0, 0), bounds, 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        try
        {
            if (!pool.BeginBatch() || !pool.Execute(request)) throw new InvalidOperationException("Entity shadow page allocation failed.");
            var result = Read(pool.Results, 8);
            if (result[0] != 0 || result[1] >= pool.Capacity) throw new InvalidOperationException("Entity shadow page was rejected.");
            int slot = checked((int)result[1]);
            PhysicalPage page;
            using (var entry = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read))
            {
                if (!entry.IsMapped) throw new InvalidOperationException("Entity shadow page assignment could not be read.");
                page = new(checked((int)entry.Span[0]), checked((int)entry.Span[1]), checked((int)entry.Span[2]));
            }
            if (!pool.ClearReplacement() || !pool.FreezeWritableAssignments())
                throw new InvalidOperationException("Entity shadow page was not frozen for raster writes.");
            return (pool, page);
        }
        catch { pool.Dispose(); throw; }
    }

    /// <summary>Creates a view whose undisplaced world z of zero has independently known depth one-half.</summary>
    private static ShadowDrawView CreateView()
    {
        var source = new ShadowSourceKey(111, 0, ShadowSourceKind.Sun, 8, 2, 0);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            new ShadowBounds(-16, -16, -16, 16, 16, 16), baseTexelWidth: 1, levels: 1, pageSize: 8);
        var region = new ShadowRasterRegion(0, 0, 8, 8);
        return ShadowDrawView.Directional(projection, 0, region, region, Vector3d.Zero);
    }

    /// <summary>Creates the constant atlas sample used by the installed entity alpha threshold.</summary>
    private static GpuTexture Atlas(byte alpha)
    {
        var commands = RHICommandContext.Current;
        var texture = commands.CreateTexture(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
            Width = 1, Height = 1, Depth = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.NativeEntityAtlas"
        }).RequireResource();
        var encoding = commands.GetTextureEncoding(texture, PixelComponentType.UnsignedByte);
        commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 1, 1), encoding, [255, 255, 255, alpha]);
        return texture;
    }

    /// <summary>Creates the one object/view selector consumed by the private instanced geometry packet.</summary>
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

    /// <summary>Creates an exact image view for atomic replacement depth writes.</summary>
    private static RHIImageBinding IntegerImage(GpuTexture texture) =>
        new(texture, ImageAccess.ReadWrite, Layered: true, Format: TextureFormat.R32ui);

    /// <summary>Maps the allocator's bounded result prefix for exact page assignment inspection.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Retains the allocator-selected layer and physical tile for depth readback.</summary>
    private readonly record struct PhysicalPage(int Layer, int TileX, int TileY);

    /// <summary>Owns native-shaped position, UV, flag, joint and element allocations while the adapter borrows them.</summary>
    private sealed class NativeEntityStorage : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = VertexBuffer("Tests.Shadows.NativeEntityPositions");
        private readonly GpuVbo uv = VertexBuffer("Tests.Shadows.NativeEntityUv");
        private readonly GpuVbo flags = VertexBuffer("Tests.Shadows.NativeEntityFlags");
        private readonly GpuVbo joints = VertexBuffer("Tests.Shadows.NativeEntityJoints");
        private readonly GpuEbo indices = IndexBuffer("Tests.Shadows.NativeEntityIndices");
        internal VAO Mesh { get; }

        /// <summary>Creates a two-triangle native shape with every vertex attached to one controlled joint.</summary>
        internal NativeEntityStorage()
        {
            positions.UploadData<float>([-8, -8, 0, 8, -8, 0, -8, 8, 0, 8, 8, 0]);
            uv.UploadData<float>([0, 0, 1, 0, 0, 1, 1, 1]);
            flags.UploadData<int>(new int[4]);
            joints.UploadData<int>(new int[4]);
            indices.UploadIndices(new uint[] { 0, 1, 2, 2, 1, 3 });
            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = indices.BufferId; Mesh.IndicesCount = 6;
            Mesh.drawMode = PrimitiveType.Triangles; Mesh.xyzVboId = positions.BufferId;
            Mesh.uvVboId = uv.BufferId; Mesh.flagsVboId = flags.BufferId;
            Mesh.customDataIntVboId = joints.BufferId;
        }

        /// <summary>Retires only test allocations after native borrowed packets have been disposed.</summary>
        public void Dispose()
        {
            indices.Dispose(); joints.Dispose(); flags.Dispose(); uv.Dispose(); positions.Dispose(); vao.Dispose();
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
    }
    #endregion
}
