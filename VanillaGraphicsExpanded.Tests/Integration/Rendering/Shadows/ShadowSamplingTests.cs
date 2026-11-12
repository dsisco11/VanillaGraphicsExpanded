using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Sampling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Exercises the shared point-fetched GL 4.5 PCF shader with controlled synthetic depth pages.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowSamplingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Every directional tap uses virtual lookup through one array and whole-query fallback on misses.</summary>
    [Fact]
    public void DirectionalPcfCoversFourPagesAndFailsUnshadowedAsOneQuery()
    {
        // Arrange: place one receiver at the intersection of four pages in distinct layers of one integer array.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(layers: 4, tilesPerLayer: 1);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new DirectionalShadowProjection(Sun, 61, Vector3d.UnitZ,
            new ShadowBounds(-1024, -1024, -1024, 1024, 1024, 1024), baseTexelWidth: 1,
            levels: 1, pageSize: settings.PageSize);
        var request = ShadowSampleRequest.Directional(projection, 0, new Vector3d(127.5, 127.5, 0),
            Vector3d.UnitZ, 23, 29, 31, settings);
        Assert.True(pool.BeginBatch());

        // Act: dispatch once with no certificates, then derive expected page keys from independently known texel bounds.
        sampler.Sample(pool, request);
        var missing = Read(sampler.Results, 40);
        var missingVisibility = BitConverter.UInt32BitsToSingle(missing[0]);
        var missingKeys = Addresses(missing);

        // Assert: one missing tap invalidates the complete query, and page-edge taps span exactly four virtual pages.
        Assert.Equal(0x1ffu, missing[1]);
        Assert.Equal(1, missingVisibility);
        var expectedPages = new HashSet<(int Face, int X, int Y)>
        { (0, 0, 0), (0, 0, 1), (0, 1, 0), (0, 1, 1) };
        Assert.Equal(expectedPages, missingKeys.Select(x => (x.Face, x.X, x.Y)).ToHashSet());
        Assert.Equal(9u, missing[2]);

        // Act: allocate and publish every required virtual page with known fully-lit depth.
        var assignments = new Dictionary<(int Face, int X, int Y), (int Slot, long Generation, PhysicalPage Physical)>();
        foreach (var address in UniquePages(missingKeys))
            assignments.Add((address.Face, address.X, address.Y), PublishDepth(pool, request, address, 1f));

        // Assert: all four pages land in different array layers, with no sample failures.
        Assert.Equal(new[] { 0, 1, 2, 3 }, assignments.Values.Select(x => x.Physical.Layer).Order().ToArray());
        sampler.Sample(pool, request);
        var lit = Read(sampler.Results, 40);
        Assert.Equal(0u, lit[1]);
        Assert.Equal(9u, lit[2]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(lit[0]));
        Assert.All(Addresses(lit), address => Assert.InRange(address.Reference, 0.49f, 0.51f));

        // Act: change one authored tile to zero depth and sample again without changing its certificate.
        var darkPage = assignments.First().Key;
        var darkAssignment = assignments[darkPage];
        UploadDepth(pool, darkAssignment.Slot, 0f, pool.PageSize);
        sampler.Sample(pool, request);
        var mixed = Read(sampler.Results, 40);
        var mixedAddresses = Addresses(mixed);
        int visibleTaps = mixedAddresses.Count(address => (address.Face, address.X, address.Y) != darkPage);

        // Assert: explicit comparisons count only taps in lit physical pages; no filtered depth interpolation is used.
        Assert.Equal(0u, mixed[1]);
        Assert.Equal(visibleTaps / 9f, BitConverter.UInt32BitsToSingle(mixed[0]));

        // Act: retire only the upper-right virtual page, which owns exactly the noncenter final PCF tap.
        var noncenter = missingKeys.First(address => address.X == 1 && address.Y == 1);
        var missingCornerKey = request.PageRequest.Key with { FaceOrLevel = noncenter.Face, X = noncenter.X, Y = noncenter.Y };
        Assert.True(pool.Execute(Request(ShadowPageOperation.RetireKey, missingCornerKey, request)));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);
        sampler.Sample(pool, request);
        var oneMissingTap = Read(sampler.Results, 40);

        // Assert: the single missing off-center tap withdraws the entire filtered result to unshadowed visibility.
        Assert.Equal(1u << 8, oneMissingTap[1]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(oneMissingTap[0]));
        pool.SealBatch();
        Assert.Throws<InvalidOperationException>(() => sampler.Sample(pool, request));
    }

    /// <summary>Adjacent directional levels address the same receiver independently and both require complete PCF pages.</summary>
    [Fact]
    public void DirectionalClipmapLevelsSampleWorldConsistentReceiverIndependently()
    {
        // Arrange: select one world point at a clipmap page corner and create separate fine/coarse requests.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(layers: 4, tilesPerLayer: 2);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var receiver = new Vector3d(127.5, 127.5, 0);
        var projection = new DirectionalShadowProjection(Sun, 67, Vector3d.UnitZ,
            new ShadowBounds(-1024, -1024, -1024, 1024, 1024, 1024), baseTexelWidth: 1,
            levels: 2, pageSize: settings.PageSize);
        var fine = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 23, 29, 31, settings);
        var coarse = ShadowSampleRequest.Directional(projection, 1, receiver, Vector3d.UnitZ, 23, 29, 31, settings);
        Assert.False(fine.Footprint.Contains(coarse.Footprint));
        Assert.True(pool.BeginBatch());

        // Act: satisfy the fine-level page requests, then inspect the still-independent coarse-level demand.
        sampler.Sample(pool, fine);
        var fineDemand = Addresses(Read(sampler.Results, 40));
        Assert.Equal(4, fineDemand.Select(x => (x.Face, x.X, x.Y)).Distinct().Count());
        Assert.Equal(0x1ffu, Read(sampler.Results, 40)[1]);
        foreach (var address in UniquePages(fineDemand)) PublishDepth(pool, fine, address, 1f);
        sampler.Sample(pool, coarse);
        var coarseDemand = Addresses(Read(sampler.Results, 40));

        // Assert: the same absolute receiver maps to level-specific keys, and coarse pages are separately admitted.
        Assert.Equal(0, fine.PageRequest.Key.FaceOrLevel);
        Assert.Equal(1, coarse.PageRequest.Key.FaceOrLevel);
        Assert.True(fine.Footprint.Contains(new(receiver.X, receiver.Y, receiver.Z, receiver.X, receiver.Y, receiver.Z)));
        Assert.True(coarse.Footprint.Contains(new(receiver.X, receiver.Y, receiver.Z, receiver.X, receiver.Y, receiver.Z)));
        Assert.Equal(0x1ffu, Read(sampler.Results, 40)[1]);
        Assert.Single(coarseDemand.Select(x => (x.Face, x.X, x.Y)).Distinct());
        foreach (var address in UniquePages(coarseDemand)) PublishDepth(pool, coarse, address, 1f);
        sampler.Sample(pool, coarse);
        Assert.Equal(0u, Read(sampler.Results, 40)[1]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(Read(sampler.Results, 40)[0]));
        sampler.Sample(pool, fine);
        Assert.Equal(0u, Read(sampler.Results, 40)[1]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(Read(sampler.Results, 40)[0]));
        pool.SealBatch();
    }

    /// <summary>Point-light taps cross the +Z/+X cube edge and recompute reference depth for the destination face.</summary>
    [Fact]
    public void PointPcfRemapsFaceEdgesAndKeepsEveryTapAddressable()
    {
        // Arrange: choose a point receiver next to the +Z/+X cube seam and enable the four baseline texture layers.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(layers: 4, tilesPerLayer: 1);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new PointShadowProjection(Point, 71, Vector3d.Zero, near: 0.05, far: 30,
            faceResolution: 128, pageSize: settings.PageSize);
        var receiver = new Vector3d(10.15, 0.2, 10.1);
        var request = ShadowSampleRequest.Point(projection, receiver, Vector3d.UnitZ, 23, 29, 31, settings);
        Assert.True(pool.BeginBatch());

        // Act: observe tap demand before pages exist, then install one complete page for every returned face/page pair.
        sampler.Sample(pool, request);
        var initial = Read(sampler.Results, 40);
        var demanded = Addresses(initial);
        var faces = demanded.Select(x => x.Face).Distinct().ToHashSet();
        var expectedTaps = PointTapOracle(projection, receiver, Vector3d.UnitZ, settings);

        // Assert: the shifted receiver stays on +Z, edge taps reach +X, and each reference uses that destination face.
        Assert.Contains(4, faces);
        Assert.Contains(0, faces);
        Assert.All(demanded, tap => Assert.InRange(tap.Reference, 0, 1));
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(expectedTaps[i].Face, demanded[i].Face);
            Assert.Equal(expectedTaps[i].Depth, demanded[i].Reference, 5);
        }
        foreach (var address in UniquePages(demanded))
            PublishDepth(pool, request, address, 1f, ShadowProjectionSnapshot.Point(projection, address.Face));

        // Act: rerun full PCF with all requested source-face and remapped-face pages present.
        sampler.Sample(pool, request);
        var complete = Read(sampler.Results, 40);

        // Assert: every destination face/page lookup succeeds and all nine point fetches compare against valid depths.
        Assert.Equal(0u, complete[1]);
        Assert.Equal(9u, complete[2]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(complete[0]));
        Assert.Contains(4, Addresses(complete).Select(x => x.Face));
        Assert.Contains(0, Addresses(complete).Select(x => x.Face));
        pool.SealBatch();
    }

    /// <summary>Point filtering preserves dominant-axis ownership and finite depth across all six signed world axes.</summary>
    [Theory]
    [InlineData(0, 10.0, 0.0, 0.0)]
    [InlineData(1, -10.0, 0.0, 0.0)]
    [InlineData(2, 0.0, 10.0, 0.0)]
    [InlineData(3, 0.0, -10.0, 0.0)]
    [InlineData(4, 0.0, 0.0, 10.0)]
    [InlineData(5, 0.0, 0.0, -10.0)]
    public void PointPcfSamplesEverySignedFaceCenter(int expectedFace, double x, double y, double z)
    {
        // Arrange: place one receiver on each signed axis so the filter stays away from cube-face seams.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings();
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new PointShadowProjection(Point, 79, Vector3d.Zero, near: 0.05, far: 30,
            faceResolution: 128, pageSize: settings.PageSize);
        var receiver = new Vector3d(x, y, z);
        var normal = Vector3d.Normalize(receiver);
        var request = ShadowSampleRequest.Point(projection, receiver, normal, 23, 29, 31, settings);
        Assert.True(pool.BeginBatch());

        // Act: capture unbacked tap addresses, then publish the matching face page with dark depth.
        sampler.Sample(pool, request);
        var demandedResult = Read(sampler.Results, 40);
        Assert.Equal(0x1ffu, demandedResult[1]);
        Assert.Equal(9u, demandedResult[2]);
        var demanded = Addresses(demandedResult);
        var oracle = PointTapOracle(projection, receiver, normal, settings);
        Assert.All(demanded, tap => Assert.Equal(expectedFace, tap.Face));
        Assert.All(demanded, tap => Assert.InRange(tap.Reference, 0f, 1f));
        Assert.All(oracle, tap => Assert.Equal(expectedFace, tap.Face));
        foreach (var address in UniquePages(demanded))
            PublishDepth(pool, request, address, 0f, ShadowProjectionSnapshot.Point(projection, expectedFace));

        // Assert: matching point-face certificates resolve every tap and the far receiver is fully shadowed.
        sampler.Sample(pool, request);
        var sampled = Read(sampler.Results, 40);
        Assert.Equal(0u, sampled[1]);
        Assert.Equal(9u, sampled[2]);
        Assert.Equal(0, BitConverter.UInt32BitsToSingle(sampled[0]));
        Assert.All(Addresses(sampled), tap => Assert.Equal(expectedFace, tap.Face));
        Assert.All(Addresses(sampled), tap => Assert.InRange(tap.Reference, 0f, 1f));
        pool.SealBatch();
    }

    /// <summary>A changed world dependency withdraws a previously shadowed result to diagnosed unshadowed visibility.</summary>
    [Fact]
    public void StaleWorldDependencyWithdrawsPreviouslyShadowedVisibility()
    {
        // Arrange: publish one directional page certificate and open a consumer interval for the matching query.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(pageSize: 128, layers: 1, tilesPerLayer: 8);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new DirectionalShadowProjection(Sun, 81, Vector3d.UnitZ,
            new ShadowBounds(-100, -100, -100, 100, 100, 100), baseTexelWidth: 1, pageSize: 128);
        var request = ShadowSampleRequest.Directional(projection, 0, new Vector3d(30, 30, 10),
            Vector3d.UnitZ, 23, 29, 31, settings);
        Assert.True(pool.BeginBatch());
        sampler.Sample(pool, request);
        var taps = UniquePages(Addresses(Read(sampler.Results, 40)));
        foreach (var tap in taps) PublishDepth(pool, request, tap, 0.1f);

        // Act: verify finite resident depths shadow the current request, then change only its world revision.
        sampler.Sample(pool, request);
        var current = Read(sampler.Results, 40);
        var stale = ShadowSampleRequest.Directional(projection, 0, new Vector3d(30, 30, 10),
            Vector3d.UnitZ, 24, 29, 31, settings);
        sampler.Sample(pool, stale);
        var staleLookup = Read(sampler.Results, 40);

        // Assert: valid resident pages shadow the current world; stale world certificates fail open as one query.
        Assert.Equal(0u, current[1]);
        Assert.Equal(0, BitConverter.UInt32BitsToSingle(current[0]));
        Assert.Equal(0x1ffu, staleLookup[1]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(staleLookup[0]));
        pool.SealBatch();
    }

    /// <summary>Only explicitly invalidated directional pages reuse depth within both age and angular limits.</summary>
    [Fact]
    public void DirectionalTemporalReuseRequiresInvalidationAndHonorsStrictAndMotionLimits()
    {
        // Arrange: render complete pages for one directional mapping, then sample them while their dependencies are current.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings();
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var bounds = new ShadowBounds(-1024, -1024, -1024, 1024, 1024, 1024);
        var renderedProjection = new DirectionalShadowProjection(Sun, 101, Vector3d.UnitZ, bounds,
            baseTexelWidth: 1, levels: 1, pageSize: settings.PageSize);
        var receiver = new Vector3d(12, 20, 30);
        var rendered = ShadowSampleRequest.Directional(renderedProjection, 0, receiver, Vector3d.UnitZ,
            41, 43, 47, settings);
        Assert.True(pool.BeginBatch());
        sampler.Sample(pool, rendered);
        var requestedPages = UniquePages(Addresses(Read(sampler.Results, 40)));
        Assert.NotEmpty(requestedPages);
        foreach (var address in requestedPages) PublishDepth(pool, rendered, address, PatternDepth(settings.PageSize));
        sampler.Sample(pool, rendered);
        var current = Read(sampler.Results, 40);
        Assert.Equal(0u, current[1]);
        Assert.Equal(0u, current[3]);
        Assert.Equal(PatternVisibility(renderedProjection, receiver, settings.PageSize, Addresses(current)),
            BitConverter.UInt32BitsToSingle(current[0]));

        // Act: change scene dependencies, explicitly invalidate every requested page, and move the sun slightly.
        pool.SealBatch();
        Assert.True(pool.BeginBatch());
        var changed = ShadowSampleRequest.Directional(renderedProjection, 0, receiver, Vector3d.UnitZ,
            42, 43, 48, settings);
        foreach (var address in requestedPages)
        {
            var key = changed.PageRequest.Key with { FaceOrLevel = address.Face, X = address.X, Y = address.Y };
            var invalidation = changed.PageRequest with
            {
                Operation = ShadowPageOperation.Invalidate,
                Key = key,
                Timestamp = 0.02
            };
            Assert.True(pool.Execute(invalidation));
            Assert.Equal(0u, Read(pool.Results, 8)[0]);
        }
        var slightlyMoved = renderedProjection.Update(Vector3d.Normalize(new(0.003, 0, 1)), bounds);
        var allowed = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(slightlyMoved, 0), 0.08,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 0.5 }));
        sampler.Sample(pool, allowed);
        var retained = Read(sampler.Results, 40);

        // Assert: a fully covered query can reuse retained depth inside the fixed age and angular budgets.
        Assert.Equal(0u, retained[1]);
        Assert.Equal(0x1ffu, retained[3]);
        Assert.Equal(PatternVisibility(renderedProjection, receiver, settings.PageSize, Addresses(retained)),
            BitConverter.UInt32BitsToSingle(retained[0]));

        // Act: repeat invalidation after the first deadline has started, and request a receiver beyond retained coverage.
        foreach (var address in requestedPages)
        {
            var key = changed.PageRequest.Key with { FaceOrLevel = address.Face, X = address.X, Y = address.Y };
            Assert.True(pool.Execute(changed.PageRequest with
            {
                Operation = ShadowPageOperation.Invalidate, Key = key, Timestamp = 0.1
            }));
            Assert.Equal(0u, Read(pool.Results, 8)[0]);
        }
        var repeatedInvalidation = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(slightlyMoved, 0), 0.13,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 0.5 }));
        sampler.Sample(pool, repeatedInvalidation);
        var repeatedResult = Read(sampler.Results, 40);
        var outsideCoverage = ShadowSampleRequest.Directional(renderedProjection, 0,
            new(receiver.X, receiver.Y, receiver.Z + 10), Vector3d.UnitZ, 42, 43, 48, settings)
            .WithTemporal(new ShadowTemporalQuery(ShadowProjectionSnapshot.Directional(slightlyMoved, 0), 0.08,
                new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 0.5 }));
        sampler.Sample(pool, outsideCoverage);
        var uncoveredResult = Read(sampler.Results, 40);

        // Act: query the unchanged invalidation with an expired clock, strict zero-age policy, and excessive sun motion.
        var expired = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(slightlyMoved, 0), 0.15,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 0.5 }));
        sampler.Sample(pool, expired);
        var expiredResult = Read(sampler.Results, 40);
        var strict = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(slightlyMoved, 0), 0.05,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0, MaxDirectionalAngleDegrees = 0.5 }));
        sampler.Sample(pool, strict);
        var strictResult = Read(sampler.Results, 40);
        var turnedTooFar = renderedProjection.Update(Vector3d.Normalize(new(0.03, 0, 1)), bounds);
        var movedResultRequest = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(turnedTooFar, 0), 0.08,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxDirectionalAngleDegrees = 0.5 }));
        sampler.Sample(pool, movedResultRequest);
        var movedResult = Read(sampler.Results, 40);

        // Assert: each fail-closed condition diagnoses every tap and reports fully lit fallback visibility.
        foreach (var unavailable in new[] { repeatedResult, uncoveredResult, expiredResult, strictResult, movedResult })
        {
            Assert.Equal(0x1ffu, unavailable[1]);
            Assert.Equal(0u, unavailable[3]);
            Assert.Equal(1, BitConverter.UInt32BitsToSingle(unavailable[0]));
        }
        var wrongLifetime = new DirectionalShadowProjection(Sun with { Lifetime = Sun.Lifetime + 1 }, 102,
            Vector3d.UnitZ, bounds, baseTexelWidth: 1, levels: 1, pageSize: settings.PageSize);
        Assert.Throws<ArgumentException>(() => changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(wrongLifetime, 0), 0.08, new ShadowTemporalSettings())));
        pool.SealBatch();
    }

    /// <summary>Rendered-age expiry is independent of a recently started stale-age window.</summary>
    [Fact]
    public void DirectionalTemporalReuseExpiresByRenderedAgeBeforeStaleAge()
    {
        // Arrange: render one page at time zero and invalidate it shortly before rendered-age expiry.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings();
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var bounds = new ShadowBounds(-1024, -1024, -1024, 1024, 1024, 1024);
        var projection = new DirectionalShadowProjection(Sun, 105, Vector3d.UnitZ, bounds,
            baseTexelWidth: 1, levels: 1, pageSize: settings.PageSize);
        var receiver = new Vector3d(12, 20, 30);
        var rendered = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 61, 63, 67, settings);
        Assert.True(pool.BeginBatch());
        sampler.Sample(pool, rendered);
        foreach (var address in UniquePages(Addresses(Read(sampler.Results, 40))))
            PublishDepth(pool, rendered, address, PatternDepth(settings.PageSize));

        // Act: invalidate at 0.22 and sample at 0.26, so stale age is 0.04 while rendered age is 0.26.
        pool.SealBatch();
        Assert.True(pool.BeginBatch());
        var changed = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 62, 63, 68, settings);
        foreach (var address in UniquePages(Addresses(Read(sampler.Results, 40))))
        {
            var key = changed.PageRequest.Key with { FaceOrLevel = address.Face, X = address.X, Y = address.Y };
            Assert.True(pool.Execute(changed.PageRequest with
            {
                Operation = ShadowPageOperation.Invalidate, Key = key, Timestamp = 0.22
            }));
            Assert.Equal(0u, Read(pool.Results, 8)[0]);
        }
        sampler.Sample(pool, changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(projection, 0), 0.26,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1 })));
        var expired = Read(sampler.Results, 40);

        // Assert: fresh invalidation time cannot extend a result past its independent maximum rendered age.
        Assert.Equal(0x1ffu, expired[1]);
        Assert.Equal(0u, expired[3]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(expired[0]));
        pool.SealBatch();
    }

    /// <summary>An eligible retained page remains sampleable when its one-tile pool cannot admit a replacement.</summary>
    [Fact]
    public void EligibleDirectionalDepthSurvivesReplacementCapacityExhaustion()
    {
        // Arrange: fill the only resident tile with dark depth, then change the page's scene dependency.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(tilesPerLayer: 1, layers: 1);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new DirectionalShadowProjection(Sun, 107, Vector3d.UnitZ,
            new ShadowBounds(-1024, -1024, -1024, 1024, 1024, 1024), baseTexelWidth: 1,
            levels: 1, pageSize: settings.PageSize);
        var receiver = new Vector3d(12, 20, 30);
        var rendered = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 71, 73, 77, settings);
        Assert.True(pool.BeginBatch());
        sampler.Sample(pool, rendered);
        var page = Assert.Single(UniquePages(Addresses(Read(sampler.Results, 40))));
        PublishDepth(pool, rendered, page, 0.1f);

        // Act: invalidate and fail replacement allocation because the retained assignment owns the only tile.
        pool.SealBatch();
        Assert.True(pool.BeginBatch());
        var changed = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 72, 73, 78, settings);
        var key = changed.PageRequest.Key with { FaceOrLevel = page.Face, X = page.X, Y = page.Y };
        Assert.True(pool.Execute(changed.PageRequest with { Operation = ShadowPageOperation.Invalidate, Key = key, Timestamp = 0.02 }));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);
        Assert.True(pool.Execute(changed.PageRequest with { Operation = ShadowPageOperation.Allocate, Key = key }));
        Assert.Equal(3u, Read(pool.Results, 8)[0]);
        sampler.Sample(pool, changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Directional(projection, 0), 0.04,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1 })));
        var retained = Read(sampler.Results, 40);

        // Assert: exhaustion leaves the eligible dark assignment intact, and sampling identifies every tap as stale.
        Assert.Equal(0u, retained[1]);
        Assert.Equal(0x1ffu, retained[3]);
        Assert.Equal(0, BitConverter.UInt32BitsToSingle(retained[0]));
        pool.SealBatch();
    }

    /// <summary>Replacement depth and current metadata become visible together only after complete publication.</summary>
    [Fact]
    public void DirectionalReplacementPublicationSwitchesFromRetainedToCurrentDepthAtomically()
    {
        // Arrange: render one dark complete page, then invalidate it while retaining its sampleable assignment.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(tilesPerLayer: 1, layers: 2);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
                var projection = new DirectionalShadowProjection(Sun, 109, Vector3d.UnitZ,
            new ShadowBounds(-1024, -1024, -1024, 1024, 1024, 1024), baseTexelWidth: 1,
            levels: 1, pageSize: settings.PageSize);
        var receiver = new Vector3d(12, 20, 30);
        var rendered = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 81, 83, 87, settings);
        Assert.True(pool.BeginBatch());
        sampler.Sample(pool, rendered);
        var page = Assert.Single(UniquePages(Addresses(Read(sampler.Results, 40))));
        PublishDepth(pool, rendered, page, 0.1f);

        // Act: begin a complete replacement while the old assignment remains eligible for stale sampling.
        pool.SealBatch();
        Assert.True(pool.BeginBatch());
        var changed = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 82, 83, 88, settings);
        var key = changed.PageRequest.Key with { FaceOrLevel = page.Face, X = page.X, Y = page.Y };
        Assert.True(pool.Execute(changed.PageRequest with { Operation = ShadowPageOperation.Invalidate, Key = key, Timestamp = 0.02 }));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);
        Assert.True(pool.Execute(changed.PageRequest with { Operation = ShadowPageOperation.Require, Key = key }));
        Assert.True(pool.Execute(changed.PageRequest with { Operation = ShadowPageOperation.Allocate, Key = key }));
        var replacement = Read(pool.Results, 8);
        Assert.Equal(0u, replacement[0]);
        int slot = checked((int)replacement[1]);
        long generation = unchecked((long)(((ulong)replacement[3] << 32) | replacement[2]));
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        UploadDepth(pool, slot, 1f, pool.PageSize);

        var temporal = changed.WithTemporal(new ShadowTemporalQuery(ShadowProjectionSnapshot.Directional(projection, 0), 0.04,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1 }));
        sampler.Sample(pool, temporal);
        var beforePublish = Read(sampler.Results, 40);
        Assert.True(pool.Execute(changed.PageRequest with
        {
            Operation = ShadowPageOperation.Publish, Key = key, Region = changed.Footprint,
            Assignment = generation, Complete = true, Timestamp = 0.04
        }));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);
        sampler.Sample(pool, temporal);
        var afterPublish = Read(sampler.Results, 40);

        // Assert: old dark depth remains stale until the complete switch, then new depth is current and fully lit.
        Assert.Equal(0u, beforePublish[1]);
        Assert.Equal(0x1ffu, beforePublish[3]);
        Assert.Equal(0, BitConverter.UInt32BitsToSingle(beforePublish[0]));
        Assert.Equal(0u, afterPublish[1]);
        Assert.Equal(0u, afterPublish[3]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(afterPublish[0]));
        pool.SealBatch();
    }

    /// <summary>Retained point depth fails closed once the current source moves beyond its configured world-space limit.</summary>
    [Fact]
    public void PointTemporalReuseRejectsExcessiveSourceTranslation()
    {
        // Arrange: render and certify one point-light face with a receiver centered well inside the face.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings();
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var renderedProjection = new PointShadowProjection(Point, 111, Vector3d.Zero,
            near: 0.05, far: 30, faceResolution: 128, pageSize: settings.PageSize);
        var receiver = new Vector3d(0, 0, 10);
        var rendered = ShadowSampleRequest.Point(renderedProjection, receiver, Vector3d.UnitZ,
            51, 53, 59, settings);
        Assert.True(pool.BeginBatch());
        sampler.Sample(pool, rendered);
        var requestedPages = UniquePages(Addresses(Read(sampler.Results, 40)));
        foreach (var address in requestedPages)
            PublishDepth(pool, rendered, address, 0.1f, ShadowProjectionSnapshot.Point(renderedProjection, address.Face));
        sampler.Sample(pool, rendered);
        Assert.Equal(0u, Read(sampler.Results, 40)[1]);

        // Act: invalidate each existing face page, then query with the same retained cube mapping and a moved point source.
        pool.SealBatch();
        Assert.True(pool.BeginBatch());
        var changed = ShadowSampleRequest.Point(renderedProjection, receiver, Vector3d.UnitZ,
            52, 53, 60, settings);
        foreach (var address in requestedPages)
        {
            var key = changed.PageRequest.Key with { FaceOrLevel = address.Face, X = address.X, Y = address.Y };
            Assert.True(pool.Execute(changed.PageRequest with
            {
                Operation = ShadowPageOperation.Invalidate, Key = key, Timestamp = 0.02,
                Projection = ShadowProjectionSnapshot.Point(renderedProjection, address.Face)
            }));
            Assert.Equal(0u, Read(pool.Results, 8)[0]);
        }
        var movedProjection = new PointShadowProjection(Point, 112, new Vector3d(0.2, 0, 0),
            near: 0.05, far: 30, faceResolution: 128, pageSize: settings.PageSize);
        var withinTolerance = new PointShadowProjection(Point, 112, new Vector3d(0.1, 0, 0),
            near: 0.05, far: 30, faceResolution: 128, pageSize: settings.PageSize);
        var retained = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Point(withinTolerance, changed.PageRequest.Key.FaceOrLevel), 0.04,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxPointTranslationMetres = 0.15 }));
        sampler.Sample(pool, retained);
        var retainedResult = Read(sampler.Results, 40);
        var moved = changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Point(movedProjection, changed.PageRequest.Key.FaceOrLevel), 0.04,
            new ShadowTemporalSettings { MaxRenderedAgeSeconds = 0.25, MaxStaleAgeSeconds = 0.1, MaxPointTranslationMetres = 0.15 }));
        sampler.Sample(pool, moved);
        var unavailable = Read(sampler.Results, 40);

        // Assert: permitted source motion reuses all retained taps, while excessive motion reports all tap failures.
        Assert.Equal(0u, retainedResult[1]);
        Assert.Equal(0x1ffu, retainedResult[3]);
        Assert.Equal(0, BitConverter.UInt32BitsToSingle(retainedResult[0]));
        Assert.Equal(0x1ffu, unavailable[1]);
        Assert.Equal(0u, unavailable[3]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(unavailable[0]));
        var wrongLifetime = new PointShadowProjection(Point with { Lifetime = Point.Lifetime + 1 }, 113,
            Vector3d.Zero, near: 0.05, far: 30, faceResolution: 128, pageSize: settings.PageSize);
        Assert.Throws<ArgumentException>(() => changed.WithTemporal(new ShadowTemporalQuery(
            ShadowProjectionSnapshot.Point(wrongLifetime, changed.PageRequest.Key.FaceOrLevel), 0.04, new ShadowTemporalSettings())));
        pool.SealBatch();
    }

    /// <summary>Double world origins preserve large distinct virtual pages through the baseline GPU sampler path.</summary>
    [Fact]
    public void LargeWorldOriginsKeepDirectionalPageAddressesDistinct()
    {
        // Arrange: use legal int32 page keys far from origin and a four-tap page intersection.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(pageSize: 128, layers: 1, tilesPerLayer: 4);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new DirectionalShadowProjection(Sun, 91, Vector3d.UnitZ,
            new ShadowBounds(-7e10, -7e10, -7e10, 7e10, 7e10, 7e10), baseTexelWidth: 1,
            levels: 1, pageSize: settings.PageSize);
        // The signed chunk origin remains representable while the virtual texel coordinates exceed int32 magnitude.
        var receiver = new Vector3d(-68_719_476_608.5, -68_719_476_608.5, 500_000_000.25);
        var request = ShadowSampleRequest.Directional(projection, 0, receiver,
            Vector3d.UnitZ, 23, 29, 31, settings);
        Assert.True(pool.BeginBatch());

        // Act: demand the four pages without certificates, then publish a different authored depth at every texel.
        sampler.Sample(pool, request);
        var result = Read(sampler.Results, 40);
        var addresses = Addresses(result);

        // Assert: expected page coordinates retain sub-page offsets at large positive and negative world positions.
        Assert.Equal(0x1ffu, result[1]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(result[0]));
        Assert.Equal(-536_870_912, request.PageRequest.Key.X);
        Assert.Equal(-536_870_912, request.PageRequest.Key.Y);
        Assert.Equal(new[] { -536_870_912, -536_870_911 }, addresses.Select(x => x.X).Distinct().Order().ToArray());
        Assert.Equal(new[] { -536_870_912, -536_870_911 }, addresses.Select(x => x.Y).Distinct().Order().ToArray());
        foreach (var address in UniquePages(addresses)) PublishDepth(pool, request, address, PatternDepth(settings.PageSize));

        // Act: sample those pages and independently derive each tap's expected local texel by Euclidean page division.
        sampler.Sample(pool, request);
        var fetched = Read(sampler.Results, 40);
        var fetchedAddresses = Addresses(fetched);
        int visible = 0;
        for (int i = 0; i < 9; i++)
        {
            double texelX = Math.Floor(receiver.X) + i % 3 - 1;
            double texelY = Math.Floor(receiver.Y) + i / 3 - 1;
            int pageX = (int)Math.Floor(texelX / settings.PageSize);
            int pageY = (int)Math.Floor(texelY / settings.PageSize);
            int localX = checked((int)(texelX - (double)pageX * settings.PageSize));
            int localY = checked((int)(texelY - (double)pageY * settings.PageSize));
            float depth = PatternDepth(localX, localY);
            Assert.Equal(pageX, fetchedAddresses[i].X);
            Assert.Equal(pageY, fetchedAddresses[i].Y);
            visible += fetchedAddresses[i].Reference <= depth ? 1 : 0;
        }

        // Assert: real per-texel point fetches preserve signed page seams, Euclidean local coordinates and explicit compares.
        Assert.Equal(0u, fetched[1]);
        Assert.Equal(visible / 9f, BitConverter.UInt32BitsToSingle(fetched[0]));
        Assert.InRange(visible, 1, 8);
        pool.SealBatch();
    }

    /// <summary>GPU sampling accepts interior texels on the last signed page and fails only taps beyond its range.</summary>
    [Fact]
    public void PositiveVirtualPageLimitSamplesInteriorAndRejectsOnlyOverflowingTaps()
    {
        // Arrange: use legal world chunk origins while projecting into the final positive int32 virtual page.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        const int pageSize = 128;
        const int lastPage = int.MaxValue;
        var settings = Settings(pageSize: pageSize, layers: 1, tilesPerLayer: 2);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new DirectionalShadowProjection(Sun, 121, Vector3d.UnitZ,
            new ShadowBounds(-2e10, -2e10, -100, 2e10, 2e10, 100), baseTexelWidth: 0.05,
            levels: 1, pageSize: pageSize);
        double texelBase = (double)lastPage * pageSize;
        var interiorReceiver = new Vector3d((texelBase + 64) * 0.05, (texelBase + 64) * 0.05, 0);
        var interior = ShadowSampleRequest.Directional(projection, 0, interiorReceiver,
            Vector3d.UnitZ, 91, 93, 97, settings);
        var edgeReceiver = new Vector3d((texelBase + pageSize - 0.5) * 0.05, (texelBase + 64) * 0.05, 0);
        var edge = ShadowSampleRequest.Directional(projection, 0, edgeReceiver,
            Vector3d.UnitZ, 91, 93, 97, settings);
        var coverage = new ShadowBounds(
            Math.Min(interior.Footprint.MinX, edge.Footprint.MinX),
            Math.Min(interior.Footprint.MinY, edge.Footprint.MinY),
            Math.Min(interior.Footprint.MinZ, edge.Footprint.MinZ),
            Math.Max(interior.Footprint.MaxX, edge.Footprint.MaxX),
            Math.Max(interior.Footprint.MaxY, edge.Footprint.MaxY),
            Math.Max(interior.Footprint.MaxZ, edge.Footprint.MaxZ));
        Assert.Equal(lastPage, interior.PageRequest.Key.X);
        Assert.Equal(lastPage, interior.PageRequest.Key.Y);
        Assert.True(pool.BeginBatch());

        // Act: inspect the missing last-page taps, then install the exact legal page and sample its patterned depths.
        sampler.Sample(pool, interior);
        var missing = Read(sampler.Results, 40);
        var page = Assert.Single(UniquePages(Addresses(missing)));
        Assert.Equal((0, lastPage, lastPage), (page.Face, page.X, page.Y));
        PublishDepth(pool, interior, page, PatternDepth(pageSize), coverage: coverage);
        sampler.Sample(pool, interior);
        var fetched = Read(sampler.Results, 40);

        // Assert: every interior tap addresses the last legal page and produces a real comparison result.
        Assert.Equal(0u, fetched[1]);
        Assert.Equal(lastPage, Addresses(fetched)[0].X);
        Assert.Equal(lastPage, Addresses(fetched)[0].Y);

        // Act: move only the X tap center to the final texel so its right column crosses the signed page limit.
        sampler.Sample(pool, edge);
        var overflowing = Read(sampler.Results, 40);

        // Assert: only the rightmost column is explicitly missing; no neighboring legal page is rejected.
        Assert.Equal(0x124u, overflowing[1]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(overflowing[0]));
        pool.SealBatch();
    }
    #endregion

    #region Private
    private static readonly ShadowSourceKey Sun = new(23, 0, ShadowSourceKind.Sun, 3, 29, 0);
    private static readonly ShadowSourceKey Point = new(23, 0, ShadowSourceKind.Hand, 3, 31, 2);
    #region Sample Request Setup and Result Decoding
    /// <summary>Creates a small fixed-capacity array for deterministic synthetic depth tests.</summary>
    private static ShadowPageSettings Settings(int pageSize = 128, int tilesPerLayer = 1, int layers = 4) => new()
    { MemoryMiB = 16, PageSize = pageSize, TileColumns = tilesPerLayer, TileRows = 1, ArrayLayers = layers,
        ProbeLimit = 128, OperationsPerBatch = 256 };
    /// <summary>Reads an explicitly requested result prefix after ordered GPU execution.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer, int words)
    {
        using var mapped = buffer.MapRange<uint>(0, words, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped); return mapped.Span.ToArray();
    }
    /// <summary>Builds a retirement/query request with the same exact world and scene certificate as the sample.</summary>
    private static ShadowPageRequest Request(ShadowPageOperation operation, ShadowPageKey key, ShadowSampleRequest sample) =>
        new(operation, key, sample.Footprint, sample.PageRequest.WorldRevision,
            sample.PageRequest.SceneEpoch, sample.PageRequest.SceneSequence,
            Projection: sample.PageRequest.Projection, Timestamp: sample.PageRequest.Timestamp);
    /// <summary>Decodes the nine test-only face/page/depth outputs without treating them as runtime readback.</summary>
    private static (int Face, int X, int Y, float Reference)[] Addresses(uint[] words) => Enumerable.Range(0, 9)
        .Select(i => (unchecked((int)words[4 + i * 4]), unchecked((int)words[5 + i * 4]),
            unchecked((int)words[6 + i * 4]), BitConverter.UInt32BitsToSingle(words[7 + i * 4])))
        .ToArray();
    /// <summary>Evaluates each captured tap against independently authored retained texels in the rendered projection.</summary>
    private static float PatternVisibility(DirectionalShadowProjection projection, Vector3d receiver, int pageSize,
        (int Face, int X, int Y, float Reference)[] taps)
    {
        var center = projection.Project(receiver, 0);
        int visible = 0;
        for (int i = 0; i < taps.Length; i++)
        {
            int localX = checked((int)Math.Floor(center.X) + i % 3 - 1 - taps[i].X * pageSize);
            int localY = checked((int)Math.Floor(center.Y) + i / 3 - 1 - taps[i].Y * pageSize);
            if (taps[i].Reference <= PatternDepth(localX, localY)) visible++;
        }
        return visible / 9f;
    }
    /// <summary>Deduplicates page demand by virtual identity, since each tap carries its own depth reference.</summary>
    private static (int Face, int X, int Y, float Reference)[] UniquePages(IEnumerable<(int Face, int X, int Y, float Reference)> addresses) =>
        addresses.GroupBy(address => (address.Face, address.X, address.Y)).Select(group => group.First()).ToArray();
    #endregion
    #region Independent Point Tap Reference
    /// <summary>Calculates point tap rays analytically and compares their destination-face perspective depths.</summary>
    private static (int Face, double Depth)[] PointTapOracle(PointShadowProjection projection, Vector3d receiver,
        Vector3d normal, ShadowPageSettings settings)
    {
        double receiverDistance = Math.Sqrt(Vector3d.Dot(receiver - projection.Position, receiver - projection.Position));
        double width = 2 * receiverDistance / projection.FaceResolution;
        var shifted = receiver + normal * (settings.NormalBiasTexels * width);
        double radius = Math.Sqrt(Vector3d.Dot(shifted - projection.Position, shifted - projection.Position));
        int original = PointShadowProjection.Face(shifted - projection.Position);
        var center = projection.Project(shifted, original);
        var towardLight = Vector3d.Normalize(projection.Position - shifted);
        double cosine = Math.Clamp(Math.Abs(Vector3d.Dot(normal, towardLight)), 0, 1);
        double slope = Math.Min(4, Math.Sqrt(Math.Max(0, 1 - cosine * cosine)) / Math.Max(0.1, cosine));
        double bias = Math.Min(4, settings.DepthBiasTexels + settings.SlopeBiasTexels * slope) * width;
        var expected = new List<(int Face, double Depth)>(9);
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            double pixelX = Math.Floor(center.X) + x + 0.5;
            double pixelY = Math.Floor(center.Y) + y + 0.5;
            var ray = PointShadowProjection.Direction(original,
                pixelX * (2.0 / projection.FaceResolution) - 1,
                pixelY * (2.0 / projection.FaceResolution) - 1);
            int face = PointShadowProjection.Face(ray);
            var target = projection.Position + Vector3d.Normalize(ray) * radius;
            double major = Math.Max(Math.Abs(target.X - projection.Position.X), Math.Max(
                Math.Abs(target.Y - projection.Position.Y), Math.Abs(target.Z - projection.Position.Z)));
            double depth = projection.Project(target, face).Z
                - bias * projection.Far * projection.Near / ((projection.Far - projection.Near) * major * major);
            expected.Add((face, depth));
        }
        return expected.ToArray();
    }
    #endregion
    #region Synthetic Page Allocation and Depth Upload
    /// <summary>Publishes a page after filling its complete depth tile with one constant comparison value.</summary>
    private static (int Slot, long Generation, PhysicalPage Physical) PublishDepth(ShadowPagePool pool, ShadowSampleRequest sample,
        (int Face, int X, int Y, float Reference) address, float depth)
        => PublishDepth(pool, sample, address, UniformDepth(pool.PageSize, depth));
    /// <summary>Publishes a page certificate only after uploading every caller-supplied texel depth.</summary>
    private static (int Slot, long Generation, PhysicalPage Physical) PublishDepth(ShadowPagePool pool, ShadowSampleRequest sample,
        (int Face, int X, int Y, float Reference) address, float depth, ShadowProjectionSnapshot projection)
        => PublishDepth(pool, sample, address, UniformDepth(pool.PageSize, depth), projection);
    /// <summary>Publishes one complete page using the exact face snapshot associated with its virtual identity.</summary>
    private static (int Slot, long Generation, PhysicalPage Physical) PublishDepth(ShadowPagePool pool, ShadowSampleRequest sample,
        (int Face, int X, int Y, float Reference) address, float[] depths,
        ShadowProjectionSnapshot? projection = null, ShadowBounds? coverage = null)
    {
        var key = sample.PageRequest.Key with { FaceOrLevel = address.Face, X = address.X, Y = address.Y };
        projection ??= sample.PageRequest.Projection;
        if (projection is null || projection.FaceOrLevel != address.Face)
            throw new ArgumentException("The page identity must use its matching rendered face snapshot.", nameof(address));
        var page = new ShadowPageRequest(ShadowPageOperation.Allocate, key, coverage ?? sample.Footprint,
            sample.PageRequest.WorldRevision, sample.PageRequest.SceneEpoch, sample.PageRequest.SceneSequence,
            Projection: projection, Timestamp: sample.PageRequest.Timestamp);
        if (!pool.Execute(page))
        {
            pool.SealBatch();
            Assert.True(pool.BeginBatch());
            Assert.True(pool.Execute(page));
        }
        var allocation = Read(pool.Results, 8);
        Assert.Equal(0u, allocation[0]);
        int slot = checked((int)allocation[1]);
        long generation = unchecked((long)(((ulong)allocation[3] << 32) | allocation[2]));
        Assert.True(pool.ClearReplacement());
        Assert.True(pool.FreezeWritableAssignments());
        UploadDepth(pool, slot, depths, pool.PageSize);
        Assert.True(pool.Execute(page with { Operation = ShadowPageOperation.Publish, Assignment = generation, Complete = true }));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);
        return (slot, generation, Physical(pool, slot));
    }
    /// <summary>Uploads one constant test depth into the explicitly assigned physical tile.</summary>
    private static void UploadDepth(ShadowPagePool pool, int slot, float value, int size) =>
        UploadDepth(pool, slot, UniformDepth(size, value), size);
    /// <summary>Uploads caller-authored test values into the explicitly assigned physical tile.</summary>
    private static void UploadDepth(ShadowPagePool pool, int slot, float[] depth, int size)
    {
        var physical = Physical(pool, slot);
        var texture = pool.DepthArray;
        uint[] encoded = depth.Select(ShadowDepthEncoding.Encode).ToArray();
        var encoding = RHICommandContext.Current.GetTextureEncoding(texture, PixelComponentType.UnsignedInt);
        RHICommandContext.Current.UploadTexture(texture,
            new(new(0, physical.Layer), physical.TileX * size, physical.TileY * size, 0, size, size),
            encoding, MemoryMarshal.AsBytes(encoded.AsSpan()));
    }
    /// <summary>Reads array layer and tile coordinates directly from the entry table for one GPU-selected assignment.</summary>
    private static PhysicalPage Physical(ShadowPagePool pool, int slot)
    {
        using var mapped = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return new(checked((int)mapped.Span[0]), checked((int)mapped.Span[1]), checked((int)mapped.Span[2]));
    }
    /// <summary>Stores the physical layer and 2D tile coordinates explicitly recorded for one page entry.</summary>
    private readonly record struct PhysicalPage(int Layer, int TileX, int TileY);
    /// <summary>Fills every texel in a page tile with one constant comparison depth.</summary>
    private static float[] UniformDepth(int size, float value) => Enumerable.Repeat(value, size * size).ToArray();
    /// <summary>Builds one resident page with a spatially varying depth pattern.</summary>
    private static float[] PatternDepth(int size) =>
        Enumerable.Range(0, size * size).Select(i => PatternDepth(i % size, i / size)).ToArray();
    /// <summary>Returns one authored texel value from a bounded, spatially varying depth pattern.</summary>
    private static float PatternDepth(int localX, int localY) => 0.05f + ((localX * 3 + localY * 5) % 17) * 0.05f;
    #endregion
    #endregion
}
