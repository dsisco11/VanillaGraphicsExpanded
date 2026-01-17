# EventSource CPU Profiling (Scoped Markers)

This mod includes a lightweight scoped CPU profiling marker system backed by .NET `EventSource`.
Markers show up in ETW (PerfView) and EventPipe (`dotnet-trace`) as Start/Stop events.

## Provider

- Provider name: `VanillaGraphicsExpanded.Profiling`
- Keyword: `CpuScopes` (bit 0)
- Events:
  - `1` = ScopeStart (`id`, `name`, `category`, `threadId`)
  - `2` = ScopeStop (`id`, `threadId`)

## Usage (in code)

Use low-cardinality names and avoid string interpolation in hot paths:

- `using var scope = Profiler.BeginScope("LumOn.Frame", "Render");`

When the provider is disabled, `Profiler.BeginScope(...)` returns a no-op scope.

## Capture on Windows (PerfView)

1. Start PerfView
2. Collect → Collect
3. Enable .NET events (default is usually fine) and ensure EventSource events are collected
4. Start collection, reproduce the workload, then stop
5. In the trace, locate the provider `VanillaGraphicsExpanded.Profiling` and inspect Start/Stop events

## Capture with dotnet-trace

If the target process is running a .NET runtime that supports EventPipe:

- List providers:
  - `dotnet-trace providers -p <pid>`
- Collect only this provider:
  - `dotnet-trace collect -p <pid> --providers VanillaGraphicsExpanded.Profiling:0x1:Informational`

Notes:

- `0x1` corresponds to the `CpuScopes` keyword.
- `Informational` matches the current event level.
