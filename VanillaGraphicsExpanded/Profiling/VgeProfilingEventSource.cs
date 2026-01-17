using System;
using System.Diagnostics.Tracing;
using System.Threading;

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

    private VgeProfilingEventSource() { }

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
