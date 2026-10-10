# Cached incremental SPIR-V builds

Status: proposed; no implementation changes are included in this document.

## Intent

Make incremental shader work proportional to changed inputs and their dependents. A change to one standalone stage must skip import expansion, variant emission, compilation, and interface extraction for unrelated stages and variants. Preserve source coverage checks, runtime contracts, binary integrity checks, bounded concurrency, and coherent catalogue publication.

The existing compiler-result cache remains useful. This proposal adds dependency-aware reuse before expensive source processing and reusable interface metadata after compilation.

## Current behavior and measured baseline

[ShaderBuild.Incremental.md](ShaderBuild.Incremental.md) documents the current implementation. An unchanged catalogue takes a receipt shortcut. Any shader input change invalidates that receipt and enters [ShaderVariantBuild](../ShaderBuildTool/Spirv/ShaderVariantBuild.cs), which expands every distinct source, emits every variant, checks the compiler-result cache, extracts interfaces even for cache hits, and republishes the complete catalogue.

The progress counter in [ShaderCompilationBatch](../ShaderBuildTool/Spirv/ShaderCompilationBatch.cs) counts processed variants, including cache hits. It does not count compiler invocations.

An investigation using the existing Debug tool, copied production assets, and isolated outputs measured:

| Invocation | Wall time | Compiler cache hits | Compiler invocations |
| --- | ---: | ---: | ---: |
| Cold catalogue | 35.498 s | 18 | 418 |
| Unchanged catalogue | 0.458 s | 436 | 0 |
| One edit to pbr_post_ssao.fsh | 14.716 s | 435 | 1 |

The edit changed one numeric threshold from 0.02 to 0.021. All 436 variants were processed. Expansion took 3.709 s; compiler work took 0.310 s. Emission work totaled 75.197 s across concurrent workers, and interface extraction totaled 3.714 s across workers; these work totals are not additive wall timings. The cold run reused equivalent inputs encountered within the same invocation.

Local evidence: [single-edit log](../artifacts/investigation/incremental-shaders/tool-single-shader-edit.log), [unchanged log](../artifacts/investigation/incremental-shaders/tool-unchanged.log), and [baseline log](../artifacts/investigation/incremental-shaders/tool-baseline.log). These investigation artifacts may be cleaned independently of this proposal. The measurements establish the current bottleneck; they do not predict a completed implementation's speed.

## Required behavior

| Input or output change | Required response |
| --- | --- |
| No change | Validate inputs and published outputs; skip source processing and reflection |
| One root shader changes | Expand that source and reevaluate only its variants |
| An imported file changes | Expand only roots whose recorded transitive dependency sets contain that file |
| An import directive changes | Re-expand its dependent roots and replace their dependency records |
| One variant contract changes | Reevaluate that variant and any other variants whose effective contracts changed |
| A stage or variant is added | Build the added work and update catalogue membership |
| A stage or variant is removed | Prune its published outputs and records from current membership |
| A published binary is missing or corrupt | Restore a verified cached result; compile only if no valid result remains |
| Cached interface metadata is invalid | Re-extract the affected interface from a verified binary |
| Relevant processing or compiler implementation changes | Invalidate records governed by that implementation identity |
| Clean requested | Discard reusable state and regenerate the catalogue |

Cheap catalogue enumeration, dependency hashing, registry resolution, and output verification can remain proportional to catalogue size. The requirement is to eliminate repeated parsing, emission, reflection, and compiler execution for unchanged work. Changing a widely shared include can legitimately affect much of the catalogue.

## Existing ownership and proposed layout

Retain the current owning APIs:

- The shared shader resolver remains authoritative for programs, stages, variant selection, contracts, and output paths.
- TinyPreprocessor 0.4.0 owns dependency discovery, graph relationships, cycle detection, and dependency ordering. The existing TinyAst.Preprocessor 0.4.3 bridge exposes this information through PreprocessResult<SyntaxTree>.DependencyGraph and ProcessedResources.
- ShaderSourcePreprocessor retains the library result alongside expanded text. Its existing resource resolver supplies canonical resource IDs, physical-file associations, and hashes of the actual bytes read; it does not maintain a competing dependency graph.
- ShaderFileHashIndex remains the shared content-hash provider, including strict content verification.
- ShaderVariantCache remains the verified compiler-result store.
- ShaderInterfaceExtraction remains the owner of packaged interface production.
- ShaderOutputLease and ShaderBuildReceipt retain writer exclusion and successful-build publication responsibilities.

