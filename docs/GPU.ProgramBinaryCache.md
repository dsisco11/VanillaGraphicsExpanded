# Driver-linked program binary cache

Graphics `GpuProgram` and `ComputeShaderAsset` retain executable-cache acceptance policy. Frozen shader
descriptions supply an accepted binary or stage inputs to the RHI backend. The backend installs/links a
candidate, obtains its result/interface once and exposes a completed `RHIShader`; pipeline realization
does not repeat those operations. Failed replacement preserves the installed set. Cache miss/rejection
selects the existing stage-link creation policy without adding an alternate rendering executor.

## Identity and ownership

The SHA-256 key covers the schema, exact stage byte digests and lengths, stage kinds, entry points,
ordered specialization IDs and raw value bits, and the supported monolithic link policy. Driver
identity includes vendor, renderer, GL/GLSL version, context profile/flags, OS and process architecture.
Future pre-link settings must be included in the key before their use on this path. Stage assets are captured once for specialization; their digests come from the matching build manifest.

Cache hits require no shader-stage objects. The completed native program belongs to its RHI shader
generation; temporary stages from ordinary linking retire after completion. Engine registry membership,
placeholder stages and disposal interception are not part of this ownership.

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

## Verification boundary

Cache tests verify digests, corrupt/missing/driver-rejected entries, candidate cleanup and interface
equivalence. Shader/pipeline tests qualify the completed executable and exact generation. Canonical
package identity and user-run appearance are separate evidence, not implied by a cache hit.
