using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises packaged standard-object shadows with native transforms, point near crossings, and page ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowStaticShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares integer comparison and inspection executables used to verify full GPU shadow tiles.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        programs.Prepare("compare", 2, 0);
        programs.Prepare("inspection", 2, 0);
    }

    /// <summary>Static objects retain native transforms and cutouts while overlapping instances compete by depth.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PackagedStaticShaderPreservesNativeTransformsAndCutout(bool instanced, bool opaqueAtlas)
    {
        // Arrange: capture a writable page, native object stream, and exact packaged static shader selection.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4,
            "The context cannot execute the four-plane standard-object shadow fixture.");
        using var api = new BinaryShaderApiFixture();
        var (poolOwner, pages) = CreateWritablePages(api.Api);
        using var pool = poolOwner;
        using var views = new ShadowViewBuffer([CreateView(ViewSource(0)), CreateView(ViewSource(1))], pool, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 2, RasterPixels = 128
        });
        using var native = new NativeStaticStorage(instanced);
        using var geometry = new NativeShadowStaticGeometry(native.Mesh, 3, native.Instances, instanced);
        using var selector0 = VertexData([0u, 0u], "Tests.Shadows.NativeStaticSelector0");
        using var selector1 = VertexData([0u, 1u], "Tests.Shadows.NativeStaticSelector1");
        var inputs0 = geometry.WithView(new(new GpuBufferRef(selector0), 0, 2 * sizeof(uint)));
        var inputs1 = geometry.WithView(new(new GpuBufferRef(selector1), 0, 2 * sizeof(uint)));
        using var shader = ShadowStaticShader.Create(api.Api, instanced, waving: true);
        using var atlas = Atlas(opaqueAtlas ? (byte)255 : (byte)0);
        CaptureMaterial(shader);
        shader.CaptureFlags(addedFlags: 0, dontWarpVertices: 0);
        CaptureVirtualBindings(shader, views, pool, atlas);

        var commands = RHICommandContext.Current;
        var passes = new[] { views.DescribePass(0), views.DescribePass(1) };
        using var pipeline = commands.CreateGraphicsPipeline(new(shader.Shader, inputs0.Layout,
            commands.DescribeRenderTargets(passes[0]), DynamicPipelineState.Viewport,
            rasterizer: new() { Cull = false }, label: "Tests.Shadows.NativeStaticObject")).RequireResource();
        var bindings = shader.CaptureBindings();

        // Act: submit the same complete native instance interval once for each distinct source/view selector.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.NativeStaticObject", true,
                   graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                for (int viewIndex = 0; viewIndex < 2; viewIndex++)
                {
                    commands.BeginRenderPass(passes[viewIndex]);
                    commands.SetPipeline(pipeline);
                    commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                    commands.BindResources(bindings);
                    commands.DrawIndexed(viewIndex == 0 ? inputs0 : inputs1,
                        new(0, geometry.IndexCount, InstanceCount: geometry.Instances));
                    commands.EndRenderPass();
                }
                commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }

        // Assert: each selected view owns both original transformed instances and the unselected protected tile stays clear.
        Assert.Equal(3, pages.Select(page => (page.Bank, page.TileX, page.TileY)).Distinct().Count());
        Assert.Equal(3, pages.Select(page => page.Bank).Distinct().Count());
        for (int viewIndex = 0; viewIndex < 2; viewIndex++)
        {
            var page = pages[viewIndex];
            var bank = pool.DepthBank(page.Bank);
            var region = TileRegion(page, pool.PageSize);
            uint written = CountWritten(bank, page, pool.PageSize);
            if (!opaqueAtlas) Assert.Equal(0u, written);
            else
            {
                Assert.True(written > 0);
                if (instanced)
                {
                    var nearer = ExpectedDepthRange(.05f, 2f, 6f);
                    var farther = ExpectedDepthRange(.15f, 2f, 6f);
                    var middle = ExpectedDepthRange(.10f, 2f, 6f);
                    uint nearCount = CountRange(bank, region, nearer);
                    uint farCount = CountRange(bank, region, farther);
                    Assert.True(nearCount > 0);
                    Assert.True(farCount > 0);
                    Assert.Equal(0u, CountRange(bank, region, middle));
                    Assert.Equal(written, nearCount + farCount);
                    Assert.True(CountWritten(bank, page, pool.PageSize, leftHalf: true) > 0);
                    Assert.True(CountWritten(bank, page, pool.PageSize, leftHalf: false) > 0);
                }
                else
                {
                    var expected = ExpectedDepthRange(0, -2f, 2f);
                    Assert.Equal(written, CountRange(bank, region, expected));
                }
            }
        }
        var protectedPage = pages[2];
        var protectedClear = ImageAssertions.Comparisons.CompareUnsignedConstant(
            pool.DepthBank(protectedPage.Bank), ShadowDepthEncoding.Clear, TileRegion(protectedPage, pool.PageSize));
        Assert.Equal((uint)(pool.PageSize * pool.PageSize), protectedClear.ConstantMatches);
        pool.SealBatch();
        views.Seal();
    }

    /// <summary>Native static geometry crosses the point near plane and page seam while only one bank tile is writable.</summary>
    [Fact]
    public void PackagedStaticPointCrossingPreservesSampledNeighborAcrossBanks()
    {
        // Arrange: publish a sentinel page in the second bank, then open a sparse replacement interval for its neighbor.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4,
            "The context cannot execute the four-plane native point-shadow fixture.");
        using var api = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(api.Api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, BankCapacity = 2,
            ProbeLimit = 2, OperationsPerBatch = 24
        });
        while (pool.ResidentBanks < 2) Assert.True(pool.Grow());
        var source = new ShadowSourceKey(221, 0, ShadowSourceKind.Entity, 9, 3, 0);
        var projection = new PointShadowProjection(source, 1, Vector3d.Zero,
            near: .1, far: 10, faceResolution: 16, pageSize: 8);
        ShadowPageRequest protectedRequest = PointRequest(projection, pageX: 1);
        Assert.True(pool.BeginBatch());
        var protectedPage = AllocatePage(pool, protectedRequest);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        const uint retained = 0x3f400000u;
        ClearTile(pool, protectedPage, retained);
        pool.FinishReplacements(complete: true, worldRevision: 1, sceneEpoch: 1, sceneSequence: 0, timestamp: 0);
        pool.SealBatch();
        RHICommandContext.Current.WaitIdle();
        pool.Poll();
        Assert.True(pool.BeginBatch());
        var writableRequest = PointRequest(projection, pageX: 0);
        var writablePage = AllocatePage(pool, writableRequest);
        Assert.NotEqual(protectedPage.Bank, writablePage.Bank);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        var lookup = PointRequest(projection, pageX: 1, operation: ShadowPageOperation.Lookup);
        Assert.True(pool.Execute(lookup));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);

        var region = new ShadowRasterRegion(0, 0, 16, 8);
        var view = ShadowDrawView.Point(projection, 4, region, region, Vector3d.Zero);
        using var views = new ShadowViewBuffer([view], pool, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 128
        });
        // One vertex lies in front of the point near plane; the two farther vertices span both virtual pages.
        using var native = new NativeStaticStorage(false,
            positionData: [0, .15f, .05f, -.1f, .20f, .25f, .05f, .20f, .25f],
            uvData: [0, 0, 1, 0, .5f, 1]);
        using var geometry = new NativeShadowStaticGeometry(native.Mesh, 3);
        using var selectors = VertexData([0u, 0u], "Tests.Shadows.NativeStaticPointSelector");
        var inputs = geometry.WithView(new(new GpuBufferRef(selectors), 0, 2 * sizeof(uint)));
        using var shader = ShadowStaticShader.Create(api.Api, instanced: false, waving: false);
        using var atlas = Atlas(255);
        CaptureMaterial(shader);
        shader.CaptureFlags(addedFlags: 0, dontWarpVertices: 0);
        CaptureVirtualBindings(shader, views, pool, atlas);
        var commands = RHICommandContext.Current;
        var pass = views.DescribePass(0);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader.Shader, inputs.Layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
            rasterizer: new() { Cull = false }, label: "Tests.Shadows.NativeStaticPointCrossing")).RequireResource();

        // Act: execute one packaged native material draw over the near-plane and virtual-page boundary crossings.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.NativeStaticPointCrossing", true,
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

        // Assert: clipped fragments write finite point depths on the writable side; the sampled tile remains exact.
        var writableBank = pool.DepthBank(writablePage.Bank);
        uint written = CountWritten(writableBank, writablePage, pool.PageSize);
        Assert.True(written > 0);
        var finite = ImageAssertions.Statistics.Inspect(writableBank, 0, ShadowDepthEncoding.Encode(.61f),
            TileRegion(writablePage, pool.PageSize));
        Assert.Equal(written, finite.InsideRange);
        var near = ImageAssertions.Statistics.Inspect(writableBank, 0,
            ShadowDepthEncoding.Encode(.6f) - 1d, TileRegion(writablePage, pool.PageSize));
        Assert.True(near.InsideRange > 0);
        var retainedTile = ImageAssertions.Comparisons.CompareUnsignedConstant(pool.DepthBank(protectedPage.Bank),
            retained, TileRegion(protectedPage, pool.PageSize));
        Assert.Equal((uint)(pool.PageSize * pool.PageSize), retainedTile.ConstantMatches);
        pool.SealBatch();
        views.Seal();
    }

    /// <summary>A native static draw is admitted only by the exact selection that produced its command interval.</summary>
    [Fact]
    public void StaticPreparedDrawCannotBeSubmittedThroughAnotherSelectionWithSameInterval()
    {
        // Arrange: both GPU selectors use the same view, candidate, page batch, and object interval, but have distinct ownership.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var api = new BinaryShaderApiFixture();
        using var scene = new SelectionOwnerFixture(api.Api);
        var source = new ShadowSourceKey(1, 0, ShadowSourceKind.Sun, 1, 1, 0);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            new ShadowBounds(0, 0, 0, 32, 32, 32), 4, 1, 8);
        var region = new ShadowRasterRegion(0, 0, 8, 8);
        var view = ShadowDrawView.Directional(projection, 0, region, region, Vector3d.Zero);
        using var views = new ShadowViewBuffer([view], scene.Pages, new ShadowDrawSettings
        {
            MemoryMiB = 2, ViewCapacity = 1, RasterPixels = 64
        });
        using var native = new NativeStaticStorage(instanced: false);
        using var geometry = new NativeShadowStaticGeometry(native.Mesh, 3);
        using var shader = ShadowStaticShader.Create(api.Api, instanced: false, waving: false);
        using var atlas = Atlas(255);
        CaptureMaterial(shader);
        shader.CaptureFlags(addedFlags: 0, dontWarpVertices: 0);
        ImmutableArray<ShadowObjectDrawRange> objects = [new(0, 0, 3, 0, 1, 1, 10)];
        var candidate = scene.Candidate(source, projection.Generation);
        using var owner = new ShadowGeometrySelection(api.Api, [view], [candidate], scene.Candidates,
            scene.Publication, scene.WorldBuffers, scene.DrawRanges, commandsPerView: 1, scene.Pages,
            SelectionSettings(), otherDrawingBytes: 0, objects: objects);
        using var sameIntervalOtherOwner = new ShadowGeometrySelection(api.Api, [view], [candidate], scene.Candidates,
            scene.Publication, scene.WorldBuffers, scene.DrawRanges, commandsPerView: 1, scene.Pages,
            SelectionSettings(), otherDrawingBytes: 0, objects: objects);
        Assert.NotSame(owner, sameIntervalOtherOwner);
        using var draw = new NativeShadowStaticDraw(geometry, atlas.TextureId, static () => true, shader,
            view, views, viewIndex: 0, owner, objectRangeIndex: 0, scene.Pages);
        int operationsBeforeValidation = scene.Pages.OperationsRemaining;

        // Act: validate against the producing selector, then attempt the identical draw against its peer.
        owner.ValidateSubmission([draw], scene.Pages);
        var foreignOwnerFailure = Assert.Throws<InvalidOperationException>(
            () => sameIntervalOtherOwner.ValidateSubmission([draw], scene.Pages));

        // Assert: ownership mismatch is rejected while the writable page interval remains untouched.
        Assert.Contains("obsolete", foreignOwnerFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(operationsBeforeValidation, scene.Pages.OperationsRemaining);
        Assert.True(scene.Pages.CanPrepareReplacements);
    }
    #endregion

    #region Private
    /// <summary>Captures identity native/rebase matrices and deterministic nonzero global warp.</summary>
    private static void CaptureMaterial(ShadowStaticShader shader)
    {
        byte[] material = new byte[208];
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        System.Buffer.BlockCopy(identity, 0, material, 0, 64);
        System.Buffer.BlockCopy(identity, 0, material, 144, 64);
        System.Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, material, 116, sizeof(float));
        shader.CaptureMaterial(material);
    }

    /// <summary>Supplies the native atlas, one selected view, exact page authorization, and integer destinations.</summary>
    private static void CaptureVirtualBindings(ShadowStaticShader shader, ShadowViewBuffer views,
        ShadowPagePool pool, GpuTexture atlas)
    {
        var material = (IShadowStaticBindings)shader;
        material.Views = views.Texture;
        material.Atlas = atlas;
        material.Entries = pool.Entries;
        material.Writable = pool.WritableAssignments;
        material.Bank0 = IntegerImage(pool.DepthBank(0));
        material.Bank1 = IntegerImage(pool.DepthBank(Math.Min(1, pool.ResidentBanks - 1)));
        material.Bank2 = IntegerImage(pool.DepthBank(Math.Min(2, pool.ResidentBanks - 1)));
        material.Bank3 = IntegerImage(pool.DepthBank(Math.Min(3, pool.ResidentBanks - 1)));
    }

    /// <summary>Allocates one exact page and captures its physical coordinates before later commands overwrite results.</summary>
    private static (ShadowPagePool Pool, PhysicalPage[] Pages) CreateWritablePages(Vintagestory.API.Common.ICoreAPI api)
    {
        var pool = ShadowPagePool.Create(api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, BankCapacity = 3,
            ProbeLimit = 3, OperationsPerBatch = 16
        });
        var bounds = new ShadowBounds(-16, -16, -16, 16, 16, 16);
        try
        {
            while (pool.ResidentBanks < 3)
                if (!pool.Grow()) throw new InvalidOperationException("Static depth bank growth failed.");
            if (!pool.BeginBatch()) throw new InvalidOperationException("Static shadow page interval could not begin.");
            var pages = new PhysicalPage[3];
            for (int i = 0; i < pages.Length; i++)
            {
                var source = ViewSource(i);
                var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
                    bounds, baseTexelWidth: 1, levels: 1, pageSize: 8);
                var request = new ShadowPageRequest(ShadowPageOperation.Allocate,
                    new ShadowPageKey(source, 1, 0, 0, 0), bounds, 1, 1, 0,
                    Projection: ShadowProjectionSnapshot.Directional(projection, 0));
                if (!pool.Execute(request)) throw new InvalidOperationException("Static shadow page allocation failed.");
                var result = Read(pool.Results, 8);
                if (result[0] != 0 || result[1] >= pool.Capacity) throw new InvalidOperationException("Static shadow page was rejected.");
                int slot = checked((int)result[1]);
                using var entry = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read);
                if (!entry.IsMapped) throw new InvalidOperationException("Static shadow page assignment could not be read.");
                pages[i] = new(checked((int)entry.Span[0]), checked((int)entry.Span[1]), checked((int)entry.Span[2]));
                if (!pool.ClearReplacement()) throw new InvalidOperationException("Static shadow replacement tile could not be cleared.");
            }
            if (!pool.FreezeWritableAssignments())
                throw new InvalidOperationException("Static shadow page was not frozen for writes.");
            return (pool, pages);
        }
        catch { pool.Dispose(); throw; }
    }

    /// <summary>Creates an independent source identity while retaining one projection convention for depth comparison.</summary>
    private static ShadowSourceKey ViewSource(int index) => new(119, 0, ShadowSourceKind.Sun, 11, 4, index);

    /// <summary>Creates a directional view whose flat world z=0 has independently known depth one-half.</summary>
    private static ShadowDrawView CreateView(ShadowSourceKey source)
    {
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            new ShadowBounds(-16, -16, -16, 16, 16, 16), baseTexelWidth: 1, levels: 1, pageSize: 8);
        var region = new ShadowRasterRegion(0, 0, 8, 8);
        return ShadowDrawView.Directional(projection, 0, region, region, Vector3d.Zero);
    }

    /// <summary>Creates one point-face page request with current source identity and a world-space caster envelope.</summary>
    private static ShadowPageRequest PointRequest(PointShadowProjection projection, int pageX,
        ShadowPageOperation operation = ShadowPageOperation.Allocate) => new(operation,
        new(projection.Source, projection.Generation, 4, pageX, 0), new(-1, -1, 0, 1, 1, 1),
        1, 1, 0, Projection: ShadowProjectionSnapshot.Point(projection, 4));

    /// <summary>Allocates one page and resolves its GPU-assigned physical destination for exact image inspection.</summary>
    private static PhysicalPage AllocatePage(ShadowPagePool pool, ShadowPageRequest request)
    {
        Assert.True(pool.Execute(request));
        uint[] result = Read(pool.Results, 8);
        Assert.Equal(0u, result[0]);
        int slot = checked((int)result[1]);
        using var entry = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read);
        Assert.True(entry.IsMapped);
        return new(checked((int)entry.Span[0]), checked((int)entry.Span[1]), checked((int)entry.Span[2]));
    }

    /// <summary>Seeds a physical integer tile through the RHI for retained-page protection checks.</summary>
    private static void ClearTile(ShadowPagePool pool, PhysicalPage page, uint value)
    {
        var texture = pool.DepthBank(page.Bank);
        var region = new RHITextureRegion(new(), page.TileX * pool.PageSize, page.TileY * pool.PageSize,
            0, pool.PageSize, pool.PageSize);
        var encoding = new RHIPixelEncoding(PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt);
        RHICommandContext.Current.ClearTexture(texture, region, encoding, BitConverter.GetBytes(value));
    }

    /// <summary>Bounds the GPU depth generated by the authored sine warp over the triangle's world-Y interval.</summary>
    private static (float Minimum, float Maximum) ExpectedDepthRange(float objectTranslationZ, float minimumWorldY, float maximumWorldY)
    {
        // The fixture sets globalWarpIntensity=1 and all other global-warp terms to zero.
        // VgeGlobalDeform therefore adds .05*sin(worldY*.13) to world Z before projection.
        float first = .5f - (objectTranslationZ + .05f * MathF.Sin(minimumWorldY * .13f)) / 34f;
        float second = .5f - (objectTranslationZ + .05f * MathF.Sin(maximumWorldY * .13f)) / 34f;
        return (Math.Min(first, second), Math.Max(first, second));
    }

    /// <summary>Creates a one-texel nearest-sampled atlas with native alpha coverage chosen by the test case.</summary>
    private static GpuTexture Atlas(byte alpha)
    {
        var commands = RHICommandContext.Current;
        var texture = commands.CreateTexture(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
            Width = 1, Height = 1, Depth = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.NativeStaticAtlas"
        }).RequireResource();
        var encoding = commands.GetTextureEncoding(texture, PixelComponentType.UnsignedByte);
        commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 1, 1), encoding, [255, 255, 255, alpha]);
        return texture;
    }

    /// <summary>Creates one object/view selector for the whole native instance interval.</summary>
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

    /// <summary>Builds an explicit read/write view of one integer depth bank.</summary>
    private static RHIImageBinding IntegerImage(GpuTexture texture) =>
        new(texture, ImageAccess.ReadWrite, Format: TextureFormat.R32ui);

    /// <summary>Builds the selected physical tile's source-coordinate region in its depth bank.</summary>
    private static RHITextureRegion TileRegion(PhysicalPage page, int pageSize) =>
        new(new(0), page.TileX * pageSize, page.TileY * pageSize, 0, pageSize, pageSize);

    /// <summary>Counts non-clear integer depths over the selected physical tile on the GPU.</summary>
    private uint CountWritten(GpuTexture bank, PhysicalPage page, int pageSize, bool? leftHalf = null)
    {
        var region = leftHalf is null ? TileRegion(page, pageSize) :
            new RHITextureRegion(new(0), page.TileX * pageSize + (leftHalf.Value ? 0 : pageSize / 2),
                page.TileY * pageSize, 0, pageSize / 2, pageSize);
        var clear = ImageAssertions.Comparisons.CompareUnsignedConstant(bank, ShadowDepthEncoding.Clear, region);
        return checked((uint)(region.Width * region.Height) - clear.ConstantMatches);
    }

    /// <summary>Counts integer depth words within an independently authored float interval on the GPU.</summary>
    private uint CountRange(GpuTexture bank, RHITextureRegion region, (float Minimum, float Maximum) range)
    {
        var inspected = ImageAssertions.Statistics.Inspect(bank,
            ShadowDepthEncoding.Encode(Math.Max(0f, range.Minimum - .0002f)),
            ShadowDepthEncoding.Encode(range.Maximum + .0002f), region);
        return inspected.InsideRange;
    }

    /// <summary>Maps the current page result prefix for the exact GPU-assigned slot.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Creates finite selector budgets for one view and one native object command.</summary>
    private static ShadowDrawSettings SelectionSettings() => new()
    {
        MemoryMiB = 4, ViewCapacity = 1, PairCapacity = 4, ExpansionVisits = 64,
        CommandSlots = 1, ViewInstances = 1, RasterPixels = 64,
        EstimatedTriangles = 8, EstimatedRasterSamples = 4096
    };

    /// <summary>Retains the assigned bank and tile identity for post-raster inspection.</summary>
    private readonly record struct PhysicalPage(int Bank, int TileX, int TileY);

    /// <summary>Owns real world, candidate, and page producers for the selection identity boundary test.</summary>
    private sealed class SelectionOwnerFixture : IDisposable
    {
        private readonly ShadowCandidateChanges changes = new(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        internal ShadowWorldPublication Publication { get; }
        internal ImmutableArray<ShadowDrawRange> DrawRanges { get; }
        internal ShadowWorldGpuBuffers WorldBuffers { get; } = new();
        internal ShadowCandidatePool Candidates { get; }
        internal ShadowPagePool Pages { get; }
        private ShadowChunkDomain Domain { get; }
        private ShadowBounds Receiver { get; }

        /// <summary>Creates one complete native chunk table and opens its current GPU candidate/page intervals.</summary>
        internal SelectionOwnerFixture(Vintagestory.API.Common.ICoreAPI api)
        {
            var domain = new ShadowChunkDomain(0, 0, 0, 0, 0, 0, 0);
            Domain = domain; Receiver = domain.Bounds;
            var table = new ShadowChunkGpu[2];
            ImmutableArray<ShadowGeometryRange> ranges = [new(10, 0, 3, 0, 3, 0, 1, 0)];
            var entry = new ShadowChunkGpu
            {
                X = 0, Y = 0, Z = 0, Dimension = 0, NativeLow = 1, LifetimeLow = 1,
                RevisionLow = 1, Flags = 3, RangeOffset = 0, RangeCount = 1
            };
            table[Hash(entry.Dimension, entry.X, entry.Y, entry.Z) & 1] = entry;
            Publication = new(1, true, null, ImmutableArray.Create(table), ranges,
                ImmutableDictionary<int, ShadowBounds>.Empty.Add(0,
                    new ShadowBounds(Receiver.MinX * 32, Receiver.MinY * 32, Receiver.MinZ * 32,
                        Receiver.MaxX * 32, Receiver.MaxY * 32, Receiver.MaxZ * 32)), table.Length) { Owner = 1 };
            DrawRanges = [new(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0)];
            if (!WorldBuffers.Upload(Publication)) throw new InvalidOperationException("Static selection test world upload failed.");
            Candidates = ShadowCandidatePool.Create(api, new ShadowCandidateSettings
            {
                MemoryMiB = 8, HeaderCapacity = 8, SegmentsPerAssociation = 1, DedupCapacity = 128,
                ProbeLimit = 128, ChangeCapacity = 16, SlabsPerSlice = 64, CellsPerSlice = 64,
                ProbesPerSlice = 1024, ChangesPerSlice = 16, SlabsPerBatch = 256,
                CellsPerBatch = 4096, ProbesPerBatch = 16384, ChangesPerBatch = 64
            });
            if (!Candidates.BeginBatch(changes, Publication, WorldBuffers))
                throw new InvalidOperationException("Static selection test candidate interval could not begin.");
            Pages = ShadowPagePool.Create(api, new ShadowPageSettings
            {
                MemoryMiB = 2, PageSize = 8, TileColumns = 1, TileRows = 1,
                BankCapacity = 1, ProbeLimit = 8, OperationsPerBatch = 8
            });
            if (!Pages.BeginBatch()) throw new InvalidOperationException("Static selection test page interval could not begin.");
        }

        /// <summary>Runs the real bounded candidate producer until its sole coordinate reaches a completed state.</summary>
        internal ShadowViewCandidates Candidate(ShadowSourceKey source, long generation)
        {
            var request = ShadowCandidateRequest.Directional(source, Publication.Owner, Publication.Generation,
                1, Domain, Receiver, Vector3d.UnitZ, 1);
            if (!Candidates.TryAllocate(request, out var handle)) throw new InvalidOperationException("Static candidate allocation failed.");
            var current = new ShadowCandidateCurrent(source, Publication.Owner, Publication.Generation, 1,
                Receiver, Vector3d.UnitZ, changes.Epoch, changes.ToSequence, generation);
            ShadowCandidateHeader last = default;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (!Candidates.BeginBatch(changes, Publication, WorldBuffers) || !Candidates.Advance(handle, current))
                    throw new InvalidOperationException("Static candidate advancement failed.");
                using var header = Candidates.Headers.MapRange<ShadowCandidateHeader>(handle.Slot * 128, 1, BufferMapAccess.Read);
                if (!header.IsMapped) throw new InvalidOperationException("Static candidate status could not be read.");
                last = header.Span[0];
                if (last.Status == 3 && last.Unresolved == 0) return new(0, handle, current);
            }
            throw new InvalidOperationException($"Static candidate did not complete within its finite dispatch bound: status={last.Status}, unresolved={last.Unresolved}, stage={last.Stage}, slabs={last.SlabVisits}, cells={last.CellVisits}, probes={last.ProbeVisits}.");
        }

        /// <summary>Closes page and candidate owners after every derived draw and selection is retired.</summary>
        public void Dispose()
        {
            if (Pages.CanSubmit) Pages.SealBatch();
            Candidates.Dispose(); WorldBuffers.Dispose(); Pages.Dispose();
        }

        /// <summary>Matches the selector's integer coordinate hash while leaving all nonempty slots distinct.</summary>
        private static uint Hash(int dimension, int x, int y, int z)
        {
            unchecked
            {
                uint hash = 2166136261;
                hash = (hash ^ (uint)dimension) * 16777619;
                hash = (hash ^ (uint)x) * 16777619;
                hash = (hash ^ (uint)y) * 16777619;
                return (hash ^ (uint)z) * 16777619;
            }
        }
    }

    /// <summary>Owns native-shaped object streams and an optional original matrix stream while the adapter borrows them.</summary>
    private sealed class NativeStaticStorage : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = VertexBuffer("Tests.Shadows.NativeStaticPositions");
        private readonly GpuVbo uv = VertexBuffer("Tests.Shadows.NativeStaticUv");
        private readonly GpuVbo flags = VertexBuffer("Tests.Shadows.NativeStaticFlags");
        private readonly GpuVbo? transforms;
        private readonly GpuEbo indices = IndexBuffer("Tests.Shadows.NativeStaticIndices");
        internal VAO Mesh { get; }
        internal int Instances { get; }

        /// <summary>Creates one triangle with zero flags and three native matrix records when instanced.</summary>
        internal NativeStaticStorage(bool instanced, float[]? positionData = null, float[]? uvData = null)
        {
            positions.UploadData<float>(positionData ?? new float[] { -2, -2, 0, 2, -2, 0, 0, 2, 0 });
            uv.UploadData<float>(uvData ?? new float[] { 0, 0, 1, 0, 0.5f, 1 });
            flags.UploadData<int>(new int[3]);
            indices.UploadIndices(new uint[] { 0, 1, 2 });
            Instances = instanced ? 3 : 1;
            if (instanced)
            {
                transforms = VertexBuffer("Tests.Shadows.NativeStaticTransforms");
                transforms.UploadData<float>(InstanceMatrices());
            }
            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = indices.BufferId;
            Mesh.IndicesCount = 3; Mesh.drawMode = PrimitiveType.Triangles;
            Mesh.xyzVboId = positions.BufferId; Mesh.uvVboId = uv.BufferId;
            Mesh.flagsVboId = flags.BufferId; Mesh.customDataFloatVboId = transforms?.BufferId ?? 0;
        }

        /// <summary>Releases the private source allocations after all derived RHI geometry is retired.</summary>
        public void Dispose()
        {
            transforms?.Dispose(); indices.Dispose(); flags.Dispose(); uv.Dispose(); positions.Dispose(); vao.Dispose();
        }

        /// <summary>Packs one isolated object and two overlapping native instances that compete at the nearer depth.</summary>
        private static float[] InstanceMatrices() =>
        [
            1,0,0,0, 0,1,0,0, 0,0,1,0, 2,4,.05f,1,
            1,0,0,0, 0,1,0,0, 0,0,1,0, 6,4,.15f,1,
            1,0,0,0, 0,1,0,0, 0,0,1,0, 6,4,.10f,1
        ];

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
