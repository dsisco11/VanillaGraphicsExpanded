using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the atlas owner's independent clear and complete multigrid bake entries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class MaterialAtlasBakeSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    /// <summary>All three entries preserve hostile caller state while clear, bounded bake and whole-page bake produce matching pixels.</summary>
    [Fact]
    public void IndependentAtlasEntriesPreserveStateAndRectangles()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var statics = new AtlasStatics();
        const int edge = 64, origin = 16, size = 32;
        float[] input = new float[edge * edge * 4];
        for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
        {
            float value = ((x / 4 + y / 4) & 1) == 0 ? .2f : .8f;
            int at = (y * edge + x) * 4;
            input[at] = input[at + 1] = input[at + 2] = value; input[at + 3] = 1;
        }
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = edge, Height = edge, Format = TextureFormat.Rgba16f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, edge, edge), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba16f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((input).AsSpan()).ToArray())]).RequireResource();
        using var destination = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, edge), Height = System.Math.Max(1, edge), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var caller = CreateRenderTarget(3, 2, TextureFormat.Rgba16f);
        using var read = CreateRenderTarget(2, 3, TextureFormat.Rgba16f);
        using var transfer = RHICommandContext.Current.CreatePixelPackBuffer(new RHIBufferDescription { Role = BufferRole.PixelPack, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamRead, Label = null }).RequireResource();
        transfer.AllocateOrphan(128 * 128 * 16);
        using var hostilePack = OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(8, RowLength: 73, SkipRows: 2, SkipPixels: 3));
        using var hostileTransfer = OpenGlStateCache.Current.BindBufferScope(BufferTarget.PixelPackBuffer, transfer.BufferId);
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer, caller.FboId);
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer, read.FboId);
        OpenGlStateCache.Current.ApplyDynamic(new() { X = 1, Y = 0, Width = 2, Height = 2 });
        try
        {
            using (var hostile = new HostileFullscreenState())
            {
                MaterialAtlasNormalDepthGpuBuilder.ClearAtlasPage(assets.Api, destination, edge, edge);
                hostile.AssertRestored(); AssertBindings();
            }
            Assert.True(GpuLocalRead.Scalar(destination,0) == .5f, $"Clear failed: {OpenGlStateCache.Current.BoundaryEntryFailure}; {string.Join(Environment.NewLine, assets.Logs)}");
            AssertPixels(destination, (_, _) => [.5f, .5f, 1f, .5f]);
            float[] sentinel = [.125f, .25f, .75f, .125f];
            destination.UploadDataImmediate(Enumerable.Range(0, edge * edge).SelectMany(_ => sentinel).ToArray());
            // Reject a real cold executable load before the prepared owner allocates scratch images.
            var candidates = new List<PbrHeightBakeShaderProgram>();
            GpuFramebuffer? failedScratch = null;
            bool injected = false;
            assets.BeforeRead = path =>
            {
                if (!path.Contains("pbr_heightbake", StringComparison.Ordinal)) return;
                injected = true;
                const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
                var owner = typeof(MaterialAtlasNormalDepthGpuBuilder);
                candidates.AddRange(owner.GetFields(fields).Select(field => field.GetValue(null)).OfType<PbrHeightBakeShaderProgram>());
                failedScratch = (GpuFramebuffer?)owner.GetField("scratchFbo", fields)!.GetValue(null);
                throw new IOException("Injected atlas executable load failure.");
            };
            using (var hostile = new HostileFullscreenState())
            {
                Assert.False(MaterialAtlasNormalDepthGpuBuilder.BakePerRect(assets.Api, source,
                    destination, edge, edge, origin, origin, size, size, 1, 1));
                hostile.AssertRestored(); AssertBindings();
            }
            assets.BeforeRead = null;
            Assert.True(injected);
            Assert.NotEmpty(candidates);
            Assert.All(candidates, candidate => Assert.True(candidate.IsRetired));
            Assert.Null(failedScratch);
            const BindingFlags initializationFields = BindingFlags.Static | BindingFlags.NonPublic;
            var atlasOwner = typeof(MaterialAtlasNormalDepthGpuBuilder);
            Assert.False((bool)atlasOwner.GetField("initialized", initializationFields)!.GetValue(null)!);
            foreach (var field in atlasOwner.GetFields(initializationFields).Where(field =>
                field.FieldType == typeof(PbrHeightBakeShaderProgram) || field.Name is "geometry" or "scratchFbo"))
                Assert.Null(field.GetValue(null));
            AssertPixels(destination, (_, _) => sentinel);
            using (var hostile = new HostileFullscreenState())
            {
                Assert.True(MaterialAtlasNormalDepthGpuBuilder.BakePerRect(assets.Api, source,
                    destination, edge, edge, origin, origin, size, size, 1, 1), string.Join(Environment.NewLine, assets.Logs));
                hostile.AssertRestored(); AssertBindings();
            }
            using var rectangle = new GpuImageSnapshot(destination);
            // The complete rectangle is finite; every exterior texel retains the independently authored sentinel.
            ImageAssertions.Predicates.Predicate(rectangle.Texture, MaterialPredicates.MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles,parameters:new(sentinel[0],sentinel[1],sentinel[2],sentinel[3])).AssertEqual();
            var heights=ImageAssertions.Statistics.Inspect(rectangle.Texture,
                selection:new(new(),origin,origin,0,size,size),channelMask:8);
            Assert.True(heights.Maximum-heights.Minimum>.001f,"The production solver must produce nonflat checkerboard relief.");
            Assert.True(ImageAssertions.Predicates.Predicate(rectangle.Texture, MaterialPredicates.MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_2).MatchingTexels>0);
            using (var hostile = new HostileFullscreenState())
            {
                // An absent input is rejected before admission without touching caller state.
                Assert.Throws<ArgumentOutOfRangeException>(() => MaterialAtlasNormalDepthGpuBuilder.BakePerRect(
                    assets.Api, default(GpuTextureRef), destination, edge, edge, origin, origin, size, size, 1, 1));
                hostile.AssertRestored(); AssertBindings();
            }
            var expiredOwner = RHICommandContext.Current.CreateDynamicTexture2D(new()
            {
                Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = edge, Height = edge,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest
            }).RequireResource();
            GpuTextureRef expired = expiredOwner;
            expiredOwner.Dispose();
            Assert.False(expired.IsEmpty); Assert.False(expired.IsValid);
            using (var hostile = new HostileFullscreenState())
            {
                // A retained but retired sampler reaches the bake boundary and fails binding validation.
                Assert.False(MaterialAtlasNormalDepthGpuBuilder.BakePerRect(assets.Api, expired,
                    destination, edge, edge, origin, origin, size, size, 1, 1));
                hostile.AssertRestored(); AssertBindings();
            }
            ImageAssertions.Comparisons.CompareHistory(destination,rectangle).AssertEqual();
            using (var hostile = new HostileFullscreenState())
            {
                MaterialAtlasNormalDepthGpuBuilder.BakePerTexture(assets.Api, source, destination, edge, edge,
                    [new TextureAtlasPosition { atlasTextureId = source.TextureId, x1 = .25f, y1 = .25f, x2 = .75f, y2 = .75f }]);
                hostile.AssertRestored(); AssertBindings();
            }
            ImageAssertions.Predicates.Predicate(destination, MaterialPredicates.MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_3,rectangle.Texture).AssertEqual();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { OpenGlStateCache.Current.InvalidateAll(); }

        /// <summary>Checks all native bindings that the independent entry borrows.</summary>
        void AssertBindings()
        {
            Assert.Equal(transfer.BufferId, GL.GetInteger(GetPName.PixelPackBufferBinding));
            Assert.Equal(8, GL.GetInteger(GetPName.PackAlignment));
            Assert.Equal(73, GL.GetInteger(GetPName.PackRowLength));
            Assert.Equal(2, GL.GetInteger(GetPName.PackSkipRows));
            Assert.Equal(3, GL.GetInteger(GetPName.PackSkipPixels));
            Assert.Equal(caller.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
            Assert.Equal(new[] { 1, 0, 2, 2 }, viewport);
            Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Null(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
        }
        /// <summary>Compares the entire destination to independently authored CPU expectations on the GPU.</summary>
        void AssertPixels(GpuTexture actual,Func<int,int,float[]> expected)
        {
            var values=new float[edge*edge*4];
            for(int y=0;y<edge;y++)for(int x=0;x<edge;x++)
                expected(x,y).CopyTo(values,(y*edge+x)*4);
            ImageAssertions.Comparisons.Compare(actual,values,tolerance:.001f).AssertEqual();
        }
    }
    #endregion

    #region Private
    /// <summary>Isolates only resources created by this fixture without adding a production context lifetime abstraction.</summary>
    private sealed class AtlasStatics : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type Owner = typeof(MaterialAtlasNormalDepthGpuBuilder);
        /// <summary>Rejects borrowing a preexisting initialized singleton owned by another fixture.</summary>
        internal AtlasStatics() => Assert.False((bool)Owner.GetField("initialized", Flags)!.GetValue(null)!);
        /// <summary>Releases borrowed pipelines before test-created programs, intermediates and geometry.</summary>
        public void Dispose()
        {
            var pipelines = (Dictionary<PbrHeightBakeShaderProgram, GraphicsPipelineAsset>)Owner.GetField("Pipelines", Flags)!.GetValue(null)!;
            foreach (var pipeline in pipelines.Values) pipeline.Dispose(); pipelines.Clear();
            foreach (var field in Owner.GetFields(Flags).Where(field => !field.IsInitOnly && !field.IsLiteral))
            {
                object? value = field.GetValue(null);
                if (value is IDisposable owned) owned.Dispose();
                else if (field.Name == "tile" && value is not null)
                    foreach (var property in value.GetType().GetProperties()) (property.GetValue(value) as IDisposable)?.Dispose();
                else if (field.Name == "mg" && value is not null)
                    value.GetType().GetMethod("DisposeAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null);
                field.SetValue(null, field.FieldType == typeof(bool) ? false : null);
            }
        }
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
    }
    #endregion
}
