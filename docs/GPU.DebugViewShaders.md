# Per-view debug shaders

Each of the 68 fullscreen `LumOnDebugMode` values has its own shader contract and fragment
entrypoint named `lumon_debug_view_<view>.fsh`. `LumOnDebugShaderProgramFamily.GetProgramName`
provides the explicit mapping. Off, the material atlas display and point-sprite probe orbs retain
their existing non-fullscreen paths.

Each fragment entrypoint owns its view implementation. Fourteen helper includes contain genuinely
shared operations such as surface reconstruction, color mapping and geometry-status sampling.
All fullscreen views share the same vertex stage and binding schema, without sharing an executable
that dispatches on a runtime view-mode uniform.

The old universal dispatcher, ten category fragment entrypoints and category dispatch includes
have been removed. The renderer looks up and prepares only the selected view. If preparation
fails, it skips that overlay; it does not fall back to a large unrelated program. The shared
preparation lifecycle still bounds retries and preserves valid installed generations for reload.

The existing parameter-buffer layout is retained for compatibility. Its old mode field no longer
selects shader behavior: changing that field cannot turn one view's executable into another view.
Runtime resource binding and UI mode information retain their existing host-side behavior.

Per-view declarations retain the required configuration groups and world-topology specialization
inputs. Views that only compare already-rendered lighting do not request world-probe sampling
variants. Contract lookup uses an immutable identity index, avoiding repeated scans of the expanded
catalog.

Switching to another view now prepares a separate executable; returning to an unchanged view reuses
its existing owner. This increases the possible number of resident debug programs while keeping
unused views unlinked and avoiding the cost of compiling unrelated view implementations together.

## Validation

The Release shader build passed. The broad validation run passed 621/623 checks, including all
386 compiled variants, all 68 default per-view program links, all 11 world-enabled debug views,
renderer switching/failure isolation and an actual Surface Cache renderer lifecycle. Two migrated
fixture assumptions failed: a binding check needed its world-enabled selection, and source-mapping
setup needed recursive include loading. Both corrections passed in the final **144/144** consumer
run, which also covers direct/world visibility, wall visibility, interfaces, paired tracing/gather
outputs and binary reload. No outstanding failures remain in these selected checks.

Receipts: `artifacts/DebugViews/debug-views-validation.trx` and
`artifacts/DebugViews/debug-views-final-consumers.trx`. The final consumer run took 55 seconds.
Contract coverage verifies all fullscreen enum values map uniquely, unsupported modes cannot select
a fullscreen executable, and the old dispatcher/category contracts are absent. A fixed-view test
also verifies that changing the old UBO mode field cannot select another rendering implementation.

Validation caught an actual consequence of narrowing the shaders: optimization removes unused
world-probe specialization constants. Six view contracts now declare only IDs 11, 12 and 14;
the radiance-sampling views retain IDs 13 and 15 as well. Enabled-world linking tests cover every
applicable view so default disabled variants cannot conceal this error.

This is a source and executable isolation change; no startup-time, first-selection latency or in-game
visual acceptance is claimed without separate measurements.
