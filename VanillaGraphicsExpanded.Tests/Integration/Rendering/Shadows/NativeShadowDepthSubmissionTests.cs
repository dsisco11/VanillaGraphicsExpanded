using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
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

/// <summary>Exercises complete native raster submissions and their page-table admission boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowDepthSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares integer comparison and inspection executables used to verify full GPU shadow tiles.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        programs.Prepare("compare", 2, 0);
        programs.Prepare("inspection", 2, 0);
    }

    /// <summary>Submits an actual native indexed draw and observes its atomics through finalized page lookup.</summary>
    [Fact]
    public void DepthSubmissionDrawsAndPublishesACompleteSampleablePage()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var batch = new NativeSubmissionBatch(projectionGeneration: 31);
        using var draw = batch.CreateDraw();
        var request = batch.Replacement(projectionGeneration: 31, timestamp: 0);

        // Act: the coordinator admits, clears, freezes, draws, rechecks and finalizes without selection readback.
        ShadowDepthSubmission.Submit(batch.Scene.Pages, batch.Selection, [new(0, request)], [draw],
            batch.Changes, batch.ChangeWatermark, () => draw.IsCurrent, timestamp: 0);
        Assert.True(batch.Scene.Pages.Execute(batch.Lookup(request)));
        uint[] result = Read(batch.Scene.Pages.Results, 8);
        uint written = CountWritten(batch.Scene.Pages.DepthBank(0), batch.Scene.Pages.PageSize);

        // Assert: table lookup resolves a finalized assignment and real terrain fragments changed integer depth.
        Assert.Equal(0u, result[0]);
        Assert.True(Assignment(result) > 0);
        Assert.True(written > 0);
        batch.Seal();
    }

    /// <summary>Separately proves the production player-body and held-item range can contribute finalized depth.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeEntitySceneBodyAndHeldRangesIndependentlyPublishDepth(bool bodyOpaque)
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var batch = new NativeSubmissionBatch(projectionGeneration: 36);
        using var storage = new NativeEntityStorage();
        var bodyAtlas = batch.CreateObjectAtlas(bodyOpaque ? (byte)255 : (byte)0);
        var heldAtlas = batch.CreateObjectAtlas(bodyOpaque ? (byte)0 : (byte)255);
        var bodyMesh = MeshRef(storage.Mesh, bodyAtlas.TextureId);
        var heldMesh = MeshRef(storage.Mesh, heldAtlas.TextureId);
        using var player = new NativeShadowHeldItemSnapshotTests.PlayerSnapshotFixture(false,
            bodyMesh, heldMesh, positionY: 10, firstCommand: 1, neutralWarp: true);
        Assert.Equal(2, player.Scene.Ranges.Length);
        batch.PrepareSelection(player.Scene.Ranges);
        using var terrain = batch.CreateDraw(terrainAlpha: 0);
        using var bodies = ShadowEntityShader.Create(batch.Api, waving: false);
        using var items = ShadowStaticShader.Create(batch.Api, instanced: false, waving: false);
        var objectDraws = player.Scene.Prepare(bodies, items, [batch.View], batch.Views,
            batch.Selection, batch.Scene.Pages);
        try
        {
            Assert.Equal(2, objectDraws.Length);
            Assert.All(objectDraws, draw => Assert.Same(batch.Selection, draw.Selection));
            var request = batch.Replacement(projectionGeneration: 36, timestamp: 0);

            // Act: production scene ranges drive the selector and native body/held adapters submit with the terrain interval.
            ShadowDepthSubmission.Submit(batch.Scene.Pages, batch.Selection, [new(0, request)],
                [terrain, .. objectDraws], batch.Changes, batch.ChangeWatermark,
                () => player.Scene.IsCurrent && terrain.IsCurrent && objectDraws.All(draw => draw.IsCurrent), timestamp: 0);
            Assert.True(batch.Scene.Pages.Execute(batch.Lookup(request)));
            uint written = CountWritten(batch.Scene.Pages.DepthBank(0), batch.Scene.Pages.PageSize);

            // Assert: finalized table lookup accepts the page and actual native-shaped body/held streams wrote integer depth.
            uint[] result = Read(batch.Scene.Pages.Results, 8);
            Assert.Equal(0u, result[0]);
            Assert.True(Assignment(result) > 0);
            Assert.True(written > 0);
            Assert.True(player.Scene.IsCurrent);
        }
        finally
        {
            foreach (var draw in objectDraws) draw.Dispose();
        }
        batch.Seal();
    }

    /// <summary>Omitted or duplicate intervals fail before any page clear while an earlier sample remains exact.</summary>
    [Theory]
    [InlineData(SubmissionIntervalFault.Omitted)]
    [InlineData(SubmissionIntervalFault.Duplicate)]
    public void DepthSubmissionRejectsIncompleteIntervalsBeforeClear(SubmissionIntervalFault fault)
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var batch = new NativeSubmissionBatch(projectionGeneration: 42, tilesPerBank: 2);
        long oldAssignment = batch.PublishOldPage(projectionGeneration: 41, marker: 0x6b42a19d);
        using var draw = batch.CreateDraw();
        var request = batch.Replacement(projectionGeneration: 42, timestamp: 0);
        var operations = batch.Scene.Pages.OperationsRemaining;
        IReadOnlyList<ShadowPreparedDraw> draws = fault == SubmissionIntervalFault.Omitted
            ? Array.Empty<ShadowPreparedDraw>() : [draw, draw];

        // Act: malformed CPU-owned intervals are rejected by preflight before allocation or selective clearing.
        Assert.Throws<InvalidOperationException>(() => ShadowDepthSubmission.Submit(batch.Scene.Pages,
            batch.Selection, [new(0, request)], draws, batch.Changes, batch.ChangeWatermark,
            () => draw.IsCurrent, timestamp: 0));
        uint retainedDepth = ReadDepth(batch.Scene.Pages.DepthBank(0), 0, 0);
        Assert.True(batch.Scene.Pages.Execute(batch.Lookup(batch.OldRequest)));
        uint[] retained = Read(batch.Scene.Pages.Results, 8);

        // The explicit retained-page lookup above is the sole operation after preflight; no replacement work ran.
        Assert.Equal(operations - 1, batch.Scene.Pages.OperationsRemaining);
        // Assert: failed preflight kept the prior assignment and sampled texel.
        Assert.Equal(0u, retained[0]);
        Assert.Equal(oldAssignment, Assignment(retained));
        Assert.Equal(0x6b42a19du, retainedDepth);
        batch.Seal();
    }

    /// <summary>A native range change after drawing aborts the replacement and leaves older sampled depth intact.</summary>
    [Fact]
    public void DepthSubmissionAbortsAfterNativeChangeAndPreservesOldPage()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var batch = new NativeSubmissionBatch(projectionGeneration: 52, tilesPerBank: 2);
        long oldAssignment = batch.PublishOldPage(projectionGeneration: 51, marker: 0x13c07e5b);
        using var draw = batch.CreateDraw();
        var request = batch.Replacement(projectionGeneration: 52, timestamp: 1);
        int checks = 0;
        bool InputsCurrent()
        {
            // The coordinator calls this after drawing; changing the native pool there simulates owner withdrawal.
            if (++checks == 3) batch.NativePool.Pool.VerticesPoolSize++;
            return draw.IsCurrent;
        }

        // Act: raster work reaches the replacement, then its owner changes before whole-page completion.
        Assert.Throws<InvalidOperationException>(() => ShadowDepthSubmission.Submit(batch.Scene.Pages,
            batch.Selection, [new(0, request)], [draw], batch.Changes, batch.ChangeWatermark,
            InputsCurrent, timestamp: 1));
        Assert.Equal(3, checks);
        Assert.False(draw.IsCurrent);
        Assert.True(batch.Scene.Pages.Execute(batch.Lookup(batch.OldRequest)));
        uint[] retained = Read(batch.Scene.Pages.Results, 8);
        uint retainedDepth = ReadDepth(batch.Scene.Pages.DepthBank(0), 0, 0);
        Assert.True(batch.Scene.Pages.Execute(batch.Lookup(request)));
        uint[] abandoned = Read(batch.Scene.Pages.Results, 8);

        // Assert: the changed batch is not sampleable, and the earlier page keeps its assignment and depth word.
        Assert.Equal(0u, retained[0]);
        Assert.Equal(oldAssignment, Assignment(retained));
        Assert.Equal(0x13c07e5bu, retainedDepth);
        Assert.Equal(1u, abandoned[0]);
        batch.Seal();
    }
    #endregion

    #region Private
    /// <summary>Copies production output words after the compute producer's dispatch barrier.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Combines the page assignment's low and high words without narrowing its generation.</summary>
    private static long Assignment(uint[] result) => unchecked((long)(((ulong)result[3] << 32) | result[2]));

    /// <summary>Counts non-clear integer depths over one complete physical tile on the GPU.</summary>
    private uint CountWritten(GpuTexture bank, int size)
    {
        var clear = ImageAssertions.Comparisons.CompareUnsignedConstant(bank, ShadowDepthEncoding.Clear,
            new(new(0), 0, 0, 0, size, size));
        return checked((uint)(size * size) - clear.ConstantMatches);
    }

    /// <summary>Builds a complete native table with one current three-index terrain range.</summary>
    private static ShadowWorldPublication Publication()
    {
        var table = new ShadowChunkGpu[2];
        var entry = new ShadowChunkGpu
        {
            X = 0, Y = 0, Z = 0, Dimension = 0, NativeLow = 100, LifetimeLow = 1,
            RevisionLow = 1, Flags = 3, RangeOffset = 0, RangeCount = 1
        };
        table[Hash(entry.Dimension, entry.X, entry.Y, entry.Z) & 1u] = entry;
        var envelope = ImmutableDictionary<int, ShadowBounds>.Empty.Add(0, new(0, 0, 0, 32, 32, 32));
        return new(1, true, null, ImmutableArray.Create(table),
            ImmutableArray.Create(new ShadowGeometryRange(100, 0, 3, 0, 3, 0, 1, 0)),
            envelope, table.Length) { Owner = 41 };
    }

    /// <summary>Hashes dimension-aware native coordinates using the production wrapping FNV contract.</summary>
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

    /// <summary>Maps the publication's one current range into the selector's CPU-resolved command interval.</summary>
    private static ImmutableArray<ShadowDrawRange> DrawRanges(ShadowWorldPublication publication)
    {
        var range = publication.Ranges[0];
        return ImmutableArray.Create(new ShadowDrawRange(0, 0, 1, 1, (uint)range.IndexStart,
            (uint)range.IndexCount, range.VertexStart, range.RevisionLow, range.RevisionHigh,
            (uint)range.VertexStart, (uint)range.VertexCount));
    }

    /// <summary>Creates the known light identity shared by native candidate, view and page records.</summary>
    private static ShadowSourceKey Source(ShadowSourceKind kind, long lifetime, int dimension = 1) =>
        new(1, dimension, kind, lifetime, lifetime, 0);

    /// <summary>Freezes one directional raster view covering the page submitted by this fixture.</summary>
    private static ShadowDrawView View(ShadowSourceKey source, long generation,
        ShadowRasterRegion region, Vector3d origin)
    {
        var projection = Projection(source, generation, 0, 32);
        return ShadowDrawView.Directional(projection, 0, region, region, origin);
    }

    /// <summary>Fits one directional raster to the test chunk and preserves its requested virtual X interval.</summary>
    private static DirectionalShadowProjection Projection(ShadowSourceKey source, long generation, double minX, double maxX) =>
        new(source, generation, Vector3d.UnitZ, new(minX, 0, 0, maxX, 32, 32), 4, 1, 8);

    /// <summary>Uses finite aggregate bounds for the one prepared native range and one virtual view.</summary>
    private static ShadowDrawSettings DrawSettings() => new()
    {
        MemoryMiB = 1, ViewCapacity = 1, PairCapacity = 4, ExpansionVisits = 128,
        CommandSlots = 4, ViewInstances = 4, RasterPixels = 64,
        EstimatedTriangles = 8, EstimatedRasterSamples = 512
    };

    /// <summary>Wraps one realized native VAO as a single texture-backed submesh.</summary>
    private static MultiTextureMeshRef MeshRef(VAO mesh, int texture)
    {
        var result = (MultiTextureMeshRef)RuntimeHelpers.GetUninitializedObject(typeof(MultiTextureMeshRef));
        result.meshrefs = [mesh]; result.textureids = [texture];
        return result;
    }

    /// <summary>Reads one atlas integer texel to prove that an omitted draw did not clear a sampled page.</summary>
    private static uint ReadDepth(GpuTexture texture, int x, int y)
    {
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
        var encoding = new RHIPixelEncoding(PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt);
        using var request = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(texture,
            new(new(0), x, y, 0, 1, 1, 1), encoding));
        Assert.Equal(CompletionStatus.Ready, request.Wait());
        using var mapped = request.MapReadyReadback();
        return System.Runtime.InteropServices.MemoryMarshal.Read<uint>(mapped.Bytes);
    }

    /// <summary>Builds a minimal native client camera required by the installed terrain material snapshot.</summary>
    private static ClientPlayer Camera()
    {
        var entity = new EntityPlayer();
        entity.CameraPos.Set(0, 0, 0);
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var playerData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(playerData, entity);
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(player, playerData);
        return player;
    }

    /// <summary>Selects which CPU-known interval fault is injected before any replacement operation.</summary>
    public enum SubmissionIntervalFault { Omitted, Duplicate }

    /// <summary>Owns native-shaped animated vertex streams that the production body and held adapters borrow.</summary>
    private sealed class NativeEntityStorage : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = VertexBuffer("Tests.Shadows.SceneEntity.Positions");
        private readonly GpuVbo uv = VertexBuffer("Tests.Shadows.SceneEntity.Uv");
        private readonly GpuVbo flags = VertexBuffer("Tests.Shadows.SceneEntity.Flags");
        private readonly GpuVbo joints = VertexBuffer("Tests.Shadows.SceneEntity.Joints");
        private readonly GpuEbo indices = IndexBuffer("Tests.Shadows.SceneEntity.Indices");
        internal VAO Mesh { get; }

        /// <summary>Creates two native indexed triangles with every vertex attached to joint zero.</summary>
        internal NativeEntityStorage()
        {
            positions.UploadData<float>([-8, -8, 0, 8, -8, 0, -8, 8, 0, 8, 8, 0]);
            uv.UploadData<float>([0, 0, 1, 0, 0, 1, 1, 1]);
            flags.UploadData<int>(new int[4]); joints.UploadData<int>(new int[4]);
            indices.UploadIndices(new uint[] { 0, 1, 2, 2, 1, 3 });
            Mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(Mesh);
            Mesh.VaoId = vao.VertexArrayId; Mesh.vboIdIndex = indices.BufferId; Mesh.IndicesCount = 6;
            Mesh.drawMode = PrimitiveType.Triangles; Mesh.xyzVboId = positions.BufferId;
            Mesh.uvVboId = uv.BufferId; Mesh.flagsVboId = flags.BufferId;
            Mesh.customDataIntVboId = joints.BufferId;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            indices.Dispose(); joints.Dispose(); flags.Dispose(); uv.Dispose(); positions.Dispose(); vao.Dispose();
        }

        /// <summary>Allocates one native-compatible vertex stream whose size is fixed by its upload.</summary>
        private static GpuVbo VertexBuffer(string label) => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Allocates one native-compatible element stream whose size is fixed by its upload.</summary>
        private static GpuEbo IndexBuffer(string label) => RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();
    }

    /// <summary>Owns a complete candidate, page interval, native indexed terrain pool and prepared material draw.</summary>
    private sealed class NativeSubmissionBatch : IDisposable
    {
        private readonly BinaryShaderApiFixture assets = new();
        private readonly List<GpuTexture> objectAtlases = [];
        private readonly ImmutableArray<ShadowDrawRange> ranges;
        private readonly ShadowDrawSettings drawSettings = DrawSettings();
        private readonly ShadowDrawView view;
        private readonly ShadowWorldPublication publication;
        private readonly ShadowSourceKey source;
        private ShadowPageRequest? oldRequest;
        private bool sealedBatch;
        private NativeShadowTerrainGroup? group;
        private ShadowTerrainShader? shader;
        private GpuTexture? atlas;

        #region Internal API
        /// <summary>Exposes the production GPU candidate and page owners used by the submission under test.</summary>
        internal SubmissionScene Scene { get; }
        /// <summary>Owns the uploaded view records and their bounded offscreen render target.</summary>
        internal ShadowViewBuffer Views { get; private set; } = null!;
        /// <summary>Owns one realized ordinary native mesh and the engine-shaped pool metadata.</summary>
        internal NativeDrawPool NativePool { get; }
        /// <summary>Owns the immutable candidate-to-range/view selection for the open page interval.</summary>
        internal ShadowGeometrySelection Selection { get; private set; } = null!;
        /// <summary>Exposes the packaged binary shader API fixture to native scene material owners.</summary>
        internal ICoreClientAPI Api => assets.Api;
        /// <summary>Exposes the stable scene watermark captured for page completion checks.</summary>
        internal ShadowSceneChanges Changes { get; } = new(8);
        /// <summary>Captures a complete, empty current change interval for this native fixture.</summary>
        internal ShadowChangeBatch ChangeWatermark { get; }
        /// <summary>Exposes the current view-to-candidate association for the draw selection.</summary>
        internal ShadowViewCandidates Candidate { get; }
        /// <summary>Exposes the frozen projection and exact source generation for page requests.</summary>
        internal ShadowDrawView View => view;
        /// <summary>Exposes the old certified page identity used by retained-depth cases.</summary>
        internal ShadowPageRequest OldRequest => oldRequest ?? throw new InvalidOperationException("No retained old page was published.");

        /// <summary>Creates a bounded GPU fixture with one complete current native range and a current candidate.</summary>
        internal NativeSubmissionBatch(long projectionGeneration, int tilesPerBank = 1)
        {
            source = Source(ShadowSourceKind.Sun, 71, dimension: 0);
            publication = Publication();
            Scene = new SubmissionScene(assets.Api, publication, tilesPerBank);
            Candidate = Scene.Candidate(source, projectionGeneration);
            view = View(source, projectionGeneration, new(0, 0, 8, 8), Vector3d.Zero);
            ranges = DrawRanges(publication);
            NativePool = new(100, Vector3d.Zero);
            ChangeWatermark = Changes.Capture(0, 1);
        }

        /// <summary>Creates a fresh prepared native terrain draw over this batch's exact selector interval.</summary>
        internal NativeShadowTerrainDraw CreateDraw(byte terrainAlpha = 255)
        {
            EnsureSelection();
            var nativeWorld = new Mock<IClientWorldAccessor>();
            nativeWorld.SetupGet(value => value.Player).Returns(Camera());
            var preparedAtlas = atlas ?? Atlas(terrainAlpha);
            atlas = preparedAtlas;
            var preparedGroup = new NativeShadowTerrainGroup(NativePool.Pool, EnumChunkRenderPass.Opaque, 0, preparedAtlas.TextureId);
            group = preparedGroup;
            try
            {
                var placement = new NativeShadowTerrainPlacement(preparedGroup, nativeWorld.Object, view.Origin, .016f);
                var preparedShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
                shader = preparedShader;
                try
                {
                    return new(preparedGroup, placement, new DefaultShaderUniforms { PlayerPos = new Vec3f(0, 0, 0) },
                        preparedShader, view, Views, 0, Selection, new(0, 0, 1), Scene.Pages, 0, 0);
                }
                catch { preparedShader.Dispose(); throw; }
            }
            catch { preparedGroup.Dispose(); throw; }
        }

        /// <summary>Creates an independently controlled alpha atlas for one captured native scene owner.</summary>
        internal GpuTexture CreateObjectAtlas(byte alpha)
        {
            var texture = Atlas(alpha);
            objectAtlases.Add(texture);
            return texture;
        }

        /// <summary>Builds the current selector from exact native scene ranges as well as terrain's current ranges.</summary>
        internal void PrepareSelection(ImmutableArray<ShadowObjectDrawRange> objects)
        {
            if (Selection is not null) throw new InvalidOperationException("The batch selection is already frozen.");
            EnsureSelection(objects);
        }

        /// <summary>Captures current page-batch selectors only after any seeded old page has fenced and retired.</summary>
        private void EnsureSelection(ImmutableArray<ShadowObjectDrawRange> objects = default)
        {
            if (Selection is not null) return;
            Views = new([view], Scene.Pages, new ShadowDrawSettings
            {
                MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 64
            });
            Selection = new(assets.Api, [view], [Candidate], Scene.Candidates, publication,
                Scene.WorldBuffers, ranges, 4, Scene.Pages, drawSettings, otherDrawingBytes: 0, objects: objects);
        }

        /// <summary>Publishes an older complete tile, then opens a new page interval and writes a known depth witness.</summary>
        internal long PublishOldPage(long projectionGeneration, uint marker)
        {
            var old = Replacement(projectionGeneration, 0);
            oldRequest = old;
            Assert.True(Scene.Pages.Execute(old));
            uint[] allocation = Read(Scene.Pages.Results, 8);
            Assert.Equal(0u, allocation[0]);
            long assignment = Assignment(allocation);
            Assert.True(assignment > 0);
            Assert.True(Scene.Pages.ClearReplacement());
            Assert.True(Scene.Pages.FreezeWritableAssignments());
            Scene.Pages.FinishReplacements(complete: true, worldRevision: 1, sceneEpoch: 1,
                sceneSequence: 0, timestamp: 0);
            Assert.Equal(0u, Read(Scene.Pages.Results, 8)[0]);
            Scene.Pages.SealBatch();
            RHICommandContext.Current.WaitIdle();
            Scene.Pages.Poll();
            Assert.True(Scene.Pages.BeginBatch());
            RHICommandContext.Current.ClearTexture(Scene.Pages.DepthBank(0), new(new(0), 0, 0, 0,
                Scene.Pages.DepthBank(0).Width, Scene.Pages.DepthBank(0).Height, 1),
                new(PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt), BitConverter.GetBytes(marker));
            return assignment;
        }

        /// <summary>Builds the exact selected replacement certificate and projection requested by one virtual page.</summary>
        internal ShadowPageRequest Replacement(long projectionGeneration, double timestamp)
        {
            var key = new ShadowPageKey(source, projectionGeneration, 0, 0, 0);
            return new(ShadowPageOperation.Allocate, key, new(0, 0, 0, 8, 8, 8),
                1, 1, 0, Timestamp: timestamp,
                Projection: ShadowProjectionSnapshot.Directional(Projection(source, projectionGeneration, 0, 32), 0));
        }

        /// <summary>Builds a lookup for a previously allocated projection while retaining its original page bounds.</summary>
        internal ShadowPageRequest Lookup(ShadowPageRequest request) => new(ShadowPageOperation.Lookup,
            request.Key, request.Region, request.WorldRevision, request.SceneEpoch, request.SceneSequence,
            Timestamp: request.Timestamp, Projection: request.Projection);

        /// <summary>Seals all submitted GPU owners before the helper releases native or private inputs.</summary>
        internal void Seal()
        {
            if (sealedBatch) return;
            if (Selection is not null) Selection.Seal();
            if (Views is not null) Views.Seal();
            if (Scene.Pages.CanSubmit) Scene.Pages.SealBatch();
            RHICommandContext.Current.WaitIdle();
            if (Selection is not null) Selection.Poll();
            if (Views is not null) Views.Poll();
            Scene.Pages.Poll();
            sealedBatch = true;
        }
        #endregion

        #region Public API
        /// <inheritdoc />
        public void Dispose()
        {
            if (!sealedBatch) Seal();
            if (Selection is not null) Selection.Dispose();
            if (Views is not null) Views.Dispose();
            shader?.Dispose(); atlas?.Dispose();
            foreach (var objectAtlas in objectAtlases) objectAtlas.Dispose();
            group?.Dispose();
            Scene.Dispose(); NativePool.Dispose(); assets.Dispose();
        }
        #endregion

        #region Private
        /// <summary>Creates the production opaque atlas input used by the captured material bindings.</summary>
        private static GpuTexture Atlas(byte alpha = 255)
        {
            var rhi = RHICommandContext.Current;
            var texture = rhi.CreateTexture(new RHITextureDescription
            {
                Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
                Width = 1, Height = 1, Depth = 1, MipLevels = 1,
                Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
                BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.DepthSubmissionAtlas"
            }).RequireResource();
            rhi.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 1, 1),
                rhi.GetTextureEncoding(texture, PixelComponentType.UnsignedByte), [255, 255, 255, alpha]);
            return texture;
        }
        #endregion
    }

    /// <summary>Owns one production world upload, complete candidate and page batch for native submission checks.</summary>
    private sealed class SubmissionScene : IDisposable
    {
        private readonly ShadowWorldPublication publication;
        private readonly ShadowCandidateChanges changes = new(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        internal ShadowWorldGpuBuffers WorldBuffers { get; } = new();
        internal ShadowCandidatePool Candidates { get; }
        internal ShadowPagePool Pages { get; }

        /// <summary>Uploads the complete world and opens bounded candidate/page owners.</summary>
        internal SubmissionScene(Vintagestory.API.Common.ICoreAPI api,
            ShadowWorldPublication publication, int tilesPerBank)
        {
            this.publication = publication;
            Assert.True(WorldBuffers.Upload(publication));
            Candidates = ShadowCandidatePool.Create(api, new ShadowCandidateSettings
            {
                MemoryMiB = 8, HeaderCapacity = 4, SegmentsPerAssociation = 1, DedupCapacity = 64,
                ProbeLimit = 16, ChangeCapacity = 8, SlabsPerSlice = 64, CellsPerSlice = 64,
                ProbesPerSlice = 128, ChangesPerSlice = 8, SlabsPerBatch = 128,
                CellsPerBatch = 512, ProbesPerBatch = 2048, ChangesPerBatch = 32
            });
            Assert.True(Candidates.BeginBatch(changes, publication, WorldBuffers));
            Pages = ShadowPagePool.Create(api, new ShadowPageSettings
            {
                MemoryMiB = 8, PageSize = 8, TileColumns = tilesPerBank, TileRows = 1,
                BankCapacity = 1, ProbeLimit = 64, OperationsPerBatch = 32
            });
            Assert.True(Pages.BeginBatch());
        }

        /// <summary>Builds and completes the production GPU candidate for one native terrain receiver domain.</summary>
        internal ShadowViewCandidates Candidate(ShadowSourceKey source, long generation)
        {
            var domain = new ShadowChunkDomain(0, 0, 0, 0, 0, 0, 0);
            var receiver = domain.Bounds;
            var request = ShadowCandidateRequest.Directional(source, publication.Owner, publication.Generation,
                1, domain, receiver, Vector3d.UnitZ, 179.9);
            Assert.True(Candidates.TryAllocate(request, out var handle));
            var current = new ShadowCandidateCurrent(source, publication.Owner, publication.Generation, 1,
                receiver, Vector3d.UnitZ, changes.Epoch, changes.ToSequence, generation);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Assert.True(Candidates.BeginBatch(changes, publication, WorldBuffers));
                Assert.True(Candidates.Advance(handle, current));
                using var header = Candidates.Headers.MapRange<ShadowCandidateHeader>(handle.Slot * 128, 1, BufferMapAccess.Read);
                Assert.True(header.IsMapped);
                if (header.Span[0].Status == 3)
                {
                    Assert.Equal(0u, header.Span[0].Unresolved);
                    return new(0, handle, current);
                }
            }
            throw new Xunit.Sdk.XunitException("The native submission candidate did not complete within its bounded dispatch count.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Candidates.Dispose(); WorldBuffers.Dispose(); Pages.Dispose();
        }
    }

    /// <summary>Owns engine-shaped native terrain streams until the borrowing group and prepared draw retire.</summary>
    private sealed class NativeDrawPool : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = Vertex("Tests.Shadows.Submission.Position", 36);
        private readonly GpuVbo uv = Vertex("Tests.Shadows.Submission.Uv", 24);
        private readonly GpuVbo colors = Vertex("Tests.Shadows.Submission.Color", 12);
        private readonly GpuVbo flags = Vertex("Tests.Shadows.Submission.Flags", 12);
        private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 12,
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.Submission.Indices"
        }).RequireResource();
        internal MeshDataPool Pool { get; }

        /// <summary>Realizes three native vertices, all four terrain streams and one indexed triangle.</summary>
        internal NativeDrawPool(int poolId, Vector3d origin)
        {
            positions.UploadData<float>([-1, -1, 0, 17, -1, 0, -1, 17, 0]);
            uv.UploadData<float>(new float[6]);
            colors.UploadData<byte>([255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255]);
            flags.UploadData<uint>(new uint[3]);
            indices.UploadIndices(new uint[] { 0, 1, 2 });
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            // The engine MeshRef needs a native VAO, and the RHI has no foreign-VAO setup/export API.
            GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
            Attribute(0, 3, VertexAttribType.Float, positions.BufferId, 12, false, false);
            Attribute(1, 2, VertexAttribType.Float, uv.BufferId, 8, false, false);
            Attribute(2, 4, VertexAttribType.UnsignedByte, colors.BufferId, 4, true, false);
            Attribute(3, 1, VertexAttribType.UnsignedInt, flags.BufferId, 4, false, true);
            var mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(mesh);
            mesh.VaoId = vao.VertexArrayId; mesh.vboIdIndex = indices.BufferId; mesh.drawMode = PrimitiveType.Triangles;
            mesh.xyzVboId = positions.BufferId; mesh.uvVboId = uv.BufferId;
            mesh.rgbaVboId = colors.BufferId; mesh.flagsVboId = flags.BufferId;
            Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            Pool.VerticesPoolSize = 3; Pool.IndicesPoolSize = 3;
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, mesh);
            AccessTools.Field(typeof(MeshDataPool), "poolId").SetValue(Pool, poolId);
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(Pool, 0);
            AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(Pool,
                new Vec3i((int)origin.X, (int)origin.Y, (int)origin.Z));
            Pool.indicesGroupsCount = 1; Pool.indicesStartsByte = [0, 0]; Pool.indicesSizes = [3];
        }

        /// <inheritdoc />
        public void Dispose() { vao.Dispose(); indices.Dispose(); positions.Dispose(); uv.Dispose(); colors.Dispose(); flags.Dispose(); }

        /// <summary>Creates one mutable engine-shaped vertex stream with its exact three-vertex extent.</summary>
        private static GpuVbo Vertex(string label, int capacity) => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = capacity,
            Usage = BufferUsage.StaticDraw, Label = label
        }).RequireResource();

        /// <summary>Configures one realized native VAO attribute without transferring VAO ownership.</summary>
        private void Attribute(int location, int components, VertexAttribType type,
            int buffer, int stride, bool normalized, bool integer)
        {
            // Configure the engine-shaped foreign VAO required by the native MeshRef boundary.
            // One binding index per attribute prevents each later stream from replacing the prior stream's source.
            GL.VertexArrayVertexBuffer(vao.VertexArrayId, location, buffer, IntPtr.Zero, stride);
            if (integer) GL.VertexArrayAttribIFormat(vao.VertexArrayId, location, components, (VertexAttribIType)type, 0);
            else GL.VertexArrayAttribFormat(vao.VertexArrayId, location, components, type, normalized, 0);
            GL.VertexArrayAttribBinding(vao.VertexArrayId, location, location);
            GL.EnableVertexArrayAttrib(vao.VertexArrayId, location);
        }
    }
    #endregion
}
