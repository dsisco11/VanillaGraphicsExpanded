using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;
using VanillaGraphicsExpanded.Rendering.Shadows.Sampling;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns a real current directional VSM page publication for isolated GPU consumer tests.</summary>
internal sealed class PublishedSurfaceShadowFixture : IDisposable
{
    private static readonly ShadowSourceKey Sun = new(23, 0, ShadowSourceKind.Sun, 3, 29, 0);
    private const int PageSize = 128;
    private readonly BinaryShaderApiFixture assets;
    private ShadowPagePool? pages;
    private ShadowReadinessPool? readiness;
    private ShadowFramePublication? publication;
    private SurfaceShadowInputSet? inputs;
    private ShadowLightSnapshot? lights;
    private bool batchOpen;
    private bool disposed;

    #region Public API
    /// <summary>Creates one published sun page at an optional camera origin, receiver position and direct irradiance.</summary>
    internal PublishedSurfaceShadowFixture(float pageDepth, Vector3d cameraOrigin = default, Vector3d? receiverRelative = null,
        (int Min, int Max)? shadowedColumns = null, bool useDemandCoverage = false, Vector3? solar = null)
    {
        assets = new BinaryShaderApiFixture();
        try
        {
            var pageSettings = new ShadowPageSettings
            {
                MemoryMiB = 16, PageSize = PageSize, TileColumns = 1, TileRows = 1,
                ArrayLayers = 1, ProbeLimit = 8, OperationsPerBatch = 16
            };
            var scheduling = new ShadowSchedulingSettings
            {
                Enabled = true, MemoryMiB = 16, LightCapacity = 1, DemandRequests = 1,
                RecoveryCompleteFrames = 1
            };
            var temporal = new ShadowTemporalSettings
            {
                MaxRenderedAgeSeconds = 1, MaxStaleAgeSeconds = 0, MaxDirectionalAngleDegrees = 0
            };
            var createdPages = ShadowPagePool.Create(assets.Api, pageSettings);
            pages = createdPages;
            var createdReadiness = new ShadowReadinessPool(assets.Api, scheduling, temporal);
            readiness = createdReadiness;
            const double now = 0.1;
            var receiver = cameraOrigin + (receiverRelative ?? new Vector3d(64, 64, -1));
            var projection = new DirectionalShadowProjection(Sun, 61, new Vector3d(0, 0, 1),
                new ShadowBounds(receiver.X - 100, receiver.Y - 100, receiver.Z - 100,
                    receiver.X + 100, receiver.Y + 100, receiver.Z + 100),
                baseTexelWidth: 1, levels: 1, pageSize: PageSize);
            var sample = ShadowSampleRequest.Directional(projection, 0, receiver, Vector3d.UnitZ, 13, 17, 19, pageSettings);
            ShadowDemandPage demandPage;
            if (useDemandCoverage)
            {
                // Build the coverage certificate through the same receiver demand path used by production.
                var budget = new ShadowFrameBudget(new(Requests: 64, Coverage: 64, Inputs: 8, Bytes: 65536), 0);
                var demand = new ShadowSurfaceDemand(budget, pageSettings, capacity: 64, cameraOrigin: cameraOrigin);
                var receiverBounds = new ShadowBounds(receiver.X, receiver.Y, receiver.Z,
                    receiver.X, receiver.Y, receiver.Z);
                demand.AddDirectional(new(ShadowSurfaceCategory.Opaque, receiverBounds), projection, projection,
                    level: 0, world: 13, epoch: 17, sequence: 19);
                demand.CompleteCategory(ShadowSurfaceCategory.All);
                if (!demand.Complete) throw new InvalidOperationException($"The production demand fixture was rejected: {demand.Failure}.");
                demandPage = demand.Pages.Single(page => page.Request.Key == sample.PageRequest.Key);
            }
            else
            {
                // Leave a wider certificate than the per-pixel PCF footprint for camera-rebase rounding tests.
                var coverage = new ShadowBounds(receiver.X - 16, receiver.Y - 16, receiver.Z - 16,
                    receiver.X + 16, receiver.Y + 16, receiver.Z + 16);
                var coverageRequest = sample.PageRequest with { Region = coverage };
                demandPage = new(coverageRequest, ShadowProjectionSnapshot.Directional(projection, 0));
            }
            var request = demandPage.Request with { Timestamp = now };
            demandPage = demandPage with { Request = request };
            if (!createdPages.BeginBatch()) throw new InvalidOperationException("Could not open the VSM fixture page batch.");
            batchOpen = true;
            if (!createdPages.Execute(request with { Operation = ShadowPageOperation.Allocate }))
                throw new InvalidOperationException("Could not allocate the VSM fixture page.");
            var allocation = ReadWords(createdPages.Results, 8);
            if (allocation[0] != 0) throw new InvalidOperationException("The VSM fixture page allocation failed.");
            int slot = checked((int)allocation[1]);
            long assignment = unchecked((long)(((ulong)allocation[3] << 32) | allocation[2]));
            if (!createdPages.ClearReplacement() || !createdPages.FreezeWritableAssignments())
                throw new InvalidOperationException("The VSM fixture page was not writable after allocation.");
            UploadDepth(createdPages, slot, pageDepth, shadowedColumns);
            if (!createdPages.Execute(request with { Operation = ShadowPageOperation.Publish, Assignment = assignment, Complete = true }))
                throw new InvalidOperationException("Could not publish the VSM fixture page contents.");

            createdReadiness.Publish(createdPages, 1, now, [new ShadowLightDemand(Sun, 0, true, true, [demandPage])], capabilitiesCurrent: true);
            var sunLight = new ShadowDirectionalLight(Sun, Vector3.UnitZ, solar ?? Vector3.One);
            var moonSource = new ShadowSourceKey(23, 0, ShadowSourceKind.Moon, 4, 30, 0);
            var moonLight = new ShadowDirectionalLight(moonSource, -Vector3.UnitZ, Vector3.Zero);
            var origin = SplitOrigin(cameraOrigin);
            var capturedLights = new ShadowLightSnapshot(1, 23, 0, [], sunLight, moonLight,
                [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
                origin.X, origin.Y, origin.Z, origin.Remainder);
            lights = capturedLights;
            var createdPublication = new ShadowFramePublication(capturedLights, createdPages, createdReadiness.Requests, createdReadiness.Results,
                createdReadiness.SampleFailures, createdReadiness.SourceIndices);
            publication = createdPublication;
            inputs = new SurfaceShadowInputSet(capturedLights, createdPublication, null, pageSettings);
        }
        catch
        {
            DisposePartial();
            throw;
        }
    }

    /// <summary>Provides the exact current publication's direct native-consumer inputs.</summary>
    internal SurfaceShadowInputSet Inputs => inputs ?? throw new ObjectDisposedException(nameof(PublishedSurfaceShadowFixture));

    /// <summary>Builds a new current surface interval using the same retained rendered page after a render-origin rebase.</summary>
    internal SurfaceShadowInputSet CreateRebasedInputs(Vector3d cameraOrigin)
    {
        var previous = lights ?? throw new ObjectDisposedException(nameof(PublishedSurfaceShadowFixture));
        var origin = SplitOrigin(cameraOrigin);
        var current = previous with
        { OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z, OriginRemainder = origin.Remainder };
        var retained = new ShadowFramePublication(current, pages!, readiness!.Requests, readiness.Results,
            readiness.SampleFailures, readiness.SourceIndices);
        return new SurfaceShadowInputSet(current, retained, atmosphere: null, pages!.Settings);
    }

    /// <summary>Withdraws the consumer view before releasing the publication's page and readiness owners.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        inputs?.Dispose();
        publication?.Withdraw();
        if (batchOpen) pages?.SealBatch();
        readiness?.Dispose();
        pages?.Dispose();
        assets.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Splits a finite world origin into native integer chunks and a bounded block remainder.</summary>
    private static (int X, int Y, int Z, Vector3 Remainder) SplitOrigin(Vector3d origin)
    {
        double chunkX = Math.Floor(origin.X / 32), chunkY = Math.Floor(origin.Y / 32), chunkZ = Math.Floor(origin.Z / 32);
        if (!double.IsFinite(chunkX) || !double.IsFinite(chunkY) || !double.IsFinite(chunkZ) ||
            chunkX < int.MinValue || chunkX > int.MaxValue || chunkY < int.MinValue || chunkY > int.MaxValue ||
            chunkZ < int.MinValue || chunkZ > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(origin), "The camera origin is outside the native chunk coordinate range.");
        int x = (int)chunkX, y = (int)chunkY, z = (int)chunkZ;
        var remainder = new Vector3((float)(origin.X - x * 32.0), (float)(origin.Y - y * 32.0),
            (float)(origin.Z - z * 32.0));
        if (!float.IsFinite(remainder.X) || !float.IsFinite(remainder.Y) || !float.IsFinite(remainder.Z))
            throw new ArgumentOutOfRangeException(nameof(origin), "The camera origin remainder is not representable.");
        return (x, y, z, remainder);
    }

    /// <summary>Reads only allocation-result metadata required to select the tile owned by the publication.</summary>
    private static uint[] ReadWords(GpuShaderStorageBuffer buffer, int count)
    {
        using var mapped = buffer.MapRange<uint>(0, count, BufferMapAccess.Read);
        if (!mapped.IsMapped) throw new InvalidOperationException("The VSM fixture allocation result was unavailable.");
        return mapped.Span.ToArray();
    }

    /// <summary>Uploads the deterministic test depth through RHI before certifying the page as current.</summary>
    private static void UploadDepth(ShadowPagePool pool, int slot, float depth, (int Min, int Max)? shadowedColumns)
    {
        using var mapped = pool.Entries.MapRange<uint>(checked(slot * 512 + 17 * sizeof(uint)), 3, BufferMapAccess.Read);
        if (!mapped.IsMapped) throw new InvalidOperationException("The VSM fixture page coordinates were unavailable.");
        int layer = checked((int)mapped.Span[0]);
        int tileX = checked((int)mapped.Span[1]);
        int tileY = checked((int)mapped.Span[2]);
        uint clearDepth = ShadowDepthEncoding.Encode(depth);
        uint[] texels = new uint[PageSize * PageSize];
        for (int y = 0; y < PageSize; y++)
        for (int x = 0; x < PageSize; x++)
            texels[y * PageSize + x] = shadowedColumns is { } stripe && (x < stripe.Min || x > stripe.Max)
                ? ShadowDepthEncoding.Encode(1f) : clearDepth;
        var texture = pool.DepthArray;
        var encoding = RHICommandContext.Current.GetTextureEncoding(texture, PixelComponentType.UnsignedInt);
        RHICommandContext.Current.Barrier(new(ResourceAccess.TextureUpdate));
        RHICommandContext.Current.UploadTexture(texture,
            new(new(0, layer), tileX * PageSize, tileY * PageSize, 0, PageSize, PageSize, 1),
            encoding, MemoryMarshal.AsBytes(texels.AsSpan()));
    }

    /// <summary>Releases only successfully initialized owners when construction fails partway through.</summary>
    private void DisposePartial()
    {
        // Construction-failure cleanup is intentionally best effort because each field may not exist yet.
        inputs?.Dispose();
        publication?.Withdraw();
        if (batchOpen) pages?.SealBatch();
        readiness?.Dispose();
        pages?.Dispose();
        assets.Dispose();
        disposed = true;
    }
    #endregion
}
