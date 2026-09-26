# Driver-linked program binary cache

Graphics `GpuProgram` and typed `GpuComputePipeline` loaders now try driver-linked executables
before specializing and linking their SPIR-V stages. Both paths rebuild the same current contract
interface and bindings before publishing a candidate. Failed replacement preserves the installed
graphics program. Cache misses call the existing `GpuComputePipeline.TryCreate` linking and cleanup path with an optional executable-retrieval hint. Cache hits and normal links share interface preparation and pipeline construction. Direct shader-module callers bypass cache lookup and persistence.

## Identity and ownership

The SHA-256 key covers the schema, exact stage byte digests and lengths, stage kinds, entry points,
ordered specialization IDs and raw value bits, and the supported monolithic link policy. Driver
identity includes vendor, renderer, GL/GLSL version, context profile/flags, OS and process architecture.
Future pre-link settings must be included in the key before their use on this path. Stage assets are captured once for specialization; their digests come from the matching build manifest.

Cache hits have no shader-stage objects. Engine stage slots are null because the engine's final
Dispose implementation detaches every non-null slot. Uncached replacements prepare missing wrappers
before publication. The linked executable alone owns a cache-hit generation; no placeholder shader
objects or disposal interception are needed.

## Storage and fallback

The default directory is `GamePaths.DataPath/VGE/Cache/Programs`. A versioned `index.json` maps hex
keys to binary format, size, SHA-256 checksum and last-used UTC time; each payload is `<key>.bin`.
Recency writes are throttled to one minute per entry. Defaults bound storage to 256 MiB and 512
entries, with a 32 MiB individual executable limit. Least-recently-used eviction retains the new
entry. Maintenance removes orphan binaries and interrupted temporary files.

A nonblocking process lease serializes index mutation. Contention skips caching. Payload publication
precedes atomic JSON replacement, with same-directory temporary files. Missing, corrupt, oversized,
incompatible or inaccessible cache data falls back to normal linking; unsupported binary formats
are rejected before driver loading. Driver-rejected binaries are removed. Optional cache GL errors
are drained within cache operations, while preexisting GL errors remain attributed to their caller.

After successful normal linking and interface preparation, retrieval and persistence are optional.
Warm loads still perform disk reads, hashing and driver binary loading; they are not free and do not
change shader execution speed. A driver advertising no program binary formats bypasses storage.
The supported minimum context already supports the binary API; format availability is queried on
the active context rather than cached globally across contexts.

## Validation

The headless fixture explicitly bypasses caching by default, preserving deliberate compile/link
lifecycle tests. Dedicated cache tests use a scoped thread-local temporary store. Tests exercise
normal linking, cache hits, pixel/compute results, production sampler bindings, cached disposal and
recreation, failed replacement, driver rejection, bypass, exact input identity, storage corruption
and bounded eviction. Build/test receipts and Release timing results are recorded below after
validation completes.

