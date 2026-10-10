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

## Resolved implementation contracts

These decisions specify the future implementation; the current build remains unchanged. Graph semantics are qualified by the focused fixture described below. Assembly separation, cache integration, and publication recovery are implementation work in the linked checklist.

### Project and declaration boundary

The current [tool project](../ShaderBuildTool/ShaderBuildTool.csproj) compiles all Rendering/Contracts sources, GpuBindingRegistry, preprocessing helpers, packaged-interface models, and the terrain fixture transform. It runs ShaderContractGenerator with ShaderContractsOffline=true and supplies runtime C# sources as AdditionalFiles. The generator constructs a semantic view of those files and emits owner shells, option enums as needed, and GeneratedShaderCatalog. The [generator project](../ShaderContractGenerator/ShaderContractGenerator.csproj) separately source-links pure validation models for its netstandard2.0 analyzer host. The [runtime project](../VanillaGraphicsExpanded/VanillaGraphicsExpanded.csproj) runs the same generator against its real implementations.

Adopt two offline projects, both net10.0:

- ShaderBuildModel: source-link the existing engine-independent contract models, attributes, resolver, settings, stage declarations, and descriptors. Exclude GpuShaderContracts.cs, BuildValidationShaderPrograms.cs, IShaderIncludeBindings.cs, and IShaderInterfaceLocations.cs from this model assembly. Those four contain catalogue lookup or authored binding declarations. Do not put GpuBindingRegistry constants in the model assembly.
- ShaderBuildCatalog: reference ShaderBuildModel; own the offline generator invocation, runtime AdditionalFiles, generated owner shells/catalogue/enums, the four excluded authored files, GpuBindingRegistry, and TestShaderPrograms (currently in the tool). The latter is catalogue data even though it constructs declarations by hand. Expose its registry/scope entry points to the tool through internal friend access rather than exposing the model publicly.

ShaderBuildTool references both offline projects and removes duplicate model/catalogue Compile items and its own analyzer/AdditionalFiles generation. ShaderBuildModel references neither the catalogue nor the executable. Grant deliberate InternalsVisibleTo access from the model to catalogue, tool, and focused test assemblies, and from the catalogue to tool/tests. The tool retains preprocessing, emission/layout, compiler execution, reflection, cache, and publication code. The current test project references the tool and receives the shared model transitively; it must not recompile it.

The runtime continues compiling the authoritative model sources and generating its own real shader implementations; it does not reference or package these offline assemblies. The analyzer keeps its private source-linked validation subset because its host targets netstandard2.0. These are isolated host copies, not competing model identities within the offline tool's dependency closure. Retain a single source definition for each model. AdditionalFiles may include the model source for semantic analysis, but must never compile another model definition into the catalogue output. Verify symbol binding against the referenced model and reject duplicate emitted types during the separation tests.

[GpuShaderContracts](../VanillaGraphicsExpanded/Rendering/Contracts/GpuShaderContracts.cs) and BuildValidationShaderPrograms are catalogue-facing facades because they reference GeneratedShaderCatalog. Pure ShaderVariantResolver remains in the model. Keep ShaderStageDeclarations in the model; its scope-qualified interning is model behavior, not generated data.

The existing build chain is runtime project -> SpirvBuild -> ShaderBuildTool, with analyzer project references during compilation. [SpirvBuild](../SpirvBuild/VanillaGraphicsExpanded.SpirvBuild.csproj) restores dotnet-shaderc, resolves the executable via GetTargetPath with inherited output properties, and runs it incrementally after Build. Program selects production, tests, build-validation, or build-validation-graphics. The runtime CopySpirvArtifactsToOutputAssets target copies only SPIR-V/manifests, excludes private trees, removes obsolete outputs, and uses SkipUnchangedFiles. Preserve these boundaries and configuration-specific outputs when adding the offline projects.

### Concrete identity and projection inputs

Use ordinal ordering and typed scalar bits plus scalar kind for canonical data; do not serialize culture-dependent display strings or mutable runtime values. Use the resolver's current immutable stage selections and declaration defaults. Keep output association separate from reusable compiler content so moving an unchanged binary does not force compilation.

