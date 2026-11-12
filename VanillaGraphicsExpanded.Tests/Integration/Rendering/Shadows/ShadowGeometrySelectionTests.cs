using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks production GPU selection against complete chunk ranges and bounded view submissions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowGeometrySelectionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Deduplicates repeated associations while emitting one initialized command per geometry/view pair.</summary>
    [Fact]
    public void SelectionDeduplicatesViewPairsAndInitializesIndirectCommands()
    {
        // Arrange: each virtual view spans two page columns, while one current chunk range is repeated by associations.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [2]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Sun, 1);
        var candidate = scene.Candidate(source, 7);
        var views = new[]
        {
            View(source, 7, new(0, 0, 16, 8), Vector3d.Zero),
            View(source, 7, new(0, 0, 16, 8), Vector3d.Zero)
        };
        ShadowViewCandidates[] associations = [candidate, candidate, candidate with { View = 1 }];

        // Act: run the packaged selector against the complete candidate, uploaded world table and native-resolved ranges.
        using var selection = new ShadowGeometrySelection(assets.Api, views, associations, scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication, [1, 0]), commandsPerView: 2, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0);
        Assert.Equal(0, selection.TerrainPadding);
        uint[] output = Read(selection.Output, 16 + 2 * 2 * 7);
        var secondViewCommand = selection.Commands(1, new ShadowDrawInterval(0, 0, 1));

        // Assert: repeated associations collapse to two view pairs, not four per-page draws, and every inactive slot is zero.
        Assert.Equal(1u, output[0]);
        Assert.Equal(0u, output[1]);
        Assert.Equal(2u, output[3]);
        Assert.Equal(2u, output[4]);
        Assert.Equal(2u * 16u * 8u, output[5]);
        Assert.Equal(new uint[] { 3, 1, 0, 0, 0 }, output.AsSpan(16, 5).ToArray());
        Assert.All(output.AsSpan(21, 5).ToArray(), word => Assert.Equal(0u, word));
        Assert.Equal(new uint[] { 3, 1, 0, 0, 2 }, output.AsSpan(26, 5).ToArray());
        Assert.All(output.AsSpan(31, 5).ToArray(), word => Assert.Equal(0u, word));
        Assert.Equal(new uint[] { 0, 0, 0, 0, 0, 1, 0, 0 }, output.AsSpan(36, 8).ToArray());
        Assert.Equal((nint)104, secondViewCommand.Arguments.Offset);
        Assert.Equal((nint)20, secondViewCommand.Arguments.Length);
        Assert.Equal((1, 20), (secondViewCommand.Count, secondViewCommand.Stride));
    }

    /// <summary>Negative virtual pages select only current ranges intersecting the signed raster origin.</summary>
    [Fact]
    public void SelectionClipsOffscreenChunksAtNegativeVirtualOrigin()
    {
        // Arrange: use three negative-coordinate chunks, a split-page view around chunk -3, and a distant offscreen chunk.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(-3, 0, 0, -1, [1, 1, 1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Moon, 2);
        var candidate = scene.Candidate(source, 11);
        var projection = Projection(source, 11, -96, -32);
        var candidateHeader = ReadHeader(scene.Candidates.Headers, candidate.Handle.Slot);
        var candidateCells = ReadCandidateCells(scene.Candidates, candidate.Handle.Slot, candidateHeader.Count);
        Assert.Contains((-3, 0, 0), candidateCells);
        Assert.Contains((-1, 0, 0), candidateCells);
        var view = ShadowDrawView.Directional(projection, 0,
            new(-25, 0, 8, 8), new(-25, 0, 8, 8), new(-84, 16, 16));
        var drawViews = new[] { view };

        // Act: the GPU resolves all candidate chunk coordinates, then clips their current ranges against this view.
        using var selection = new ShadowGeometrySelection(assets.Api, drawViews, [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 3, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0);
        uint[] output = Read(selection.Output, 16 + 3 * 7);

        // Assert: the signed view origin is retained, the intersecting range is active, and the distant range is absent.
        Assert.Equal(-25L, view.Region.X);
        Assert.Equal(1u, output[0]);
        Assert.Equal(1u, output[3]);
        Assert.Equal(new uint[] { 3, 1, 0, 0, 0 }, output.AsSpan(16, 5).ToArray());
        Assert.All(output.AsSpan(21, 10).ToArray(), word => Assert.Equal(0u, word));
        Assert.Equal(new uint[] { 0, 0, 0, 0, 0, 0 }, output.AsSpan(31, 6).ToArray());
    }

    /// <summary>Rejects a view whose source dimension does not match its completed candidate input.</summary>
    [Fact]
    public void SelectionRejectsCandidateViewDimensionMismatch()
    {
        // Arrange: the candidate belongs to dimension zero while the otherwise valid view uses a dimension-one source.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var candidateSource = Source(ShadowSourceKind.Sun, 5);
        var candidate = scene.Candidate(candidateSource, 19);
        var otherDimension = new ShadowSourceKey(1, 1, ShadowSourceKind.Sun, 6, 6, 0);
        var view = View(otherDimension, 19, new(0, 0, 8, 8), Vector3d.Zero);

        // Act: attempt to combine current candidate metadata with a different dimension's projection.
        var error = Assert.Throws<InvalidOperationException>(() => new ShadowGeometrySelection(assets.Api,
            [view], [candidate], scene.Candidates, publication, scene.WorldBuffers, DrawRanges(publication),
            commandsPerView: 1, scene.Pages, DrawSettings(), otherDrawingBytes: 0));

        // Assert: mismatched source identity is rejected before a GPU selection executable or command batch is published.
        Assert.Contains("snapshots disagree", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Preserves complete owner object metadata and independent view selectors across original instances.</summary>
    [Fact]
    public void SelectionExpandsNativeObjectAcrossViewsWithoutRebasingInstances()
    {
        // Arrange: associate one complete candidate with two views and one object carrying a three-instance native draw.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Sun, 47);
        var candidate = scene.Candidate(source, 53);
        var views = new[]
        {
            View(source, 53, new(0, 0, 8, 8), Vector3d.Zero),
            View(source, 53, new(8, 0, 8, 8), Vector3d.Zero)
        };
        ShadowViewCandidates[] associations = [candidate, candidate with { View = 1 }];
        ImmutableArray<ShadowObjectDrawRange> objects = [new(1, 6, 12, 4, 3, 18, 55)];

        // Act: select current terrain ranges plus the owner-supplied instanced draw through one production compute dispatch.
        using var selection = new ShadowGeometrySelection(assets.Api, views, associations, scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 2, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0, objects: objects);
        uint[] output = Read(selection.Output, 16 + 4 * 7);
        var firstObjectSelector = selection.ObjectSelector(0, 1);
        var secondObjectSelector = selection.ObjectSelector(1, 1);
        var secondObjectCommand = selection.Commands(1, new ShadowDrawInterval(1, 1, 1));

        // Assert: original index/base-vertex/instance identity survives, while each view receives its own object selector.
        Assert.Equal(1u, output[0]);
        Assert.Equal(4u, output[3]);
        Assert.Equal(38u, output[4]);
        Assert.Equal(2432u, output[5]);
        Assert.Equal(8u, output[6]);
        Assert.Equal(new uint[] { 12, 3, 6, 4, 0 }, output.AsSpan(21, 5).ToArray());
        Assert.Equal(new uint[] { 12, 3, 6, 4, 0 }, output.AsSpan(31, 5).ToArray());
        Assert.Equal(new uint[] { 0, 0, 55, 0, 0, 1, 55, 1 }, output.AsSpan(36, 8).ToArray());
        Assert.Equal((nint)152, firstObjectSelector.Offset);
        Assert.Equal((nint)8, firstObjectSelector.Length);
        Assert.Equal((nint)168, secondObjectSelector.Offset);
        Assert.Equal((nint)8, secondObjectSelector.Length);
        Assert.Equal((nint)124, secondObjectCommand.Arguments.Offset);
        Assert.Equal((1, 20), (secondObjectCommand.Count, secondObjectCommand.Stride));
    }

    /// <summary>Excludes an object whose semantic dimension differs from a view despite overlapping local coordinates.</summary>
    [Fact]
    public void SelectionRejectsObjectFromAnotherDimension()
    {
        // Arrange: overlap local coordinates while separating object and view by their native semantic dimensions.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Sun, 48, dimension: 0);
        var candidate = scene.Candidate(source, 54);
        var view = View(source, 54, new(0, 0, 8, 8), Vector3d.Zero);
        ImmutableArray<ShadowObjectDrawRange> objects =
            [new(1, 0, 3, 0, 2, 2, 56, Dimension: 1)];

        // Act: select one in-dimension terrain range and the owner-supplied foreign-dimension object.
        using var selection = new ShadowGeometrySelection(assets.Api, [view], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 2, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0, objects: objects);
        uint[] output = Read(selection.Output, 16 + 2 * 7);

        // Assert: terrain remains executable, while the other-dimension object's command and instance work stay absent.
        Assert.Equal(1u, output[0]);
        Assert.Equal(1u, output[3]);
        Assert.Equal(1u, output[6]);
        Assert.Equal(3u, output[16]);
        Assert.Equal(0u, output[23]);
        Assert.Equal(0u, output[24]);
    }

    /// <summary>Combines terrain and object work under one budget and withdraws every command when the sum exceeds it.</summary>
    [Fact]
    public void SelectionCombinedTerrainAndObjectBudgetFailureWithdrawsAllCommands()
    {
        // Arrange: let one terrain pair consume the triangle allowance before an owner object exceeds the remainder.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Moon, 59);
        var candidate = scene.Candidate(source, 61);
        var settings = DrawSettings();
        settings.EstimatedTriangles = 10;
        ImmutableArray<ShadowObjectDrawRange> objects = [new(1, 6, 12, 4, 3, 12, 71)];

        // Act: the candidate and terrain range succeed before the object draw exceeds the shared triangle budget.
        using var selection = new ShadowGeometrySelection(assets.Api,
            [View(source, 61, new(0, 0, 8, 8), Vector3d.Zero)], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 2, scene.Pages,
            settings, otherDrawingBytes: 0, objects: objects);
        uint[] output = Read(selection.Output, 16 + 2 * 7);

        // Assert: diagnostics show prior terrain work, but neither terrain nor object indirect command can execute.
        Assert.Equal(0u, output[0]);
        Assert.Equal(4u, output[1]);
        Assert.Equal(1u, output[3]);
        Assert.Equal(1u, output[4]);
        Assert.Equal(1u, output[6]);
        AssertCommandsInactive(output, 2);
    }

    /// <summary>Rejects non-terrain object ranges that overlap a native terrain command slot.</summary>
    [Fact]
    public void SelectionRejectsObjectAndTerrainCommandOverlap()
    {
        // Arrange: give both the current terrain range and an object the same sole command interval.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Sun, 67);
        var candidate = scene.Candidate(source, 71);

        // Act and assert: overlapping ownership is rejected before dispatch so no ambiguous indirect slot is created.
        var error = Assert.Throws<InvalidOperationException>(() => new ShadowGeometrySelection(assets.Api,
            [View(source, 71, new(0, 0, 8, 8), Vector3d.Zero)], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0,
            objects: [new ShadowObjectDrawRange(0, 3, 3, 0, 1, 1, 73)]));
        Assert.Contains("complete bounded command intervals", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rejects object cost estimates that count triangles once instead of once per native instance.</summary>
    [Fact]
    public void SelectionRejectsUnderestimatedInstancedObjectCost()
    {
        // Arrange: provide twelve indices and three original instances but charge only the four per-instance triangles.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Moon, 73);
        var candidate = scene.Candidate(source, 79);

        // Act and assert: whole-instance raster work must be bounded before any selection dispatch is created.
        var error = Assert.Throws<InvalidOperationException>(() => new ShadowGeometrySelection(assets.Api,
            [View(source, 79, new(0, 0, 8, 8), Vector3d.Zero)], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 2, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0,
            objects: [new ShadowObjectDrawRange(1, 6, 12, 4, 3, 4, 83)]));
        Assert.Contains("complete bounded command intervals", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Selects an independently bounded native range even when no static candidate range owns it.</summary>
    [Fact]
    public void MovingNativeRangeIsSelectedOutsideStaticCandidateRanges()
    {
        // Arrange: the current chunk is complete but publishes an empty static range span; its moving range is independent.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [0]);
        var movingRange = new ShadowGeometryRange(700, 0, 3, 0, 3, 0, 1, 0);
        publication = publication with { Ranges = ImmutableArray.Create(movingRange) };
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Sun, 91);
        var candidate = scene.Candidate(source, 53);
        var view = View(source, 53, new(0, 0, 8, 8), Vector3d.Zero);

        // Act: eligibility two enters the conservative per-view list independently of candidate chunk ranges.
        using var selection = new ShadowGeometrySelection(assets.Api, [view], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication, [2]), commandsPerView: 1, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0);
        uint[] output = Read(selection.Output, 16 + 7);

        // Assert: the complete batch emits one active command and binds the independent range's exact selector.
        Assert.Equal(1u, output[0]);
        Assert.Equal(1u, output[3]);
        Assert.Equal(3u, output[16]);
        Assert.Equal(0u, output[21]);
        Assert.Equal(0u, output[22]);
        selection.Seal(); scene.Pages.SealBatch();
    }

    /// <summary>Candidate chunks stay dimension-local while vertex-space clipping uses the native internal-Y offset.</summary>
    [Fact]
    public void NonzeroDimensionChunkSelectionUsesInternalProjectionY()
    {
        // Arrange: publish a local-Y-zero chunk in dimension two and a projection centered at its native internal Y.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        const int dimension = 2;
        var publication = Publication(0, 0, 0, 0, [1], dimension);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Sun, 92, dimension);
        var candidate = scene.Candidate(source, 54);
        var projection = new DirectionalShadowProjection(source, 54, Vector3d.UnitZ,
            new(0, dimension * 32768.0, 0, 32, dimension * 32768.0 + 32, 32), 4, 1, 8);
        int projectedY = checked((int)(dimension * 32768.0 / projection.TexelWidth(0)));
        var view = ShadowDrawView.Directional(projection, 0, new(0, projectedY, 8, 8),
            new(0, projectedY, 8, 8), new(0, dimension * 32768.0, 0));

        // Act: run candidate-to-range selection against the local world table and the internal-space projection.
        using var selection = new ShadowGeometrySelection(assets.Api, [view], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0);
        uint[] output = Read(selection.Output, 16 + 7);

        // Assert: local-Y-zero geometry survives only after dimension two contributes its 65536-unit internal offset.
        Assert.Equal(1u, output[0]);
        Assert.Equal(1u, output[3]);
        Assert.Equal(3u, output[16]);
        selection.Seal(); scene.Pages.SealBatch();
    }

    /// <summary>Requires source-motion and receiver enclosures to cover the padded replacement page.</summary>
    [Theory]
    [InlineData(PointEnvelopeFailure.SourceBox)]
    [InlineData(PointEnvelopeFailure.Receiver)]
    public void PointTerrainSelectionRejectsIncompleteDeformationEnvelope(PointEnvelopeFailure failure)
    {
        // Arrange: omit either the source motion allowance or the full receiver enclosure for the padded page.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Entity, 93);
        var requestReceiver = new ShadowBounds(-3, -3, -3, 3, 3, 3);
        var currentReceiver = new ShadowBounds(-2, -2, -2, 2, 2, 2);
        var sourceBox = failure == PointEnvelopeFailure.SourceBox
            ? new ShadowBounds(-1, -1, -1, 1, 1, 1) : new ShadowBounds(-2, -2, -2, 2, 2, 2);
        var candidate = scene.PointCandidate(source, 55, requestReceiver, sourceBox, currentReceiver, Vector3d.Zero);
        var projection = new PointShadowProjection(source, 55, Vector3d.Zero, near: 1, far: 100,
            faceResolution: 8, pageSize: 8);
        var view = ShadowDrawView.Point(projection, 4, new(0, 0, 8, 8), new(0, 0, 8, 8), Vector3d.Zero);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate,
            new(source, 55, 4, 0, 0), new(-64, -64, -64, 64, 64, 64),
            publication.Generation, 1, 0, Projection: ShadowProjectionSnapshot.Point(projection, 4));

        // Act and assert: insufficient source motion rejects selection, while insufficient receiving bounds reject certification.
        if (failure == PointEnvelopeFailure.SourceBox)
        {
            Assert.Throws<InvalidOperationException>(() => new ShadowGeometrySelection(assets.Api, [view], [candidate],
                scene.Candidates, publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1,
                scene.Pages, DrawSettings(), otherDrawingBytes: 0, terrainPadding: 64));
        }
        else
        {
            using var selection = new ShadowGeometrySelection(assets.Api, [view], [candidate], scene.Candidates,
                publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
                DrawSettings(), otherDrawingBytes: 0, terrainPadding: 64);
            Assert.Throws<InvalidOperationException>(() => selection.ValidateReplacement(0, request));
            selection.Seal();
        }
        Assert.True(scene.Pages.CanPrepareReplacements);
        scene.Pages.SealBatch();
    }
    /// <summary>Admits a point candidate whose source motion and receiver bounds cover the full padded page.</summary>
    [Fact]
    public void PointTerrainSelectionAcceptsExpandedSourceAndReceiverCoverage()
    {
        // Arrange: cover the page's two-chunk deformation margin in receiver coordinates and the point source motion box.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var source = Source(ShadowSourceKind.Entity, 94);
        var expanded = new ShadowBounds(-4, -4, -4, 4, 4, 4);
        var sourceBox = new ShadowBounds(-2, -2, -2, 2, 2, 2);
        var candidate = scene.PointCandidate(source, 56, expanded, sourceBox, expanded, Vector3d.Zero);
        var projection = new PointShadowProjection(source, 56, Vector3d.Zero, near: 1, far: 100,
            faceResolution: 8, pageSize: 8);
        var view = ShadowDrawView.Point(projection, 4, new(0, 0, 8, 8), new(0, 0, 8, 8), Vector3d.Zero);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate,
            new(source, 56, 4, 0, 0), new(-64, -64, -64, 64, 64, 64),
            publication.Generation, 1, 0, Projection: ShadowProjectionSnapshot.Point(projection, 4));

        // Act: package the complete candidate and ask the production selector to certify the full padded request.
        using var selection = new ShadowGeometrySelection(assets.Api, [view], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0, terrainPadding: 64);

        // Assert: the view keeps the declared margin and the candidate encloses the complete replacement receiver.
        Assert.Equal(64, selection.TerrainPadding);
        selection.ValidateReplacement(0, request);
        selection.Seal(); scene.Pages.SealBatch();
    }
    /// <summary>Candidate, work and pair-capacity failures withdraw every indirect command after partial selection.</summary>
    [Theory]
    [InlineData(SelectionFailure.Candidate)]
    [InlineData(SelectionFailure.Work)]
    [InlineData(SelectionFailure.PairCapacity)]
    public void SelectionFailureNeverPublishesPartialIndirectCommands(SelectionFailure failure)
    {
        // Arrange: two current ranges let the first range write before the second range triggers the selected failure.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [2]);
        using var scene = new SelectionScene(assets.Api, publication);
        var sun = Source(ShadowSourceKind.Sun, 3);
        var first = scene.Candidate(sun, 13);
        var views = new[] { View(sun, 13, new(0, 0, 8, 8), Vector3d.Zero) };
        var associations = new List<ShadowViewCandidates> { first };
        var settings = DrawSettings();
        if (failure == SelectionFailure.Candidate)
        {
            // A completed-looking but withdrawn second header reaches the GPU validation boundary after the first view.
            var moon = Source(ShadowSourceKind.Moon, 4);
            var second = scene.Candidate(moon, 17);
            views = [views[0], View(moon, 17, new(0, 0, 8, 8), Vector3d.Zero)];
            associations = [first, second with { View = 1 }];
            var header = ReadHeader(scene.Candidates.Headers, second.Handle.Slot);
            header.Status = 2;
            scene.Candidates.Headers.UploadSubData<ShadowCandidateHeader>([header], second.Handle.Slot * 128);
        }
        else if (failure == SelectionFailure.Work)
        {
            // Association header, candidate cell, world probe and first range consume four visits;
            // the next range must fail before creating a second pair.
            settings.ExpansionVisits = 4;
        }
        else
        {
            settings.PairCapacity = 1;
        }

        // Act: production compute initializes all slots, selects the first range, then observes a whole-batch failure.
        using var selection = new ShadowGeometrySelection(assets.Api, views, associations, scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 2, scene.Pages,
            settings, otherDrawingBytes: 0);
        uint[] output = Read(selection.Output, 16 + views.Length * 2 * 7);

        // Assert: failure stays explicit, partial work stays diagnostic, and every indirect command remains inactive.
        Assert.Equal(0u, output[0]);
        Assert.Equal(failure switch
        {
            SelectionFailure.Candidate => 1u,
            SelectionFailure.Work => 2u,
            SelectionFailure.PairCapacity => 4u,
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        }, output[1]);
        AssertCommandsInactive(output, views.Length * 2);
        Assert.Equal(failure == SelectionFailure.Candidate ? 2u : 1u, output[3]);

        // Act: pass the actual GPU status word to page allocation; a nonzero failure certificate must not admit storage.
        var rejectedSource = Source(ShadowSourceKind.Sun, 31);
        var rejectedKey = new ShadowPageKey(rejectedSource, 23, 0, 0, 0);
        var rejectedRegion = new ShadowBounds(0, 0, 0, 8, 8, 8);
        var rejectedProjection = ShadowProjectionSnapshot.Directional(
            Projection(rejectedSource, 23, 0, 32), 0);
        var rejectedRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, rejectedKey, rejectedRegion,
            1, 1, 0, Projection: rejectedProjection);
        Assert.True(scene.Pages.AllocateSelected(rejectedRequest, selection.Certificate));
        Assert.True(scene.Pages.ClearReplacement());
        Assert.True(scene.Pages.Execute(new ShadowPageRequest(ShadowPageOperation.Lookup, rejectedKey,
            rejectedRegion, 1, 1, 0)));
        using var pageResult = scene.Pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(pageResult.IsMapped);

        // Assert: rejected selection cannot create a page that the normal lookup path can sample.
        Assert.Equal(1u, pageResult.Span[0]);
    }

    /// <summary>A successful complete GPU selection certificate permits the matching projected page allocation.</summary>
    [Fact]
    public void CompleteSelectionCertificatePermitsPageAllocation()
    {
        // Arrange: produce one real current-world geometry selection and retain its GPU success word.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication, tilesPerLayer: 2);
        var source = Source(ShadowSourceKind.Sun, 37);
        var candidate = scene.Candidate(source, 29);
        using var selection = new ShadowGeometrySelection(assets.Api,
            [View(source, 29, new(0, 0, 8, 8), Vector3d.Zero)], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0);

        // Act: submit page allocation with the selector's actual status word at byte offset zero.
        var key = new ShadowPageKey(source, 29, 0, 0, 0);
        var region = new ShadowBounds(0, 0, 0, 8, 8, 8);
        var request = new ShadowPageRequest(ShadowPageOperation.Allocate, key, region, 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(Projection(source, 29, 0, 32), 0));
        Assert.True(scene.Pages.AllocateSelected(request, selection.Certificate));
        using (var pageResult = scene.Pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read))
        {
            Assert.True(pageResult.IsMapped);

            // Assert: the ordinary allocation result reports a fresh assignment for this exact virtual page.
            Assert.Equal(0u, pageResult.Span[0]);
            Assert.True(((ulong)pageResult.Span[2] | ((ulong)pageResult.Span[3] << 32)) > 0);
        }

        // Act: after the page batch advances, reuse the old successful selection certificate for a second key.
        scene.Pages.SealBatch();
        RHICommandContext.Current.WaitIdle();
        scene.Pages.Poll();
        Assert.True(scene.Pages.BeginBatch());
        var staleKey = key with { X = key.X + 1 };
        var staleRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, staleKey, region, 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(Projection(source, 29, 0, 32), 0));
        Assert.True(scene.Pages.AllocateSelected(staleRequest, selection.Certificate));
        using var nextSelection = new ShadowGeometrySelection(assets.Api,
            [View(source, 29, new(0, 0, 8, 8), Vector3d.Zero)], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
            DrawSettings(), otherDrawingBytes: 0);
        var availableKey = key with { X = key.X + 2 };
        var availableRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, availableKey, region, 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(Projection(source, 29, 0, 32), 0));
        Assert.True(scene.Pages.AllocateSelected(availableRequest, nextSelection.Certificate));
        using (var availableResult = scene.Pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read))
        {
            Assert.True(availableResult.IsMapped);
            // Assert: stale certification left the second resident slot available to a current selection certificate.
            Assert.Equal(0u, availableResult.Span[0]);
            Assert.True(((ulong)availableResult.Span[2] | ((ulong)availableResult.Span[3] << 32)) > 0);
        }
    }

    /// <summary>A failed later selection cannot evict or clear a previously published sampled tile.</summary>
    [Fact]
    public void FailedSelectionPreservesRetainedSampledPageAndDepth()
    {
        // Arrange: publish a sampled page in an earlier fenced interval, then mark its atlas texel with a known depth word.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var publication = Publication(0, 0, 0, 0, [1]);
        using var scene = new SelectionScene(assets.Api, publication);
        var retainedSource = Source(ShadowSourceKind.Sun, 41);
        var retainedKey = new ShadowPageKey(retainedSource, 31, 0, 0, 0);
        var retainedRegion = new ShadowBounds(0, 0, 0, 8, 8, 8);
        var retainedRequest = new ShadowPageRequest(ShadowPageOperation.Allocate, retainedKey, retainedRegion,
            1, 1, 0, Projection: ShadowProjectionSnapshot.Directional(Projection(retainedSource, 31, 0, 32), 0));
        Assert.True(scene.Pages.Execute(retainedRequest));
        long retainedAssignment;
        using (var allocation = scene.Pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read))
        {
            Assert.True(allocation.IsMapped);
            Assert.Equal(0u, allocation.Span[0]);
            retainedAssignment = unchecked((long)(((ulong)allocation.Span[3] << 32) | allocation.Span[2]));
        }
        Assert.True(retainedAssignment > 0);
        Assert.True(scene.Pages.ClearReplacement());
        Assert.True(scene.Pages.FreezeWritableAssignments());
        scene.Pages.FinishReplacements(complete: true, worldRevision: 1, sceneEpoch: 1,
            sceneSequence: 0, timestamp: 0);
        using (var published = scene.Pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read))
        {
            Assert.True(published.IsMapped);
            Assert.Equal(0u, published.Span[0]);
        }
        scene.Pages.SealBatch();
        RHICommandContext.Current.WaitIdle();
        scene.Pages.Poll();
        Assert.True(scene.Pages.BeginBatch());
        const uint retainedDepth = 0x5a17c0de;
        RHICommandContext.Current.ClearTexture(scene.Pages.DepthArray,
            new(new(0), 0, 0, 0, scene.Pages.DepthArray.Width, scene.Pages.DepthArray.Height, 1),
            new(PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt), BitConverter.GetBytes(retainedDepth));

        // Arrange: create a real candidate but force bounded selection work to fail before it can certify completion.
        var currentSource = Source(ShadowSourceKind.Moon, 43);
        var candidate = scene.Candidate(currentSource, 37);
        var failureSettings = DrawSettings();
        failureSettings.ExpansionVisits = 3;
        using var selection = new ShadowGeometrySelection(assets.Api,
            [View(currentSource, 37, new(0, 0, 8, 8), Vector3d.Zero)], [candidate], scene.Candidates,
            publication, scene.WorldBuffers, DrawRanges(publication), commandsPerView: 1, scene.Pages,
            failureSettings, otherDrawingBytes: 0);
        uint[] failedOutput = Read(selection.Output, 16 + 7);
        Assert.Equal(0u, failedOutput[0]);
        Assert.Equal(2u, failedOutput[1]);

        // Act: attempt selected allocation and the usual replacement clear using the failed GPU certificate.
        var rejectedRequest = new ShadowPageRequest(ShadowPageOperation.Allocate,
            new ShadowPageKey(currentSource, 37, 0, 0, 0), retainedRegion, 1, 1, 0,
            Projection: ShadowProjectionSnapshot.Directional(Projection(currentSource, 37, 0, 32), 0));
        Assert.True(scene.Pages.AllocateSelected(rejectedRequest, selection.Certificate));
        Assert.True(scene.Pages.ClearReplacement());
        Assert.True(scene.Pages.Execute(new ShadowPageRequest(ShadowPageOperation.Lookup, retainedKey,
            retainedRegion, 1, 1, 0)));
        using var retainedResult = scene.Pages.Results.MapRange<uint>(0, 8, BufferMapAccess.Read);
        Assert.True(retainedResult.IsMapped);
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureUpdate | ResourceAccess.TextureFetch));
        var encoding = new RHIPixelEncoding(PixelChannelLayout.RedInteger, PixelComponentType.UnsignedInt);
        using var depthReadback = RHICommandContext.Current.EnqueueReadback(new RHIReadbackDescription(
            scene.Pages.DepthArray, new(new(0), 0, 0, 0, 1, 1, 1), encoding));
        Assert.Equal(CompletionStatus.Ready, depthReadback.Wait());
        using var depthMapping = depthReadback.MapReadyReadback();
        uint depthAfter = System.Runtime.InteropServices.MemoryMarshal.Read<uint>(depthMapping.Bytes);

        // Assert: the original sampled assignment and its depth survive a later failed selection and clear attempt.
        Assert.Equal(0u, retainedResult.Span[0]);
        Assert.Equal(retainedAssignment, unchecked((long)(((ulong)retainedResult.Span[3] << 32) | retainedResult.Span[2])));
        Assert.Equal(retainedDepth, depthAfter);
    }
    #endregion

    #region Private
    /// <summary>Creates a complete native world table whose range spans are authoritative for selection.</summary>
    private static ShadowWorldPublication Publication(int minX, int minY, int minZ, int maxX,
        int[] rangesPerChunk, int dimension = 0)
    {
        var domain = new ShadowChunkDomain(dimension, minX, minY, minZ, maxX, minY, minZ);
        int chunkCount = maxX - minX + 1;
        if (rangesPerChunk.Length != chunkCount) throw new ArgumentException("Each X partition needs one range count.", nameof(rangesPerChunk));
        int tableSize = 1;
        while (tableSize < chunkCount * 2) tableSize <<= 1;
        var table = new ShadowChunkGpu[tableSize];
        var ranges = ImmutableArray.CreateBuilder<ShadowGeometryRange>();
        for (int x = minX; x <= maxX; x++)
        {
            int count = rangesPerChunk[x - minX];
            uint offset = (uint)ranges.Count;
            for (int local = 0; local < count; local++)
            {
                int ordinal = ranges.Count;
                ranges.Add(new(100 + x * 4 + local, ordinal * 3, 3, ordinal * 3, 3, 0, 1, 0));
            }
            var entry = new ShadowChunkGpu
            {
                X = x, Y = minY, Z = minZ, Dimension = dimension,
                NativeLow = (uint)(x - minX + 1), LifetimeLow = 1, RevisionLow = 1,
                Flags = 3, RangeOffset = offset, RangeCount = (uint)count
            };
            uint slot = Hash(entry.Dimension, entry.X, entry.Y, entry.Z) & (uint)(tableSize - 1);
            while (table[slot].Flags != 0) slot = (slot + 1) & (uint)(tableSize - 1);
            table[slot] = entry;
        }
        var bounds = domain.Bounds;
        return new(1, true, null, ImmutableArray.Create(table), ranges.ToImmutable(),
            ImmutableDictionary<int, ShadowBounds>.Empty.Add(dimension, new(bounds.MinX * 32, bounds.MinY * 32,
                bounds.MinZ * 32, bounds.MaxX * 32, bounds.MaxY * 32, bounds.MaxZ * 32)), tableSize)
        { Owner = 41 };
    }

    /// <summary>Creates one CPU-resolved input per world range while retaining caller-selected eligibility.</summary>
    private static ImmutableArray<ShadowDrawRange> DrawRanges(ShadowWorldPublication publication, uint[]? eligibility = null)
    {
        if (eligibility is not null && eligibility.Length != publication.Ranges.Length)
            throw new ArgumentException("Eligibility must match the native range table.", nameof(eligibility));
        var result = ImmutableArray.CreateBuilder<ShadowDrawRange>(publication.Ranges.Length);
        for (uint i = 0; i < publication.Ranges.Length; i++)
        {
            var range = publication.Ranges[(int)i];
            uint eligible = eligibility is null ? 1u : eligibility[i];
            result.Add(new(i, i, eligible, (uint)(range.IndexCount / 3), (uint)range.IndexStart,
                (uint)range.IndexCount, range.VertexStart, range.RevisionLow, range.RevisionHigh,
                (uint)range.VertexStart, (uint)range.VertexCount));
        }
        return result.ToImmutable();
    }

    /// <summary>Creates one known source identity for a single test association.</summary>
    private static ShadowSourceKey Source(ShadowSourceKind kind, long lifetime, int dimension = 0) =>
        new(1, dimension, kind, lifetime, lifetime, 0);

    /// <summary>Builds a directional view and stable projection from the represented native chunk envelope.</summary>
    private static ShadowDrawView View(ShadowSourceKey source, long generation, ShadowRasterRegion region, Vector3d origin)
    {
        var projection = Projection(source, generation, 0, 32);
        return ShadowDrawView.Directional(projection, 0, region, region, origin);
    }

    /// <summary>Fits a directional depth interval that contains the current test chunks.</summary>
    private static DirectionalShadowProjection Projection(ShadowSourceKey source, long generation, double minX, double maxX) =>
        new(source, generation, Vector3d.UnitZ, new(minX, 0, 0, maxX, 32, 32), 4, 1, 8);

    /// <summary>Uses finite work defaults except where one test deliberately lowers a selection allowance.</summary>
    private static ShadowDrawSettings DrawSettings() => new()
    {
        MemoryMiB = 8, ViewCapacity = 4, PairCapacity = 16, ExpansionVisits = 256,
        CommandSlots = 16, ViewInstances = 16, RasterPixels = 4096,
        EstimatedTriangles = 4096, EstimatedRasterSamples = 1048576
    };

    /// <summary>Copies GPU selection words after its production compute and indirect barrier have completed.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }

    /// <summary>Checks the indirect count fields that determine whether any native geometry can execute.</summary>
    private static void AssertCommandsInactive(uint[] output, int slots)
    {
        for (int slot = 0; slot < slots; slot++)
        {
            int command = 16 + slot * 5;
            Assert.Equal(0u, output[command]);
            Assert.Equal(0u, output[command + 1]);
        }
    }

    /// <summary>Reads one candidate certificate to prepare a targeted stale-input fault.</summary>
    private static ShadowCandidateHeader ReadHeader(GpuShaderStorageBuffer buffer, int slot)
    {
        using var mapped = buffer.MapRange<ShadowCandidateHeader>(slot * 128, 1, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span[0];
    }

    /// <summary>Copies one completed association so clipping assertions prove the offscreen coordinate was a candidate.</summary>
    private static HashSet<(int X, int Y, int Z)> ReadCandidateCells(ShadowCandidatePool pool, int slot, uint count)
    {
        using var mapped = pool.Coordinates.MapRange<int>(slot * pool.EntryCapacity * 16,
            checked((int)count * 4), BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        var cells = new HashSet<(int, int, int)>();
        for (int i = 0; i < count; i++)
        {
            int offset = i * 4;
            Assert.Equal(0, mapped.Span[offset + 3]);
            Assert.True(cells.Add((mapped.Span[offset], mapped.Span[offset + 1], mapped.Span[offset + 2])));
        }
        return cells;
    }

    /// <summary>Matches the uploaded world table's wrapping FNV coordinate hash.</summary>
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

    /// <summary>Selects the intentionally injected whole-batch failure boundary.</summary>
    public enum SelectionFailure { Candidate, Work, PairCapacity }

    /// <summary>Identifies which half of the point-source terrain envelope a rejection omits.</summary>
    public enum PointEnvelopeFailure { SourceBox, Receiver }

    /// <summary>Owns the production world, candidate and page GPU programs used by one selection test.</summary>
    private sealed class SelectionScene : IDisposable
    {
        private readonly ShadowWorldPublication publication;
        private readonly ShadowChunkDomain domain;
        private readonly ShadowBounds receiver;
        private readonly ShadowCandidateChanges changes = new(1, 0, 0, true, ImmutableArray<ShadowChunkIdentity>.Empty);
        private readonly List<(ShadowCandidateHandle Handle, ShadowCandidateCurrent Current)> activeCandidates = new();

        #region Internal API
        /// <summary>Exposes the real candidate producer and uploaded native world to the selection under test.</summary>
        internal ShadowCandidatePool Candidates { get; }
        /// <summary>Exposes the uploaded current range table borrowed by the production selector.</summary>
        internal ShadowWorldGpuBuffers WorldBuffers { get; } = new();
        /// <summary>Keeps the real integer shadow pool's immutable batch token open for view packing.</summary>
        internal ShadowPagePool Pages { get; }

        /// <summary>Uploads a complete test world and opens bounded candidate and page submission owners.</summary>
        internal SelectionScene(Vintagestory.API.Common.ICoreAPI api, ShadowWorldPublication publication,
            int tilesPerLayer = 1)
        {
            this.publication = publication;
            int dimension = publication.Table.First(entry => entry.Flags != 0).Dimension;
            domain = new(dimension, publication.Table.Where(entry => entry.Flags != 0).Min(entry => entry.X), 0, 0,
                publication.Table.Where(entry => entry.Flags != 0).Max(entry => entry.X), 0, 0);
            receiver = domain.Bounds;
            Assert.True(WorldBuffers.Upload(publication));
            Candidates = ShadowCandidatePool.Create(api, CandidateSettings());
            Assert.True(Candidates.BeginBatch(changes, publication, WorldBuffers));
            Pages = ShadowPagePool.Create(api, new ShadowPageSettings
            {
                MemoryMiB = 4, PageSize = 8, TileColumns = tilesPerLayer, TileRows = 1, ArrayLayers = 1,
                ProbeLimit = 64, OperationsPerBatch = 16
            });
            Assert.True(Pages.BeginBatch());
        }

        /// <summary>Creates a complete real candidate header through the packaged candidate compute producer.</summary>
        internal ShadowViewCandidates Candidate(ShadowSourceKey source, long projectionGeneration)
        {
            var request = ShadowCandidateRequest.Directional(source, publication.Owner, publication.Generation, 1,
                domain, receiver, Vector3d.UnitZ, 179.9);
            Assert.True(Candidates.TryAllocate(request, out var handle));
            var current = new ShadowCandidateCurrent(source, publication.Owner, publication.Generation, 1,
                receiver, Vector3d.UnitZ, changes.Epoch, changes.ToSequence, projectionGeneration);
            activeCandidates.Add((handle, current));
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Assert.True(Candidates.BeginBatch(changes, publication, WorldBuffers));
                foreach (var active in activeCandidates)
                    Assert.True(Candidates.Advance(active.Handle, active.Current));
                bool complete = activeCandidates.All(active =>
                {
                    var header = ReadHeader(Candidates.Headers, active.Handle.Slot);
                    return header.Status == 3 && header.Unresolved == 0;
                });
                if (complete) return new(0, handle, current);
            }
            throw new Xunit.Sdk.XunitException("The test candidate did not complete within its finite dispatch bound.");
        }

        /// <summary>Builds a bounded point-source candidate with independently selected receiver and source motion bounds.</summary>
        internal ShadowViewCandidates PointCandidate(ShadowSourceKey source, long projectionGeneration,
            ShadowBounds requestReceiver, ShadowBounds sourceBox, ShadowBounds currentReceiver, Vector3d position)
        {
            var request = ShadowCandidateRequest.Point(source, publication.Owner, publication.Generation, 1,
                domain, requestReceiver, sourceBox, changes.Epoch, changes.ToSequence);
            Assert.True(Candidates.TryAllocate(request, out var handle));
            var current = new ShadowCandidateCurrent(source, publication.Owner, publication.Generation, 1,
                currentReceiver, position, changes.Epoch, changes.ToSequence, projectionGeneration);
            activeCandidates.Add((handle, current));
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Assert.True(Candidates.BeginBatch(changes, publication, WorldBuffers));
                foreach (var active in activeCandidates)
                    Assert.True(Candidates.Advance(active.Handle, active.Current));
                bool complete = activeCandidates.All(active =>
                {
                    var header = ReadHeader(Candidates.Headers, active.Handle.Slot);
                    return header.Status == 3 && header.Unresolved == 0;
                });
                if (complete || !request.Covers(current)) return new(0, handle, current);
            }
            return new(0, handle, current);
        }
        #endregion

        #region Public API
        /// <summary>Retires the page batch before disposing the selection dependencies it keeps current.</summary>
        public void Dispose()
        {
            if (Pages.CanSubmit) Pages.SealBatch();
            Candidates.Dispose();
            WorldBuffers.Dispose();
            Pages.Dispose();
        }
        #endregion

        #region Private
        /// <summary>Allocates one association with enough charged capacity for these tiny domains.</summary>
        private static ShadowCandidateSettings CandidateSettings() => new()
        {
            MemoryMiB = 8, HeaderCapacity = 8, SegmentsPerAssociation = 1, DedupCapacity = 128,
            ProbeLimit = 128, ChangeCapacity = 16, SlabsPerSlice = 64, CellsPerSlice = 64,
            ProbesPerSlice = 1024, ChangesPerSlice = 16, SlabsPerBatch = 256,
            CellsPerBatch = 4096, ProbesPerBatch = 16384, ChangesPerBatch = 64
        };
        #endregion
    }
    #endregion
}
