# Incremental SPIR-V builds

Normal MSBuild invocations use `--incremental` without `--clean`. Both the shader
build project and the mod's asset-copy target default to
`artifacts/spirv/$(Configuration)` beneath the repository root. This shorter path
also keeps the longest variant paths within MSBuild's Windows copy limits.
Debug and Release retain
independent outputs, receipts and caches. An explicit `SpirvArtifactsDir` property
still overrides this path. The existing adjacent output lease prevents concurrent
writers to the same directory.

Variant filenames use the first 128 bits of the canonical key's SHA-256 hash,
encoded as 26 uppercase, unpadded RFC 4648 Base32 characters. The shared resolver
generates the same names for building and runtime loading, and rejects output
path collisions with a case-insensitive comparison. Cache filenames encode all 256 SHA-256 bits as 52 uppercase, unpadded Base32
characters. Binary and payload integrity digests retain their hexadecimal representation. Old variant filenames
are pruned by the normal successful catalog build and asset-copy cleanup.

An unchanged catalog uses the success receipt with content-verified outputs and runs
no shader compiler processes. Receipt enumeration skips private cache/work trees
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
reused versus files read. The generated SH include is regenerated each invocation,
so its changed timestamp normally requires rehashing that one input.

Compiler and processing identities are computed once per invocation. Compiler-result
keys contain compiler package contents and the effective invocation policy, while the
catalogue receipt additionally includes processing implementation identities and resolved
contract/output membership. Offline declarations live in ShaderBuildCatalog; shared model
types live in ShaderBuildModel. Catalogue assembly contents are excluded from processing
implementation hashes, so a declaration edit does not invalidate unrelated compiler results. A receipt miss expands source imports and emits
each variant through the existing TinyAst and layout pipeline. Its cache key
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

Logs report cache hits, misses, compiler invocations and elapsed time.
The IDE build task uses `--tl:off -v:minimal` so successful tool output remains
visible; the terminal logger (`--tl:on`) suppresses even high-importance success
messages. Command-line builds should also use `--tl:off` to see these messages.
Both successful paths print a dedicated `Cache hits=...; misses=...;
shadersRecompiled=...; compilerInvocations=...` summary; an unchanged receipt
reports zero recompilations. Counts refer to shader binary variants.
Receipt invalidation reports missing/malformed receipts, exact missing or modified
outputs, and unexpected published files. Successful receipts retain per-input
content identities; fingerprint changes report added, removed or changed shader,
compiler/tool and policy inputs with their old/new values. Older receipts remain
valid but cannot identify individual changed inputs until a successful rebuild
records this information. Variant builds also report cache-miss categories and
counts (missing metadata/binary, invalid digest, malformed metadata or unreadable
entries), separately from the receipt reason. Processing assembly changes
invalidate the whole-build shortcut without changing compiler identity. Contract changes
are represented by deterministic effective-contract projections in catalogue membership.
Startup messages identify
tool restoration, fingerprint checks and receipt/output verification before any
variant work begins. Catalog rebuilds report source expansion and variant progress
(at most once every five seconds as jobs finish, plus the final count), followed
by output publication. Input-check and total timings help distinguish validation
cost from compiler work; processing a variant can be a cache hit without compiling.
Failed jobs emit recognized `SPIRV002` build errors with their stage, source and
configuration. Compiler failures include decimal/hex exit codes and both captured
diagnostic streams; worker exceptions retain their original stack and inner causes.
Process launch/IO exceptions also retain the compiler command and working directory.
The final `SPIRV001` exception retains all reported job failures, so the build
summary remains useful even when earlier ordinary log lines are hidden.
Explicit `--clean` overrides `--incremental`, discards outputs/cache and forces compilation.
The cache retains historical successful variants to support reverting changes;
explicit clean is currently the mechanism for reclaiming that storage.

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
These APIs are tested independently; the normal catalogue loop still processes every
variant after a receipt miss until selective orchestration is integrated.

Compiler and processing record filenames use the same Base32 alphabet as runtime variant
identifiers, retaining the complete SHA-256 digest rather than truncating it. Old hexadecimal
cache filenames are not reused by the new key format; entries populate on the next build.
Explicit clean can reclaim the historical entries. Runtime variant filenames remain unchanged.