| Record/input group | Source-derived contents and implementation coverage |
| --- | --- |
| Expanded source | Root canonical ID and physical association, asset root/domain resolution context, root/import byte digests, dependency edges, default preprocessing options, and successful expanded text. Cover ShaderSourcePreprocessor, FileSystemSyntaxTreeResourceResolver, ShaderSyntaxTreePreprocessor sanitation, GlslSchema, LineDirectiveInjector, StripNonAscii, and the terrain-capture fixture transform. TinyTokenizer/TinyAst, TinyAst.Preprocessor, TinyPreprocessor and their managed dependency closure are implementation inputs. |
| Emitted variant | Expanded text digest; ordered fixed define names/typed values; structural names/typed selected values; active specialization IDs with option names, GLSL types and declaration defaults; stage kind; effective binding maps. Cover ShaderVariantSource, DerivedGlobalConstants, ShaderSourceLayout, schema/interface nodes, model selection/projection behavior and their dependencies. |
| Binding projection | UniformLocations; sampler/image names and slots; uniform/storage block names and slots; varying locations; fragment-output locations. Persist the full effective binding contract, including descriptor type/required/policy information, conservatively so changes to contract interpretation cannot silently escape reevaluation. Layout currently consumes a subset; retain the owning model's canonical projection rather than a parallel ad hoc map. |
| Compiler result | Final source digest, stage, entry point, effective arguments (-x=glsl, target, optimization, warnings and Debug information), tool-manifest selection and actual managed/native dotnet-shaderc package contents. Execution policy must be derived from the same argument builder that invokes the compiler. Include compiler-visible source/work path context when Debug output embeds it; establish this with equivalence tests before permitting reuse across paths. |
| Packaged interface | Binary digest, selected stage kind/entry point, selection.Key, Debug/Release label, PackagedShaderInterface schema, extraction identity and actual ShaderInterfaceExtraction/ShaderInterfaceReflection implementation plus Silk.NET/SPIRV-Cross managed/native dependencies. Current native reflection reads stage inputs/outputs and execution modes; it does not infer all resource bindings. Bindings remain authoritative contract data. |
| Output membership | Registry scope, ordered resolved stage identities and selection keys, BinaryPath association, and expected manifest schema/output set. Re-resolve declarations for coverage/collisions; changes in assignments, conditions, structural defaults/domains, aliases and groups are validated by the resolver before effective selections are compared. |

Canonical projection must include the active specialization set after conditions have been evaluated, including changes from active to inactive. Structural defaults also influence which selection receives the base binary path. A full catalogue identity belongs to receipt/membership validation, not every compiler key. ShaderStageContract.Equivalent and ShaderStageSelection already define ownership/projection semantics; extend their owning model with canonical projection rather than deriving semantics from generated C# text.

Initially the stable ShaderBuildTool and ShaderBuildModel assembly content hashes may conservatively govern preprocessing/emission/reflection implementation identities. Remove catalogue bytes from this implementation set explicitly; recursive hashing of every DLL beside the executable is unsuitable after separation. Resolve and hash the actual dependencies for each processing family. Package/native changes and schema changes remain inputs. Compiler identity uses actual compiler contents and effective invocation policy; the build-tool assembly is not itself a shader compiler. Thus an extractor implementation edit may conservatively re-emit variants while byte-identical source still avoids compiler execution. Generated shared SH table content is a normal dependency of its consumers; deterministic generation must avoid timestamp churn for identical bytes.

### Preprocessing result boundary

Introduce an immutable expanded-source result owned by the offline preprocessing layer: expanded text, root ID, immutable processed-resource IDs, canonical dependency node/edge snapshot, and per-resource physical-path/actual-read-digest associations. Construct it only from a successful library result. Keep the live ResourceDependencyGraph local to preprocessing/planning; workers receive the immutable snapshot. The root is captured explicitly because it bypasses the resolver callback. Preserve source-map injection and fixture transforms before taking the final expanded-text digest.

The callback captures input bytes and metadata; DependencyGraph supplies topology. Restore isolated nodes before edges using AddResource/AddDependency. A removed import replaces that root's snapshot, not a shared mutable union. Derive resource-to-root ownership from per-root records. The executed ShaderDependencyGraphTests fixture establishes that GetDependencies/GetDependents return direct neighbors only: traverse repeatedly with a visited set for nested invalidation. For root -> outer -> leaf, both library processing order and ProcessedResources return leaf, outer, root. For an isolated root, GetAllResources and ProcessedResources contain the root but GetProcessingOrder is empty; never use processing order as the persisted node inventory. Cyclic imports fail with library diagnostics, and restored cycles are detected by HasCycles/DetectCycles. The callback records only imports, confirming the need for explicit root capture. See [focused graph fixtures](../ShaderBuildTool.Tests/ShaderDependencyGraphTests.cs). Missing IDs, inconsistent edge endpoints, unresolved resources, or failed preprocessing cannot create reusable records.