Keep Program limited to options and orchestration. Put dependency records, variant records, identity construction, build selection, and publication in separately named files under ShaderBuildTool/Spirv, grouped into domain directories if needed. Do not duplicate import parsing, shader contract resolution, or layout rules in a new incremental layer.

## Identity boundaries

The current CompilerFingerprint hashes the build-tool assembly and dependencies. Generated shader declarations are compiled into that same assembly. A contract change can therefore invalidate every compiler-cache entry even when unrelated emitted GLSL is unchanged.

Separate the offline catalogue data from the stable processing implementation. The generated catalogue should be supplied by a dedicated data-bearing assembly with a one-way dependency on the shared contract model; it must not be part of the global compiler-result identity. The implementation must inventory the existing contract compilation graph before introducing this boundary and avoid compiling conflicting copies of shared types. Runtime declarations continue to use the same generator and authoritative models.

Use canonical, versioned identities for these independent inputs:

| Identity | Governs |
| --- | --- |
| Preprocessing implementation and dependencies | Expanded source records |
| Emission/layout implementation and dependencies | Variant source records |
| Compiler binaries and compilation policy | Compiler results |
| Reflection/packaging implementation and dependencies | Interface records |
| Effective variant contract | Emission and interface association for that variant |
| Catalogue membership | Receipt and published output set |

A shared processing assembly can conservatively invalidate several processing layers. Catalogue-only edits must not change those implementation identities. Build implementation identities automatically from actual implementation inputs; do not depend solely on developers remembering to increment a string constant. Include managed/native dependencies and schema versions where they influence results.

The effective variant contract must canonically cover stage kind, entry point, source identity, fixed and structural defines, active specialization declarations and defaults, binding/layout inputs, and any selection information used by interface extraction. Include output association where necessary without making unrelated program declarations a dependency. Use deterministic ordering and unambiguous serialization. Share contract projection logic with the existing resolver rather than maintaining an informal parallel list of relevant fields.

Compiler-result keys continue to identify final emitted source, compiler policy, stage, and entry point. Target environment, optimization, warnings, and Debug information remain inputs. A processing implementation change forces reevaluation of emitted source; identical resulting compiler input may still reuse the compiler result. An interface-only change must not force recompilation.

## Persistent records

### Expanded source record

Store a versioned record containing the qualified root source identity, preprocessing identity, a serializable snapshot of the library dependency graph, root and transitive dependency content hashes, expanded-text digest, and a reference to the cached expanded text. Persist resource identities and dependency relationships as data, not live ASTs or the library object representation. Retain per-root provenance so replacing one root record removes its obsolete relationships without deleting relationships still required by another root.

The existing ShaderSyntaxTreePreprocessor already returns PreprocessResult<SyntaxTree>. ShaderSourcePreprocessor currently consumes its content, source map, and diagnostics, then returns only text. Change that boundary to retain DependencyGraph and ProcessedResources from successful preprocessing. TinyPreprocessor.Graph.ResourceDependencyGraph already exposes GetAllResources, GetDependencies, GetDependents, GetProcessingOrder, and cycle detection. Use these APIs rather than implementing another import scanner, dependency resolver, or graph algorithm.

Capture the graph produced by the library during actual expansion. Associate its canonical resource IDs with physical files and hashes through the existing resolver callback, and explicitly record the root input supplied directly to preprocessing. Treat ResourceId as an opaque identity supplied by the resolver; do not reconstruct paths or edges from import strings. Every graph resource must have a validated input association before its record can be reused. Cross-domain imports are dependencies too; the receipt shortcut must include them even if they lie outside the current single-domain enumeration.

An unchanged record can be accepted before AST construction. Missing dependencies or mismatched hashes invalidate it. Changes to import topology are discovered by re-expanding the affected root and replacing its complete graph snapshot and input records. Failed preprocessing must not publish a reusable partial graph. The current resolver uses exact paths; if search paths, precedence, or other resolution inputs are introduced, those inputs must join the identity.

Rehydrate graph snapshots using ResourceDependencyGraph.AddResource and AddDependency, preserving isolated roots. Use the library dependency/dependent queries to select affected roots, traversing to transitive closure with a visited set where required by the installed API semantics. A derived lookup may associate resources with owning root records, but it must not become an independently persisted source of dependency truth. Retain the library cycle detection and processing order. Verify direct versus transitive query behavior with a nested-import fixture before relying on it.

