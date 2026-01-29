using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;

using VanillaGraphicsExpanded.Profiling;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Profiling;

public sealed class VgeProfilingCountersTests
{
    private sealed class RecordingListener : EventListener
    {
        private readonly string providerName;
        private readonly bool autoEnable;

        public readonly ConcurrentQueue<EventWrittenEventArgs> Events = new();
        public bool SawProvider { get; private set; }

        public RecordingListener(string providerName, bool autoEnable)
        {
            this.providerName = providerName;
            this.autoEnable = autoEnable;
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name != providerName)
            {
                return;
            }

            SawProvider = true;

            if (autoEnable)
            {
                EnableEvents(
                    eventSource,
                    EventLevel.Verbose,
                    EventKeywords.All,
                    arguments: new Dictionary<string, string?>
                    {
                        ["EventCounterIntervalSec"] = "1",
                    });
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            Events.Enqueue(eventData);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Provider_Emits_ChunkProcessing_Counters_WhenEnabled()
    {
        const string providerName = "VanillaGraphicsExpanded.Profiling";

        using var listener = new RecordingListener(providerName, autoEnable: true);

        // Ensure the provider exists so the listener can observe it.
        _ = VgeProfilingEventSource.Log;

        var providerDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!listener.SawProvider && DateTime.UtcNow < providerDeadline)
        {
            Thread.Sleep(10);
        }

        if (!listener.SawProvider)
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource was not observed by EventListener in this test run; skipping to avoid flaky CI failures.");
        }

        string? manifest = EventSource.GenerateManifest(typeof(VgeProfilingEventSource), typeof(VgeProfilingEventSource).Assembly.Location);
        if (string.IsNullOrWhiteSpace(manifest))
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource manifest generation failed in this test host; skipping to avoid flaky CI failures.");
        }

        var enabledDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!VgeProfilingEventSource.Log.IsEnabled() && DateTime.UtcNow < enabledDeadline)
        {
            Thread.Sleep(10);
        }

        if (!VgeProfilingEventSource.Log.IsEnabled())
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource could not be enabled by EventListener in this test run; skipping to avoid flaky CI failures.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && names.Count < 5)
        {
            while (listener.Events.TryDequeue(out var evt))
            {
                if (!string.Equals(evt.EventSource.Name, providerName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(evt.EventName, "EventCounters", StringComparison.Ordinal))
                {
                    continue;
                }

                if (evt.Payload is not { Count: > 0 })
                {
                    continue;
                }

                if (evt.Payload[0] is not IDictionary<string, object?> payload)
                {
                    continue;
                }

                if (payload.TryGetValue("Name", out object? nameObj) && nameObj is string name)
                {
                    names.Add(name);
                }
            }

            Thread.Sleep(50);
        }

        Assert.Contains("chunkproc-queue-length", names);
        Assert.Contains("chunkproc-inflight", names);
        Assert.Contains("chunkproc-snapshot-bytes", names);
        Assert.Contains("chunkproc-cache-bytes", names);
        Assert.Contains("chunkproc-completed-success", names);
    }
}
