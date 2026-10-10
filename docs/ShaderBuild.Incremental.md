# Incremental SPIR-V builds

Normal MSBuild invocations use `--incremental` without `--clean`. Both the shader
build project and the mod's asset-copy target default to
`artifacts/spirv/$(Configuration)` beneath the repository root. This shorter path
also keeps the longest variant paths within MSBuild's Windows copy limits.
Debug and Release retain
independent outputs, receipts and caches. An explicit `SpirvArtifactsDir` property
still overrides this path. The existing adjacent output lease prevents concurrent
writers to the same directory.

For the standard shader-enabled mod build, run `dotnet build VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj -c Debug --tl:off -v:minimal`
from the repository root. The shader project restores
the pinned compiler and resolves the tool built with the inherited output properties.
Use `-p:SpirvConcurrency=1` for serial processing or another positive limit to bound
selected jobs; the default is `min(8, logical CPU count)`. `SpirvAssetsRoot`,
`SpirvRegistry` and `SpirvArtifactsDir` support isolated build fixtures.

For focused validation, run `ShaderBuildTool/Tests/ValidateBuildContract.ps1` with
`-Configuration Debug` or `-Configuration Release`. It restores the pinned tool and
runs the maintained integration tests with temporary fixture assets. It does not
replace the shader-enabled mod build or the Cake packaging task.

Variant filenames use the first 128 bits of the canonical key's SHA-256 hash,
encoded as 26 uppercase, unpadded RFC 4648 Base32 characters. The shared resolver
generates the same names for building and runtime loading, and rejects output
path collisions with a case-insensitive comparison. Cache filenames encode all 256 SHA-256 bits as 52 uppercase, unpadded Base32
characters. Binary and payload integrity digests retain their hexadecimal representation. Old variant filenames
are pruned by the normal successful catalog build and asset-copy cleanup.

An unchanged catalog validates the version-2 success receipt before loading source, variant
or interface records. It checks current inputs and identities, the receipt's consumed-dependency
snapshot (including cross-domain inputs), and content-verified published outputs. It performs zero import expansions, AST
emissions, compiler invocations or interface extractions. Receipt enumeration skips private cache/work trees
before descending into them, so historical cache entries do not add directory
traversal work. Source, include and compiler/tool hashes share a single
`_cache/file-hashes.json` index per output directory. Normal builds reuse hashes
when file size, UTC last-write time and creation time match; changed files are
streamed through SHA-256. Paths are still enumerated to detect additions and
deletions, and saving the index prunes entries no longer used. Index publication
is atomic under the existing output lease. Missing or malformed index data falls
back to hashing file contents.

`--verifyContents` (MSBuild `-p:SpirvVerifyContents=true`) bypasses metadata reuse;
`--clean` also rehashes inputs. This detects edits that preserve size and timestamps,
which ordinary metadata-assisted builds can miss. Published output and compiler
result integrity checks still hash their actual bytes. Logs report input hashes
reused versus files read. The generated SH include is computed each invocation but replaced only when its bytes
change. A named mutex serializes comparison and atomic replacement across configurations.
Strict mode reads each distinct input once per verification boundary; repeated references
share that result. Commit boundaries start fresh verification epochs.

Compiler and processing identities are computed once per invocation. Compiler-result
keys contain compiler package contents and the effective invocation policy, while the
catalogue receipt additionally includes processing implementation identities and resolved
contract/output membership. Offline declarations live in ShaderBuildCatalog; shared model
types live in ShaderBuildModel. Catalogue assembly contents are excluded from processing
implementation hashes, so a declaration edit does not invalidate unrelated compiler results. A receipt miss checks persisted TinyPreprocessor dependency snapshots and expands only
invalid roots. Variant records select emission independently using expanded content and
effective contracts. The compiler cache key
includes the final emitted source (including defines, specialization declarations
and binding layouts), stage, entry point, compiler contents, target,
optimization, warning policy and debug-information policy.

