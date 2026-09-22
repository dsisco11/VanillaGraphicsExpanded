using System.Diagnostics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures controlled source completion, real publication and retained payload for repeatable movement/edit workloads.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SharedGeometryCostMeasurementsTests : RenderTestBase
{
    private readonly ITestOutputHelper output;
    /// <summary>Uses the standard GL context and emits machine-readable measurements into test receipts.</summary>
    public SharedGeometryCostMeasurementsTests(HeadlessGLFixture fixture, ITestOutputHelper output) : base(fixture) => this.output = output;

    #region Measurement scenarios
    /// <summary>Settles startup, a sixteen-block move and one source-chunk edit under production publication limits.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public void StartupMovementAndEdit(int surfaceSize)
    {
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
            Assert.Equal(surfaceSize == 0 ? 27 : 512, expectedCells.Count);
            int expectedCaptures = scenario switch
            {
                "startup" => surfaceSize == 0 ? 8 : 64,
                "movement16" => surfaceSize == 0 ? 4 : 16,
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
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                long frameReads = cache.SourceReads, frameUploads = scene.UploadedBytes;
                partition.Prepare(plan); coordinator.Pump(++tick); partition.Service(plan);
                frameMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                frameAllocated += GC.GetAllocatedBytesForCurrentThread() - allocated;
                peakSnapshots = Math.Max(peakSnapshots, cache.SnapshotBytes);
                peakStaging = Math.Max(peakStaging, partition.StagedPayloadBytes);
                Assert.InRange(cache.SourceReads - frameReads, 0, 2);
                Assert.InRange(scene.UploadedBytes - frameUploads, 0, 8L * 1024 * 1024);
                Assert.InRange(cache.SnapshotBytes, 0, 8L * 32768 * 12);
                Assert.InRange(partition.StagedPayloadBytes, 0, 64L * 4096 * 12);
                frames++;
                Assert.True(frames < 2000, "Finite supported demand failed to settle.");
            } while (coordinator.Cells(partition.Instance).Any(cell => !cell.Ready));
            long finish = Stopwatch.GetTimestamp(); GL.Finish(); finishMs = Stopwatch.GetElapsedTime(finish).TotalMilliseconds;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var cellsReady = coordinator.Cells(partition.Instance).ToArray();
            Assert.Equal(expectedCells.Count, cellsReady.Length);
            Assert.Equal(expectedCaptures, cache.SourceReads - reads);
            Assert.Equal(captures.Count, captures.Distinct().Count());
            Assert.All(cellsReady, cell => Assert.True(cell.Ready));
            output.WriteLine("COST " + JsonSerializer.Serialize(new
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
}
