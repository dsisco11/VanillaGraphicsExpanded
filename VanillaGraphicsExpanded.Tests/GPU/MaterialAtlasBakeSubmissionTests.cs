using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the atlas owner's independent clear and complete multigrid bake entries.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        using var source = DynamicTexture2D.CreateWithData(edge, edge, PixelInternalFormat.Rgba16f, input);
        using var destination = DynamicTexture2D.Create(edge, edge, PixelInternalFormat.Rgba16f);
        using var caller = CreateRenderTarget(3, 2, PixelInternalFormat.Rgba16f);
        using var read = CreateRenderTarget(2, 3, PixelInternalFormat.Rgba16f);
        using var transfer = GpuPixelPackBuffer.Create();
        transfer.AllocateOrphan(128 * 128 * 16);
        using var hostilePack = StateCache.Current.SetPixelPackScope(new(8, RowLength: 73, SkipRows: 2, SkipPixels: 3));
        using var hostileTransfer = StateCache.Current.BindBufferScope(BufferTarget.PixelPackBuffer, transfer.BufferId);
        StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, caller.FboId);
        StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
        StateCache.Current.ApplyDynamic(new() { X = 1, Y = 0, Width = 2, Height = 2 });
        try
        {
            using (var hostile = new HostileFullscreenState())
            {
                MaterialAtlasNormalDepthGpuBuilder.ClearAtlasPage(assets.Api, destination.TextureId, edge, edge);
                hostile.AssertRestored(); AssertBindings();
            }
            Assert.True(ReadDestination()[0] == .5f, $"Clear failed: {StateCache.Current.BoundaryEntryFailure}; {string.Join(Environment.NewLine, assets.Logs)}");
            AssertPixels(ReadDestination(), (_, _) => [.5f, .5f, 1f, .5f]);
            float[] sentinel = [.125f, .25f, .75f, .125f];
            destination.UploadDataImmediate(Enumerable.Range(0, edge * edge).SelectMany(_ => sentinel).ToArray());
            // Reject a real cold executable load after the candidate owner allocated its resources.
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
                Assert.False(MaterialAtlasNormalDepthGpuBuilder.BakePerRect(assets.Api, source.TextureId,
                    destination.TextureId, edge, edge, origin, origin, size, size, 1, 1));
                hostile.AssertRestored(); AssertBindings();
            }
            assets.BeforeRead = null;
            Assert.True(injected);
            Assert.NotEmpty(candidates);
            Assert.All(candidates, candidate => Assert.True(candidate.IsRetired));
            Assert.NotNull(failedScratch);
            Assert.True(failedScratch.IsDisposed);
            const BindingFlags initializationFields = BindingFlags.Static | BindingFlags.NonPublic;
            var atlasOwner = typeof(MaterialAtlasNormalDepthGpuBuilder);
            Assert.False((bool)atlasOwner.GetField("initialized", initializationFields)!.GetValue(null)!);
            foreach (var field in atlasOwner.GetFields(initializationFields).Where(field =>
                field.FieldType == typeof(PbrHeightBakeShaderProgram) || field.Name is "geometry" or "scratchFbo"))
                Assert.Null(field.GetValue(null));
            AssertPixels(ReadDestination(), (_, _) => sentinel);
            using (var hostile = new HostileFullscreenState())
            {
                Assert.True(MaterialAtlasNormalDepthGpuBuilder.BakePerRect(assets.Api, source.TextureId,
                    destination.TextureId, edge, edge, origin, origin, size, size, 1, 1), string.Join(Environment.NewLine, assets.Logs));
                hostile.AssertRestored(); AssertBindings();
            }
            float[] rectangle = ReadDestination();
            var heights = new List<float>();
            for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
            {
                int at = (y * edge + x) * 4;
                if (Inside(x, y))
                {
                    Assert.All(rectangle[at..(at + 4)], value => Assert.True(float.IsFinite(value)));
                    heights.Add(rectangle[at + 3]);
                }
                else Assert.Equal(sentinel, rectangle[at..(at + 4)]);
            }
            Assert.True(heights.Max() - heights.Min() > .001f, "The production solver must produce nonflat checkerboard relief.");
            Assert.Contains(heights, value => value is > .01f and < .99f);
            using (var hostile = new HostileFullscreenState())
            {
                // The invalid sampler fails after pass entry; optional bake catches the error only after restoration.
                Assert.False(MaterialAtlasNormalDepthGpuBuilder.BakePerRect(assets.Api, int.MaxValue,
                    destination.TextureId, edge, edge, origin, origin, size, size, 1, 1));
                hostile.AssertRestored(); AssertBindings();
            }
            Assert.Equal(rectangle, ReadDestination());
            using (var hostile = new HostileFullscreenState())
            {
                MaterialAtlasNormalDepthGpuBuilder.BakePerTexture(assets.Api, source.TextureId, destination.TextureId, edge, edge,
                    [new TextureAtlasPosition { atlasTextureId = source.TextureId, x1 = .25f, y1 = .25f, x2 = .75f, y2 = .75f }]);
                hostile.AssertRestored(); AssertBindings();
            }
            AssertPixels(ReadDestination(), (x, y) => Inside(x, y)
                ? rectangle[((y * edge + x) * 4)..((y * edge + x) * 4 + 4)] : [.5f, .5f, 1f, .5f]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { StateCache.Current.InvalidateAll(); }

        /// <summary>Preserves both caller framebuffer bindings around the texture readback helper.</summary>
        float[] ReadDestination()
        {
            using var bindings = StateCache.Current.BindFramebufferScope();
            using var pack = StateCache.Current.SetPixelPackScope(new(1));
            using var transferScope = StateCache.Current.BindBufferScope(BufferTarget.PixelPackBuffer, 0);
            return destination.ReadPixels();
        }
        /// <summary>Identifies the requested central atlas rectangle.</summary>
        static bool Inside(int x, int y) => x >= origin && x < origin + size && y >= origin && y < origin + size;
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
        /// <summary>Compares every output channel to the independent rectangle or clear expectation.</summary>
        static void AssertPixels(float[] actual, Func<int, int, float[]> expected)
        {
            for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
            {
                float[] wanted = expected(x, y);
                for (int channel = 0; channel < 4; channel++)
                    Assert.InRange(MathF.Abs(actual[(y * edge + x) * 4 + channel] - wanted[channel]), 0, .001f);
            }
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
            var pipelines = (Dictionary<PbrHeightBakeShaderProgram, GraphicsPipeline>)Owner.GetField("Pipelines", Flags)!.GetValue(null)!;
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
}