Verified compiler results live under `_cache` as binary data and digest metadata.
An entry is reusable only when the binary's length and SHA-256 match its metadata.
Missing, malformed or corrupt entries become misses. Hits restore missing or
damaged published binaries; identical published binaries retain their timestamps.
Compiler changes invalidate compiler results. Processing implementation changes invalidate
the receipt and cause source reevaluation, but identical emitted compiler input remains
cache eligible. Ordinary GLSL/include or contract edits recompile only variants whose
final compiler input changes. Debug keys also include compiler-visible source and working
paths; Release keys omit that debug-only context. The new key schema cold-populates
older entries rather than assuming compatibility.

Only successful compiler results enter the cache. Successful individual results
can survive another variant's failure, but the complete runtime digest index and
success receipt are published only after all required variants succeed. The
receipt is written to a temporary file and atomically replaces the prior marker.
Failure removes the old success receipt and cannot claim a complete build. A successful
catalog build removes obsolete runtime outputs; the mod copy target also removes
obsolete deployed SPIR-V files. Cache and temporary files are not packaged.

Logs report roots reused/expanded, variants reused/emitted, interfaces reused/extracted,
and binary outputs retained/replaced/repaired/removed. Replaced includes new binaries;
repaired means a previously declared binary was missing or failed its previous digest.
Removed includes obsolete declared or physical binary paths. Counts exclude the manifest.
The IDE build task uses `--tl:off -v:minimal` so successful tool output remains
visible; the terminal logger (`--tl:on`) suppresses even high-importance success
messages. Command-line builds should also use `--tl:off` to see these messages.
Both successful paths print a dedicated `Cache hits=...; misses=...;
shadersRecompiled=...; compilerInvocations=...` summary; an unchanged receipt
reports zero recompilations. Hits count variant uses satisfied without their own compiler
invocation, including aliases. Misses, shadersRecompiled and compilerInvocations count
actual unique compiler processes. Equivalent emitted inputs share a compiler job; Debug
source-path policy can keep otherwise equivalent variants distinct.
Receipt invalidation reports missing/malformed receipts, exact missing or modified
outputs, and unexpected published files. Successful receipts retain per-input
content identities; fingerprint changes report added, removed or changed shader,
compiler/tool and policy inputs with their old/new values. Receipts require schema version 2. Missing, older or unknown versions cannot skip
record population; verified compiler results remain independently reusable. Receipts also retain
the base input fingerprint and opaque consumed-resource observations needed for early validation.
Earlier version-2 receipts without this snapshot refresh once through selective processing. Consumed
cross-domain resources are included in the complete receipt fingerprint. Variant builds also report cache-miss categories and
counts (missing metadata/binary, invalid digest, malformed metadata or unreadable
entries), separately from the receipt reason. Processing assembly changes
invalidate the whole-build shortcut without changing compiler identity. Contract changes
are represented by deterministic effective-contract projections in catalogue membership.
Startup messages identify
tool restoration, fingerprint checks and receipt/output verification before any
variant work begins. Builds report immediate record reuse and progress over selected emission jobs
(at most once every five seconds as jobs finish, plus the final count), followed
by output publication. Input-check, selected-processing and publication timings describe
wall time; selected processing includes source expansion and interface repair performed
during planning. Emission/compiler/interface work timings accumulate concurrent work and
are not elapsed time. Publication separately reports link/copy/write and fallback cost.
Failed jobs emit recognized `SPIRV002` build errors with their stage, source and
configuration. Compiler failures include decimal/hex exit codes and both captured
diagnostic streams; worker exceptions retain their original stack and inner causes.
Process launch/IO exceptions also retain the compiler command and working directory.
The final `SPIRV001` exception retains all reported job failures, so the build
summary remains useful even when earlier ordinary log lines are hidden.
Explicit `--clean` overrides `--incremental`, discards outputs/cache and forces compilation.
The cache retains historical successful variants to support reverting changes;
explicit clean is currently the mechanism for reclaiming that storage.

