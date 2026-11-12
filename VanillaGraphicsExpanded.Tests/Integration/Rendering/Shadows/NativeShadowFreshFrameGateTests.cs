using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
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
using Vintagestory.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Runs the frame gate through a fresh native chunk publication and real GPU traversal/raster path.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShadowFreshFrameGateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepares the GPU comparison program used to count non-clear depth without copying the tile to the CPU.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs) => programs.Prepare("compare", 2, 0);

    /// <summary>A real current native terrain scene reaches complete page publication before readiness becomes available.</summary>
    [Fact]
    public void FreshNativeSceneDrawCompletesBeforeGatePublishesReadiness()
    {
        // Arrange: construct the scene from one loaded native chunk whose indexed pool is in its actual pass table.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var native = new FreshNativeSceneFixture();
        using var pages = ShadowPagePool.Create(assets.Api, Settings().Pages);
        using var worldBuffers = new ShadowWorldGpuBuffers();
        using var candidates = ShadowCandidatePool.Create(assets.Api, Settings().Candidates);
        Assert.True(worldBuffers.Upload(native.Publication));
        var changes = native.Scene.Changes;
        var watermark = changes.Capture(0, 1);
        using var terrainShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        using var bodyShader = ShadowEntityShader.Create(assets.Api, waving: false);
        using var itemShader = ShadowStaticShader.Create(assets.Api, instanced: false, waving: false);
        using var gate = new NativeShadowFrameGate(assets.Api, Settings(), pages);
        var budget = gate.BeginFrame(checked(worldBuffers.AllocatedBytes + candidates.AllocatedBytes));
        Assert.True(candidates.BeginBatch(new(native.Publication.Generation, 0, 0, true, []),
            native.Publication, worldBuffers, budget));
        var candidate = native.CompleteCandidate(candidates, worldBuffers, budget);
        var source = native.Source;
        var projection = native.Projection;
        var page = native.DemandPage;
        var frameLight = new ShadowFrameLight(source, 10, false, true,
            [new(0, true, [page], [native.View], [candidate], 0.01)],
            ShadowProjectionSnapshot.Directional(projection, 0));
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 10 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        var input = new NativeShadowFrameInput(native.Lights(1), native.Scene, native.Publication, worldBuffers,
            candidates, changes, watermark, [frameLight], [surface], native.ClientWorld,
            (storage, adaptive) => terrainShader, bodyShader, itemShader, .016f, 0, 0, 0);

        // Act: let the gate validate scene ownership, perform native clear/raster/completion, and publish readiness.
        gate.Update(input, now: 0.1, budget);

        // Assert: ready status comes from the completed GPU page while native scene and owner remain current.
        var receipt = Readiness(gate, 1);
        Assert.True(native.Scene.IsCurrent);
        Assert.True(receipt[12] == 1u,
            $"Fresh native page did not become ready (status={receipt[12]}, reason={receipt[13]}, " +
            $"writable={gate.Diagnostics!.WritablePages}, failure={gate.Diagnostics.Failure}, " +
            $"decisions={gate.Diagnostics.Decisions}, rejection={gate.Diagnostics.BudgetRejectionCause}).");
        Assert.Equal(1, gate.Diagnostics!.WritablePages);
        Assert.Null(gate.Diagnostics.Failure);
        var tile = PhysicalTile(pages, native.DemandPage.Request);
        var written = ImageAssertions.Comparisons.CompareUnsignedConstant(pages.DepthArray,
            ShadowDepthEncoding.Clear, new(new(tile.Layer), tile.X * pages.PageSize, tile.Y * pages.PageSize,
                0, pages.PageSize, pages.PageSize));
        Assert.True(written.Mismatches > 0);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);
        gate.Poll();
    }

    /// <summary>Captures one complete native frame from the production source and passes its exact owners to the gate.</summary>
    [Fact]
    public void ProductionFrameSourceTransfersCompleteCurrentDemandAndSceneToGate()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var native = new FreshNativeSceneFixture();
        var settings = SourceSettings();
        using var pages = ShadowPagePool.Create(assets.Api, settings.Pages);
        using var terrainShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        using var bodyShader = ShadowEntityShader.Create(assets.Api, waving: false);
        using var itemShader = ShadowStaticShader.Create(assets.Api, instanced: false, waving: false);
        var sourceApi = ApiWithWorld(assets, native.ClientWorld);
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Create(sourceApi);
        using var receiver = new CompleteReceiverOwner();
        var preparation = new NativeShadowEntityPreparation(8, native.Changes);
        preparation.Begin(native.Entities);
        preparation.End(nativeFailed: false);
        using var gate = new NativeShadowFrameGate(assets.Api, settings, pages);
        double sourceNow = 0;
        using var source = new NativeShadowFrameSource(sourceApi, native.Renderer, native.Culler, native.WorldOwner,
            preparation, MaterialAtlasSystem.Instance, native.Uniforms, native.Changes, receiver, settings, false,
            () => sourceNow, (_, _) => terrainShader, bodyShader, itemShader, gate.Scheduler);
        try
        {
            NativeShadowFrameInput? readyInput = null;
            uint[]? readyReceipt = null;
            ShadowFramePublication? retainedPublication = null;
            for (long frame = 1; frame <= 8; frame++)
            {
                sourceNow = frame * 0.02;
                var lights = SourceLights(frame);
                var budget = gate.BeginFrame(source.LiveAndRetiringBytes, lights.Frame);

                // Capture uses the real source owners and returns one immutable light interval plus its native scene.
                var input = source.Capture(lights, .016f, budget);
                Assert.Same(lights, input.Lights);
                var capturedScene = input.Scene ?? throw new InvalidOperationException("Production frame capture omitted its native scene.");
                Assert.Same(native.Changes, input.Changes);
                Assert.Same(capturedScene.Changes, input.Changes);
                Assert.Equal(ShadowSurfaceCategory.All, receiver.LastPublication!.CompleteCategories);
                Assert.Equal(7, receiver.LastPublication.Regions.Length);
                Assert.Equal(2, receiver.LastPublication.Regions.Count(region => region.Category == ShadowSurfaceCategory.Transparent));
                Assert.Equal(3, input.Sources.Length);
                Assert.Contains(input.Sources, light => light.Source.Kind == ShadowSourceKind.Sun);
                Assert.Contains(input.Sources, light => light.Source.Kind == ShadowSourceKind.Moon);
                Assert.Contains(input.Sources, light => light.Source.Kind == ShadowSourceKind.Block);
                AssertGeneratedDemandContainsEveryReceiver(input, receiver.LastPublication.Regions);

                // The gate consumes this same capture; no test-built replacement input or scene is substituted.
                gate.Update(input, now: sourceNow, budget);
                Assert.True(capturedScene.IsCurrent);
                var receipt = Readiness(gate, input.Sources.Length);
                if (input.Sources.Select((light, index) => receipt[index * 32 + 12] == 1u).All(ready => ready))
                {
                    var publication = gate.Publication
                        ?? throw new InvalidOperationException("The complete current frame has no published consumer snapshot.");
                    Assert.Same(lights, publication.Lights);
                    Assert.Equal(lights.Frame, publication.Lights.Frame);
                    retainedPublication = publication;
                    readyInput = input;
                    readyReceipt = receipt;
                    break;
                }
                gate.EndFrame();
                Assert.Null(gate.Publication);
            }

            Assert.NotNull(readyInput);
            Assert.NotNull(readyReceipt);
            var capturedInput = readyInput ?? throw new InvalidOperationException("No complete current source frame was published.");
            var capturedReceipt = readyReceipt ?? throw new InvalidOperationException("Current GPU readiness receipt is unavailable.");
            var priorPublication = retainedPublication ?? throw new InvalidOperationException("The first complete frame was not published.");
            Assert.Equal(3, capturedInput.Sources.Length);
            for (int index = 0; index < capturedInput.Sources.Length; index++)
            {
                Assert.Equal(1u, capturedReceipt[index * 32 + 12]);
                int detail = checked((int)capturedReceipt[index * 32 + 14]);
                var selected = Assert.Single(capturedInput.Sources[index].Alternatives, alternative => alternative.Detail == detail);
                bool wroteDepth = false;
                foreach (var page in selected.Pages)
                {
                    var tile = PhysicalTile(pages, page.Request);
                    if (CountWritten(pages, tile) > 0) { wroteDepth = true; break; }
                }
                Assert.True(wroteDepth, $"The selected {capturedInput.Sources[index].Source.Kind} page set has no native depth writes.");
            }
            Assert.True(gate.Diagnostics!.WritablePages > 0);

            // Preserve the actual GPU-selected detail's point pages before a bounded movement within candidate coverage.
            int pointSourceIndex = -1;
            for (int index = 0; index < capturedInput.Sources.Length; index++)
                if (capturedInput.Sources[index].Source.Kind == ShadowSourceKind.Block) { pointSourceIndex = index; break; }
            Assert.InRange(pointSourceIndex, 0, capturedInput.Sources.Length - 1);
            var pointSource = capturedInput.Sources[pointSourceIndex];
            int selectedDetail = checked((int)capturedReceipt[pointSourceIndex * 32 + 14]);
            var priorPointAlternative = Assert.Single(pointSource.Alternatives, alternative => alternative.Detail == selectedDetail);
            var oldPointPages = priorPointAlternative.Pages;
            Assert.NotEmpty(oldPointPages);
            long oldProjectionGeneration = oldPointPages[0].Request.Key.Projection;
            Assert.All(oldPointPages, page => Assert.Equal(oldProjectionGeneration, page.Request.Key.Projection));
            var oldPointTiles = new List<(ShadowDemandPage Page, int Slot, uint AssignmentLow, uint AssignmentHigh,
                (int Layer, int X, int Y) Tile, GpuImageSnapshot Snapshot)>();

            try
            {
                foreach (var page in oldPointPages)
                {
                    var mapping = PhysicalMapping(pages, page.Request);
                    oldPointTiles.Add((page, mapping.Slot, mapping.AssignmentLow, mapping.AssignmentHigh,
                        mapping.Tile, new GpuImageSnapshot(pages.DepthArray, TileRegion(pages, mapping.Tile))));
                }
                gate.EndFrame();
                Assert.False(pages.CanSubmit);
                Assert.Null(gate.Publication);
                Assert.False(priorPublication.IsCurrent);

                long movedFrame = capturedInput.Lights.Frame + 1;
                sourceNow = movedFrame * 0.02;
                var movedLights = SourceLights(movedFrame, pointDisplacement: 0.25f);
                var movedBudget = gate.BeginFrame(source.LiveAndRetiringBytes, movedLights.Frame);
                Assert.Null(gate.Publication);
                Assert.False(priorPublication.IsCurrent);
                var movedInput = source.Capture(movedLights, .016f, movedBudget);
                Assert.Same(movedLights, movedInput.Lights);
                Assert.Same(native.Changes, movedInput.Changes);
                var movedScene = movedInput.Scene ?? throw new InvalidOperationException("Moved source capture omitted its native scene.");
                Assert.Same(movedScene.Changes, movedInput.Changes);
                var movedPoint = movedInput.Sources[pointSourceIndex];
                Assert.Equal(ShadowSourceKind.Block, movedPoint.Source.Kind);
                var movedAlternative = Assert.Single(movedPoint.Alternatives, alternative => alternative.Detail == selectedDetail);
                Assert.NotEmpty(movedAlternative.Pages);
                Assert.NotEqual(oldProjectionGeneration, movedAlternative.Pages[0].Request.Key.Projection);
                Assert.NotEmpty(movedAlternative.RetainedPages);
                Assert.Contains(movedAlternative.RetainedPages,
                    page => page.Request.Key.Projection == oldProjectionGeneration);
                AssertGeneratedDemandContainsEveryReceiver(movedInput, receiver.LastPublication!.Regions);

                // Only the changed point mapping is rerendered; the previous generation remains read-only stale coverage.
                gate.Update(movedInput, now: sourceNow, movedBudget);
                var movedPublication = gate.Publication
                    ?? throw new InvalidOperationException("The moved current frame has no published consumer snapshot.");
                Assert.Same(movedLights, movedPublication.Lights);
                Assert.Equal(movedLights.Frame, movedPublication.Lights.Frame);
                Assert.False(priorPublication.IsCurrent);
                Assert.True(movedPublication.IsCurrent);
                var movedReceipt = Readiness(gate, movedInput.Sources.Length);
                int movedSelectedDetail = checked((int)movedReceipt[pointSourceIndex * 32 + 14]);
                var movedSelectedAlternative = Assert.Single(movedPoint.Alternatives,
                    alternative => alternative.Detail == movedSelectedDetail);
                Assert.NotEmpty(movedSelectedAlternative.Pages);
                for (int index = 0; index < movedInput.Sources.Length; index++)
                {
                    var status = movedReceipt[index * 32 + 12];
                    Assert.Contains(status, new uint[] { 1u, 2u });
                    if (movedInput.Sources[index].Source.Kind is ShadowSourceKind.Sun or ShadowSourceKind.Moon)
                        Assert.Equal(1u, status);
                }

                foreach (var old in oldPointTiles)
                {
                    var lookup = LookupResult(pages, old.Page.Request);
                    Assert.Equal(2u, lookup[0]); // Stale projections deliberately do not expose their resident slot.
                    uint[] metadata = PageMetadata(pages, old.Slot);
                    Assert.Equal(2u, metadata[3]); // Existing resident mapping remains retained as stale coverage.
                    Assert.Equal(old.Tile.Layer, checked((int)metadata[0]));
                    Assert.Equal(old.Tile.X, checked((int)metadata[1]));
                    Assert.Equal(old.Tile.Y, checked((int)metadata[2]));
                    Assert.Equal(old.AssignmentLow, metadata[4]);
                    Assert.Equal(old.AssignmentHigh, metadata[5]);
                    var preserved = ImageAssertions.Comparisons.CompareHistory(pages.DepthArray, old.Snapshot,
                        TileRegion(pages, old.Tile), bitwise: true);
                    Assert.Equal(0u, preserved.Mismatches);
                }
                gate.EndFrame();
                Assert.False(pages.CanSubmit);
                Assert.Null(gate.Publication);
                Assert.False(priorPublication.IsCurrent);
                gate.Poll();
            }
            finally
            {
                foreach (var old in oldPointTiles) old.Snapshot.Dispose();
            }
        }
        finally
        {
            gate.EndFrame();
            VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Dispose(sourceApi);
        }
    }

    /// <summary>A missing participating receiver family rejects capture before it can become gate input.</summary>
    [Fact]
    public void ProductionFrameSourceRejectsIncompleteReceiverCategories()
    {
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var native = new FreshNativeSceneFixture();
        var sourceApi = ApiWithWorld(assets, native.ClientWorld);
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Create(sourceApi);
        var settings = SourceSettings();
        using var receiver = new CompleteReceiverOwner(includeTransparent: false);
        var preparation = new NativeShadowEntityPreparation(8, native.Changes);
        preparation.Begin(native.Entities);
        preparation.End(nativeFailed: false);
        using var terrainShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        using var bodyShader = ShadowEntityShader.Create(assets.Api, waving: false);
        using var itemShader = ShadowStaticShader.Create(assets.Api, instanced: false, waving: false);
        using var pages = ShadowPagePool.Create(assets.Api, settings.Pages);
        using var gate = new NativeShadowFrameGate(assets.Api, settings, pages);
        using var source = new NativeShadowFrameSource(sourceApi, native.Renderer, native.Culler, native.WorldOwner,
            preparation, MaterialAtlasSystem.Instance, native.Uniforms, native.Changes, receiver, settings, false,
            () => 0.1, (_, _) => terrainShader, bodyShader, itemShader, gate.Scheduler);
        try
        {
            var budget = new ShadowFrameBudget(ShadowWork.Limits(settings), source.LiveAndRetiringBytes);
            var error = Assert.Throws<InvalidOperationException>(() => source.Capture(SourceLights(1), .016f, budget));
            Assert.Contains("complete current early receiver bounds", error.Message);
            Assert.Equal(ShadowSurfaceCategory.All & ~ShadowSurfaceCategory.Transparent,
                receiver.LastPublication!.CompleteCategories);
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Dispose(sourceApi);
        }
    }

    /// <summary>A native range withdrawn after capture prevents fresh raster admission and leaves readiness unavailable.</summary>
    [Fact]
    public void WithdrawnNativeRangeCannotPublishFreshReadiness()
    {
        // Arrange: capture the same complete native scene, then change its borrowed pool before gate admission.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var native = new FreshNativeSceneFixture();
        using var pages = ShadowPagePool.Create(assets.Api, Settings().Pages);
        using var worldBuffers = new ShadowWorldGpuBuffers();
        using var candidates = ShadowCandidatePool.Create(assets.Api, Settings().Candidates);
        Assert.True(worldBuffers.Upload(native.Publication));
        var changes = native.Scene.Changes;
        var watermark = changes.Capture(0, 1);
        using var terrainShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        using var bodyShader = ShadowEntityShader.Create(assets.Api, waving: false);
        using var itemShader = ShadowStaticShader.Create(assets.Api, instanced: false, waving: false);
        using var gate = new NativeShadowFrameGate(assets.Api, Settings(), pages);
        var budget = gate.BeginFrame(checked(worldBuffers.AllocatedBytes + candidates.AllocatedBytes));
        Assert.True(candidates.BeginBatch(new(native.Publication.Generation, 0, 0, true, []),
            native.Publication, worldBuffers, budget));
        var candidate = native.CompleteCandidate(candidates, worldBuffers, budget);
        var source = native.Source;
        var frameLight = new ShadowFrameLight(source, 10, false, true,
            [new(0, true, [native.DemandPage], [native.View], [candidate], 0.01)],
            ShadowProjectionSnapshot.Directional(native.Projection, 0));
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 10 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        var input = new NativeShadowFrameInput(native.Lights(2), native.Scene, native.Publication, worldBuffers,
            candidates, changes, watermark, [frameLight], [surface], native.ClientWorld,
            (storage, adaptive) => terrainShader, bodyShader, itemShader, .016f, 0, 0, 0);
        native.Storage.Pool.VerticesPoolSize++;
        bool sceneWasWithdrawnBeforeAdmission = !native.Scene.IsCurrent;

        // Act: the gate sees the revoked native scene before it can clear or draw a replacement page.
        gate.Update(input, now: 0.2, budget);

        // Assert: stale scene ownership cannot certify a page, even though a complete candidate was already available.
        var receipt = Readiness(gate, 1);
        Assert.True(sceneWasWithdrawnBeforeAdmission);
        Assert.Equal(0u, receipt[12]);
        Assert.Equal(0, gate.Diagnostics!.WritablePages);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);
    }

    /// <summary>A native range change after raster keeps the prior complete page stale and rejects replacement readiness.</summary>
    [Fact]
    public void NativeRangeMutationAfterDrawRetainsPriorPageAsStale()
    {
        // Arrange: publish one complete native page, then prepare a new scene and projection for a replacement.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var native = new FreshNativeSceneFixture();
        using var pages = ShadowPagePool.Create(assets.Api, Settings().Pages);
        using var worldBuffers = new ShadowWorldGpuBuffers();
        using var candidates = ShadowCandidatePool.Create(assets.Api, Settings().Candidates);
        Assert.True(worldBuffers.Upload(native.Publication));
        var changes = native.Scene.Changes;
        var watermark = changes.Capture(0, 1);
        using var terrainShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        using var bodyShader = ShadowEntityShader.Create(assets.Api, waving: false);
        using var itemShader = ShadowStaticShader.Create(assets.Api, instanced: false, waving: false);
        using var gate = new NativeShadowFrameGate(assets.Api, Settings(), pages);
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 10 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);

        var firstBudget = gate.BeginFrame(checked(worldBuffers.AllocatedBytes + candidates.AllocatedBytes));
        Assert.True(candidates.BeginBatch(new(native.Publication.Generation, 0, 0, true, []),
            native.Publication, worldBuffers, firstBudget));
        var firstCandidate = native.CompleteCandidate(candidates, worldBuffers, firstBudget, native.Projection);
        var firstPage = native.DemandPage;
        var firstLight = FrameLight(native, firstCandidate, firstPage, native.View, native.Projection);
        gate.Update(Input(native, native.Scene, native.Lights(1), worldBuffers, candidates, changes,
            watermark, firstLight, surface, terrainShader, bodyShader, itemShader), now: 0.1, firstBudget);
        var firstReceipt = Readiness(gate, 1);
        Assert.Equal(1u, firstReceipt[12]);
        var priorTile = PhysicalTile(pages, firstPage.Request);
        uint priorDepthCount = CountWritten(pages, priorTile);
        Assert.True(priorDepthCount > 0);
        var priorTileRegion = TileRegion(pages, priorTile);
        using var priorDepthSnapshot = new GpuImageSnapshot(pages.DepthArray, priorTileRegion);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);

        // A completed first submission makes its mapping independently retainable before the next frame starts.
        RHICommandContext.Current.WaitIdle();
        gate.Poll();
        var secondScene = native.CaptureScene();
        var secondWatermark = changes.Capture(watermark.Sequence, watermark.Epoch);
        var secondProjection = native.ProjectionFor(2);
        var secondView = native.ViewFor(secondProjection);
        var secondPage = native.DemandPageFor(secondProjection, secondWatermark);
        var stalePage = firstPage with
        { Current = ShadowProjectionSnapshot.Directional(secondProjection, 0) };
        var secondBudget = gate.BeginFrame(checked(worldBuffers.AllocatedBytes + candidates.AllocatedBytes));
        Assert.True(candidates.BeginBatch(new(native.Publication.Generation, 0, 0, true, []),
            native.Publication, worldBuffers, secondBudget));
        var secondCandidate = native.CompleteCandidate(candidates, worldBuffers, secondBudget, secondProjection);
        var secondLight = FrameLight(native, secondCandidate, secondPage, secondView, secondProjection, [stalePage]);
        using var mutation = new MutateNativeRangeAfterTerrainDraw(native.Storage);

        // Act: the Harmony postfix mutates the borrowed native pool only after Submit has issued its indirect draw.
        gate.Update(Input(native, secondScene, native.Lights(2, secondProjection), worldBuffers, candidates, changes,
            secondWatermark, secondLight, surface, terrainShader, bodyShader, itemShader), now: 0.16, secondBudget);
        var failedRefreshReceipt = Readiness(gate, 1);
        var replacementLookup = LookupResult(pages, secondPage.Request);
        var priorTileAfterFailure = CountWritten(pages, priorTile);
        var priorTileComparison = ImageAssertions.Comparisons.CompareHistory(pages.DepthArray,
            priorDepthSnapshot, priorTileRegion, bitwise: true);

        // Assert: one actual native draw preceded withdrawal; no new mapping publishes and old complete depth stays stale.
        Assert.True(mutation.SubmitCount == 1,
            $"The second native Submit was not reached (submits={mutation.SubmitCount}, " +
            $"readiness={failedRefreshReceipt[12]}, reason={failedRefreshReceipt[13]}, " +
            $"failure={gate.Diagnostics!.Failure}, decisions={gate.Diagnostics.Decisions}, " +
            $"budgetRejection={gate.Diagnostics.BudgetRejectionCause}).");
        Assert.False(secondScene.IsCurrent);
        Assert.Equal(2u, failedRefreshReceipt[12]);
        Assert.Equal(0, gate.Diagnostics!.WritablePages);
        Assert.StartsWith("Refresh deferred:", gate.Diagnostics.Failure);
        Assert.NotEqual(0u, replacementLookup[0]);
        Assert.True(priorTileAfterFailure > 0);
        Assert.Equal(0u, priorTileComparison.Mismatches);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);
        RHICommandContext.Current.WaitIdle();
        gate.Poll();
    }

    /// <summary>A caster beyond a receiver-fitted directional near plane reaches the page at clamped near depth.</summary>
    [Fact]
    public void DirectionalCasterBeyondReceiverNearPlaneWritesClampedDepth()
    {
        // Arrange: put the only native geometry two chunks toward the light while the projection fits chunk zero.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var native = new FreshNativeSceneFixture(casterBeyondReceiverNear: true);
        using var pages = ShadowPagePool.Create(assets.Api, Settings().Pages);
        using var worldBuffers = new ShadowWorldGpuBuffers();
        using var candidates = ShadowCandidatePool.Create(assets.Api, Settings().Candidates);
        Assert.True(worldBuffers.Upload(native.Publication));
        var changes = native.Scene.Changes;
        var watermark = changes.Capture(0, 1);
        using var terrainShader = ShadowTerrainShader.Create(assets.Api, storage: false, waving: false);
        using var bodyShader = ShadowEntityShader.Create(assets.Api, waving: false);
        using var itemShader = ShadowStaticShader.Create(assets.Api, instanced: false, waving: false);
        using var gate = new NativeShadowFrameGate(assets.Api, Settings(), pages);
        var budget = gate.BeginFrame(checked(worldBuffers.AllocatedBytes + candidates.AllocatedBytes));
        Assert.True(candidates.BeginBatch(new(native.Publication.Generation, 0, 0, true, []),
            native.Publication, worldBuffers, budget));
        var candidate = native.CompleteCandidate(candidates, worldBuffers, budget, native.Projection);
        var page = native.DemandPage;
        var light = FrameLight(native, candidate, page, native.View, native.Projection);
        var surface = new ShaderProgram { PassName = "standard", ProgramId = 10 };
        ShaderCapabilities.Publish(surface, ShaderCapability.SharedVertexDeformation);
        double receiverCenterDepth = native.Projection.Project(new(16, 16, 16), 0).Z;
        double casterDepth = native.Projection.Project(new(16, 16, 64), 0).Z;

        // Act: submit the actual chunk-two mesh through the same native terrain and page-writing gate.
        gate.Update(Input(native, native.Scene, native.Lights(1), worldBuffers, candidates, changes,
            watermark, light, surface, terrainShader, bodyShader, itemShader), now: 0.1, budget);
        var readiness = Readiness(gate, 1);
        var tile = PhysicalTile(pages, page.Request);
        var written = CountWritten(pages, tile);
        var clampedNearDepth = ImageAssertions.Comparisons.CompareUnsignedConstant(pages.DepthArray,
            BitConverter.SingleToUInt32Bits(0f), TileRegion(pages, tile));

        // Assert: receiver depth is interior while off-range caster fragments exist at clamped depth zero.
        Assert.InRange(receiverCenterDepth, 0, 1);
        Assert.True(casterDepth < 0);
        Assert.Equal(1u, readiness[12]);
        Assert.True(written > 0);
        Assert.True(clampedNearDepth.ConstantMatches > 0);
        gate.EndFrame();
        Assert.False(pages.CanSubmit);
    }
    #endregion

    #region Private
    /// <summary>Creates finite policies for one native command, one candidate and one replacement tile.</summary>
    private static ShadowSettings Settings() => new()
    {
        Scheduling = new ShadowSchedulingSettings
        {
            Enabled = true, MemoryMiB = 48, LightCapacity = 1, DemandRequests = 8,
            CoverageChecks = 4096, RecoveryCompleteFrames = 1, GpuTargetMilliseconds = 100,
            DirectionalMemoryShare = 1, DirectionalWorkShare = 1, MaxShadowedLocalLights = 0,
            MaximumDetailReductions = 0
        },
        Candidates = new ShadowCandidateSettings
        {
            MemoryMiB = 8, HeaderCapacity = 4, SegmentsPerAssociation = 1, DedupCapacity = 64,
            ProbeLimit = 16, ChangeCapacity = 8, SlabsPerSlice = 64, CellsPerSlice = 64,
            ProbesPerSlice = 128, ChangesPerSlice = 8, SlabsPerBatch = 128,
            CellsPerBatch = 512, ProbesPerBatch = 2048, ChangesPerBatch = 32
        },
        Drawing = new ShadowDrawSettings
        {
            MemoryMiB = 8, ViewCapacity = 1, PairCapacity = 4, ExpansionVisits = 4096,
            CommandSlots = 4, ViewInstances = 4, RasterPixels = 64,
            EstimatedTriangles = 8, EstimatedRasterSamples = 512,
            GroupVisits = 256, EntityVisits = 8, RangeVisits = 8
        },
        Pages = new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 3, TileRows = 1,
            ArrayLayers = 1, ProbeLimit = 32, OperationsPerBatch = 32
        },
        Temporal = new ShadowTemporalSettings
        { MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = .1, MaxDirectionalAngleDegrees = 1 }
    };

    /// <summary>Expands bounded source and pool capacities for two celestial and one local light owner.</summary>
    private static ShadowSettings SourceSettings()
    {
        var settings = Settings();
        settings.Scheduling.LightCapacity = 3;
        settings.Scheduling.MaxShadowedLocalLights = 1;
        settings.Scheduling.ProjectionHistoryCapacity = 8;
        settings.Scheduling.MaximumDetailReductions = 1;
        settings.Scheduling.DirectionalTexelMetres = 1;
        settings.Scheduling.PointFaceResolution = 64;
        settings.Scheduling.DemandRequests = 16384;
        settings.Inputs.ChunkCapacity = 8;
        settings.Inputs.RangeCapacity = 16;
        settings.Candidates.SlabsPerBatch = 256;
        settings.Drawing.ViewCapacity = 16;
        settings.Drawing.PairCapacity = 16;
        settings.Drawing.CommandSlots = 16;
        settings.Drawing.ViewInstances = 16;
        settings.Drawing.ExpansionVisits = 16384;
        settings.Drawing.EstimatedTriangles = 64;
        settings.Drawing.EstimatedRasterSamples = 65536;
        settings.Drawing.RasterPixels = 65536;
        settings.Pages.TileColumns = 128;
        settings.Pages.TileRows = 16;
        settings.Pages.OperationsPerBatch = 4096;
        settings.Pages.ProbeLimit = 128;
        settings.Scheduling.CoverageChecks = 262144;
        settings.Temporal.MaxPointTranslationMetres = 0.5;
        return settings;
    }

    /// <summary>Provides distinct current celestial and point sources under an invertible world/view convention.</summary>
    private static ShadowLightSnapshot SourceLights(long frame, float pointDisplacement = 0)
    {
        var sun = new ShadowSourceKey(1, 0, ShadowSourceKind.Sun, 71, 71, 0);
        var moon = new ShadowSourceKey(1, 0, ShadowSourceKind.Moon, 72, 72, 0);
        var local = new ShadowSourceKey(1, 0, ShadowSourceKind.Block, 73, 73, 0, 0, 0, 0);
        return new(frame, 1, 0,
            [new(local, new(16 + pointDisplacement, 16, 16), new(1, 1, 1, 64))],
            new(sun, Vector3.UnitZ, Vector3.One), new(moon, -Vector3.UnitZ, Vector3.One),
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], 0, 0, 0, Vector3.Zero);
    }

    /// <summary>Extends the shader test API with the exact native client-world owner required by capture.</summary>
    private static Vintagestory.API.Client.ICoreClientAPI ApiWithWorld(BinaryShaderApiFixture assets,
        IClientWorldAccessor world)
    {
        var api = DispatchProxy.Create<Vintagestory.API.Client.ICoreClientAPI, BinaryShaderApiFixture.AssetApiProxy>();
        ((BinaryShaderApiFixture.AssetApiProxy)(object)api).InvokeMethod = (method, arguments) =>
            method.Name == "get_World" ? world : method.Invoke(assets.Api, arguments);
        return api;
    }

    /// <summary>Reads the fixed per-light readiness record after its producer barrier.</summary>
    private static uint[] Readiness(NativeShadowFrameGate gate, int lights)
    {
        using var mapped = gate.Readiness.MapRange<uint>(0, lights * 32, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Builds one direct gate input from the real native fixture and packaged shadow executables.</summary>
    private static NativeShadowFrameInput Input(FreshNativeSceneFixture native, NativeShadowScene scene,
        ShadowLightSnapshot lights, ShadowWorldGpuBuffers worldBuffers, ShadowCandidatePool candidates,
        ShadowSceneChanges changes, ShadowChangeBatch watermark, ShadowFrameLight light, ShaderProgram surface,
        ShadowTerrainShader terrainShader, ShadowEntityShader bodyShader, ShadowStaticShader itemShader) =>
        new(lights, scene, native.Publication, worldBuffers, candidates, changes, watermark, [light], [surface],
            native.ClientWorld, (storage, adaptive) => terrainShader, bodyShader, itemShader, .016f, 0, 0, 0);

    /// <summary>Creates a complete current page and optional previously rendered page for one coherent light offer.</summary>
    private static ShadowFrameLight FrameLight(FreshNativeSceneFixture native, ShadowViewCandidates candidate,
        ShadowDemandPage page, ShadowDrawView view, DirectionalShadowProjection projection,
        ImmutableArray<ShadowDemandPage> retained = default)
    {
        var alternative = new ShadowFrameAlternative(0, true, [page], [view], [candidate], 0.01, retained);
        return new(native.Source, 10, false, true, [alternative],
            ShadowProjectionSnapshot.Directional(projection, 0));
    }

    /// <summary>Counts non-clear integer texels on the GPU within the assigned array tile.</summary>
    private uint CountWritten(ShadowPagePool pages, (int Layer, int X, int Y) tile)
    {
        var result = ImageAssertions.Comparisons.CompareUnsignedConstant(pages.DepthArray,
            ShadowDepthEncoding.Clear, TileRegion(pages, tile));
        return result.Mismatches;
    }

    /// <summary>Addresses one physical array tile for copy and GPU comparison operations.</summary>
    private static RHITextureRegion TileRegion(ShadowPagePool pages, (int Layer, int X, int Y) tile) =>
        new(new(tile.Layer), tile.X * pages.PageSize, tile.Y * pages.PageSize, 0, pages.PageSize, pages.PageSize);

    /// <summary>Uses the page lookup receipt to identify the assigned physical tile without reading depth texels.</summary>
    private static (int Layer, int X, int Y) PhysicalTile(ShadowPagePool pages, ShadowPageRequest request)
    {
        var mapping = PhysicalMapping(pages, request);
        return mapping.Tile;
    }

    /// <summary>Captures the currently addressable page slot and its GPU-assigned physical tile.</summary>
    private static (int Slot, uint AssignmentLow, uint AssignmentHigh, (int Layer, int X, int Y) Tile)
        PhysicalMapping(ShadowPagePool pages, ShadowPageRequest request)
    {
        uint[] words = LookupResult(pages, request);
        Assert.True(words[0] == 0u && words[1] < checked((uint)pages.Capacity),
            $"Page lookup did not return a current slot (reason={words[0]}, slot={words[1]}, capacity={pages.Capacity}).");
        int slot = checked((int)words[1]);
        uint[] metadata = PageMetadata(pages, slot);
        return (slot, words[2], words[3],
            (checked((int)metadata[0]), checked((int)metadata[1]), checked((int)metadata[2])));
    }

    /// <summary>Reads only physical address, resident state, and assignment words from one known compact page slot.</summary>
    private static uint[] PageMetadata(ShadowPagePool pages, int slot)
    {
        Assert.InRange(slot, 0, pages.Capacity - 1);
        using var entry = pages.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 6, BufferMapAccess.Read);
        Assert.True(entry.IsMapped);
        return entry.Span.ToArray();
    }

    /// <summary>Reads the compact GPU lookup status for one virtual page key.</summary>
    private static uint[] LookupResult(ShadowPagePool pages, ShadowPageRequest request)
    {
        Assert.True(pages.Execute(request with { Operation = ShadowPageOperation.Lookup }));
        using var result = pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(result.IsMapped);
        return result.Span.ToArray();
    }

    /// <summary>Confirms each category has a projected-page request and both transparent layers extend demand.</summary>
    private static void AssertGeneratedDemandContainsEveryReceiver(NativeShadowFrameInput input,
        ImmutableArray<ShadowReceiverRegion> receivers)
    {
        var opaqueBounds = receivers.Where(receiver => receiver.Category != ShadowSurfaceCategory.Transparent)
            .Select(receiver => receiver.Bounds).Aggregate(Union);
        var paddedOpaqueBounds = Expand(opaqueBounds, 8);
        foreach (var receiver in receivers.Where(receiver => receiver.Category == ShadowSurfaceCategory.Transparent))
            Assert.False(paddedOpaqueBounds.Contains(receiver.Bounds),
                "Transparent test layers must extend beyond the non-transparent bounds plus the configured maximum request padding.");

        foreach (var light in input.Sources)
        {
            var pages = light.Alternatives[0].Pages;
            Assert.NotEmpty(pages);
            foreach (var receiver in receivers)
                Assert.Contains(pages, page => page.Request.Region.Contains(receiver.Bounds));
        }
    }

    /// <summary>Combines projected page receiver enclosures for complete-source coverage assertions.</summary>
    private static ShadowBounds Union(ShadowBounds a, ShadowBounds b) => new(
        Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Min(a.MinZ, b.MinZ),
        Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY), Math.Max(a.MaxZ, b.MaxZ));

    /// <summary>Expands a world-space receiver envelope by the maximum fixture PCF and bias footprint.</summary>
    private static ShadowBounds Expand(ShadowBounds bounds, double padding) => new(
        bounds.MinX - padding, bounds.MinY - padding, bounds.MinZ - padding,
        bounds.MaxX + padding, bounds.MaxY + padding, bounds.MaxZ + padding);

    /// <summary>Injects one native pool mutation only after the production terrain draw method returns.</summary>
    private sealed class MutateNativeRangeAfterTerrainDraw : IDisposable
    {
        private static NativeTerrainStorage? activeStorage;
        private static int submitCount;
        private readonly Harmony harmony = new("VGE.Tests.NativeShadowFreshFrameGate.AfterTerrainDraw");

        /// <summary>Reports completed native Submit calls observed by the postfix.</summary>
        internal int SubmitCount => Volatile.Read(ref submitCount);

        /// <summary>Hooks the actual prepared native terrain draw and retains the target until unpatch.</summary>
        internal MutateNativeRangeAfterTerrainDraw(NativeTerrainStorage storage)
        {
            if (Interlocked.CompareExchange(ref activeStorage, storage, null) is not null)
                throw new InvalidOperationException("A native terrain draw mutation hook is already active.");
            Volatile.Write(ref submitCount, 0);
            var target = AccessTools.Method(typeof(NativeShadowTerrainDraw), nameof(NativeShadowTerrainDraw.Submit))
                ?? throw new MissingMethodException(typeof(NativeShadowTerrainDraw).FullName, "Submit");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(MutateNativeRangeAfterTerrainDraw), nameof(AfterSubmit)));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            Interlocked.Exchange(ref activeStorage, null);
        }

        /// <summary>Runs after the actual RHI indirect draw and changes the captured pool extent once.</summary>
        private static void AfterSubmit()
        {
            var storage = Volatile.Read(ref activeStorage);
            if (storage is not null && Interlocked.Increment(ref submitCount) == 1)
                storage.Pool.VerticesPoolSize++;
        }
    }

    /// <summary>Owns the real map chunk, uploaded terrain storage and production native scene certificates.</summary>
    private sealed class FreshNativeSceneFixture : IDisposable
    {
        private readonly NativeShadowWorldAdapter worldOwner;
        private readonly Dictionary<long, EntityRenderer> entities = new();
        private readonly ShadowSceneChanges changes = new(8);
        private readonly NativeShadowEntityPreparation preparation;
        private readonly GpuTexture atlas;
        private readonly ChunkRenderer renderer;
        private readonly DefaultShaderUniforms uniforms;
        private readonly ShadowSettings settings = Settings();
        private readonly bool casterBeyondReceiverNear;

        /// <summary>Provides the complete live coordinate table captured from the native world owner.</summary>
        internal ShadowWorldPublication Publication { get; }
        /// <summary>Provides the native scene whose terrain range and chunk lifetime share the publication.</summary>
        internal NativeShadowScene Scene { get; }
        /// <summary>Provides the exact native mesh-pool range shared by the world and terrain adapters.</summary>
        internal NativeTerrainStorage Storage { get; }
        /// <summary>Supplies the native client-world camera required by terrain placement.</summary>
        internal IClientWorldAccessor ClientWorld { get; }
        /// <summary>Provides the exact current native membership used to prepare the source-owned entity scene.</summary>
        internal IReadOnlyDictionary<long, EntityRenderer> Entities => entities;
        /// <summary>Shares the same sequence/epoch stream with source preparation and gate admission.</summary>
        internal ShadowSceneChanges Changes => changes;
        /// <summary>Exposes the real render-pass table used to capture a fresh source-owned native scene.</summary>
        internal ChunkRenderer Renderer => renderer;
        /// <summary>Exposes the bootstrap owner whose live publication backs native chunk drawing.</summary>
        internal NativeShadowWorldAdapter WorldOwner => worldOwner;
        /// <summary>Exposes the current deformation uniforms used by native terrain capture.</summary>
        internal DefaultShaderUniforms Uniforms => uniforms;
        /// <summary>Exposes the engine detail thresholds read by the production source.</summary>
        internal FrustumCulling Culler { get; }
        /// <summary>Provides the one directional source used by candidate and page certificates.</summary>
        internal ShadowSourceKey Source { get; }
        /// <summary>Provides the frozen full-domain directional projection for the fixture's one chunk.</summary>
        internal DirectionalShadowProjection Projection { get; }
        /// <summary>Provides the virtual view matching the one physical demand page.</summary>
        internal ShadowDrawView View { get; }
        /// <summary>Provides the complete page request and current projection consumed by the gate.</summary>
        internal ShadowDemandPage DemandPage { get; }

        /// <summary>Creates the loaded chunk first, then captures native world and terrain owners over its pool range.</summary>
        internal FreshNativeSceneFixture(bool casterBeyondReceiverNear = false)
        {
            this.casterBeyondReceiverNear = casterBeyondReceiverNear;
            var map = CreateMap();
            int casterChunkZ = casterBeyondReceiverNear ? 2 : 0;
            if (casterBeyondReceiverNear)
            {
                NativeChunks(map).Add(map.ChunkIndex3D(0, 0, 0, 0), CreateChunk([]));
                NativeChunks(map).Add(map.ChunkIndex3D(0, 0, 1, 0), CreateChunk([]));
            }
            long chunkIndex = map.ChunkIndex3D(0, 0, casterChunkZ, 0);
            var chunk = CreateChunk([CreateLocation(37, 0, 3, 0, 3, 0)]);
            NativeChunks(map).Add(chunkIndex, chunk);
            worldOwner = new(map, 4, 16, 8, 16);
            Publication = worldOwner.Capture();
            if (!Publication.Complete) throw new InvalidOperationException(Publication.Failure);

            Storage = new(37, new(0, 0, casterChunkZ * 32));
            atlas = CreateAtlas();
            renderer = CreateRenderer(Storage.Pool, atlas.TextureId);
            Culler = (FrustumCulling)RuntimeHelpers.GetUninitializedObject(typeof(FrustumCulling));
            SetField(typeof(FrustumCulling), Culler, "lod0BiasSq", 4096f);
            SetField(typeof(FrustumCulling), Culler, "lod2BiasSq", 4096d);
            uniforms = new DefaultShaderUniforms
            {
                PlayerPos = new Vec3f(), TimeCounter = 1, WindWaveCounter = 2,
                WindWaveCounterHighFreq = 3, WaterWaveCounter = 4, WindSpeed = 0,
                GlobalWorldWarp = 0, GlitchWaviness = 0, WindWaveIntensity = 0,
                WaterWaveIntensity = 0, PerceptionEffectIntensity = 0
            };
            var cameraEntity = new EntityPlayer();
            cameraEntity.CameraPos.Set(0, 0, 0);
            var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
            var playerData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
            AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(playerData, cameraEntity);
            AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(player, playerData);
            var clientWorld = new Mock<IClientWorldAccessor>();
            clientWorld.SetupGet(value => value.Player).Returns(player);
            ClientWorld = clientWorld.Object;

            Source = new(1, 0, ShadowSourceKind.Sun, 71, 71, 0);
            Projection = ProjectionFor(1);
            View = ViewFor(Projection);
            preparation = new(8, changes);
            preparation.Begin(entities);
            preparation.End(nativeFailed: false);
            Scene = CaptureScene();
            var sceneChanges = changes.Capture(0, 1);
            DemandPage = DemandPageFor(Projection, sceneChanges);
        }

        /// <summary>Captures new per-frame native terrain and entity owners over the same current world publication.</summary>
        internal NativeShadowScene CaptureScene()
        {
            var detail = new NativeShadowTerrainDetail(0, 0, 1, 1);
            var terrain = new NativeShadowTerrainScene(renderer, worldOwner, Publication,
                detail, MaterialAtlasSystem.Instance, uniforms, waving: false, settings.Drawing);
            try
            {
                var entityScene = new NativeShadowEntityScene(preparation, settings.Drawing,
                    terrain.Drawing.CommandCount, settings.Drawing.ViewCapacity);
                return new(terrain, entityScene);
            }
            catch { terrain.Dispose(); throw; }
        }

        /// <summary>Builds another valid immutable mapping generation for the same native chunk and source.</summary>
        internal DirectionalShadowProjection ProjectionFor(long generation) =>
            new(Source, generation, TowardLight(generation), new(0, 0, 0, 32, 32, 32), 4, 1, 8);

        /// <summary>Creates the whole-page view for one directional mapping generation.</summary>
        internal ShadowDrawView ViewFor(DirectionalShadowProjection projection) =>
            ShadowDrawView.Directional(projection, 0, new(0, 0, 8, 8), new(0, 0, 8, 8), Vector3d.Zero);

        /// <summary>Builds complete current page demand for a mapping while preserving native chunk coverage.</summary>
        internal ShadowDemandPage DemandPageFor(DirectionalShadowProjection projection, ShadowChangeBatch sceneChanges)
        {
            var request = new ShadowPageRequest(ShadowPageOperation.Allocate,
                new(Source, projection.Generation, 0, 0, 0), new(0, 0, 0, 8, 8, 8),
                Publication.Generation, sceneChanges.Epoch, sceneChanges.Sequence,
                Projection: ShadowProjectionSnapshot.Directional(projection, 0));
            return new(request, ShadowProjectionSnapshot.Directional(projection, 0));
        }

        /// <summary>Returns a domain containing the native receiver partition and every loaded caster partition.</summary>
        private ShadowChunkDomain CandidateDomain => new(0, 0, 0, 0, 0, 0,
            casterBeyondReceiverNear ? 2 : 0);

        /// <summary>Creates and completes one GPU candidate association against the actual loaded chunk domain.</summary>
        internal ShadowViewCandidates CompleteCandidate(ShadowCandidatePool candidates,
            ShadowWorldGpuBuffers worldBuffers, ShadowFrameBudget budget,
            DirectionalShadowProjection? projection = null)
        {
            var domain = CandidateDomain;
            var receiver = new ShadowBounds(0, 0, 0, 1, 1, 1);
            var direction = -(projection ?? Projection).Forward;
            var request = ShadowCandidateRequest.Directional(Source, Publication.Owner, Publication.Generation,
                1, domain, receiver, direction, 179.9, Publication.Generation, 0);
            if (!candidates.TryAllocate(request, out var handle))
                throw new InvalidOperationException("The bounded fixture could not allocate its native candidate.");
            var current = new ShadowCandidateCurrent(Source, Publication.Owner, Publication.Generation, 1,
                receiver, direction, Publication.Generation, 0, (projection ?? Projection).Generation);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (!candidates.BeginBatch(new(Publication.Generation, 0, 0, true, []), Publication,
                        worldBuffers, budget) || !candidates.Advance(handle, current))
                    throw new InvalidOperationException("The native candidate traversal could not advance.");
                using var header = candidates.Headers.MapRange<ShadowCandidateHeader>(handle.Slot * 128, 1, BufferMapAccess.Read);
                if (!header.IsMapped) throw new InvalidOperationException("Candidate traversal status is unavailable.");
                if (header.Span[0].Status == 3 && header.Span[0].Unresolved == 0)
                    return new(0, handle, current);
            }
            throw new InvalidOperationException("The bounded native candidate did not complete.");
        }

        /// <summary>Provides the one coherent light snapshot and world identity for the fixture.</summary>
        internal ShadowLightSnapshot Lights(long frame, DirectionalShadowProjection? projection = null)
        {
            var direction = projection is null ? Vector3.UnitZ : new Vector3(
                (float)-projection.Forward.X, (float)-projection.Forward.Y, (float)-projection.Forward.Z);
            return new(frame, 1, 0, [], new(Source, direction, Vector3.One), default,
            ImmutableArray.CreateRange(new double[16]), 0, 0, 0, Vector3.Zero);
        }

        /// <summary>Changes the solar direction slightly in generation two while remaining inside stale-angle bounds.</summary>
        private static Vector3d TowardLight(long generation) => generation == 1
            ? Vector3d.UnitZ : new(0.001, 0, 1);

        /// <inheritdoc />
        public void Dispose()
        {
            Scene.Dispose();
            worldOwner.Dispose();
            Storage.Dispose();
            atlas.Dispose();
        }

        /// <summary>Builds a minimally initialized native map with its production coordinate codec.</summary>
        private static ClientWorldMap CreateMap()
        {
            var map = (ClientWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldMap));
            SetField(typeof(ClientWorldMap), map, "chunks", new Dictionary<long, ClientChunk>(4));
            SetField(typeof(ClientWorldMap), map, "chunksLock", new object());
            SetField(typeof(WorldMap), map, "index3dMulX", 1024);
            SetField(typeof(WorldMap), map, "index3dMulZ", 1024 * 1024);
            return map;
        }

        /// <summary>Creates the current chunk-owned center and edge range arrays used by world publication.</summary>
        private static ClientChunk CreateChunk(ModelDataPoolLocation[] centers)
        {
            var chunk = (ClientChunk)RuntimeHelpers.GetUninitializedObject(typeof(ClientChunk));
            SetField(typeof(ClientChunk), chunk, "centerModelPoolLocations", centers);
            SetField(typeof(ClientChunk), chunk, "edgeModelPoolLocations", Array.Empty<ModelDataPoolLocation>());
            return chunk;
        }

        /// <summary>Creates one chunk range whose pool/index/vertex extent matches the realized native indexed mesh.</summary>
        private static ModelDataPoolLocation CreateLocation(int pool, int indexStart, int indexEnd,
            int vertexStart, int vertexEnd, int detail)
        {
            var location = new ModelDataPoolLocation();
            SetField(typeof(ModelDataPoolLocation), location, "PoolId", pool);
            SetField(typeof(ModelDataPoolLocation), location, "IndicesStart", indexStart);
            SetField(typeof(ModelDataPoolLocation), location, "IndicesEnd", indexEnd);
            SetField(typeof(ModelDataPoolLocation), location, "VerticesStart", vertexStart);
            SetField(typeof(ModelDataPoolLocation), location, "VerticesEnd", vertexEnd);
            SetField(typeof(ModelDataPoolLocation), location, "LodLevel", detail);
            return location;
        }

        /// <summary>Builds the native render-pass array with this test's actual mesh-pool owner.</summary>
        private static ChunkRenderer CreateRenderer(MeshDataPool pool, int texture)
        {
            int passCount = (int)EnumChunkRenderPass.Liquid + 1;
            var renderer = (ChunkRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkRenderer));
            var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
            platform.UseSSBOs = false;
            NativeShadowTerrainFields.Platform(renderer) = platform;
            var table = new MeshDataPoolManager[passCount][];
            var manager = (MeshDataPoolManager)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
            NativeShadowTerrainFields.Pools(manager) = [pool];
            // BlendNoCull is a native cutout-capable caster category; the white alpha texel keeps this triangle visible.
            table[(int)EnumChunkRenderPass.BlendNoCull] = [manager];
            NativeShadowTerrainFields.Table(renderer) = table;
            NativeShadowTerrainFields.Textures(renderer) = [texture];
            return renderer;
        }

        /// <summary>Creates a real one-texel alpha atlas for the native cutout terrain shader.</summary>
        private static GpuTexture CreateAtlas()
        {
            var commands = RHICommandContext.Current;
            var texture = commands.CreateTexture(new RHITextureDescription
            {
                Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
                Width = 1, Height = 1, Depth = 1, MipLevels = 1,
                Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
                BaseLevel = 0, MaxLevel = 0, Label = "Tests.Shadows.FreshGate.CutoutAtlas"
            }).RequireResource();
            commands.UploadTexture(texture, new(new(0, 0), 0, 0, 0, 1, 1),
                commands.GetTextureEncoding(texture, PixelComponentType.UnsignedByte),
                new byte[] { 255, 255, 255, 255 });
            return texture;
        }

        /// <summary>Returns the native map dictionary used to bootstrap the production world adapter.</summary>
        private static Dictionary<long, ClientChunk> NativeChunks(ClientWorldMap map) =>
            (Dictionary<long, ClientChunk>)AccessTools.Field(typeof(ClientWorldMap), "chunks").GetValue(map)!;

        /// <summary>Sets only engine-owned native metadata fields needed to build a representative loaded chunk.</summary>
        private static void SetField(Type owner, object target, string name, object value) =>
            (owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(owner.FullName, name)).SetValue(target, value);
    }

    /// <summary>Supplies all early receiving families, including two independently contributing transparent layers.</summary>
    private sealed class CompleteReceiverOwner : IShadowSurfaceReceiverSource, IDisposable
    {
        private readonly bool includeTransparent;
        private readonly ImmutableArray<ShaderProgramBase> programs;

        /// <summary>Retains the exact bounded publication that the current source capture consumed.</summary>
        internal ShadowSurfacePublication? LastPublication { get; private set; }

        /// <summary>Declares complete owner coverage while providing current conservative bounds for every layer.</summary>
        internal CompleteReceiverOwner(bool includeTransparent = true)
        {
            this.includeTransparent = includeTransparent;
            var result = ImmutableArray.CreateBuilder<ShaderProgramBase>(6);
            for (int index = 0; index < 6; index++)
            {
                var program = new ShaderProgram { PassName = $"shadow-receiver-{index}", ProgramId = 100 + index };
                ShaderCapabilities.Publish(program, ShaderCapability.SharedVertexDeformation);
                result.Add(program);
            }
            programs = result.MoveToImmutable();
        }

        /// <summary>Publishes one frame-matched receiver set or deliberately omits transparent completion for rejection.</summary>
        public ShadowSurfacePublication Capture(ShadowLightSnapshot lights, ShadowFrameBudget budget)
        {
            if (!budget.TryCharge(new(Requests: 7)))
                throw new InvalidOperationException("The test receiver publication exceeded its request allowance.");
            var regions = ImmutableArray.CreateBuilder<ShadowReceiverRegion>(7);
            regions.Add(new(ShadowSurfaceCategory.Opaque, new(0, 0, 0, 4, 4, 4)));
            regions.Add(new(ShadowSurfaceCategory.Cutout, new(8, 0, 0, 12, 4, 4)));
            regions.Add(new(ShadowSurfaceCategory.Entities, new(0, 8, 0, 4, 12, 4)));
            regions.Add(new(ShadowSurfaceCategory.Foliage, new(8, 8, 0, 12, 12, 4)));
            regions.Add(new(ShadowSurfaceCategory.Water, new(0, 0, 8, 4, 4, 12)));
            regions.Add(new(ShadowSurfaceCategory.Transparent, new(24, 24, 24, 28, 28, 28)));
            regions.Add(new(ShadowSurfaceCategory.Transparent, new(24, 24, 8, 28, 28, 12)));
            var publication = new ShadowSurfacePublication(lights.Frame, lights.World, lights.Dimension,
                includeTransparent ? ShadowSurfaceCategory.All : ShadowSurfaceCategory.All & ~ShadowSurfaceCategory.Transparent,
                regions.MoveToImmutable(), programs);
            LastPublication = publication;
            return publication;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var program in programs) ShaderCapabilities.Forget(program);
        }
    }

    /// <summary>Owns the ordinary indexed terrain streams borrowed by the native mesh-pool adapter.</summary>
    private sealed class NativeTerrainStorage : IDisposable
    {
        private readonly GpuVao vao = NativeResourceSetup.CreateVertexArray();
        private readonly GpuVbo positions = Buffer("Position", 3 * 3 * sizeof(float));
        private readonly GpuVbo uv = Buffer("Uv", 3 * 2 * sizeof(float));
        private readonly GpuVbo colors = Buffer("Color", 3 * 4 * sizeof(byte));
        private readonly GpuVbo flags = Buffer("Flags", 3 * sizeof(uint));
        private readonly GpuEbo indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 3 * sizeof(uint),
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.FreshGate.Indices"
        }).RequireResource();

        /// <summary>Exposes the exact native pool captured by the terrain scene and world range.</summary>
        internal MeshDataPool Pool { get; }

        /// <summary>Creates one indexed triangle and native pool metadata under its production category.</summary>
        internal NativeTerrainStorage(int poolId, Vector3d origin)
        {
            // Keep the complete triangle inside its fixed 32-voxel chunk partition for conservative candidate bounds.
            positions.UploadData<float>([1, 1, 1, 31, 1, 1, 1, 31, 1]);
            uv.UploadData<float>(new float[6]);
            colors.UploadData<byte>([255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255]);
            flags.UploadData<uint>(new uint[3]);
            indices.UploadIndices(new uint[] { 0, 1, 2 });
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
            Attribute(0, 3, VertexAttribPointerType.Float, positions.BufferId, 12, false, false);
            Attribute(1, 2, VertexAttribPointerType.Float, uv.BufferId, 8, false, false);
            Attribute(2, 4, VertexAttribPointerType.UnsignedByte, colors.BufferId, 4, true, false);
            Attribute(3, 1, VertexAttribPointerType.UnsignedInt, flags.BufferId, 4, false, true);

            var mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(mesh);
            mesh.VaoId = vao.VertexArrayId; mesh.vboIdIndex = indices.BufferId; mesh.drawMode = PrimitiveType.Triangles;
            mesh.xyzVboId = positions.BufferId; mesh.uvVboId = uv.BufferId;
            mesh.rgbaVboId = colors.BufferId; mesh.flagsVboId = flags.BufferId;
            Pool = (MeshDataPool)RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPool));
            Pool.VerticesPoolSize = 3; Pool.IndicesPoolSize = 3; Pool.indicesGroupsCount = 1;
            Pool.indicesStartsByte = [0, 0]; Pool.indicesSizes = [3];
            AccessTools.Field(typeof(MeshDataPool), "modelRef").SetValue(Pool, mesh);
            AccessTools.Field(typeof(MeshDataPool), "poolId").SetValue(Pool, poolId);
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(Pool, 0);
            AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(Pool,
                new Vec3i((int)origin.X, (int)origin.Y, (int)origin.Z));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            vao.Dispose(); indices.Dispose(); flags.Dispose(); colors.Dispose(); uv.Dispose(); positions.Dispose();
        }

        /// <summary>Creates a fixed-size native-shaped stream through the RHI buffer owner.</summary>
        private static GpuVbo Buffer(string name, int capacity) => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = capacity,
            Usage = BufferUsage.StaticDraw, Label = "Tests.Shadows.FreshGate." + name
        }).RequireResource();

        /// <summary>Configures engine-owned foreign VAO attributes because the RHI cannot describe an existing engine VAO.</summary>
        private static void Attribute(int location, int components, VertexAttribPointerType type,
            int buffer, int stride, bool normalized, bool integer)
        {
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, buffer);
            if (integer) GL.VertexAttribIPointer(location, components, VertexAttribIntegerType.UnsignedInt, stride, IntPtr.Zero);
            else GL.VertexAttribPointer(location, components, type, normalized, stride, IntPtr.Zero);
            GL.EnableVertexAttribArray(location);
        }
    }
    #endregion
}