API reference: [Khronos program binary loading](https://wikis.khronos.org/opengl/GlProgramBinary)
and [binary retrieval](https://wikis.khronos.org/opengl/GLAPI/glGetProgramBinary).

## Review and measurement scope

Separate implementation review passed after checking the installed engine's disposal implementation.
The current driver supports program binaries. Unsupported-format rejection, explicit bypass, and
storage-unavailable fallback are executable tests; the hardware path advertising zero formats is
source-reviewed, not demonstrated on this machine.

Timing samples use production loading APIs with small deterministic fixture shaders. The uncached
baseline runs before the empty application-cache miss, so the latter does not imply a cold vendor
driver cache. The first graphics baseline draw also includes lazy fixture geometry setup. First-use
completion times include a GPU finish; do not attribute their difference solely to deferred driver
work. No game process was launched and no whole-game startup or steady-state FPS claim is made.

## Release measurement receipt

Single-run timings in milliseconds (load includes asset reads, keying, cache I/O and extraction
when applicable; first use includes draw/dispatch completion):

| Workload | Bypass load / use | Empty-cache load / use | Warm load / use | Recreated warm load / use |
| --- | ---: | ---: | ---: | ---: |
| Graphics | 4.960 / 1.424 | 7.249 / 0.214 | 5.371 / 2.526 | 1.171 / 1.260 |
| Compute | 14.886 / 0.662 | 8.549 / 0.212 | 6.450 / 1.711 | 1.103 / 1.292 |

Both warm generations verified cache-hit status and identical authored output. Graphics also
covers disposal and recreation. This sample is variable: the first warm graphics load was slower
than bypass and first-use costs did not uniformly improve. Later reloads were faster in this sample,
but these observations do not establish a universal latency reduction. Production game startup
measurement remains outside this headless validation.

Subagent validation passed 259/259 Debug checks (focused cache plus graphics variant lifecycle,
compute lifecycle and contract regressions), followed by 11/11 Release focused checks. The Release
selection includes the subsequently added process-lock contention/recovery case. Receipts:
`artifacts/binary-cache-debug.log` / `.trx`, `artifacts/binary-cache-release.log` / `.trx`.

Final Debug restoration and 11/11 focused checks passed on the final source. Across the regression
and focused selections, 260 distinct cases passed; separate implementation review passed. Final
receipt: `artifacts/binary-cache-final-debug.log` / `.trx`. Debug shader artifacts were restored after
the Release run. No game process was launched.

Compute ownership follow-up: cache misses now reuse `TryCreate` for linking and cleanup, with an
optional retrieval hint. Both paths share linked-interface preparation. Explicit file loading also
rejects missing/empty inputs before querying GL. Debug build and 32/32 focused cache, file-loading,
compute integration and direct-link disposal checks passed after this refactor. Evidence:
`artifacts/compute-trycreate-reuse-fixed.log` / `.trx`.

Graphics ownership follow-up: `GpuProgram.Spirv.TryCreate` now owns ordinary program creation,
attachment, linking and failed-candidate cleanup. Cache hits and linked candidates continue through
one interface-preparation/publication path. Driver cache GL queries use named OpenTK enum members.

SPIR-V stage hashing occurs during compilation. After every variant succeeds, the compiler emits
one `spirv-digests.json` in the shader asset root. Its versioned `Binaries` object maps exact relative
`.spv` paths (including structural variants) to `Length` and hexadecimal `Digest` values. Entries
are written in stable path order. The build receipt verifies the manifest together with all binaries;
production and test packaging copy it and remove obsolete per-variant `.sha256` sidecars.

The application loads and parses the manifest lazily once per asset-manager/domain, sharing the
same index across graphics and compute programs, including program recreation. Explicit file loads
share an index by canonical manifest path. Unavailable or malformed indexes are cached too. A missing/malformed manifest, missing entry, invalid digest or mismatched binary length
bypasses caching for the whole program; ordinary loading still works without runtime stage hashing.
Explicit binary-file loading resolves the manifest from the declared shader root, or beside a
relocated standalone binary. Cache bypass does not read the manifest. Shader asset initialization/disposal clears these indexes.
A prefix on the engine ShaderRegistry.ReloadShaders clears them before asset replacement and
registered program compilation; the public reload event occurs afterward and is too late.
Normal program creation and settings-only recompilation do not invalidate the shared index.

Runtime still hashes the small combined cache key and checksums driver executable payloads.
The build manifest must be packaged with its matching binaries: same-length binary edits without
regenerating the manifest cannot be detected without rehashing. Asset updates must not race shader
loading. Earlier timing samples predate build-generated digests and this consolidation.

Debug build and 17/17 focused cache/store and graphics lifecycle checks passed after the graphics
extraction and enum changes. Evidence: `artifacts/graphics-trycreate-enums.log` / `.trx`.

Initial sidecar implementation validation: Debug build and 259/259 runtime/cache/lifecycle checks passed, plus
2/2 build-receipt integrity checks. The packaged inventory verified all 250 compiled variant digests
against their actual bytes; production output also contained all 250 sidecars. Missing/invalid
metadata bypass and binary-only reads when caching is disabled are covered. Evidence:
`artifacts/build-digest-focused.log` / `.trx` and `artifacts/build-digest-receipt.log` / `.trx`.

Single-manifest validation: clean Debug build, 31/31 focused runtime checks and 2/2 build-receipt
checks passed. Generated, production and test shader roots each contain exactly one manifest with
250 entries and no obsolete `.sha256` files. The inventory verified all 250 digest values against
packaged binaries. Evidence: `artifacts/build-digest-manifest.log` / `.trx` and
`artifacts/build-digest-manifest-receipt.log` / `.trx`.

Shared-index validation: Debug build and 25/25 focused checks passed. An actual loader test performs
three graphics and three compute program loads against one asset manager with exactly one manifest
read; the reload prefix makes the next load read it once again. Tests also cover asset-source/domain
isolation, cached unavailable metadata, explicit-file identity and reload refresh. Engine IL inspection
confirmed registry compilation precedes the public reload event. Evidence:
`artifacts/SharedDigestCache/SharedDigestCache.log` and `.trx`. No game process was launched.