### Publication transaction and recovery

Use a versioned private journal under outputRoot/_tmp and unique same-parent pending/previous directories for the published shader directory. The adjacent outputRoot.lock remains the writer lease and survives clean. Journal data identifies the transaction, normalized owned paths, whether a prior generation existed, prior/new complete output digests, manifest digest, and intended input/receipt identity. Never trust journal paths without containment checks. Publish journal revisions by temporary file replacement. Staging has no authority to establish success.

Use the following deterministic recovery rules; directory contents and expected digests are authoritative if a crash occurred between a rename and its journal update:

| Interruption boundary | Recovery under the lease before receipt checking |
| --- | --- |
| Building staging, before active mutation | Discard only owned incomplete staging. Retain the old generation. A rebuild has no current success marker. |
| Staging validated and prepared journal committed | If active still matches prior generation and previous is absent, abandon staging and retain prior. If first publication has no prior, discard staging and restart. |
| Prior renamed to previous, active absent | Restore previous to active, discard pending, and leave no success receipt. |
| New generation installed, before receipt commit | If active matches new complete digests, retain it as a repair/cache source but remove any receipt and restart normal input/output validation; clean up the owned previous/pending paths. If new is invalid and previous validates, restore previous. |
| Receipt replacement completed, cleanup incomplete | Accept only after ordinary receipt input/output verification and exact journal-to-generation matching. Then finish cleanup. A changed current input causes a normal rebuild. |
| Invalid journal or ambiguous/damaged generations | Do not accept a success marker or delete unverified paths. Fail with explicit repair guidance unless one generation can be proven coherent from trusted matching digests. |

The normal successful sequence is: invalidate prior success before selected work; construct/validate staging; commit prepared journal; verify input snapshot; move prior; install new; validate installed output; recheck input snapshot; atomically replace receipt; then remove previous/staging and journal last. Preserve the old generation until the receipt commits during normal execution. If publication completes but receipt publication fails, the next build validates the retained complete generation without treating it as a prior success. Successful cache entries remain independently reusable.

If membership, manifest and binaries are identical, do not swap directories. Revalidate inputs and outputs and atomically publish the matching receipt; an interruption before that leaves no success marker. Explicit clean first recovers any outstanding transaction, then discards outputs/cache under the same lease. All destructive cleanup stays within verified owned transaction paths.

Hard-link only verified immutable binaries from the previous generation or cache on the same filesystem. Never link a mutable receipt/journal/manifest. All writers replace binary files rather than opening them for in-place modification; cache repair follows the same rule. Unsupported/cross-volume/permission-related hard-link failures fall back to a verified copy, preserve timestamps, and report the cost. The output generation is verified again before acceptance, so corruption of a linked source cannot bypass integrity checks. Protection from arbitrary external concurrent in-place writers is not promised. Actual filesystem and interruption tests belong to the publication implementation gate.

### Input snapshot contract

Capture the registry selections and relevant loaded implementation identities once. For each newly read root/import, hash the same bytes supplied to the parser; compare file metadata before/after reading and fail on detected concurrent mutation. Deduplicate repeated resource observations only when their content identities agree. Reused dependencies are checked through ShaderFileHashIndex using the documented normal/strict policy.

Before directory mutation and again before receipt replacement, compare input membership, dependency observations, catalogue/implementation files, compiler policy, and generated include against the captured snapshot. Never replace a changed loaded catalogue with fresh on-disk hashes while still using old in-memory declarations. On any mismatch, fail the invocation with a precise input-changed diagnostic; do not retry automatically. Leave no success receipt, retain valid immutable cache entries, and preserve/recover a coherent generation. A subsequent invocation is the explicit retry.

Strict mode rehashes all checked content at these boundaries, including reused dependencies. Normal mode retains the existing metadata-assisted limitation for edits that preserve size and timestamps. These checks detect observable concurrent edits; they do not claim an atomic filesystem snapshot against external writers after the final check. A receipt records the captured inputs, and the next invocation validates them again. Shared include generation remains atomic and content-preserving so identical concurrent generation does not create a false content change.