The pinned dotnet-shaderc 1.2.2 CLI does not support warnings-as-errors.
`--warningsAsErrors` now fails explicitly before processing with that limitation;
previously the unsupported `-Werror` argument was treated as another input filename.
Successful warning diagnostics are also omitted by that CLI, so scanning its output
cannot safely emulate the policy.

Reusable processing record APIs live under `ShaderBuildTool/Spirv/Records`. Successful
preprocessing now retains immutable TinyPreprocessor resource/edge snapshots, processed
resource IDs, and physical input associations with hashes of the exact bytes read.
Expanded text is embedded in a content-addressed source record; a context-specific head
points to the latest successful record. Replacing a root preserves other roots and
historical records. Current dependency planning restores private library graphs and
uses their direct-neighbor queries with visited traversal.

Variant records validate emitted text and its compiler key against verified compiler
artifacts. Interface records independently validate binary, contract, implementation,
configuration and runtime schema; missing or corrupt interfaces can be extracted from
verified binaries without compilation. All new record families use digest-only references
and atomic, integrity-checked envelopes under `_cache`. They confer no catalogue success.
The normal catalogue loop uses these records before scheduling expensive work. The
source planner restores library graphs, checks recorded physical inputs, and follows
changed resource dependents. It replaces only affected successful root snapshots.
The variant processor schedules selected work with bounded concurrency, retains compiler
diagnostics, and assembles the complete current manifest from reused and new records.

Compiler and processing record filenames use the same Base32 alphabet as runtime variant
identifiers, retaining the complete SHA-256 digest rather than truncating it. Old hexadecimal
cache filenames are not reused by the new key format; entries populate on the next build.
Explicit clean can reclaim the historical entries. Runtime variant filenames remain unchanged.

On upgrade, incompatible or absent source heads and graph records are rebuilt by expanding
their roots; invalid variant records require emission, and invalid interface records require
reflection. Independently verified compiler results remain reusable when their current keys
match. Missing, malformed, unknown-schema, or digest-mismatched processing records are local
misses when selective processing needs those records. A current receipt skips intermediate
cache inspection; damaged private records are repaired when a later build needs them. A damaged manifest or receipt is rebuilt
from the complete verified generation without forcing compilation. Compiler metadata with
a mismatching length or digest requires recompilation of its affected input. No legacy-key
conversion or compatibility reader is used.

Generation publication uses a versioned, integrity-checked journal at
`_tmp/publication.json` under the output lease. Recovery runs before receipt checking
or clean. Pending and previous generations are reserved siblings of the active shader
directory; journal paths, file references and filesystem redirection are checked before
recovery reads or removes them. Invalid or ambiguous recovery data invalidates success
and requires repair rather than guessing which outputs to keep.

The publisher stages and verifies the complete manifest/binary set, retains the previous
generation through receipt commit, and writes the receipt last. Ordinary installation
or receipt failures restore a verified previous generation when available. On restart,
an interrupted rename restores the previous generation; a verified installed generation
can be retained without a success marker. A receipt committed before interruption is
accepted only after ordinary input/output verification and exact journal matching.

Unchanged verified binaries use same-volume Windows hard links where supported. Writers
replace files rather than modifying linked contents. Unsupported linking falls back to
verified copies; publication reports linked/copied/written binary counts and copy work
time. Retained binaries and identical manifests preserve last-write timestamps. If the
complete output membership, manifest and bytes are identical, publication skips the
directory replacement entirely. Damaged outputs can be reconstructed from verified
compiler-cache results through the same complete-generation path.

Consumed source bytes, catalogue content and processing/compiler fingerprints are checked
before active mutation and again before receipt publication. Strict mode rehashes content;
normal mode retains the documented metadata-assisted limitation. Input changes fail the
invocation without automatic retry. Cache entries remain independently reusable.

Downstream asset copying runs after successful shader build completion, excludes private
pending/previous generation directories, and prunes removed binaries from current output
membership. Directory renames provide recoverable writer publication, not an atomic
transaction for arbitrary live readers. External in-place modification of hard-linked
files is outside the writer ownership guarantee.

