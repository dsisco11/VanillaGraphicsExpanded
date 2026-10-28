# OpenTK binding-table interception

Status: approved for implementation in subphase 8h of
[Rendering.GpuCommands.todo](Rendering.GpuCommands.todo). This document refines the external interception
boundary of the approved [RHI proposal](Rendering.GpuCommands.Proposal.md) and
[contracts](Rendering.GpuCommands.Contracts.md); it does not claim the hooks are installed.
Interception is observational: forward native calls without throwing into the caller or cancelling dispatch.

## Purpose and scope

Observe selected OpenGL operations at OpenTK's native dispatch boundary, independently of the caller's
assembly. The implementation will replace engine call-site rewriting for those operations.

Coverage includes both `OpenTK.Graphics.OpenGL.GL` and `OpenTK.Graphics.OpenGL4.GL`, for supported
Core, Ext and Arb operations. Excluded auxiliary owners and legacy compatibility features stay excluded.
Other graphics libraries and independently cached driver pointers are outside this boundary.
Hook queries/content-only operations only where an explicit observation contract requires them.

The chosen boundary is the supported installed OpenTK tables, irrespective of caller assembly.
Engine-only caller rewriting was considered, but native interception is the approved replacement.
Existing caller rewriting remains explicitly incomplete until the owning replacement unit removes it.

## Dispatch design

OpenTK wrappers load driver function pointers from their binding tables and invoke them. Replace
selected entries with ABI-compatible callbacks, retaining the original driver pointers privately.

```mermaid
flowchart TD
    E[Engine or mod calls OpenTK] --> W[OpenTK overload and argument marshalling]
    W --> H[Hooked native table entry]
    H --> A[Capture observation context and affected knowledge]
    A --> N[Saved original driver function]
    N --> R[Reconcile observed operation]
    R --> C[Existing OpenGlStateCache]
    R --> M[Existing manager metadata publication]
    B[Managed RHI backend operation] --> NB[Saved original driver function, hook bypassed]
    NB --> P[Existing backend reconciliation and publication]
    P --> C
    P --> M
```

An external `GL.DepthMask(false)` would execute the original driver function once and update existing
depth-write knowledge when its effect is established. For `GL.BufferData<T>`, OpenTK still pins the
array; the callback receives a native pointer and byte count without needing the managed element type.
Backend-owned calls bypass interception and retain their existing admission and reconciliation.
External observation never suppresses even a known-equal call; OpenGL receives the original arguments.

## Responsibility boundaries

| Component | Responsibility |
| --- | --- |
| Binding installation | Validate the installed OpenTK layout, capture original addresses, install callbacks and participate in binding reload/teardown. |
| Native callbacks | Forward original arguments once, preserve native return values, contain observer failures and reconcile effects without throwing or vetoing dispatch. |
| OpenGL backend | Execute managed RHI operations through the saved native route without duplicate interception. |
| `OpenGlStateCache` | Retain the single executed-state authority, eligible suppression, operation exclusions and restoration knowledge. |
| `GpuResourceManager` | Retain canonical metadata, associations, availability, dependencies and exactly-once publication. |

Pointer handling and native conversion stay inside the OpenGL backend. Extract shared effect logic;
do not invoke an exact adapter that issues another OpenTK call and recurses. Resource wrappers and
portable RHI contracts gain no native details, registry, cache or metadata authority.

## Installation and lifetime

- Validate the installed layout and native signatures. Experimental slot indices are not stable APIs.
  Match calling conventions and widths, including `GLboolean` and pointer-sized lengths.
- Install before tracked rendering, retaining callbacks and original pointers for their full lifetime.
  Pre-installation state remains initially unknown and uses existing acquisition rules.
- Reinstall after `LoadBindings`, which overwrites hooks. Capture newly loaded driver addresses,
  never our own callbacks as originals.
- Qualify concurrent loading/execution and originating-context ownership. Shared binding tables and
  thread-local cache knowledge have different lifetimes; completion cannot modify a foreign cache.
- Teardown only with no calls in flight. Restore only entries still owned by this installation,
  preserving later loader results or other components' replacements.

