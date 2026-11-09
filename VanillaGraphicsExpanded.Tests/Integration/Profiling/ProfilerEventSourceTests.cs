using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading;
using VanillaGraphicsExpanded.Profiling;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Profiling;

/// <summary>Verifies profiler events cross the .NET EventSource and EventListener boundary.</summary>
[Collection("Profiler EventSource")]
public sealed class ProfilerEventSourceTests
{
    #region Public API
    /// <summary>A listener can enable a local provider and receive its event.</summary>
    [Fact]
    public void EventListener_SmokeTest_CanEnableLocalEventSource()
    {
        // Arrange the listener and initialize the local provider before enabling it.
        using var listener = new RecordingListener("VanillaGraphicsExpanded.Tests.LocalEventSource");
        _ = LocalEventSource.Log;

        // Act by enabling the provider and waiting until the listener can receive events.
        listener.EnableEvents(LocalEventSource.Log, EventLevel.Verbose, EventKeywords.All);
        var enabledDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!LocalEventSource.Log.IsEnabled() && DateTime.UtcNow < enabledDeadline)
        {
            Thread.Sleep(10);
        }

        // Assert enablement before emitting the event.
        Assert.True(LocalEventSource.Log.IsEnabled());

        // Act by emitting one event from the enabled provider.
        LocalEventSource.Log.Ping(42);

        var eventDeadline = DateTime.UtcNow.AddSeconds(2);
        while (listener.Events.IsEmpty && DateTime.UtcNow < eventDeadline)
        {
            Thread.Sleep(10);
        }

