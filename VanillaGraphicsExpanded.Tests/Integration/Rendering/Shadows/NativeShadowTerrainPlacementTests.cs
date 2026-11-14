using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks native moving-pool placement and material snapshots against a separate virtual view origin.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowTerrainPlacementTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares integer comparison executables used to verify complete GPU shadow tiles.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs) =>
        programs.Prepare("compare", 2, 0);

    /// <summary>Tracks native moving offsets, transformed pivots, selection loss and camera changes before drawing.</summary>
    [Fact]
    public void MovingPlacementRevalidatesOffsetTransformAndNativeInputs()
    {
        // Arrange: capture a mini-dimension pool whose animation matrix and selection-following offset are mutable.
        EnsureContextValid();
        using var nativePool = new PlacementPool(dimension: 1, new(10, 20, 30));
        using var group = new NativeShadowTerrainGroup(nativePool.Pool, EnumChunkRenderPass.Liquid, 0, 0);
        var camera = Camera(new(100, 70, -30));
        var world = new Mock<IClientWorldAccessor>();
        world.SetupGet(value => value.Player).Returns(camera.Player);
        var dimension = new Mock<IMiniDimension>();
        dimension.SetupGet(value => value.selectionTrackingOriginalPos).Returns(new BlockPos());
        var poseType = typeof(IMiniDimension).GetProperty("CurrentPos")!.PropertyType;
        SetupCurrentPos(dimension, (EntityPos)Activator.CreateInstance(poseType)!);
        var offset = new FastVec3d(4, 5, 6);
        float pivot = 2;
        dimension.Setup(value => value.GetRenderOffset(.125f)).Returns(() => offset);
        dimension.Setup(value => value.GetRenderTransformMatrix(It.IsAny<float[]>(), It.IsAny<Vec3d>()))
            .Returns((float[] matrix, Vec3d _) =>
            {
                matrix[12] = pivot;
                return matrix;
            });
        IMiniDimension selected = dimension.Object;
        world.Setup(value => value.TryGetMiniDimension(It.IsAny<Vec3i>(), out selected))
            .Returns(true);
        var reference = new Vector3d(200, 80, -10);

        // Act: freeze placement at the current native render offset and final transform.
        var placement = new NativeShadowTerrainPlacement(group, world.Object, reference, .125f);

        // Assert: native camera-relative origin and view-relative final transform retain separate origins.
        Assert.True(placement.Moving);
        Assert.True(placement.IsCurrent);
        Assert.Equal((-86d, -45d, 66d), (placement.Origin.X, placement.Origin.Y, placement.Origin.Z));
        Assert.Equal(reference, placement.Reference);
        Assert.Equal(-98f, placement.Transform[12]);
        Assert.Equal(-10f, placement.Transform[13]);
        Assert.Equal(-20f, placement.Transform[14]);

        // Act and assert: each native placement input independently withdraws the frozen draw packet.
        offset = new FastVec3d(7, 5, 6);
        Assert.False(placement.IsCurrent);
        offset = new FastVec3d(4, 5, 6);
        Assert.True(placement.IsCurrent);
        pivot = 3;
        Assert.False(placement.IsCurrent);
        pivot = 2;
        Assert.True(placement.IsCurrent);
        dimension.SetupGet(value => value.selectionTrackingOriginalPos).Returns((BlockPos?)null);
        Assert.False(placement.IsCurrent);
        dimension.SetupGet(value => value.selectionTrackingOriginalPos).Returns(new BlockPos());
        world.Setup(value => value.TryGetMiniDimension(It.IsAny<Vec3i>(), out selected)).Returns(false);
        Assert.False(placement.IsCurrent);
        world.Setup(value => value.TryGetMiniDimension(It.IsAny<Vec3i>(), out selected)).Returns(true);
        camera.Entity.CameraPos.X += 1;
        Assert.False(placement.IsCurrent);
    }

    /// <summary>Rejects moving pools whose current camera or selection-tracked native placement is unavailable.</summary>
    [Fact]
    public void MovingPlacementRejectsUnavailableNativeInputs()
    {
        // Arrange: capture one moving native group and a current player camera, but no selected mini-dimension.
        EnsureContextValid();
        using var nativePool = new PlacementPool(dimension: 1, new(10, 20, 30));
        using var group = new NativeShadowTerrainGroup(nativePool.Pool, EnumChunkRenderPass.Liquid, 0, 0);
        var camera = Camera(new(100, 70, -30));
        var world = new Mock<IClientWorldAccessor>();
        world.SetupGet(value => value.Player).Returns(camera.Player);
        IMiniDimension selected = Mock.Of<IMiniDimension>();
        world.Setup(value => value.TryGetMiniDimension(It.IsAny<Vec3i>(), out selected)).Returns(false);

        // Act and assert: unknown native placement fails before an immutable draw transform can be captured.
        Assert.Throws<InvalidOperationException>(() => new NativeShadowTerrainPlacement(group, world.Object,
            new Vector3d(200, 80, -10), .125f));

        // Arrange: a present native dimension without selection tracking still has no qualified render placement.
        var dimension = new Mock<IMiniDimension>();
        dimension.SetupGet(value => value.selectionTrackingOriginalPos).Returns((BlockPos?)null);
        selected = dimension.Object;
        world.Setup(value => value.TryGetMiniDimension(It.IsAny<Vec3i>(), out selected)).Returns(true);

        // Assert: missing selection-following state rejects the same pool rather than assuming its block origin is current.
        Assert.Throws<InvalidOperationException>(() => new NativeShadowTerrainPlacement(group, world.Object,
            new Vector3d(200, 80, -10), .125f));
    }

    /// <summary>Submits a production-selected native draw, then rechecks placement and borrowed storage freshness.</summary>
    [Fact]
    public void PreparedNativeDrawSubmitsAndRejectsChangedPlacementOrStorage()
    {
        // Arrange: prepare a current native range, complete GPU candidate, writable page, view, and packaged material.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var worldPublication = DrawWorld();
        using var worldBuffers = new ShadowWorldGpuBuffers();
        Assert.True(worldBuffers.Upload(worldPublication));
        var source = new ShadowSourceKey(1, 0, ShadowSourceKind.Sun, 53, 53, 0);
        var bounds = new ShadowBounds(-16, -16, -16, 16, 16, 16);
        var changes = new ShadowCandidateChanges(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        var domain = new ShadowChunkDomain(0, 0, 0, 0, 0, 0, 0);
        var receiver = domain.Bounds;
        using var candidates = ShadowCandidatePool.Create(assets.Api, CandidateSettings());
        Assert.True(candidates.BeginBatch(changes, worldPublication, worldBuffers));
        var candidateRequest = ShadowCandidateRequest.Directional(source, worldPublication.Owner,
            worldPublication.Generation, 1, domain, receiver, Vector3d.UnitZ, 179.9);
        Assert.True(candidates.TryAllocate(candidateRequest, out var handle));
        var current = new ShadowCandidateCurrent(source, worldPublication.Owner, worldPublication.Generation,
            1, receiver, Vector3d.UnitZ, changes.Epoch, changes.ToSequence, 61);
        CompleteCandidate(candidates, handle, current, changes, worldPublication, worldBuffers);
        using var pages = ShadowPagePool.Create(assets.Api, PageSettings());
        Assert.True(pages.BeginBatch());
        var projection = new DirectionalShadowProjection(source, 61, Vector3d.UnitZ, bounds, 1, 1, 8);
        var view = ShadowDrawView.Directional(projection, 0, new(0, 0, 8, 8), new(0, 0, 8, 8), Vector3d.Zero);
        var key = new ShadowPageKey(source, 61, 0, 0, 0);
        var pageRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, key, bounds,
            1, 1, 0, Projection: ShadowProjectionSnapshot.Directional(projection, 0));
        Assert.True(pages.Execute(pageRequest));
        long assignment;
        using (var result = pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read))
        {
            Assert.True(result.IsMapped);
            Assert.Equal(0u, result.Span[0]);
            assignment = unchecked((long)(((ulong)result.Span[3] << 32) | result.Span[2]));
        }
        Assert.True(assignment > 0);
        Assert.True(pages.ClearReplacement());
        Assert.True(pages.FreezeWritableAssignments());

        var camera = Camera(Vector3d.Zero);
        var nativeWorld = new Mock<IClientWorldAccessor>();
        nativeWorld.SetupGet(value => value.Player).Returns(camera.Player);
        using var nativePool = new NativeDrawPool(17, new(0, 0, 0));
        using var atlas = Atlas([255, 255, 255, 255]);
        using var group = new NativeShadowTerrainGroup(nativePool.Pool, EnumChunkRenderPass.Opaque, 0, atlas.TextureId);
        var placement = new NativeShadowTerrainPlacement(group, nativeWorld.Object, view.Origin, .016f);
        using var views = new ShadowViewBuffer([view], pages, new ShadowDrawSettings
        {
            MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 64
        });
        using var selection = new ShadowGeometrySelection(assets.Api, [view],
            [new ShadowViewCandidates(0, handle, current)], candidates, worldPublication, worldBuffers,
            [new ShadowDrawRange(0, 0, 1, 1, 0, 3, 0, 1, 0, 0, 3)], 1, pages,
            new ShadowDrawSettings
            {
                MemoryMiB = 1, ViewCapacity = 1, PairCapacity = 4, ExpansionVisits = 128,
                CommandSlots = 4, ViewInstances = 4, RasterPixels = 64,
                EstimatedTriangles = 8, EstimatedRasterSamples = 512
            }, otherDrawingBytes: 0);
        using var shader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        var uniforms = new DefaultShaderUniforms { PlayerPos = new Vec3f(0, 0, 0) };
        using var draw = new NativeShadowTerrainDraw(group, placement, uniforms, shader, view,
            views, 0, selection, new ShadowDrawInterval(0, 0, 1), pages, 0, 0);

        // Act: execute the production prepared indirect draw through its captured RHI pipeline and bindings.
        var rhi = RHICommandContext.Current;
        using (var boundary = rhi.EnterBoundary(new("Tests.Shadows.NativePreparedTerrain", true,
                   graphics: [draw.Pipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                draw.Submit();
                rhi.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
            });
        }
        // Assert: at least one real native fragment reached the integer page through captured selector bindings.
        var clear = ImageAssertions.Comparisons.CompareUnsignedConstant(pages.DepthArray,
            ShadowDepthEncoding.Clear, new(new(0), 0, 0, 0, pages.PageSize, pages.PageSize));
        Assert.True(clear.Mismatches > 0);

        // Act and assert: changed camera placement and then changed native storage both withdraw before another draw.
        camera.Entity.CameraPos.X += 1;
        Assert.Throws<InvalidOperationException>(() => draw.Submit());
        camera.Entity.CameraPos.X -= 1;
        nativePool.Pool.VerticesPoolSize++;
        Assert.Throws<InvalidOperationException>(() => draw.Submit());
        pages.SealBatch();
        views.Seal();
    }

    /// <summary>Captures the native warp block with camera-space inputs and validates its independent view origin.</summary>
    [Fact]
    public void MaterialSnapshotPreservesWarpInputsAndRejectsStalePlacement()
    {
        // Arrange: use static native placement, a distant virtual origin and the packaged ordinary terrain executable.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var nativePool = new PlacementPool(dimension: 0, new(10, 20, 30));
        using var group = new NativeShadowTerrainGroup(nativePool.Pool, EnumChunkRenderPass.Liquid, 0, 0);
        var camera = Camera(new(100, 70, -30));
        var world = new Mock<IClientWorldAccessor>();
        world.SetupGet(value => value.Player).Returns(camera.Player);
        var reference = new Vector3d(200, 80, -10);
        var placement = new NativeShadowTerrainPlacement(group, world.Object, reference, .016f);
        var source = new ShadowSourceKey(1, 0, ShadowSourceKind.Sun, 3, 3, 0);
        var projection = new DirectionalShadowProjection(source, 5, Vector3d.UnitZ,
            new(-100, -100, -100, 100, 100, 100), 4, 1, 8);
        var view = ShadowDrawView.Directional(projection, 0, new(0, 0, 8, 8), new(0, 0, 8, 8), reference);
        using var shader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        var uniforms = new DefaultShaderUniforms
        {
            PlayerPos = new Vec3f(7, 8, 9), TimeCounter = 1.25f,
            WindWaveCounter = 2.5f, WindWaveCounterHighFreq = 3.5f,
            WaterWaveCounter = 4.5f, WindSpeed = 5.5f, GlobalWorldWarp = 6.5f,
            GlitchWaviness = 7.5f, WindWaveIntensity = 8.5f,
            WaterWaveIntensity = 9.5f, PerceptionEffectIntensity = 10.5f,
            PerceptionEffectId = 11
        };

        // Act: capture once, then reject a mismatched view, nonfinite material and stale native camera before recapture.
        NativeShadowTerrainMaterial.Capture(shader, uniforms, placement, view, .25f, .5f);
        var parameters = ((IShadowTerrainBindings)shader).Parameters;
        byte[] captured = parameters.Bytes.ToArray();
        Assert.Throws<InvalidOperationException>(() => NativeShadowTerrainMaterial.Capture(shader, uniforms,
            placement, ShadowDrawView.Directional(projection, 0, new(0, 0, 8, 8), new(0, 0, 8, 8), Vector3d.Zero), .25f, .5f));
        uniforms.GlobalWorldWarp = float.NaN;
        Assert.Throws<InvalidOperationException>(() => NativeShadowTerrainMaterial.Capture(shader, uniforms, placement, view, .25f, .5f));
        uniforms.GlobalWorldWarp = 6.5f;
        camera.Entity.CameraPos.X += 1;
        Assert.Throws<InvalidOperationException>(() => NativeShadowTerrainMaterial.Capture(shader, uniforms, placement, view, .25f, .5f));
        Assert.Equal(captured, parameters.Bytes.ToArray());

        // Assert: the packed block retains virtual transform, native camera-relative origin, atlas padding and warp values.
        Assert.Equal(new float[] { -90, -50, 60, .25f }, MemoryMarshal.Cast<byte, float>(parameters.Bytes.Slice(
            ShadowMaterialParameters.ChunkOriginOffset, 4 * sizeof(float))).ToArray());
        Assert.Equal(new float[] { 7, 8, 9, .5f }, MemoryMarshal.Cast<byte, float>(parameters.Bytes.Slice(
            ShadowMaterialParameters.DeformationWorldOriginOffset, 4 * sizeof(float))).ToArray());
        Assert.Equal(new float[] { 1.25f, 2.5f, 3.5f, 4.5f }, MemoryMarshal.Cast<byte, float>(parameters.Bytes.Slice(
            ShadowMaterialParameters.AnimationOffset, 4 * sizeof(float))).ToArray());
        Assert.Equal(new float[] { 5.5f, 6.5f, 7.5f, 8.5f }, MemoryMarshal.Cast<byte, float>(parameters.Bytes.Slice(
            ShadowMaterialParameters.WindOffset, 4 * sizeof(float))).ToArray());
        Assert.Equal(9.5f, UboPacking.ReadFloat(parameters.Bytes, ShadowMaterialParameters.PerceptionOffset));
        Assert.Equal(10.5f, UboPacking.ReadFloat(parameters.Bytes, ShadowMaterialParameters.PerceptionOffset + 2 * sizeof(float)));
        Assert.Equal(11, UboPacking.ReadInt32(parameters.Bytes, ShadowMaterialParameters.PerceptionModeOffset));
        Assert.Equal(-100f, UboPacking.ReadFloat(parameters.Bytes, ShadowMaterialParameters.ShadowOriginTranslationOffset));
        Assert.Equal(-10f, UboPacking.ReadFloat(parameters.Bytes, ShadowMaterialParameters.ShadowOriginTranslationOffset + sizeof(float)));
        Assert.Equal(-20f, UboPacking.ReadFloat(parameters.Bytes, ShadowMaterialParameters.ShadowOriginTranslationOffset + 2 * sizeof(float)));
    }
    #endregion

    #region Private
    /// <summary>Builds a native client player with an independently controlled camera origin.</summary>
    private static (ClientPlayer Player, EntityPlayer Entity) Camera(Vector3d origin)
    {
        var entity = new EntityPlayer();
        entity.CameraPos.Set(origin.X, origin.Y, origin.Z);
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var playerData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(playerData, entity);
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(player, playerData);
        return (player, entity);
    }

    /// <summary>Configures the native pose using Moq's statically typed property setup.</summary>
    private static void SetupCurrentPos(Mock<IMiniDimension> dimension, EntityPos position)
    {
        dimension.SetupGet(value => value.CurrentPos).Returns(position);
    }

    /// <summary>Creates one complete current chunk table and native indexed terrain range for the draw integration.</summary>
    private static ShadowWorldPublication DrawWorld()
    {
        var table = new ShadowChunkGpu[2];
        var entry = new ShadowChunkGpu
        {
            X = 0, Y = 0, Z = 0, Dimension = 0,
            NativeLow = 18, LifetimeLow = 1, RevisionLow = 1,
            Flags = 3, RangeOffset = 0, RangeCount = 1
        };
        table[Hash(entry.Dimension, entry.X, entry.Y, entry.Z) & 1u] = entry;
        var ranges = ImmutableArray.Create(new ShadowGeometryRange(17, 0, 3, 0, 3, 0, 1, 0));
        var envelope = ImmutableDictionary<int, ShadowBounds>.Empty.Add(0, new(0, 0, 0, 32, 32, 32));
        return new(1, true, null, ImmutableArray.Create(table), ranges, envelope, table.Length) { Owner = 41 };
    }

    /// <summary>Produces a candidate header with the real packaged candidate compute path before indirect selection.</summary>
    private static void CompleteCandidate(ShadowCandidatePool pool, ShadowCandidateHandle handle,
        ShadowCandidateCurrent current, ShadowCandidateChanges changes, ShadowWorldPublication publication,
        ShadowWorldGpuBuffers world)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Assert.True(pool.BeginBatch(changes, publication, world));
            Assert.True(pool.Advance(handle, current));
            using var header = pool.Headers.MapRange<ShadowCandidateHeader>(handle.Slot * 128, 1, BufferMapAccess.Read);
            Assert.True(header.IsMapped);
            if (header.Span[0].Status == 3)
            {
                Assert.Equal(0u, header.Span[0].Unresolved);
                return;
            }
        }
        throw new Xunit.Sdk.XunitException("The prepared native draw candidate did not complete within the bounded dispatch count.");
    }

    /// <summary>Creates a bounded candidate policy for the single loaded chunk used by this draw integration.</summary>
    private static ShadowCandidateSettings CandidateSettings() => new()
    {
        MemoryMiB = 8, HeaderCapacity = 4, SegmentsPerAssociation = 1, DedupCapacity = 64,
        ProbeLimit = 16, ChangeCapacity = 8, SlabsPerSlice = 64, CellsPerSlice = 64,
        ProbesPerSlice = 128, ChangesPerSlice = 8, SlabsPerBatch = 128,
        CellsPerBatch = 512, ProbesPerBatch = 2048, ChangesPerBatch = 32
    };

    /// <summary>Creates a one-layer writable page policy with enough operations for clear and publication controls.</summary>
    private static ShadowPageSettings PageSettings() => new()
    {
        MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, ArrayLayers = 1,
        ProbeLimit = 16, OperationsPerBatch = 16
    };

    /// <summary>Creates a finite selection budget for one candidate, one native range and one view.</summary>
    private static ShadowDrawSettings SelectionSettings() => new()
    {
        MemoryMiB = 1, ViewCapacity = 1, PairCapacity = 4, ExpansionVisits = 128,
        CommandSlots = 4, ViewInstances = 4, RasterPixels = 64,
        EstimatedTriangles = 8, EstimatedRasterSamples = 512
    };

    /// <summary>Hashes the singleton world coordinate using the production wrapping FNV lookup contract.</summary>
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

    /// <summary>Uploads a single opaque texel for the production terrain atlas sampler.</summary>
    private static GpuTexture Atlas(byte[] color)
    {
        var commands = RHICommandContext.Current;
        var texture = commands.CreateTexture(new RHITextureDescription
        {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
            Width = 1, Height = 1, Depth = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
            BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.NativeDrawAtlas"
        }).RequireResource();
        commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 1, 1),
            commands.GetTextureEncoding(texture, PixelComponentType.UnsignedByte), color);
        return texture;
    }

    /// <summary>Creates one realized ordinary native pool with the four engine terrain streams and indexed triangle.</summary>
    private sealed class NativeDrawPool : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = Vertex("Tests.Shadows.NativeDraw.Position", 3 * 3 * sizeof(float));
        private readonly GpuVbo uv = Vertex("Tests.Shadows.NativeDraw.Uv", 3 * 2 * sizeof(float));
        private readonly GpuVbo color = Vertex("Tests.Shadows.NativeDraw.Color", 3 * 4 * sizeof(byte));
        private readonly GpuVbo flags = Vertex("Tests.Shadows.NativeDraw.Flags", 3 * sizeof(uint));
        private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 3 * sizeof(uint),
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.NativeDraw.Indices"
        }).RequireResource();
        internal MeshDataPool Pool { get; }

        /// <summary>Retains every native buffer owner until the prepared adapter draw has been disposed.</summary>
        internal NativeDrawPool(int poolId, Vector3d origin)
        {
            positions.UploadData<float>([-1, -1, 0, 17, -1, 0, -1, 17, 0]);
            uv.UploadData<float>(new float[6]);
            color.UploadData<byte>([255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255]);
            flags.UploadData<uint>(new uint[3]);
            indices.UploadIndices(new uint[] { 0, 1, 2 });
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
            Attribute(0, 3, VertexAttribPointerType.Float, positions.BufferId, 12, false, false);
            Attribute(1, 2, VertexAttribPointerType.Float, uv.BufferId, 8, false, false);
            Attribute(2, 4, VertexAttribPointerType.UnsignedByte, color.BufferId, 4, true, false);
            Attribute(3, 1, VertexAttribPointerType.UnsignedInt, flags.BufferId, 4, false, true);

            var mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(mesh);
            mesh.VaoId = vao.VertexArrayId; mesh.vboIdIndex = indices.BufferId; mesh.drawMode = PrimitiveType.Triangles;
            mesh.xyzVboId = positions.BufferId; mesh.uvVboId = uv.BufferId;
            mesh.rgbaVboId = color.BufferId; mesh.flagsVboId = flags.BufferId;
            Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            Pool.VerticesPoolSize = 3; Pool.IndicesPoolSize = 3;
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, mesh);
            AccessTools.Field(typeof(MeshDataPool), "poolId").SetValue(Pool, poolId);
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(Pool, 0);
            AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(Pool,
                new Vec3i((int)origin.X, (int)origin.Y, (int)origin.Z));
            Pool.indicesGroupsCount = 1; Pool.indicesStartsByte = [0, 0]; Pool.indicesSizes = [3];
        }

        /// <summary>Releases fixture-owned native buffer names after the borrowing terrain group is retired.</summary>
        public void Dispose()
        {
            vao.Dispose(); indices.Dispose(); positions.Dispose(); uv.Dispose(); color.Dispose(); flags.Dispose();
        }

        /// <summary>Creates mutable engine-shaped vertex streams through the existing RHI buffer owner.</summary>
        private static GpuVbo Vertex(string label, int capacity) => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = capacity,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Configures the ordinary terrain VAO attribute interpretation used by the installed adapter.</summary>
        private static void Attribute(int location, int components, VertexAttribPointerType type,
            int buffer, int stride, bool normalized, bool integer)
        {
            // This fixture configures an engine-shaped foreign VAO; the RHI has no API to export its attribute layout.
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer);
            if (integer) GL.VertexAttribIPointer(location, components, VertexAttribIntegerType.UnsignedInt, stride, IntPtr.Zero);
            else GL.VertexAttribPointer(location, components, type, normalized, stride, IntPtr.Zero);
            GL.EnableVertexAttribArray(location);
        }
    }

    /// <summary>Creates a realized but noncaster native pool for testing placement without fabricated geometry.</summary>
    private sealed class PlacementPool : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        internal MeshDataPool Pool { get; }

        /// <summary>Publishes only the native mesh identity, dimension and pool origin read by terrain placement.</summary>
        internal PlacementPool(int dimension, Vector3d origin)
        {
            var mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(mesh);
            mesh.VaoId = vao.VertexArrayId;
            Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            Pool.VerticesPoolSize = 0;
            Pool.IndicesPoolSize = 0;
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, mesh);
            AccessTools.Field(typeof(MeshDataPool), "poolId").SetValue(Pool, 71);
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(Pool, dimension);
            AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(Pool,
                new Vec3i((int)origin.X, (int)origin.Y, (int)origin.Z));
        }

        /// <summary>Releases only the fixture VAO; native pool records and borrowed geometry remain non-owning.</summary>
        public void Dispose() => vao.Dispose();
    }
    #endregion
}
