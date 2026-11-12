using System.Collections.Generic;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises packaged virtual stage generation, compact view-buffer lookup and material rejection on the GPU.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowVirtualStageGpuTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Projects one selected view and writes only fragments inside its uncached rectangle and material coverage.</summary>
    [Theory]
    [InlineData(-2, 2, 0)]
    [InlineData(1500000000, -1500000000, 0)]
    [InlineData(-1500000000, 1500000000, 0)]
    [InlineData(-2, 2, 1)]
    public void PackagedVirtualStagesUseSignedPageOriginsAndRejectInvalidSelectors(int pageX, int pageY, uint selectedView)
    {
        // Arrange: allocate the exact page and immutable TBO record consumed by the packaged shader generation.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        var capabilities = GpuSupport.Graphics;
        Assert.True(capabilities.ApiVersion is { } apiVersion && apiVersion >= new Version(4, 5),
            $"The baseline requires OpenGL 4.5; current version is {capabilities.VersionString}.");
        Assert.True(capabilities.SupportsArbGlSpirv, "The baseline requires GL_ARB_gl_spirv for packaged shader loading.");
        output.WriteLine($"OpenGL {fixture.GLVersion}; renderer {fixture.GLRenderer}; glSpirv={capabilities.SupportsArbGlSpirv}; " +
            $"clipDistances={capabilities.MaxClipDistances}; " +
            $"vertexTextureUnits={capabilities.MaxVertexTextureImageUnits}; tessEvaluationTextureUnits={capabilities.MaxTessEvaluationTextureImageUnits}; " +
            $"textureBufferTexels={capabilities.MaxTextureBufferSize}; viewport={capabilities.MaxViewportWidth}x{capabilities.MaxViewportHeight}.");
        output.WriteLine("Native optional extensions: " + string.Join("; ", OptionalFeatureScope.MaskedExtensions
            .Select(extension => $"{extension}={GpuSupport.Supports(extension)}")));
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4, "The context cannot execute the four-plane virtual shadow fixture.");
        using var baselineFeatures = new OptionalFeatureScope();
        output.WriteLine("Application capability snapshot masks optional draw-parameters, indirect-count, bindless and vertex-layer paths.");
        Assert.True(GpuSupport.Graphics.SupportsArbGlSpirv);
        Assert.False(GpuSupport.Graphics.SupportsArbBindlessTexture);
        foreach (string extension in OptionalFeatureScope.MaskedExtensions)
            Assert.False(GpuSupport.Supports(extension));
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 2, TileRows = 1, ArrayLayers = 1,
            ProbeLimit = 2, OperationsPerBatch = 16
        });
        var source = new ShadowSourceKey(73, 0, ShadowSourceKind.Sun, 2, 11, 0);
        const int texelX = 3, texelY = 0;
        long regionX = (long)pageX * 8 + texelX, regionY = (long)pageY * 8 + texelY;
        var represented = new ShadowBounds(regionX, regionY, -16, regionX + 8, regionY + 8, 16);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            represented, baseTexelWidth: 1, levels: 1, pageSize: 8);
        var key = new ShadowPageKey(source, 1, 0, pageX, pageY);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate, key,
            represented, 1, 1, 0, Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(request));
        var allocation = Read(pool.Results, 8);
        Assert.Equal(0u, allocation[0]);
        int slot = checked((int)allocation[1]);
        Assert.NotEqual(0u, allocation[2]);
        PhysicalPage physical = ReadPhysical(pool, slot);
        Assert.True(pool.ClearReplacement());
        var adjacent = request with { Key = key with { X = pageX + 1 } };
        Assert.True(pool.Execute(adjacent));
        var adjacentAllocation = Read(pool.Results, 8);
        Assert.Equal(0u, adjacentAllocation[0]);
        PhysicalPage adjacentPhysical = ReadPhysical(pool, checked((int)adjacentAllocation[1]));
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());

        var region = new ShadowRasterRegion(regionX, regionY, 8, 8);
        // Keep the tested material-discard texel inside the virtual rectangle while the nearby
        // positive-x page is allocated, so clipping and material rejection remain distinguishable.
        var uncached = new ShadowRasterRegion(regionX + 2, regionY, 5, 7);
        var view = ShadowDrawView.Directional(projection, 0, region, uncached, new(regionX, regionY, 0));
        using var views = new ShadowViewBuffer([view], pool, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 64
        });
        Assert.Equal(320, views.AllocatedBytes);

        var declaration = ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/shadow-virtual"];
        using var shader = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(declaration)));
        Assert.Equal(4, shader.Interface.Graphics!.ClipDistanceCount);
        var commands = RHICommandContext.Current;
        var vertexLayout = new RHIVertexLayout([
            new(10, 2, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 0, 0, 8)
        ]);
        using var selection = commands.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.VirtualSelection"
        }).RequireResource();
        // Vertex 0 returns from the material body only after assigning its deformed position;
        // the generated wrapper must still project and write its clip outputs for that vertex.
        selection.UploadData(new uint[] { 1, selectedView, 0, selectedView, 0, selectedView });
        using var geometry = new ArrayGraphicsGeometry(vertexLayout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo> { [0] = selection });
        var pass = views.DescribePass(0);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader, vertexLayout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        var bindings = new RHIResourceBindings(shader,
            samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "shadowViews"), views.Texture)],
            images:
            [
                Image("shadowDepthPool", pool.DepthArray)
            ],
            buffers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "ShadowWriteEntries"),
                    BufferRole.ShaderStorage, pool.Entries),
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "ShadowWritableAssignments"),
                    BufferRole.ShaderStorage, pool.WritableAssignments)
            ]);

        // Act: the generated vertex producer applies the view projection and clip rectangle before material rasterization.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.VirtualStages", true,
                   graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(bindings);
                commands.Draw(geometry.Inputs, new(0, 3));
                commands.EndRenderPass();
                commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }

        // Assert: the negative small origin is non-page-aligned; large signed origins and local offsets
        // still address the exact assigned base/adjacent tiles without narrowing absolute texels.
        var layer = pool.DepthArray;
        Assert.Equal(ShadowDepthEncoding.Clear, ReadDepth(layer, physical, 1, 4, pool.PageSize));
        uint expectedVisibleDepth = selectedView == 0 ? ShadowDepthEncoding.Encode(0.5f) : ShadowDepthEncoding.Clear;
        Assert.Equal(expectedVisibleDepth, ReadDepth(layer, physical, texelX + 3, texelY + 4, pool.PageSize));
        Assert.Equal(expectedVisibleDepth, ReadDepth(layer, adjacentPhysical, 0, texelY + 4, pool.PageSize));
        Assert.Equal(ShadowDepthEncoding.Clear, ReadDepth(layer, physical, texelX + 3, texelY + 7, pool.PageSize));
        Assert.Equal(ShadowDepthEncoding.Clear, ReadDepth(layer, adjacentPhysical, 1, texelY + 4, pool.PageSize));
        Assert.Equal(ShadowDepthEncoding.Clear, ReadDepth(layer, adjacentPhysical, 2, texelY + 4, pool.PageSize));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        pool.SealBatch();
        views.Seal();
        Assert.Equal(320, views.AllocatedBytes);
    }
    #endregion

    #region Private
    /// <summary>Reads the GPU command result record after the allocator has completed its dispatch.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int count)
    {
        using var mapped = buffer.MapRange<uint>(0, count, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Reads the array layer and tile assigned to the virtual page from its GPU entry.</summary>
    private static PhysicalPage ReadPhysical(ShadowPagePool pool, int slot)
    {
        using var mapped = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return new(checked((int)mapped.Span[0]), checked((int)mapped.Span[1]), checked((int)mapped.Span[2]));
    }

    /// <summary>Reads one depth texel after translating view-local coordinates into its allocated physical tile.</summary>
    private static uint ReadDepth(GpuTexture layer, PhysicalPage page, int x, int y, int pageSize) =>
        GpuLocalRead.UInt32(layer, page.TileX * pageSize + x, page.TileY * pageSize + y, page.Layer);

    /// <summary>Builds one explicit read/write image view required by imageAtomicMin's read-modify-write behavior.</summary>
    private static RHIImageInput Image(string name, GpuTexture texture) =>
        new(GpuBindingEntry.Identity(ShaderBindingKind.Image, name),
            new RHIImageBinding(texture, ImageAccess.ReadWrite, Layered: true, Format: TextureFormat.R32ui));

    /// <summary>Stores the page array layer and tile coordinates returned by the GPU allocator.</summary>
    private readonly record struct PhysicalPage(int Layer, int TileX, int TileY);

    /// <summary>Temporarily masks optional dispatch capabilities while preserving native OpenGL and SPIR-V ingestion.</summary>
    private sealed class OptionalFeatureScope : IDisposable
    {
        private static readonly FieldInfo GraphicsField = typeof(GpuSupport).GetField("graphics",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo ExtensionsField = typeof(GlExtensions).GetField("cached",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly GraphicsCapabilities originalGraphics;
        private readonly HashSet<string> originalExtensions;
        internal static readonly string[] MaskedExtensions =
        [
            "GL_ARB_shader_draw_parameters", "GL_ARB_indirect_parameters",
            "GL_ARB_bindless_texture", "GL_ARB_shader_viewport_layer_array"
        ];

        /// <summary>Copies the extension snapshot and capability record, removing only optional VSM dispatch paths.</summary>
        internal OptionalFeatureScope()
        {
            originalGraphics = GpuSupport.Graphics;
            originalExtensions = (HashSet<string>?)ExtensionsField.GetValue(null)
                ?? throw new InvalidOperationException("The native extension snapshot was not initialized.");
            var maskedExtensions = new HashSet<string>(originalExtensions, StringComparer.Ordinal);
            foreach (string extension in MaskedExtensions) maskedExtensions.Remove(extension);
            ExtensionsField.SetValue(null, maskedExtensions);
            GraphicsField.SetValue(null, originalGraphics with { SupportsArbBindlessTexture = false });
        }

        /// <summary>Restores the exact cached context capability and extension snapshots.</summary>
        public void Dispose()
        {
            ExtensionsField.SetValue(null, originalExtensions);
            GraphicsField.SetValue(null, originalGraphics);
        }
    }
    #endregion
}
