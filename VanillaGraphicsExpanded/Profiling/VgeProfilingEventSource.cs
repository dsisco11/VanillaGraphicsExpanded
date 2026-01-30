using System;
using System.Diagnostics.Tracing;
using System.Threading;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Profiling;

[EventSource(Name = "VanillaGraphicsExpanded.Profiling")]
internal sealed class VgeProfilingEventSource : EventSource
{
    public static class Keywords
    {
        public const EventKeywords CpuScopes = (EventKeywords)0x1;
    }

    public static readonly VgeProfilingEventSource Log = new();

    private long nextScopeId;

    private PollingCounter? chunkProcQueueLength;
    private PollingCounter? chunkProcInFlight;
    private PollingCounter? chunkProcSnapshotBytesInUse;
    private PollingCounter? chunkProcArtifactCacheBytesInUse;

    private IncrementingPollingCounter? chunkProcCompletedSuccessRate;
    private IncrementingPollingCounter? chunkProcCompletedSupersededRate;
    private IncrementingPollingCounter? chunkProcCompletedCanceledRate;
    private IncrementingPollingCounter? chunkProcCompletedFailedRate;
    private IncrementingPollingCounter? chunkProcCompletedUnavailableRate;

    private IncrementingPollingCounter? chunkProcCacheHitsRate;
    private IncrementingPollingCounter? chunkProcCacheEvictionsRate;

    private PollingCounter? traceSceneQueueLength;
    private PollingCounter? traceSceneInFlight;
    private PollingCounter? traceSceneAppliedRegions;

    private IncrementingPollingCounter? traceSceneRegionsUploadedRate;
    private IncrementingPollingCounter? traceSceneBytesUploadedRate;
    private IncrementingPollingCounter? traceSceneRegionsDispatchedRate;
    private IncrementingPollingCounter? traceSceneComputeDispatchRate;
    private IncrementingPollingCounter? traceSceneRegionRequestsIssuedRate;
    private IncrementingPollingCounter? traceSceneSnapshotsRequestedRate;
    private IncrementingPollingCounter? traceSceneSnapshotsUnavailableRate;

    private VgeProfilingEventSource() { }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        base.OnEventCommand(command);

