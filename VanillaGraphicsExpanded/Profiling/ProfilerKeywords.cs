using System;
using System.Diagnostics.Tracing;

namespace VanillaGraphicsExpanded.Profiling;

[Flags]
internal enum ProfilerKeywords : long
{
    None = 0,
    CpuScopes = 1 << 0,
}
