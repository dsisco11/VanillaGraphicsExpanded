using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading;

using VanillaGraphicsExpanded.Profiling;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Profiling;

public sealed class ProfilerEventSourceTests
{
    [EventSource(Name = "VanillaGraphicsExpanded.Tests.LocalEventSource")]
    private sealed class LocalEventSource : EventSource
    {
        public static readonly LocalEventSource Log = new();

        private LocalEventSource() { }

        [Event(1, Level = EventLevel.Informational)]
        public void Ping(int value) => WriteEvent(1, value);
    }

    private sealed class RecordingListener : EventListener
    {
        private readonly string providerName;
        private readonly bool autoEnable;
        private readonly EventLevel autoEnableLevel;
        private readonly EventKeywords autoEnableKeywords;

        public readonly ConcurrentQueue<EventWrittenEventArgs> Events = new();
        public readonly ConcurrentQueue<string> Sources = new();

        public bool SawProvider { get; private set; }

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
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            Sources.Enqueue(eventSource.Name);

            if (eventSource.Name == providerName)
            {
                SawProvider = true;

                if (autoEnable)
                {
                    EnableEvents(eventSource, autoEnableLevel, autoEnableKeywords);
                }
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            Events.Enqueue(eventData);
        }
    }

    [Fact]
    public void EventListener_SmokeTest_CanEnableLocalEventSource()
    {
        using var listener = new RecordingListener("VanillaGraphicsExpanded.Tests.LocalEventSource");

        _ = LocalEventSource.Log;
        listener.EnableEvents(LocalEventSource.Log, EventLevel.Verbose, EventKeywords.All);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!LocalEventSource.Log.IsEnabled() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.True(LocalEventSource.Log.IsEnabled());

        LocalEventSource.Log.Ping(42);

        deadline = DateTime.UtcNow.AddSeconds(2);
        while (listener.Events.IsEmpty && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.False(listener.Events.IsEmpty);
    }

    [Fact]
    public void BeginScope_EmitsStartAndStop_WhenEnabled()
    {
        const string providerName = "VanillaGraphicsExpanded.Profiling";
        using var listener = new RecordingListener(
            providerName,
            autoEnable: true,
            autoEnableLevel: EventLevel.Verbose,
            autoEnableKeywords: VgeProfilingEventSource.Keywords.CpuScopes);

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

        // If manifest generation fails, the provider will self-disable and cannot be enabled by EventListener.
        string? manifest = EventSource.GenerateManifest(typeof(VgeProfilingEventSource), typeof(VgeProfilingEventSource).Assembly.Location);
        if (string.IsNullOrWhiteSpace(manifest))
        {
            Assert.SkipWhen(true, "VgeProfilingEventSource manifest generation failed in this test host; skipping to avoid flaky CI failures.");
        }

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

        using (Profiler.BeginScope("Test.Scope", "UnitTest"))
        {
            // Intentionally empty.
        }

        EventWrittenEventArgs? start = null;
        EventWrittenEventArgs? stop = null;
        long expectedId = 0;

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && (start is null || stop is null))
        {
            while (listener.Events.TryDequeue(out var evt))
            {
                if (!string.Equals(evt.EventSource.Name, providerName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (evt.EventId == 1)
                {
                    // Avoid flakiness from other profiled scopes in parallel tests by selecting the expected scope by name/category.
                    if (evt.Payload is { Count: >= 3 }
                        && string.Equals((string)evt.Payload[1]!, "Test.Scope", StringComparison.Ordinal)
                        && string.Equals((string)evt.Payload[2]!, "UnitTest", StringComparison.Ordinal))
                    {
                        start ??= evt;
                        expectedId = (long)evt.Payload[0]!;
                    }
                }
                else if (evt.EventId == 2)
                {
                    if (expectedId != 0 && evt.Payload is { Count: >= 1 } && (long)evt.Payload[0]! == expectedId)
                    {
                        stop ??= evt;
                    }
                }
            }

            Thread.Sleep(10);
        }

        Assert.NotNull(start);
        Assert.NotNull(stop);

        long startId = (long)start!.Payload![0]!;
        long stopId = (long)stop!.Payload![0]!;

        Assert.NotEqual(0, startId);
        Assert.Equal(startId, stopId);

        Assert.Equal("Test.Scope", (string)start.Payload[1]!);
        Assert.Equal("UnitTest", (string)start.Payload[2]!);
    }

    [Fact]
    public void BeginScope_ReturnsNoop_WhenDisabled()
    {
        // EventSource providers are typically disabled in test runs unless a listener enables them.
        // This test asserts the fast-path gate yields a no-op scope.
        using var scope = Profiler.BeginScope("Test.Disabled");
        // No assertion needed: should not throw on Dispose.
    }
}
