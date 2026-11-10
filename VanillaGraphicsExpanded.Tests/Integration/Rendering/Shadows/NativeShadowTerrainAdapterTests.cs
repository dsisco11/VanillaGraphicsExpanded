using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks the terrain adapter against native pass tables and borrowed ordinary geometry under headless OpenGL.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowTerrainAdapterTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Captures caster and noncaster categories from the native table and preserves culling state.</summary>
    [Fact]
    public void CaptureUsesNativeCategoriesAndLeavesPoolStateUntouched()
    {
        // Arrange: Publish one ordinary pool under Opaque and another under Liquid in the native table.
        EnsureContextValid();
        using var opaque = new NativeTerrainPoolStorage(3, new(12, 24, 36), 7);
        using var liquid = new NativeTerrainPoolStorage(5, new(64, 32, 16), 9);
        var renderer = Renderer((EnumChunkRenderPass.Opaque, opaque.Pool), (EnumChunkRenderPass.Liquid, liquid.Pool));
        int oldCount = opaque.Pool.indicesGroupsCount;
        int[] oldStarts = (int[])opaque.Pool.indicesStartsByte.Clone();
        int[] oldSizes = (int[])opaque.Pool.indicesSizes.Clone();

        // Act: Traverse the supplied pass/atlas table once with enough budget for all visited cells.
        using var batch = NativeShadowTerrainAdapter.Capture(renderer, new ShadowDrawSettings { GroupVisits = 256 });

        // Assert: Both table categories remain distinguishable so GPU expansion can reject liquid casters explicitly.
        Assert.True(batch.Complete, batch.Failure);
        Assert.True(batch.IsCurrent);
        Assert.Equal(2, batch.Groups.Length);
        var group = Assert.Single(batch.Groups.Where(value => value.Casts));
        var liquidGroup = Assert.Single(batch.Groups.Where(value => !value.Casts));
        Assert.Equal((int)EnumChunkRenderPass.Opaque, (int)group.Pass);
        Assert.Equal(0, group.Atlas);
        Assert.Equal(7, group.PoolId);
        Assert.Equal(0x1a, group.TextureId);
        Assert.Equal(0, group.Dimension);
        Assert.Equal((12d, 24d, 36d), (group.Origin.X, group.Origin.Y, group.Origin.Z));
        Assert.Equal(EnumChunkRenderPass.Liquid, liquidGroup.Pass);
        Assert.Null(liquidGroup.Geometry);
        Assert.True(liquidGroup.IsCurrent);
        Assert.True(GL.IsVertexArray(opaque.Mesh.VaoId));
        Assert.True(GL.IsVertexArray(liquid.Mesh.VaoId));
        Assert.Equal(oldCount, opaque.Pool.indicesGroupsCount);
        Assert.Equal(oldStarts, opaque.Pool.indicesStartsByte);
        Assert.Equal(oldSizes, opaque.Pool.indicesSizes);
    }

    /// <summary>Exhausted table traversal returns an incomplete empty batch rather than a truncated group list.</summary>
    [Fact]
    public void CaptureRejectsGroupVisitOverflow()
    {
        // Arrange: A useful pool appears after traversal has exceeded the intentionally tiny allowance.
        EnsureContextValid();
        using var storage = new NativeTerrainPoolStorage(2, new(0, 0, 0), 11);
        var renderer = Renderer((EnumChunkRenderPass.Opaque, storage.Pool));

        // Act: Bound traversal below the first complete table walk.
        using var batch = NativeShadowTerrainAdapter.Capture(renderer, new ShadowDrawSettings { GroupVisits = 1 });

        // Assert: No partial native group survives failed preparation.
        Assert.False(batch.Complete);
        Assert.False(batch.IsCurrent);
        Assert.Contains("allowance", batch.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(batch.Groups);
    }

    /// <summary>Native range structure or origin changes withdraw the captured draw group before submission.</summary>
    [Fact]
    public void CapturedGroupRejectsChangedOriginAndAllocationBounds()
    {
        // Arrange: Capture an eligible native pool with current dimensions and allocation sizes.
        EnsureContextValid();
        using var storage = new NativeTerrainPoolStorage(2, new(16, 32, 48), 17);
        var renderer = Renderer((EnumChunkRenderPass.Opaque, storage.Pool));
        using var batch = NativeShadowTerrainAdapter.Capture(renderer, new ShadowDrawSettings { GroupVisits = 256 });
        var group = Assert.Single(batch.Groups.Where(value => value.Casts));
        Assert.True(batch.IsCurrent);

        // Act: Change the pool's light-space origin, restore it, then change its allocation extent.
        object origin = AccessTools.Field(typeof(MeshDataPool), "poolOrigin").GetValue(storage.Pool)!;
        var x = AccessTools.Field(origin.GetType(), "X");
        object priorX = x.GetValue(origin)!;
        x.SetValue(origin, Convert.ChangeType(Convert.ToDouble(priorX) + 1, x.FieldType));
        AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(storage.Pool, origin);
        bool originAccepted = batch.IsCurrent;
        x.SetValue(origin, priorX);
        AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(storage.Pool, origin);
        bool restored = batch.IsCurrent;
        storage.Pool.VerticesPoolSize++;

        // Assert: Changes invalidate the captured batch, while restoring the original native state permits reuse.
        Assert.False(originAccepted);
        Assert.True(restored);
        Assert.False(group.IsCurrent);
        Assert.False(batch.IsCurrent);
    }

    /// <summary>Range sorting builds one aggregate bounded command interval per current caster group and array.</summary>
    [Fact]
    public void DrawCommandsAggregateAcrossNativeGroupsAndArrays()
    {
        // Arrange: Capture two native caster groups in table order that differs from their pool ID ordering.
        EnsureContextValid();
        using var first = new NativeTerrainPoolStorage(2, new(0, 0, 0), 17);
        using var second = new NativeTerrainPoolStorage(2, new(0, 0, 0), 5);
        using var batch = NativeShadowTerrainAdapter.Capture(Renderer(
            (EnumChunkRenderPass.Opaque, first.Pool), (EnumChunkRenderPass.TopSoil, second.Pool)),
            new ShadowDrawSettings { GroupVisits = 256 });
        var world = World([Range(17, 0, 3), Range(5, 0, 3)]);

        // Act: Resolve complete current chunk ranges into two destination arrays.
        var draws = NativeShadowDrawRanges.Create(batch, world, new uint[] { 1, 0 }, 2, 3, new ShadowDrawSettings());

        // Assert: Commands follow captured native group order and each destination receives its own bounded interval.
        Assert.Equal(4, draws.CommandCount);
        Assert.Equal(new ShadowDrawInterval(0, 0, 0, 1), draws.Intervals[0]);
        Assert.Equal(new ShadowDrawInterval(0, 1, 1, 1), draws.Intervals[1]);
        Assert.Equal(new ShadowDrawInterval(1, 0, 2, 1), draws.Intervals[2]);
        Assert.Equal(new ShadowDrawInterval(1, 1, 3, 1), draws.Intervals[3]);
        Assert.Equal((0u, 0u, 3u), (draws.Ranges[0].Group, draws.Ranges[0].Command, draws.Ranges[0].TriangleCost));
        Assert.Equal((1u, 2u, 0u), (draws.Ranges[1].Group, draws.Ranges[1].Command, draws.Ranges[1].Eligible));
        Assert.Throws<InvalidOperationException>(() => NativeShadowDrawRanges.Create(batch, world,
            new uint[] { 1, 0 }, 2, 3, new ShadowDrawSettings { CommandSlots = 3 }));
    }

    /// <summary>Unknown or repeated native pool identities cannot turn a missing range into complete draw coverage.</summary>
    [Fact]
    public void DrawExpansionRejectsUnknownAndDuplicatePoolIdentity()
    {
        // Arrange: Capture one real group and provide a range whose native pool identity is absent from it.
        EnsureContextValid();
        using var storage = new NativeTerrainPoolStorage(2, new(0, 0, 0), 21);
        using var batch = NativeShadowTerrainAdapter.Capture(Renderer((EnumChunkRenderPass.Opaque, storage.Pool)),
            new ShadowDrawSettings { GroupVisits = 256 });
        var unknown = World([Range(99, 0, 3)]);

        // Act and assert: Unknown ranges reject, and putting the same native pool under two categories is ambiguous.
        Assert.Throws<InvalidOperationException>(() => NativeShadowDrawRanges.Create(batch, unknown,
            new uint[] { 1 }, 1, 1, new ShadowDrawSettings()));
        using var duplicate = NativeShadowTerrainAdapter.Capture(Renderer(
            (EnumChunkRenderPass.Opaque, storage.Pool), (EnumChunkRenderPass.TopSoil, storage.Pool)),
            new ShadowDrawSettings { GroupVisits = 256 });
        Assert.Throws<InvalidOperationException>(() => NativeShadowDrawRanges.Create(duplicate,
            World([Range(21, 0, 3)]), new uint[] { 1 }, 1, 1, new ShadowDrawSettings()));
    }

    /// <summary>A native index interval outside its captured pool allocation prevents command publication.</summary>
    [Fact]
    public void DrawExpansionRejectsRangesOutsideBorrowedPoolBounds()
    {
        // Arrange: Capture one pool with three indices, then describe a range beginning past its allocation end.
        EnsureContextValid();
        using var storage = new NativeTerrainPoolStorage(2, new(0, 0, 0), 31);
        using var batch = NativeShadowTerrainAdapter.Capture(Renderer((EnumChunkRenderPass.Opaque, storage.Pool)),
            new ShadowDrawSettings { GroupVisits = 256 });

        // Act and assert: The malformed current range never produces a command slot.
        Assert.Throws<InvalidOperationException>(() => NativeShadowDrawRanges.Create(batch,
            World([Range(31, 2, 3)]), new uint[] { 1 }, 1, 1, new ShadowDrawSettings()));
    }

    /// <summary>Page metadata is an independent instanced input and adapter disposal leaves native VAO ownership intact.</summary>
    [Fact]
    public void PageInstancesExtendBorrowedInputsWithoutMutatingNativeVertexArray()
    {
        // Arrange: Capture an ordinary terrain pool with its original vertex attributes and element buffer.
        EnsureContextValid();
        using var storage = new NativeTerrainPoolStorage(4, new(8, 16, 32), 13);
        var renderer = Renderer((EnumChunkRenderPass.Opaque, storage.Pool));
        using var batch = NativeShadowTerrainAdapter.Capture(renderer, new ShadowDrawSettings { GroupVisits = 256 });
        var group = Assert.Single(batch.Groups.Where(value => value.Casts));
        using var pageReferences = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 80,
            Usage = BufferUsage.StaticDraw, Label = "Shadow.Test.PageReferences"
        }).RequireResource();
        pageReferences.UploadData(new uint[20]);
        int nativeVao = storage.Mesh.VaoId;
        int nativeElements = storage.Mesh.vboIdIndex;
        int nativePosition = storage.Mesh.xyzVboId;

        // Act: Append the GPU-selected page reference at instance rate to a separate RHI packet.
        var pages = group.Geometry!.WithPages(new(new GpuBufferRef(pageReferences), 0, sizeof(uint)));

        // Assert: Four matrix columns and one integer destination use a single instance-rate record.
        Assert.Equal(9, pages.Layout.Attributes.Length);
        var pageAttribute = Assert.Single(System.Linq.Enumerable.Where(pages.Layout.Attributes, value => value.Location == 14));
        Assert.Equal((4, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 10, 80, 1, 64),
            (pageAttribute.Components, pageAttribute.Storage, pageAttribute.Interpretation, pageAttribute.Binding,
                pageAttribute.Stride, pageAttribute.Divisor, pageAttribute.Offset));
        Assert.Equal(5, pages.Streams.Length);
        var pageStream = Assert.Single(System.Linq.Enumerable.Where(pages.Streams, value => value.Binding == 10));
        Assert.Equal((nint)80, pageStream.Range.Length);
        Assert.Equal(80, pageStream.Stride);
        Assert.Equal(1, pageStream.Divisor);
        Assert.Equal(3, pages.Indices!.Value.Range.Length / sizeof(uint));
        Assert.True(batch.IsCurrent);

        // Dispose the page adapter and confirm the game still owns its realized array and source buffers.
        batch.Dispose();
        Assert.True(GL.IsVertexArray(nativeVao));
        Assert.True(GL.IsBuffer(nativeElements));
        Assert.True(GL.IsBuffer(nativePosition));
        Assert.False(batch.IsCurrent);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Builds only the native table shell; table cells and pools remain the engine's actual categories.</summary>
    private static ChunkRenderer Renderer(params (EnumChunkRenderPass Pass, MeshDataPool Pool)[] entries)
    {
        int liquid = Math.Max((int)EnumChunkRenderPass.Liquid, entries.Max(entry => (int)entry.Pass));
        var renderer = (ChunkRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkRenderer));
        renderer.poolsByRenderPass = new MeshDataPoolManager[liquid + 1][];
        renderer.textureIds = [0x1a];
        foreach (var entry in entries)
        {
            int pass = (int)entry.Pass;
            renderer.poolsByRenderPass[pass] ??= new MeshDataPoolManager[1];
            var manager = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
            AccessTools.Field(typeof(MeshDataPoolManager), "pools").SetValue(manager, new List<MeshDataPool> { entry.Pool });
            renderer.poolsByRenderPass[pass][0] = manager;
        }
        return renderer;
    }

    /// <summary>Creates a complete immutable draw input with the supplied native pool ranges.</summary>
    private static ShadowWorldPublication World(ShadowGeometryRange[] ranges) => new(1, true, null, [],
        ImmutableArray.Create(ranges), ImmutableDictionary<int, ShadowBounds>.Empty, 128);

    /// <summary>Builds a native pool range with a selectable index interval and its exact three-vertex allocation.</summary>
    private static ShadowGeometryRange Range(int pool, int start, int count) => new(pool, start, count,
        0, 3, 0, 1, 0);

    /// <summary>Creates one owned ordinary terrain allocation matching the installed engine position/UV/light/flag streams.</summary>
    private sealed class NativeTerrainPoolStorage : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = Buffer(), uv = Buffer(), light = Buffer(), flags = Buffer();
        private readonly GpuEbo elements = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = null
        }).RequireResource();

        #region Public API
        /// <summary>Releases fixture allocations only after the borrowed group has been disposed.</summary>
        public void Dispose()
        {
            vao.Dispose(); elements.Dispose(); positions.Dispose(); uv.Dispose(); light.Dispose(); flags.Dispose();
        }
        #endregion

        #region Internal API
        /// <summary>Uploads three ordinary vertices and publishes their native pool category and origin.</summary>
        internal NativeTerrainPoolStorage(int dimension, Vector3d origin, int id)
        {
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            positions.UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
            uv.UploadData(new float[6]);
            light.UploadData(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 });
            flags.UploadData(new uint[3]);
            elements.UploadIndices(new uint[] { 0, 1, 2 });
            OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, elements.BufferId);
            Attribute(0, 3, VertexAttribPointerType.Float, positions.BufferId, 12, false, false);
            Attribute(1, 2, VertexAttribPointerType.Float, uv.BufferId, 8, false, false);
            Attribute(2, 4, VertexAttribPointerType.UnsignedByte, light.BufferId, 4, true, false);
            Attribute(3, 1, VertexAttribPointerType.UnsignedInt, flags.BufferId, 4, false, true);

            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = elements.BufferId; Mesh.drawMode = PrimitiveType.Triangles;
            Mesh.xyzVboId = positions.BufferId; Mesh.uvVboId = uv.BufferId;
            Mesh.rgbaVboId = light.BufferId; Mesh.flagsVboId = flags.BufferId;
            Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            Pool.VerticesPoolSize = 3; Pool.IndicesPoolSize = 3;
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, Mesh);
            AccessTools.Field(typeof(MeshDataPool), "poolId").SetValue(Pool, id);
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(Pool, dimension);
            var originField = AccessTools.Field(typeof(MeshDataPool), "poolOrigin");
            var nativeOrigin = RuntimeHelpers.GetUninitializedObject(originField.FieldType);
            foreach (var axis in new[] { "X", "Y", "Z" })
                AccessTools.Field(originField.FieldType, axis).SetValue(nativeOrigin, Convert.ChangeType(axis switch
                {
                    "X" => origin.X, "Y" => origin.Y, _ => origin.Z
                }, AccessTools.Field(originField.FieldType, axis).FieldType));
            originField.SetValue(Pool, nativeOrigin);
            Pool.indicesGroupsCount = 1; Pool.indicesStartsByte = [0, 0]; Pool.indicesSizes = [3];
        }
        #endregion

        #region Private
        /// <summary>Creates mutable vertex storage for the engine's four ordinary streams.</summary>
        private static GpuVbo Buffer() => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = null
        }).RequireResource();

        /// <summary>Configures the fixture VAO using the native ordinary terrain component interpretation.</summary>
        private static void Attribute(int location, int components, VertexAttribPointerType type,
            int buffer, int stride, bool normalized, bool integer)
        {
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer);
            if (integer) GL.VertexAttribIPointer(location, components, VertexAttribIntegerType.UnsignedInt, stride, IntPtr.Zero);
            else GL.VertexAttribPointer(location, components, type, normalized, stride, IntPtr.Zero);
            GL.EnableVertexAttribArray(location);
        }
        #endregion
    }
    #endregion
}