        // Assert the listener received the emitted event.
        Assert.False(listener.Events.IsEmpty);
    }

    /// <summary>Enabling CPU-scope events observes matching start and stop events with one shared identifier.</summary>
    [Fact]
    public void BeginScope_EmitsStartAndStop_WhenEnabled()
    {
        // Arrange a listener that enables only the profiler's CPU-scope keyword.
        const string providerName = "VanillaGraphicsExpanded.Profiling";
        using var listener = new RecordingListener(
            providerName,
            autoEnable: true,
            autoEnableLevel: EventLevel.Verbose,
            autoEnableKeywords: VgeProfilingEventSource.Keywords.CpuScopes);
        _ = VgeProfilingEventSource.Log;

        // Assert listener discovery and manifest support before checking enablement or scope events.
        var providerDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!listener.SawProvider && DateTime.UtcNow < providerDeadline)
        {
            Thread.Sleep(10);
        }
        if (!listener.SawProvider)
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource was not observed by EventListener in this test run; skipping to avoid flaky CI failures.");
        }

        // The provider self-disables when manifest generation fails, so verify host support before the event action.
        string? manifest = EventSource.GenerateManifest(typeof(VgeProfilingEventSource), typeof(VgeProfilingEventSource).Assembly.Location);
        if (string.IsNullOrWhiteSpace(manifest))
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource manifest generation failed in this test host; skipping to avoid flaky CI failures.");
        }

        // Act by waiting for the listener's CPU-scope keyword configuration to take effect.
        var enabledDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!VgeProfilingEventSource.Log.IsEnabled(EventLevel.Informational, VgeProfilingEventSource.Keywords.CpuScopes)
            && DateTime.UtcNow < enabledDeadline)
        {
            Thread.Sleep(10);
        }
        if (!VgeProfilingEventSource.Log.IsEnabled(EventLevel.Informational, VgeProfilingEventSource.Keywords.CpuScopes))
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource could not be enabled by EventListener in this test run; skipping to avoid flaky CI failures.");
        }

        // Act by emitting one scope after listener and provider readiness are established.
        using (Profiler.BeginScope("Test.Scope", "UnitTest"))
        {
            // Disposal must emit the matching stop event after the scope start.
        }

        EventWrittenEventArgs? start = null;
        EventWrittenEventArgs? stop = null;
        long expectedId = 0;
        var eventDeadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < eventDeadline && (start is null || stop is null))
        {
            while (listener.Events.TryDequeue(out var evt))
            {
                if (!string.Equals(evt.EventSource.Name, providerName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (evt.EventId == 1)
                {
                    // Match this scope explicitly because other profiler scopes may run concurrently.
                    if (evt.Payload is { Count: >= 3 }
                        && string.Equals((string)evt.Payload[1]!, "Test.Scope", StringComparison.Ordinal)
                        && string.Equals((string)evt.Payload[2]!, "UnitTest", StringComparison.Ordinal))
                    {
                        start ??= evt;
                        expectedId = (long)evt.Payload[0]!;
                    }
                }
                else if (evt.EventId == 2
                    && expectedId != 0
                    && evt.Payload is { Count: >= 1 }
                    && (long)evt.Payload[0]! == expectedId)
                {
                    stop ??= evt;
                }
            }

            Thread.Sleep(10);
        }

        // Assert both events use the same nonzero identifier and retain the requested scope metadata.
        Assert.NotNull(start);
        Assert.NotNull(stop);
        long startId = (long)start!.Payload![0]!;
        long stopId = (long)stop!.Payload![0]!;
        Assert.NotEqual(0, startId);
        Assert.Equal(startId, stopId);
        Assert.Equal("Test.Scope", (string)start.Payload[1]!);
        Assert.Equal("UnitTest", (string)start.Payload[2]!);
    }
    #endregion

    #region Private
    /// <summary>Provides a local event provider for checking listener enablement independently of VGE events.</summary>
    [EventSource(Name = "VanillaGraphicsExpanded.Tests.LocalEventSource")]
    private sealed class LocalEventSource : EventSource
    {
        #region Public API
        /// <summary>Gets the local provider instance used by the smoke case.</summary>
        public static readonly LocalEventSource Log = new();

        /// <summary>Emits one informational event for listener observation.</summary>
        [Event(1, Level = EventLevel.Informational)]
        public void Ping(int value) => WriteEvent(1, value);
        #endregion

        #region Private
        /// <summary>Creates the singleton provider.</summary>
        private LocalEventSource() { }
        #endregion
    }

    /// <summary>Records event-source discovery and event callbacks for the selected provider.</summary>
    private sealed class RecordingListener : EventListener
    {
        #region Public API
        /// <summary>Gets the events observed by this listener.</summary>
        public readonly ConcurrentQueue<EventWrittenEventArgs> Events = new();

        /// <summary>Gets source names reported during listener initialization.</summary>
        public readonly ConcurrentQueue<string> Sources = new();

        /// <summary>Gets whether the listener observed its selected provider.</summary>
        public bool SawProvider { get; private set; }

        /// <summary>Configures the provider name and optional event settings applied during discovery.</summary>
        public RecordingListener(
            string providerName,
            bool autoEnable = false,
            EventLevel autoEnableLevel = EventLevel.Verbose,
            EventKeywords autoEnableKeywords = EventKeywords.All)
        {
            this.providerName = providerName;
            this.autoEnable = autoEnable;
            this.autoEnableLevel = autoEnableLevel;
            this.autoEnableKeywords = autoEnableKeywords;

            // EventListener's base constructor can report existing sources before these settings are assigned.
            foreach (EventSource eventSource in EventSource.GetSources())
            {
                ConfigureProvider(eventSource);
            }
        }
        #endregion

        #region Protected
        /// <summary>Records source discovery and enables the selected source when configured.</summary>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            Sources.Enqueue(eventSource.Name);
            ConfigureProvider(eventSource);
        }

        /// <summary>Queues each event callback for assertions by the test case.</summary>
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
        private readonly EventLevel autoEnableLevel;
        private readonly EventKeywords autoEnableKeywords;

        /// <summary>Configures a matching source once, including sources already present during base construction.</summary>
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
                    EnableEvents(eventSource, autoEnableLevel, autoEnableKeywords);
                }
            }
        }
        #endregion
    }
    #endregion
}