ResourceDependencyGraph is documented as not thread-safe. Keep graph construction, restoration, and queries within a controlled planning step; give parallel workers immutable selected work and dependency snapshots. Sources shared by multiple stages are expanded once per equivalent preprocessing input. TinyPreprocessor supplies per-operation graph discovery; SpirvBuild remains responsible for durable snapshots, content validation, and reuse across invocations.

### Variant build record

Store a versioned record connecting a resolved variant to its expanded-source digest, effective contract digest, emission identity, emitted-source digest, compiler-result key, binary digest, and interface-record key.

If all relevant inputs and referenced artifacts validate, reuse the record before emission. When inputs change, emit only that variant. If emitted source is unchanged, reuse its existing compiler result. Changes that produce byte-identical results must not unnecessarily rewrite deployed outputs.

### Interface record

Store packaged interface metadata under an identity containing the verified binary digest, effective reflection/packaging contract, extraction implementation identity, and configuration. Binary identity alone is insufficient: current extraction also consumes the stage and selection.

Validate record version, identity, payload digest, and required schema before reuse. A missing or invalid record triggers extraction for that variant; it does not invalidate unrelated records or require compilation of an otherwise valid binary.

Keep these records under the existing private cache tree. References must be constrained to owned cache paths. Publish records atomically and retain the existing per-key synchronization for concurrent aliases. Malformed, incomplete, incompatible, or corrupt entries become local misses with explicit reasons. Historical content-addressed records may survive catalogue removal or failure; only current membership controls packaged outputs.

## Build algorithm

1. Acquire the existing output lease and recover any interrupted publication before evaluating the receipt.
2. Generate shared shader inputs, resolve the current registry, and validate source coverage. Avoid replacing generated files when their contents are unchanged.
3. Compute implementation/policy identities and input hashes once. Include previously recorded dependencies in the input check. Retain metadata-assisted hashing and the explicit verifyContents mode; do not describe metadata checks as unconditional detection of timestamp-preserving edits.
4. Try the complete receipt shortcut only when its schema, membership, relevant inputs, and published outputs validate.
5. Restore the persisted TinyPreprocessor graph snapshots and select reusable source and variant records before any expansion or emission. Use library dependency queries to identify affected roots. Re-expand invalid roots, replace their snapshots from successful preprocessing results, and reevaluate their dependent variants. Independently reevaluate variants whose contracts or identities changed.
6. For affected variants, emit source, reuse verified compiler results where possible, compile misses with bounded concurrency, and reuse or extract interface metadata as required.
7. Assemble the complete manifest from reused and newly produced records. Validate complete current membership and references before publication.
8. Publish the coherent output generation, then publish the success receipt last.

Successful immutable cache entries may survive a later failure. They do not establish a successful catalogue. No failed build may leave a current success marker.

Input changes during a build must not produce a receipt describing different contents from those processed. Record hashes of the actual source bytes consumed, detect dependency/contract changes before committing, and abort or retry when the input snapshot became inconsistent. Strict verification must also cover this check. Concurrent configurations generating the shared include must continue to use atomic, deterministic generation.

## Coherent publication and output reuse

Retain the existing complete-generation staging and rollback model. Replacing changed files individually in the active directory would expose mixed binaries and metadata and is not the proposed publication mechanism.

For unchanged verified binaries, populate the staging generation with filesystem hard links on supported same-volume filesystems. Published and cached binary files used this way must be immutable: updates replace files and never modify them in place. Create fresh files for changed binaries and a new manifest when its content changes. Preserve unchanged timestamps so the existing asset-copy target can skip unchanged outputs.

If hard linking is unavailable, copy unchanged binaries into staging and report the fallback cost. Correctness must not depend on hard-link support. This fallback still avoids source processing, reflection, and compilation, although publication performs additional I/O. Do not promise zero binary writes on all filesystems.

The existing two-directory rename sequence is not an atomic reader transaction. Preserve writer exclusion, ensure downstream MSBuild copying occurs only after success, and add deterministic interruption recovery: retain enough publication state to restore the previous complete generation or finish installing a fully validated new one before accepting another receipt. Do not claim support for arbitrary live readers during publication.

If verified output membership, bytes, and manifest are all unchanged, skip the generation swap entirely and update only the necessary private records/receipt. Removed outputs disappear only as part of successful publication. Damaged outputs are repaired from verified cache entries before publication.

## Diagnostics

Retain compiler diagnostics and invalidation reasons. Add counters for roots reused/expanded, variants reused/emitted, compiler-result reuse and actual compiler invocations, interfaces reused/extracted, and outputs retained/replaced/repaired/removed. Distinguish variants from unique compiler jobs when several variants share compiler input.