## Measured production behavior before receipt-first validation

A matched Windows x64 Debug run on 2026-10-09 (Core i9-12900K, .NET SDK 10.0.401) compared the pre-selective implementation
at `2bc2ce6d` with `260e0273`, using identical production shaders, catalogue declarations,
compiler manifest, target and concurrency 8. Both tools used the same copied asset root,
working directory and output path sequentially, controlling Debug source-path attribution.
The catalogue contained 176 declared stages, 165 distinct source roots and 436 variants.
The isolated SSAO edit changed one threshold from `0.02` to `0.021`.

| Invocation | Pre-selective command wall time | Selective command wall time |
| --- | ---: | ---: |
| Cold | 52.168 s | 45.862 s |
| Unchanged | 0.626 s | 2.080 s |
| One SSAO edit | 18.911 s | 5.385 s |

The edited run was 3.51 times faster (71.5% less elapsed time). It expanded one root,
emitted one variant, invoked one compiler and extracted one interface. The other
164 roots and 435 variants/interfaces were reused; all 435 unrelated binary timestamps
were retained. The unchanged run performed none of those four expensive operations,
but was slower than the former receipt-only shortcut because that implementation validated the processing
records before accepting the receipt. The subsequent receipt-first correction removes that
intermediate-record scan; the figures in this table describe the earlier implementation.

For the selective edit, reported internal times were 1.301 s input checking, 0.703 s
selected processing and 2.897 s publication; total tool time was 5.324 s. The external
5.385 s includes process overhead. Accumulated emission/compiler/interface work was
70.4/389.9/30.7 ms; concurrent work totals are not elapsed-time components to sum.
Both cold and edited binary/manifest generations matched the baseline byte for byte
at the controlled paths. Reverting restored the original generation, which also matched
an explicit clean rebuild across all 437 files. Reversion and a repeated cached edit
required one expansion but no emission, compilation or interface extraction. These are individual local measurements, not guaranteed latency
or evidence of runtime/GPU performance. The older 35.498/0.458/14.716 s investigation
is historical: Debug cache identity and environment costs had changed, so it was not
used to calculate this speedup.

Logs and output snapshots are retained under
`artifacts/investigation/phase7`: `timings.json` records full commands and process wall
times; `measurement-setup.json` records the environment and matching inputs; `equivalence.json`
and the `baseline-*`/`current-*` logs and snapshots retain work and byte evidence.

An isolated Debug mod build also exercised the actual mod → SpirvBuild → catalogue/tool
→ asset-copy chain using the three-stage build-validation fixture. A generated binding
contract edit changed only catalogue membership among the recorded identities: catalogue
assembly bytes changed, tool/model bytes and processing/compiler inputs did not. It required
zero source expansions and one emission/compiler/interface extraction. Unchanged builds
performed zero expensive shader work; unrelated deployed timestamps were retained. Source
and contract reversions and a same-path clean rebuild produced identical deployed binaries
and manifests. See `artifacts/investigation/phase7/msbuild-summary.json` and `msbuild-*`
logs/snapshots. This fixture qualification is separate from the production timing table.

## Receipt-first validation measurement

After moving compatible receipt validation ahead of processing-record loading, three
unchanged runs on the same copied production catalogue took 0.689, 0.663 and 0.628 s
command wall time. Each reused the receipt without loading intermediate records and
reported zero expansion, emission, compilation, extraction and publication work. The earlier
record-first measurement was 2.080 s. These local samples retain the same Debug setup and
436-variant catalogue; they are not a fixed latency guarantee. Logs and commands are under
`artifacts/investigation/receipt-shortcut`.
The subsequent SSAO edit still selected one root and one variant (one compilation and
interface extraction), retaining all 435 unrelated binary timestamps. Reversion restored
all 437 published files byte for byte. `measurements.json` and `equivalence.json` retain
the measured commands and checks; production sources and outputs were untouched.
