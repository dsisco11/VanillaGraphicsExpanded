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
    /// <summary>Every directional tap uses virtual lookup across four static sampler banks and whole-query fallback on misses.</summary>
    [Fact]
    public void DirectionalPcfCoversFourPagesAndFailsUnshadowedAsOneQuery()
    {
        // Arrange: place one receiver at the intersection of four pages backed by four independent array objects.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(arrays: 4, layers: 1);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        while (pool.ResidentArrays < 4) Assert.True(pool.Grow());
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
        var assignments = new Dictionary<(int Face, int X, int Y), (int Slot, long Generation)>();
        foreach (var address in UniquePages(missingKeys))
            assignments.Add((address.Face, address.X, address.Y), PublishDepth(pool, request, address, 1f));

        // Assert: all four pages land in different statically named sampler banks, with no sample failures.
        Assert.Equal(new[] { 0, 1, 2, 3 }, assignments.Values.Select(x => x.Slot).Order().ToArray());
        sampler.Sample(pool, request);
        var lit = Read(sampler.Results, 40);
        Assert.Equal(0u, lit[1]);
        Assert.Equal(9u, lit[2]);
        Assert.Equal(1, BitConverter.UInt32BitsToSingle(lit[0]));
        Assert.All(Addresses(lit), address => Assert.InRange(address.Reference, 0.49f, 0.51f));

        // Act: change one authored layer to zero depth and sample again without changing its certificate.
        var darkPage = assignments.First().Key;
        var darkAssignment = assignments[darkPage];
        UploadDepth(pool.DepthArray(darkAssignment.Slot / pool.LayersPerArray), 0f, darkAssignment.Slot % pool.LayersPerArray, pool.PageSize);
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
        var settings = Settings(arrays: 4, layers: 2);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        while (pool.ResidentArrays < 4) Assert.True(pool.Grow());
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
        // Arrange: choose a point receiver next to the +Z/+X cube seam and enable the four baseline texture banks.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(arrays: 4, layers: 1);
        using var pool = ShadowPagePool.Create(assets.Api, settings);
        while (pool.ResidentArrays < 4) Assert.True(pool.Grow());
        using var sampler = new ShadowSampler(assets.Api);
        var projection = new PointShadowProjection(Point, 71, Vector3d.Zero, near: 0.05, far: 30,
            faceResolution: 128, pageSize: settings.PageSize);
        var receiver = new Vector3d(10.1, 0.2, 9.99);
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
        foreach (var address in UniquePages(demanded)) PublishDepth(pool, request, address, 1f);

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

    /// <summary>A changed world dependency withdraws a previously shadowed result to diagnosed unshadowed visibility.</summary>
    [Fact]
    public void StaleWorldDependencyWithdrawsPreviouslyShadowedVisibility()
    {
        // Arrange: publish one directional page certificate and open a consumer interval for the matching query.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(pageSize: 128, arrays: 1, layers: 8);
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

    /// <summary>Double world origins preserve large distinct virtual pages through the baseline GPU sampler path.</summary>
    [Fact]
    public void LargeWorldOriginsKeepDirectionalPageAddressesDistinct()
    {
        // Arrange: use legal int32 page keys far from origin and a four-tap page intersection.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        var settings = Settings(pageSize: 128, arrays: 1, layers: 4);
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
    #endregion

    #region Private
    private static readonly ShadowSourceKey Sun = new(23, 0, ShadowSourceKind.Sun, 3, 29, 0);
    private static readonly ShadowSourceKey Point = new(23, 0, ShadowSourceKind.Hand, 3, 31, 2);
    #region Sample Request Setup and Result Decoding
    /// <summary>Creates a small four-sampler-compatible pool for deterministic synthetic depth tests.</summary>
    private static ShadowPageSettings Settings(int pageSize = 128, int arrays = 4, int layers = 1) => new()
    { MemoryMiB = 16, PageSize = pageSize, ArrayCapacity = arrays, LayersPerArray = layers,
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
            sample.PageRequest.SceneEpoch, sample.PageRequest.SceneSequence);
    /// <summary>Decodes the nine test-only face/page/depth outputs without treating them as runtime readback.</summary>
    private static (int Face, int X, int Y, float Reference)[] Addresses(uint[] words) => Enumerable.Range(0, 9)
        .Select(i => (unchecked((int)words[4 + i * 4]), unchecked((int)words[5 + i * 4]),
            unchecked((int)words[6 + i * 4]), BitConverter.UInt32BitsToSingle(words[7 + i * 4])))
        .ToArray();
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
    /// <summary>Publishes a page after filling its complete depth layer with one constant comparison value.</summary>
    private static (int Slot, long Generation) PublishDepth(ShadowPagePool pool, ShadowSampleRequest sample,
        (int Face, int X, int Y, float Reference) address, float depth)
        => PublishDepth(pool, sample, address, UniformDepth(pool.PageSize, depth));
    /// <summary>Publishes a page certificate only after uploading every caller-supplied texel depth.</summary>
    private static (int Slot, long Generation) PublishDepth(ShadowPagePool pool, ShadowSampleRequest sample,
        (int Face, int X, int Y, float Reference) address, float[] depths)
    {
        var key = sample.PageRequest.Key with { FaceOrLevel = address.Face, X = address.X, Y = address.Y };
        var page = new ShadowPageRequest(ShadowPageOperation.Allocate, key, sample.Footprint,
            sample.PageRequest.WorldRevision, sample.PageRequest.SceneEpoch, sample.PageRequest.SceneSequence);
        Assert.True(pool.Execute(page));
        var allocation = Read(pool.Results, 8);
        Assert.Equal(0u, allocation[0]);
        int slot = checked((int)allocation[1]);
        long generation = unchecked((long)(((ulong)allocation[3] << 32) | allocation[2]));
        UploadDepth(pool.DepthArray(slot / pool.LayersPerArray), depths, slot % pool.LayersPerArray, pool.PageSize);
        Assert.True(pool.Execute(page with { Operation = ShadowPageOperation.Publish, Assignment = generation, Complete = true }));
        Assert.Equal(0u, Read(pool.Results, 8)[0]);
        return (slot, generation);
    }
    /// <summary>Fills only one selected depth-array layer with an authored depth value.</summary>
    private static void UploadDepth(GpuTexture texture, float value, int layer, int size)
    {
        float[] depth = UniformDepth(size, value);
        UploadDepth(texture, depth, layer, size);
    }
    /// <summary>Uploads caller-authored values to exactly one selected resident depth layer.</summary>
    private static void UploadDepth(GpuTexture texture, float[] depth, int layer, int size)
    {
        var encoding = RHICommandContext.Current.GetTextureEncoding(texture, PixelComponentType.Float);
        RHICommandContext.Current.UploadTexture(texture, new(new(0, layer), 0, 0, 0, size, size),
            encoding, MemoryMarshal.AsBytes(depth.AsSpan()));
    }
    /// <summary>Fills every texel in a layer with one constant comparison depth.</summary>
    private static float[] UniformDepth(int size, float value) => Enumerable.Repeat(value, size * size).ToArray();
    /// <summary>Builds one resident page with a spatially varying depth pattern.</summary>
    private static float[] PatternDepth(int size) =>
        Enumerable.Range(0, size * size).Select(i => PatternDepth(i % size, i / size)).ToArray();
    /// <summary>Returns one authored texel value from a bounded, spatially varying depth pattern.</summary>
    private static float PatternDepth(int localX, int localY) => 0.05f + ((localX * 3 + localY * 5) % 17) * 0.05f;
    #endregion
    #endregion
}