Progress must label the work being counted. Report reused work immediately and show progress over selected work rather than implying every catalogue entry is being rebuilt. Keep existing summary fields compatible or document their replacement precisely; skipped variants must not be counted as compiler invocations.

Report wall time for input checking, selected processing, and publication, and label accumulated concurrent work separately. Include cache schema/identity mismatch and dependency changes in reasons without dumping every unchanged record.

## Migration and scope

Version the new records and receipt. An older receipt must not bypass establishment of complete dependency and interface records. The first upgraded build may populate those records across the catalogue; subsequent unchanged and isolated-edit builds must exercise the new shortcuts. Reuse old compiler entries only where their identities can be proven compatible; otherwise accept a one-time cold population. Never claim migrated records without validating their referenced artifacts.

Keep Debug and Release isolation, registry scope, custom output paths, output leases, strict verification, clean behavior, runtime binary naming, packaged manifest contracts, and obsolete-output cleanup. Private cache files remain excluded from packaging. Automatic cache eviction, distributed caching, a file watcher, and runtime shader-loading redesign are outside this proposal. Existing explicit clean remains the reclamation mechanism.

## Validation and acceptance

Use focused fixtures to assert both results and skipped operations. Run build/test tools through subagents as required by the workspace instructions. Validate:

- A warm unchanged build performs zero expansions, emissions, compilations, and extractions.
- A root edit processes only its dependent variants; unrelated records and published timestamps remain unchanged.
- Direct, nested, shared, cross-domain, newly added, removed, and changed imports invalidate the correct roots. Missing imports fail without publishing success.
- Graph snapshot round trips preserve isolated roots, canonical IDs, dependency relationships, and affected-root selection. Establish direct/transitive query semantics with a root-to-include-to-nested-include fixture. Removing an import eliminates obsolete relationships for that root while preserving other roots' dependencies. Cycles retain library diagnostics; failed results cannot establish reusable graph records.
- Dependency discovery comes from the existing TinyPreprocessor result; warm reuse restores persisted graph data without invoking the preprocessor. Parallel processing does not share a mutable ResourceDependencyGraph.
- A contract-only change affects only variants with changed effective contracts. Prove that a catalogue assembly change does not globally invalidate compiler identities.
- Binding, specialization, structural define, entry-point, stage, compiler policy, and configuration changes invalidate every relevant layer without omitting required work.
- An extractor-only change reuses verified binaries while refreshing interfaces; equivalent emitted source reuses compiler results.
- Added/removed stages, variants, and registry scopes produce exact output membership. Unregistered roots still fail coverage validation.
- Missing/corrupt binaries, records, manifests, and receipts recover locally where possible. Test matching and mismatching metadata, not merely missing files.
- Cancellation, compilation/reflection failure, concurrent writers, and interruptions around each publication transition never publish a false success. Exercise recovery, hard-link behavior, and copy fallback.
- Metadata-assisted and strict hashing retain their documented semantics. Strict mode detects edits with preserved file size and timestamps.
- Incremental and clean builds from identical inputs produce equivalent binaries and packaged interface manifests, including after reverting edits.

Repeat the production copied-assets experiment with the same tool/configuration, catalogue, concurrency, and edit, and measure cold, unchanged, and single-edit runs. Include an end-to-end MSBuild run to cover tool/catalogue rebuilding and deployed asset copying. Report internal timings and complete command wall time separately.

For the recorded 436-variant SSAO case, acceptance requires one affected root processed, only its affected variant emitted, and no expansion, emission, or interface extraction for the other 435 variants. The edited variant may reuse a compiler result if its final compiler input is already cached; otherwise it should invoke the compiler once. Future catalogue size changes must not turn the historical count into a hard-coded test constant.

Performance acceptance is elimination of unrelated expensive operations and a measured improvement over the matched baseline. No fixed latency is promised before measurement. This work requires no in-game visual acceptance because it preserves shader semantics, but headless validation must establish clean/incremental output equivalence and runtime manifest compatibility.

## Implementation considerations requiring resolution

Before implementation, inventory shared contract-model compilation to choose the exact catalogue assembly boundary without type duplication or circular references. Establish the canonical effective-contract projection using all actual emitter and extractor inputs. Verify Windows hard-link support and interruption recovery in the target output layout. These are concrete design checks within the proposal; none permits weakening dependency coverage, cache integrity, or successful-publication guarantees.
