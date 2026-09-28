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
path collisions with a case-insensitive comparison. Full SHA-256 digests remain
in use for cache keys and binary integrity verification. Old variant filenames
are pruned by the normal successful catalog build and asset-copy cleanup.

An unchanged catalog uses the existing content-verified success receipt and runs
no shader compiler processes. Receipt enumeration skips private cache/work trees
before descending into them, so historical cache entries do not add directory
traversal work. Compiler/tool files are streamed into a shared fingerprint once
per invocation, reused for the catalog receipt and variant keys. A receipt miss expands source imports and emits
each variant through the existing TinyAst and layout pipeline. Its cache key
includes the final emitted source (including defines, specialization declarations
and binding layouts), stage, entry point, tool/compiler contents, target,
optimization, warning policy and debug-information policy.

Verified compiler results live under `_cache` as binary data and digest metadata.
An entry is reusable only when the binary's length and SHA-256 match its metadata.
Missing, malformed or corrupt entries become misses. Hits restore missing or
damaged published binaries; identical published binaries retain their timestamps.
Compiler or build-tool changes conservatively invalidate variants, including
contract changes that rebuild the tool itself. Ordinary GLSL/include edits affect
only variants whose expanded compiler input changes.

Only successful compiler results enter the cache. Successful individual results
can survive another variant's failure, but the complete runtime digest index and
success receipt are published only after all required variants succeed. The
receipt is written to a temporary file and atomically replaces the prior marker.
Failure removes the old success receipt and cannot claim a complete build. A successful
catalog build removes obsolete runtime outputs; the mod copy target also removes
obsolete deployed SPIR-V files. Cache and temporary files are not packaged.

Logs report cache hits, misses, compiler invocations and elapsed time. Explicit
`--clean` overrides `--incremental`, discards outputs/cache and forces compilation.
The cache retains historical successful variants to support reverting changes;
explicit clean is currently the mechanism for reclaiming that storage.
