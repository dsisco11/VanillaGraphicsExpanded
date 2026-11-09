using System.Diagnostics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Measures controlled source completion, real publication and retained payload for repeatable movement/edit workloads.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SharedGeometryCostMeasurementsTests : RenderTestBase
{
    #region Public API
    private readonly ITestOutputHelper output;
    private bool recordTimings;
    /// <summary>Uses the standard GL context and emits machine-readable measurements into test receipts.</summary>
    public SharedGeometryCostMeasurementsTests(HeadlessGLFixture fixture, ITestOutputHelper output) : base(fixture) => this.output = output;

    #region Measurement scenarios
    /// <summary>Records cost telemetry separately from startup, movement and edit correctness.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    [Trait("Purpose", "Diagnostic")]
    public void MeasureStartupMovementAndEdit(int surfaceSize)
    {
        // Arrange: Enable timing only around the correctness workload so setup stays outside the report.
        recordTimings=true;
        // Act: Reuse the identical startup, movement, and edit workload used by the correctness case.
        // Assert: The invoked workload retains its source coverage, bounded upload, and all-ready checks.
        try { StartupMovementAndEdit(surfaceSize); }
        finally { recordTimings=false; }
    }

    /// <summary>Settles startup, a sixteen-block move and one source-chunk edit under production publication limits.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    public void StartupMovementAndEdit(int surfaceSize)
    {
        // Arrange: Model source completion deterministically while retaining real coordinator and GPU publication.
        EnsureContextValid();
        var plan = TraceGeometryCoverage.Plan(new(0, 128, 0), true, surfaceSize == 0 ? null : surfaceSize, 256);
        var versions = new Dictionary<ChunkKey, int>();
        var pending = new List<(ChunkKey Key, int Version, TaskCompletionSource<TraceGeometryChunk?> Completion)>();
        var captures = new List<(ChunkKey Key, int Version)>();
        using var cache = new TraceGeometrySourceCache((key, version, _) =>
        {
            var completion = new TaskCompletionSource<TraceGeometryChunk?>();
            pending.Add((key, version, completion)); captures.Add((key, version)); return completion.Task;
        }, key => versions.GetValueOrDefault(key), _ => true);
        using var scene = new TraceGeometryGpuScene(plan.Resolution);
        var coordinator = new PartitionCoordinator(new(16384, 256, 128, 128, 8L * 1024 * 1024));
        using var partition = new TraceGeometryPartition(coordinator, cache, new(), scene);
        long tick = 0;
        // Act: Settle the initial view, a sixteen-block move, and one changed source chunk.
        Measure("startup");
        plan = TraceGeometryCoverage.Plan(new(16, 128, 0), true, surfaceSize == 0 ? null : surfaceSize, 256);
        Measure("movement16");
        versions[ChunkKey.FromChunkCoords(0, 4, 0)] = 1;
        Measure("edit-one-chunk");

        /// <summary>Excludes synthetic worker completion from frame cost while retaining real GPU upload submission.</summary>
        void Measure(string scenario)
        {
            // Derive logical demand independently of coordinator output, so an empty or incomplete
            // returned set cannot accidentally satisfy the all-ready completion assertion.
            var layout = new PartitionLayout(new(16, 16, 16));
            var expectedCells = new[] { plan.NearField, plan.Surface }.Where(bounds => bounds.HasValue)
                .SelectMany(bounds => layout.Intersecting(plan.Clip(bounds!.Value))).ToHashSet();
            int side=surfaceSize >> 4, sourceSide=surfaceSize >> 5;
            // Assert: Demand and upload bounds are derived independently of coordinator output.
            Assert.Equal(surfaceSize == 0 ? 27 : side*side*side, expectedCells.Count);
            int expectedCaptures = scenario switch
            {
                "startup" => surfaceSize == 0 ? 8 : sourceSide*sourceSide*sourceSide,
                "movement16" => surfaceSize == 0 ? 4 : sourceSide*sourceSide,
                "edit-one-chunk" => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            long reads = cache.SourceReads, uploaded = scene.UploadedBytes;
            long peakSnapshots = cache.SnapshotBytes, frameAllocated = 0, peakStaging = 0;
            double frameMs = 0, finishMs;
            int frames = 0;
            do
            {
                foreach (var item in pending.ToArray())
                {
                    var cells = new TraceGeometryVoxel[32768];
                    Array.Fill(cells, new TraceGeometryVoxel(1, 0, 0));
                    item.Completion.SetResult(new(item.Key, item.Version, cells));
                    pending.Remove(item);
                }
                long allocated = recordTimings?GC.GetAllocatedBytesForCurrentThread():0, start = recordTimings?Stopwatch.GetTimestamp():0;
                long frameReads = cache.SourceReads, frameUploads = scene.UploadedBytes;
                partition.Prepare(plan); coordinator.Pump(++tick); partition.Service(plan);
                if(recordTimings)
                {
                    frameMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    frameAllocated += GC.GetAllocatedBytesForCurrentThread() - allocated;
                }
                peakSnapshots = Math.Max(peakSnapshots, cache.SnapshotBytes);
                peakStaging = Math.Max(peakStaging, partition.StagedPayloadBytes);
                Assert.InRange(cache.SourceReads - frameReads, 0, 2);
                Assert.InRange(scene.UploadedBytes - frameUploads, 0, 8L * 1024 * 1024);
                Assert.InRange(cache.SnapshotBytes, 0, 8L * 32768 * 12);
                Assert.InRange(partition.StagedPayloadBytes, 0, 64L * 4096 * 12);
                frames++;
                Assert.True(frames < 2000, "Finite supported demand failed to settle.");
            } while (coordinator.Cells(partition.Instance).Any(cell => !cell.Ready));
            finishMs=0;
            if(recordTimings)
            {
                long finish = Stopwatch.GetTimestamp(); GL.Finish(); finishMs = Stopwatch.GetElapsedTime(finish).TotalMilliseconds;
            }
            else
            {
                // Retain GPU completion without draining unrelated work for a timing report.
                using var fence=RHICommandContext.Current.InsertFence();
                Assert.Equal(CompletionStatus.Ready,RHICommandContext.Current.WaitCpu(fence,TimeSpan.FromSeconds(5)));
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var cellsReady = coordinator.Cells(partition.Instance).ToArray();
            Assert.Equal(expectedCells.Count, cellsReady.Length);
            Assert.Equal(expectedCaptures, cache.SourceReads - reads);
            Assert.Equal(captures.Count, captures.Distinct().Count());
            Assert.All(cellsReady, cell => Assert.True(cell.Ready));
            if(recordTimings)output.WriteLine("COST " + JsonSerializer.Serialize(new
            {
                backend = "shared", surfaceSize, scenario, sourceCaptures = cache.SourceReads - reads,
                duplicateSourceRevisions = captures.Count - captures.Distinct().Count(), actualUploadBytes = scene.UploadedBytes - uploaded,
                residentSnapshotBytes = cache.SnapshotBytes, peakSnapshotBytes = peakSnapshots,
                peakRetainedStagedBytesAfterUpdate = peakStaging,
                residentGpuBytes = new[] { scene.Geometry, scene.Legacy, scene.Light, scene.Readiness }
                    .Sum(t => GeometryCostStorageQuery.TextureBytes(t.TextureId, TextureTarget.Texture3D)) +
                    new[] { scene.Faces, scene.Materials, scene.Surfaces, scene.LightColors, scene.BlockLevels, scene.SunLevels }
                    .Sum(t => GeometryCostStorageQuery.TextureBytes(t.TextureId, TextureTarget.Texture2D)),
                frameThreadAllocatedBytes = frameAllocated, frameThreadMs = frameMs,
                gpuDrainMs = finishMs, frames, readyCells = cellsReady.Length,
                renderer = GL.GetString(StringName.Renderer), scope = "synthetic source completions excluded; frame submission measured; no live-game claim"
            }));
        }
    }
    #endregion
    #endregion
}
