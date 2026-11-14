using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Sampling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises production integer atomic depth writes through a packaged, clipped raster shader.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowAtomicWriteTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Writes surviving fragments across a negative local texel into its exact frozen neighboring page.</summary>
    [Fact]
    public void ClippedFragmentsAtomicallyWriteOnlyTheirAssignedDepthTile()
    {
        // Arrange: prepare two adjacent writable page assignments and the packaged production writer fixture.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4, "The context cannot execute the four-output clipped writer fixture.");
        using var assets = new BinaryShaderApiFixture();
        using var pool = ShadowPagePool.Create(assets.Api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 2, TileRows = 1, ArrayLayers = 1,
            ProbeLimit = 1, OperationsPerBatch = 16
        });
        var source = new ShadowSourceKey(71, 0, ShadowSourceKind.Sun, 3, 9, 0);
        var key = new ShadowPageKey(source, 1, 0, 0, 0);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            new ShadowBounds(-16, -16, -16, 16, 16, 16), baseTexelWidth: 1, levels: 1, pageSize: 8);
        var snapshot = ShadowProjectionSnapshot.Directional(projection, 0);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate, key,
            new ShadowBounds(-4, -4, -4, 4, 4, 4), 1, 1, 0, Projection: snapshot);
        var negativeRequest = request with { Key = key with { X = -1 } };
        Assert.True(pool.BeginBatch());
        Assert.Equal(1, pool.Batch);
        Assert.True(pool.Execute(request));
        var allocation = Read(pool.Results, 8);
        Assert.Equal(0u, allocation[0]);
        int slot = checked((int)allocation[1]);
        Assert.NotEqual(0u, allocation[2]);
        PhysicalPage physical = ReadPhysical(pool, slot);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.Execute(negativeRequest));
        var negativeAllocation = Read(pool.Results, 8);
        Assert.Equal(0u, negativeAllocation[0]);
        PhysicalPage negativePhysical = ReadPhysical(pool, checked((int)negativeAllocation[1]));
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());

        using var shader = WriterShader(assets.Api);
        Assert.Equal(4, shader.Interface.Graphics!.ClipDistanceCount);
        var commands = RHICommandContext.Current;
        var vertexLayout = WriterVertexLayout();
        using var clipping = ClipSelection(commands, 1);
        using var depth = DepthBuffer(commands, 0.25f);
        using var geometry = WriterGeometry(vertexLayout, clipping, depth);
        using var target = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader, vertexLayout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();

        // The test block supplies the virtual key and offset; both SSBOs and the single array image are real pool storage.
        // Shift the surviving right half five texels left so its first pixel addresses page X=-1.
        using var pageKey = WriterKeyBuffer(commands, pool, request, texelOffsetX: -5);
        var bindings = WriterBindings(shader, pool, pageKey);

        // Act: clip the left side in fixed-function rasterization, then run the shared production imageAtomicMin helper.
        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.AtomicWriter", true,
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
        // Assert: negative division selects the previous page's last column, and nearby fragments write page zero.
        var layer = pool.DepthArray;
        Assert.Equal(ShadowDepthEncoding.Clear, GpuLocalRead.UInt32(layer,
            physical.TileX * pool.PageSize + 4, physical.TileY * pool.PageSize + 4, physical.Layer));
        Assert.Equal(ShadowDepthEncoding.Encode(0.25f), GpuLocalRead.UInt32(layer,
            physical.TileX * pool.PageSize + 1, physical.TileY * pool.PageSize + 4, physical.Layer));
        Assert.Equal(ShadowDepthEncoding.Clear, GpuLocalRead.UInt32(layer,
            negativePhysical.TileX * pool.PageSize + 3, negativePhysical.TileY * pool.PageSize + 4, negativePhysical.Layer));
        Assert.Equal(ShadowDepthEncoding.Encode(0.25f), GpuLocalRead.UInt32(layer,
            negativePhysical.TileX * pool.PageSize + 7, negativePhysical.TileY * pool.PageSize + 4, negativePhysical.Layer));
        pool.SealBatch();
    }

    /// <summary>Contending instances keep the nearest depth and cannot clear or overwrite a sampled page in the same raster.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContendedWriterKeepsNearestDepthInBothSubmissionOrdersAndProtectsPublishedNeighborTile(bool farDepthLast)
    {
        // Arrange: publish and sample page X=1 with a distinctive depth while page X=0 remains absent.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(GpuSupport.Graphics.MaxClipDistances < 4, "The context cannot execute the four-output clipped writer fixture.");
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 2, TileRows = 1, ArrayLayers = 1,
            ProbeLimit = 128, OperationsPerBatch = 32
        };
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var source = new ShadowSourceKey(72, 0, ShadowSourceKind.Sun, 3, 10, 0);
        var projection = new DirectionalShadowProjection(source, 2, Vector3d.UnitZ,
            new ShadowBounds(-16, -16, -16, 16, 16, 16), baseTexelWidth: 1, levels: 1, pageSize: 8);
        var shadowBounds = new ShadowBounds(0, -4, -4, 16, 8, 4);
        var protectedKey = new ShadowPageKey(source, 2, 0, 1, 0);
        var protectedRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, protectedKey,
            shadowBounds, 1, 1, 0, Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        Assert.True(pool.BeginBatch());
        Assert.True(pool.Execute(protectedRequest));
        var protectedAllocation = Read(pool.Results, 8);
        Assert.Equal(0u, protectedAllocation[0]);
        int protectedSlot = checked((int)protectedAllocation[1]);
        long protectedAssignment = Assignment(protectedAllocation);
        PhysicalPage protectedTile = ReadPhysical(pool, protectedSlot);
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        UploadTile(pool, protectedTile, 0.5f);
        Assert.True(pool.Execute(protectedRequest with
        {
            Operation = ShadowPageOperation.Publish, Assignment = protectedAssignment, Complete = true
        }));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);

        var sample = ShadowSampleRequest.Directional(projection, 0, new Vector3d(12, 4, 0),
            Vector3d.UnitZ, 1, 1, 0, settings);
        sampler.Sample(pool, sample);
        var sampled = Read(sampler.Results, 40);
        Assert.Equal(0u, sampled[1]);
        Assert.All(Addresses(sampled), page => Assert.Equal((1, 0), (page.X, page.Y)));

        // Act: begin a new batch with a writable page X=0 and draw two overlapping instances across both pages.
        pool.SealBatch();
        RHICommandContext.Current.WaitIdle(); pool.Poll();
        Assert.True(pool.BeginBatch());
        var writeKey = new ShadowPageKey(source, 2, 0, 0, 0);
        var writeRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, writeKey,
            shadowBounds, 1, 1, 0, Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        Assert.True(pool.Execute(writeRequest));
        var writeAllocation = Read(pool.Results, 8);
        Assert.Equal(0u, writeAllocation[0]);
        PhysicalPage writableTile = ReadPhysical(pool, checked((int)writeAllocation[1]));
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());

        using var shader = WriterShader(assets.Api);
        var commands = RHICommandContext.Current;
        var vertexLayout = WriterVertexLayout();
        using var clipSelection = ClipSelection(commands, clipHalf: 0);
        using var target = CreateRenderTarget(16, 8, TextureFormat.Rgba8);
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader, vertexLayout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var pageKey = WriterKeyBuffer(commands, pool, writeRequest);
        var bindings = WriterBindings(shader, pool, pageKey);
        float firstDepth = farDepthLast ? 0.25f : 0.75f;
        float secondDepth = farDepthLast ? 0.75f : 0.25f;
        using var firstDepthBuffer = DepthBuffer(commands, firstDepth);
        using var secondDepthBuffer = DepthBuffer(commands, secondDepth);
        using var firstGeometry = WriterGeometry(vertexLayout, clipSelection, firstDepthBuffer);
        using var secondGeometry = WriterGeometry(vertexLayout, clipSelection, secondDepthBuffer);

        using (var boundary = commands.EnterBoundary(new("Tests.Shadows.AtomicProtect", true,
                   graphics: [pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(bindings);
                commands.Draw(firstGeometry.Inputs, new(0, 3));
                commands.Draw(secondGeometry.Inputs, new(0, 3));
                commands.EndRenderPass();
                commands.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }

        // Assert: nearest depth wins whether the far fragment is submitted first or last; sampled X=1 stays at .5.
        var layer = pool.DepthArray;
        Assert.Equal(ShadowDepthEncoding.Encode(0.25f), GpuLocalRead.UInt32(layer,
            writableTile.TileX * pool.PageSize + 3, writableTile.TileY * pool.PageSize + 4, writableTile.Layer));
        Assert.Equal(ShadowDepthEncoding.Encode(0.5f), GpuLocalRead.UInt32(layer,
            protectedTile.TileX * pool.PageSize + 3, protectedTile.TileY * pool.PageSize + 4, protectedTile.Layer));
        pool.SealBatch();
    }
    #endregion

    #region Private
    /// <summary>Reads a fixed prefix of a completed GPU command or sample result buffer.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Decodes the nine sample taps' virtual face/page identities and comparison depths.</summary>
    private static (int Face, int X, int Y, float Reference)[] Addresses(uint[] words) => Enumerable.Range(0, 9)
        .Select(i => (unchecked((int)words[4 + i * 4]), unchecked((int)words[5 + i * 4]),
            unchecked((int)words[6 + i * 4]), BitConverter.UInt32BitsToSingle(words[7 + i * 4])))
        .ToArray();

    /// <summary>Loads the isolated precompiled writer program through the production binary fixture.</summary>
    private static RHIShader WriterShader(Vintagestory.API.Common.ICoreAPI api)
    {
        var program = ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/shadow-atomic-writer"];
        return BuiltShaderFixture.LoadProgram(new(new ShaderSettings(program)));
    }

    /// <summary>Declares one integer per-vertex clipping selector consumed by the packaged witness vertex stage.</summary>
    private static RHIVertexLayout WriterVertexLayout() => new([
        new(11, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 0, 0, sizeof(uint)),
        new(12, 1, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, sizeof(float))
    ]);

    /// <summary>Creates three equal depth inputs so separate draws impose the tested atomic arrival order.</summary>
    private static GpuVbo DepthBuffer(RHICommandContext commands, float depth)
    {
        var buffer = commands.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.AtomicWriterDepth"
        }).RequireResource();
        buffer.UploadData<float>([depth, depth, depth]);
        return buffer;
    }

    /// <summary>Combines the clip selector and ordered per-vertex depth streams for one deterministic triangle draw.</summary>
    private static ArrayGraphicsGeometry WriterGeometry(RHIVertexLayout layout, GpuVbo clipSelection, GpuVbo depth) =>
        new(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo> { [0] = clipSelection, [1] = depth });

    /// <summary>Creates three identical clip-control values for a procedural fullscreen triangle.</summary>
    private static GpuVbo ClipSelection(RHICommandContext commands, uint clipHalf)
    {
        var buffer = commands.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.AtomicWriterClip"
        }).RequireResource();
        buffer.UploadData<uint>([clipHalf, clipHalf, clipHalf]);
        return buffer;
    }

    /// <summary>Allocates the exact std140 key/configuration block consumed by the packaged writer fixture.</summary>
    private static GpuUniformBuffer WriterKeyBuffer(RHICommandContext commands, ShadowPagePool pool,
        ShadowPageRequest request, int texelOffsetX = 0, int texelOffsetY = 0)
    {
        var buffer = commands.CreateUniformBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.DynamicDraw, Label = "Tests.Shadows.AtomicWriterKey"
        }).RequireResource();
        buffer.Allocate(32 * sizeof(uint));
        buffer.UploadSubData<uint>(WriterInputs(pool, request, texelOffsetX, texelOffsetY), 0);
        return buffer;
    }

    /// <summary>Packs the key, bounded lookup configuration and current batch in four-word uniform vectors.</summary>
    private static uint[] WriterInputs(ShadowPagePool pool, ShadowPageRequest request,
        int texelOffsetX = 0, int texelOffsetY = 0)
    {
        uint[] words = new uint[32];
        Array.Copy(request.Pack(), words, 17);
        words[20] = (uint)pool.Capacity; words[21] = (uint)pool.ProbeLimit;
        words[22] = (uint)pool.PageSize; words[23] = (uint)pool.ArrayLayers;
        words[24] = (uint)pool.Batch; words[25] = (uint)(pool.Batch >> 32);
        words[28] = unchecked((uint)texelOffsetX); words[29] = unchecked((uint)texelOffsetY);
        return words;
    }

    /// <summary>Binds the frozen assignment map and the read/write integer array for atomic minimum.</summary>
    private static RHIResourceBindings WriterBindings(RHIShader shader, ShadowPagePool pool, GpuUniformBuffer pageKey) =>
        new(shader,
            images:
            [
                Image("shadowDepthPool", pool.DepthArray)
            ],
            buffers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "ShadowWriteEntries"),
                    BufferRole.ShaderStorage, pool.Entries),
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "ShadowWritableAssignments"),
                    BufferRole.ShaderStorage, pool.WritableAssignments),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "ShadowAtomicWriterInputs"),
                    BufferRole.Uniform, pageKey, Length: 32 * sizeof(uint), Whole: false)
            ]);

    /// <summary>Uploads one synthetic far-to-near test value into only the nominated physical page tile.</summary>
    private static void UploadTile(ShadowPagePool pool, PhysicalPage tile, float depth)
    {
        var texture = pool.DepthArray;
        uint[] encoded = Enumerable.Repeat(ShadowDepthEncoding.Encode(depth), pool.PageSize * pool.PageSize).ToArray();
        var encoding = RHICommandContext.Current.GetTextureEncoding(texture, PixelComponentType.UnsignedInt);
        RHICommandContext.Current.UploadTexture(texture,
            new(new(0, tile.Layer), tile.TileX * pool.PageSize, tile.TileY * pool.PageSize, 0, pool.PageSize, pool.PageSize),
            encoding, System.Runtime.InteropServices.MemoryMarshal.AsBytes(encoded.AsSpan()));
    }

    /// <summary>Combines the GPU allocator's assignment words without interpreting the slot as a tile address.</summary>
    private static long Assignment(uint[] result) => unchecked((long)(((ulong)result[3] << 32) | result[2]));

    /// <summary>Reads the assigned layer and 2D tile directly from the GPU entry record.</summary>
    private static PhysicalPage ReadPhysical(ShadowPagePool pool, int slot)
    {
        using var mapped = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return new(checked((int)mapped.Span[0]), checked((int)mapped.Span[1]), checked((int)mapped.Span[2]));
    }

    /// <summary>Builds a layered R32UI array image view for a named writer resource.</summary>
    private static RHIImageInput Image(string name, GpuTexture texture) =>
        new(GpuBindingEntry.Identity(ShaderBindingKind.Image, name),
            new RHIImageBinding(texture, ImageAccess.ReadWrite, Layered: true, Format: TextureFormat.R32ui));

    /// <summary>Stores the explicit array layer and tile coordinates selected by the page allocator.</summary>
    private readonly record struct PhysicalPage(int Layer, int TileX, int TileY);
    #endregion
}
