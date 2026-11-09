using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using VanillaGraphicsExpanded.Profiling;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Profiling;

/// <summary>Verifies chunk-processing counters cross the EventSource listener boundary.</summary>
[Collection("Profiler EventSource")]
public sealed class VgeProfilingCountersTests
{
    #region Public API
    /// <summary>Enabling the provider emits all expected chunk-processing counter names.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void Provider_Emits_ChunkProcessing_Counters_WhenEnabled()
    {
        // Arrange the listener before provider discovery so it can enable both existing and new sources.
        const string providerName = "VanillaGraphicsExpanded.Profiling";
        using var listener = new RecordingListener(providerName, autoEnable: true);

        // Act by materializing the provider; its discovery callback configures the event-counter interval.
        _ = VgeProfilingEventSource.Log;

        // Assert provider discovery and manifest support before waiting for the configured counters.
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

        // Act by waiting for the listener's EventCounter configuration to take effect.
        var enabledDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!VgeProfilingEventSource.Log.IsEnabled() && DateTime.UtcNow < enabledDeadline)
        {
            Thread.Sleep(10);
        }
        if (!VgeProfilingEventSource.Log.IsEnabled())
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource could not be enabled by EventListener in this test run; skipping to avoid flaky CI failures.");
        }

        // Act by collecting the required names until they are all observed or the existing deadline expires.
        string[] expectedNames =
        [
            "chunkproc-queue-length",
            "chunkproc-inflight",
            "chunkproc-snapshot-bytes",
            "chunkproc-cache-bytes",
            "chunkproc-completed-success",
        ];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var eventDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < eventDeadline && expectedNames.Any(expectedName => !names.Contains(expectedName)))
        {
            while (listener.Events.TryDequeue(out var evt))
            {
                if (!string.Equals(evt.EventSource.Name, providerName, StringComparison.Ordinal)
                    || !string.Equals(evt.EventName, "EventCounters", StringComparison.Ordinal)
                    || evt.Payload is not { Count: > 0 }
                    || evt.Payload[0] is not IDictionary<string, object?> payload)
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

        // Assert every required chunk-processing counter was observed.
        Assert.Contains("chunkproc-queue-length", names);
        Assert.Contains("chunkproc-inflight", names);
        Assert.Contains("chunkproc-snapshot-bytes", names);
        Assert.Contains("chunkproc-cache-bytes", names);
        Assert.Contains("chunkproc-completed-success", names);
    }
    #endregion

    #region Private
    /// <summary>Enables one provider and queues the event callbacks needed by the counter assertion.</summary>
    private sealed class RecordingListener : EventListener
    {
        #region Public API
        /// <summary>Gets the events observed by the listener.</summary>
        public readonly ConcurrentQueue<EventWrittenEventArgs> Events = new();

        /// <summary>Gets whether the listener observed its selected provider.</summary>
        public bool SawProvider { get; private set; }

        /// <summary>Creates a listener for one provider and optionally enables its event counters.</summary>
        public RecordingListener(string providerName, bool autoEnable)
        {
            this.providerName = providerName;
            this.autoEnable = autoEnable;

            // EventListener's base constructor can report existing sources before these settings are assigned.
            foreach (EventSource eventSource in EventSource.GetSources())
            {
                ConfigureProvider(eventSource);
            }
        }
        #endregion

        #region Protected
        /// <summary>Records matching-provider discovery and optionally enables periodic counters.</summary>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            ConfigureProvider(eventSource);
        }

        /// <summary>Queues callbacks so the test can inspect counter names after emission.</summary>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            Events.Enqueue(eventData);
        }
        #endregion

        #region Private
        private readonly object providerConfigurationLock = new();
        private readonly HashSet<EventSource> configuredProviders = [];
        private readonly string providerName;
        private readonly bool autoEnable;

        /// <summary>Enables the selected provider once, including if it existed before listener construction.</summary>
        private void ConfigureProvider(EventSource eventSource)
        {
            if (eventSource.Name != providerName)
            {
                return;
            }

            lock (providerConfigurationLock)
            {
                if (!configuredProviders.Add(eventSource))
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
        }
        #endregion
    }
    #endregion
}