        if (command.Command == EventCommand.Enable)
        {
            EnsureCountersCreated();
        }
    }

    [NonEvent]
    private void EnsureCountersCreated()
    {
        if (chunkProcQueueLength is not null)
        {
            return;
        }

        chunkProcQueueLength = new PollingCounter("chunkproc-queue-length", this, () => ChunkProcessingMetrics.QueueLength)
        {
            DisplayName = "ChunkProc Queue Length",
        };

        chunkProcInFlight = new PollingCounter("chunkproc-inflight", this, () => ChunkProcessingMetrics.InFlight)
        {
            DisplayName = "ChunkProc In-Flight",
        };

        chunkProcSnapshotBytesInUse = new PollingCounter("chunkproc-snapshot-bytes", this, () => ChunkProcessingMetrics.SnapshotBytesInUse)
        {
            DisplayName = "ChunkProc Snapshot Bytes In-Use",
            DisplayUnits = "bytes",
        };

        chunkProcArtifactCacheBytesInUse = new PollingCounter("chunkproc-cache-bytes", this, () => ChunkProcessingMetrics.ArtifactCacheBytesInUse)
        {
            DisplayName = "ChunkProc Artifact Cache Bytes In-Use",
            DisplayUnits = "bytes",
        };

        chunkProcCompletedSuccessRate = new IncrementingPollingCounter(
            "chunkproc-completed-success", this, () => ChunkProcessingMetrics.CompletedSuccess)
        {
            DisplayName = "ChunkProc Completed / sec (Success)",
            DisplayUnits = "requests/sec",
        };

        chunkProcCompletedSupersededRate = new IncrementingPollingCounter(
            "chunkproc-completed-superseded", this, () => ChunkProcessingMetrics.CompletedSuperseded)
        {
            DisplayName = "ChunkProc Completed / sec (Superseded)",
            DisplayUnits = "requests/sec",
        };

        chunkProcCompletedCanceledRate = new IncrementingPollingCounter(
            "chunkproc-completed-canceled", this, () => ChunkProcessingMetrics.CompletedCanceled)
        {
            DisplayName = "ChunkProc Completed / sec (Canceled)",
            DisplayUnits = "requests/sec",
        };

        chunkProcCompletedFailedRate = new IncrementingPollingCounter(
            "chunkproc-completed-failed", this, () => ChunkProcessingMetrics.CompletedFailed)
        {
            DisplayName = "ChunkProc Completed / sec (Failed)",
            DisplayUnits = "requests/sec",
        };

        chunkProcCompletedUnavailableRate = new IncrementingPollingCounter(
            "chunkproc-completed-unavailable", this, () => ChunkProcessingMetrics.CompletedChunkUnavailable)
        {
            DisplayName = "ChunkProc Completed / sec (ChunkUnavailable)",
            DisplayUnits = "requests/sec",
        };

        chunkProcCacheHitsRate = new IncrementingPollingCounter(
            "chunkproc-cache-hits", this, () => ChunkProcessingMetrics.ArtifactCacheHits)
        {
            DisplayName = "ChunkProc Cache Hits / sec",
            DisplayUnits = "hits/sec",
        };

        chunkProcCacheEvictionsRate = new IncrementingPollingCounter(
            "chunkproc-cache-evictions", this, () => ChunkProcessingMetrics.ArtifactCacheEvictions)
        {
            DisplayName = "ChunkProc Cache Evictions / sec",
            DisplayUnits = "evictions/sec",
        };

        traceSceneQueueLength = new PollingCounter("lumon-tracescene-queue-length", this, () => LumonSceneTraceSceneMetrics.QueueLength)
        {
            DisplayName = "LumOn TraceScene Queue Length",
        };

        traceSceneInFlight = new PollingCounter("lumon-tracescene-inflight", this, () => LumonSceneTraceSceneMetrics.InFlight)
        {
            DisplayName = "LumOn TraceScene In-Flight",
        };

        traceSceneAppliedRegions = new PollingCounter("lumon-tracescene-applied", this, () => LumonSceneTraceSceneMetrics.AppliedRegions)
        {
            DisplayName = "LumOn TraceScene Applied Regions",
        };

        traceSceneRegionsUploadedRate = new IncrementingPollingCounter(
            "lumon-tracescene-regions-uploaded", this, () => LumonSceneTraceSceneMetrics.RegionsUploaded)
        {
            DisplayName = "LumOn TraceScene Regions Uploaded / sec",
            DisplayUnits = "regions/sec",
        };

        traceSceneBytesUploadedRate = new IncrementingPollingCounter(
            "lumon-tracescene-bytes-uploaded", this, () => LumonSceneTraceSceneMetrics.BytesUploaded)
        {
            DisplayName = "LumOn TraceScene Bytes Uploaded / sec",
            DisplayUnits = "bytes/sec",
        };

        traceSceneRegionsDispatchedRate = new IncrementingPollingCounter(
            "lumon-tracescene-regions-dispatched", this, () => LumonSceneTraceSceneMetrics.RegionsDispatched)
        {
            DisplayName = "LumOn TraceScene Regions Dispatched / sec",
            DisplayUnits = "regions/sec",
        };

        traceSceneComputeDispatchRate = new IncrementingPollingCounter(
            "lumon-tracescene-dispatches", this, () => LumonSceneTraceSceneMetrics.ComputeDispatchCount)
        {
            DisplayName = "LumOn TraceScene Compute Dispatches / sec",
            DisplayUnits = "dispatches/sec",
        };

        traceSceneRegionRequestsIssuedRate = new IncrementingPollingCounter(
            "lumon-tracescene-region-requests", this, () => LumonSceneTraceSceneMetrics.RegionRequestsIssued)
        {
            DisplayName = "LumOn TraceScene Region Requests Issued / sec",
            DisplayUnits = "requests/sec",
        };

        traceSceneSnapshotsRequestedRate = new IncrementingPollingCounter(
            "lumon-tracescene-snapshots-requested", this, () => LumonSceneTraceSceneMetrics.SnapshotsRequested)
        {
            DisplayName = "LumOn TraceScene Snapshots Requested / sec",
            DisplayUnits = "requests/sec",
        };

        traceSceneSnapshotsUnavailableRate = new IncrementingPollingCounter(
            "lumon-tracescene-snapshots-unavailable", this, () => LumonSceneTraceSceneMetrics.SnapshotsUnavailable)
        {
            DisplayName = "LumOn TraceScene Snapshots Unavailable / sec",
            DisplayUnits = "requests/sec",
        };
    }

    [NonEvent]
    public long NextScopeId() => Interlocked.Increment(ref nextScopeId);

    [Event(
        1,
        Level = EventLevel.Informational,
        Opcode = EventOpcode.Start,
        Keywords = Keywords.CpuScopes)]
    public void ScopeStart(long id, string name, string category, int threadId)
    {
        if (!IsEnabled())
        {
            return;
        }

        unsafe
        {
            fixed (char* namePtr = name)
            fixed (char* categoryPtr = category)
            {
                EventData* data = stackalloc EventData[4];

                data[0] = new EventData
                {
                    DataPointer = (IntPtr)(&id),
                    Size = sizeof(long)
                };

                data[1] = new EventData
                {
                    DataPointer = (IntPtr)namePtr,
                    Size = (name.Length + 1) * sizeof(char)
                };

                data[2] = new EventData
                {
                    DataPointer = (IntPtr)categoryPtr,
                    Size = (category.Length + 1) * sizeof(char)
                };

                data[3] = new EventData
                {
                    DataPointer = (IntPtr)(&threadId),
                    Size = sizeof(int)
                };

                WriteEventCore(1, 4, data);
            }
        }
    }

    [Event(
        2,
        Level = EventLevel.Informational,
        Opcode = EventOpcode.Stop,
        Keywords = Keywords.CpuScopes)]
    public void ScopeStop(long id, int threadId)
    {
        if (!IsEnabled())
        {
            return;
        }

        unsafe
        {
            EventData* data = stackalloc EventData[2];

            data[0] = new EventData
            {
                DataPointer = (IntPtr)(&id),
                Size = sizeof(long)
            };

            data[1] = new EventData
            {
                DataPointer = (IntPtr)(&threadId),
                Size = sizeof(int)
            };

            WriteEventCore(2, 2, data);
        }
    }
}