Saved-pointer backend dispatch is the preferred bypass because it avoids a broad suppression flag
that could accidentally hide an external call made during a nested callback. Binding aliases and nested
native dispatch still require explicit exactly-once qualification.

## Observation and failure policy

The interception layer is an observer, not an authority deciding whether an external GL call is allowed.
Every intercepted call reaches the saved driver function exactly once, including calls with invalid
arguments and calls conflicting with managed resource or rendering assumptions. OpenGL determines
native acceptance and error behavior. Preserve return values, output arguments and the native error queue.

Observer bookkeeping before dispatch must be contained so a managed failure cannot skip the driver call.
Contain post-dispatch failures separately; never retry a native call because reconciliation failed.
Observer failures neither escape through the native callback nor become deferred exceptions delivered
to the external caller. Debug diagnostics may identify an observation defect without altering dispatch.

If observation cannot establish the result, withdraw only affected facts whose correctness is uncertain
and invalidate dependent certificates as necessary. Do not retain stale facts as known, invent success,
or purge unrelated state. A completed canonical publication remains authoritative even if a recipient
notification fails; use the existing publication and recovery rules rather than rolling back native work.

Existing boundary, immutable-configuration and resource-operation guards must not be reused as vetoes
in external callbacks. A conflicting native mutation must reach the driver and be reflected in existing
cache/manager bookkeeping, including interruption of an affected managed operation or withdrawal of
invalid configuration evidence. This requires adapting shared observation logic, not merely catching a
rejection from an old adapter and returning before forwarding. Managed RHI API validation remains a
separate contract; the interception path adds no command-admission policy.

Native return alone does not establish command acceptance. Reuse the approved operation/query policy:
propagate established defined effects, preserve prior facts after proven rejection, and withdraw only
affected knowledge after interruption or an outcome that cannot be established. Preserve native errors;
do not add universal `GetError`, `Is*`, verification queries or retry.

## Bounded feasibility evidence

The isolated experiment used installed OpenTK 4.9.4.0 on Windows x64, .NET 10.0.14 and one hidden
OpenGL 4.5 context. Copied binding assemblies matched the installed binaries by SHA-256.

| Check | Observed result |
| --- | --- |
| Legacy and OpenGL4 scalar calls, plus a delegate | Hooked `DepthMask` callbacks forwarded each call once; native state matched. |
| Generic integer and floating-point uploads in both binding families | Hooked `BufferData` callbacks received pinned data once; GPU readback matched. |
| Saved original pointer | Executed without interception reentry. |
| Binding reload and reinstallation | Reload removed the hooks; reinstalling from true original addresses restored interception. |
| Contained managed rejection | The experiment skipped the native effect and returned normally. Skipping dispatch is incompatible with the agreed observational policy. |

This proves representative mechanical feasibility, not every signature, alias, already-inlined wrapper,
concurrent reload, reentry, context transition, platform or driver. The experiment predates the agreed
always-forward policy: observer-failure cases still need qualification proving dispatch occurs once,
native outputs/errors are preserved, and only uncertain affected knowledge is withdrawn. Unchecked
exception escape was deliberately not tested and is not part of the proposed design.

Local experiment artifacts, which may not be retained in a checkout:
`artifacts/interception-feasibility/native/` contains source, build/run logs, results and binary identity;
`artifacts/interception-feasibility/engine/` contains the installed-engine call-site inventory.
That inventory found 697 static GL call sites across 11 scanned assemblies, with no OpenGL4 calls,
GL delegate construction or transform-feedback begin/end/pause/resume calls found.
It is static evidence, not proof of all runtime-generated behavior or startup/JIT coverage.

## Adoption and qualification

Implementation must qualify exact native signatures, loader/context lifetime, original-pointer bypass,
always-forward observer-failure handling and exactly-once effects using representative native-state and
error oracles. The bounded probe does not substitute for these obligations. No benchmark or timing gate
is required.

Replace each operation family's caller rewriting and exact execution adapters together
with its hook, shared effect logic and dependent tests. Remove superseded routes in the same unit;
do not leave two observers active or introduce temporary forwarding shims. Retain unrelated engine
integration hooks whose purpose is broader than GL interception.

The implementation tasks and dependency ordering are recorded in subphase 8h of the RHI task list.
