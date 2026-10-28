# Resource lifecycle binding inventory

Planning input for [the approved lifecycle design](Rendering.MetadataLifecycle.Proposal.md) and
[its dedicated implementation plan](Rendering.MetadataLifecycle.todo), which feeds subphase 8i of
[the RHI implementation plan](Rendering.GpuCommands.todo). This is an initial factory/deletion inventory,
not completed configuration coverage or implementation qualification.

## Source and limits

Source: the existing 9,693-overload inventory produced by `artifacts/phase8h/inventory/Program.cs`.
Its copied `OpenTK.Graphics.dll` and the current installed reference at
`G:/Vintagestory/Lib/OpenTK.Graphics.dll` were compared directly and have SHA-256
`5B57957CA9E4C5F7BFB7EC495B991E6B88CFAC52863A19E405492AB8C33CB1D2`.
The project references `$(VINTAGE_STORY)/Lib/OpenTK.Graphics.dll`; the inventory uses that installed assembly.
This reuses a matching assembly inventory, not a newly executed probe or a completed ABI audit.

Only GL roots and their Core/Ext/Arb owners were enumerated. An extension suffix does not establish
an eligible owner. Legacy assembly programs, display lists and EXT vertex-shader symbol allocation
remain excluded even when exposed under an eligible owner. Platform imports listed below remain
subject to the existing supported capability and native-handle contracts.

The tables list native entry points, collapsing managed convenience overloads. `GL` means
`OpenTK.Graphics.OpenGL.GL`; `GL4` means `OpenTK.Graphics.OpenGL4.GL`. A plus suffix denotes
the nested binding owner. Observer presence is source evidence only, not effect correctness.

## Creation, reservation and import candidates

| Native symbol | Binding owners | Current observation / disposition |
| --- | --- | --- |
| `glCreateBuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateFramebuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateMemoryObjectsEXT` | `GL+Ext` | No creation/import observer declared |
| `glCreateProgram` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateProgramObjectARB` | `GL+Arb` | No creation/import observer declared |
| `glCreateProgramPipelines` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateQueries` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateRenderbuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateSamplers` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateShader` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateShaderObjectARB` | `GL+Arb` | No creation/import observer declared |
| `glCreateShaderProgramEXT` | `GL+Ext`, `GL4+Ext` | No creation/import observer declared |
| `glCreateShaderProgramv` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateShaderProgramvEXT` | `GL+Ext`, `GL4+Ext` | No creation/import observer declared |
| `glCreateSyncFromCLeventARB` | `GL+Arb`, `GL4+Arb` | No creation/import observer declared; reconcile optional sync integration scope |
| `glCreateTextures` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateTransformFeedbacks` | `GL`, `GL4` | No creation/import observer declared |
| `glCreateVertexArrays` | `GL`, `GL4` | No creation/import observer declared |
| `glFenceSync` | `GL`, `GL4` | No creation/import observer declared |
| `glGenBuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenBuffersARB` | `GL+Arb` | No creation/import observer declared |
| `glGenFramebuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenFramebuffersEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenLists` | `GL` | Excluded legacy feature |
| `glGenProgramPipelines` | `GL`, `GL4` | No creation/import observer declared |
| `glGenProgramPipelinesEXT` | `GL+Ext`, `GL4+Ext` | No creation/import observer declared |
| `glGenProgramsARB` | `GL+Arb` | Excluded legacy feature |
| `glGenQueries` | `GL`, `GL4` | No creation/import observer declared |
| `glGenQueriesARB` | `GL+Arb` | No creation/import observer declared |
| `glGenRenderbuffers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenRenderbuffersEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenSamplers` | `GL`, `GL4` | No creation/import observer declared |
| `glGenSemaphoresEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenSymbolsEXT` | `GL+Ext` | Excluded legacy feature |
| `glGenTextures` | `GL`, `GL4` | No creation/import observer declared |
| `glGenTexturesEXT` | `GL+Ext` | No creation/import observer declared |
| `glGenTransformFeedbacks` | `GL`, `GL4` | No creation/import observer declared |
| `glGenVertexArrays` | `GL`, `GL4` | No creation/import observer declared |
| `glGenVertexShadersEXT` | `GL+Ext` | Excluded legacy feature |
| `glImportMemoryFdEXT` | `GL+Ext` | No creation/import observer declared |
| `glImportMemoryWin32HandleEXT` | `GL+Ext` | No creation/import observer declared |
| `glImportMemoryWin32NameEXT` | `GL+Ext` | No creation/import observer declared |
| `glImportSemaphoreFdEXT` | `GL+Ext` | No creation/import observer declared |
| `glImportSemaphoreWin32HandleEXT` | `GL+Ext` | No creation/import observer declared |
| `glImportSemaphoreWin32NameEXT` | `GL+Ext` | No creation/import observer declared |
| `glImportSyncEXT` | `GL+Ext` | No creation/import observer declared; reconcile optional sync integration scope |

## Deletion candidates

| Native symbol | Binding owners | Current observation / disposition |
| --- | --- | --- |
| `glDeleteBuffers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteBuffersARB` | `GL+Arb` | Observer present; inspect per-name outcomes |
| `glDeleteFramebuffers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteFramebuffersEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteLists` | `GL` | No observer declared; reconcile feature scope before adding |
| `glDeleteMemoryObjectsEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteNamedStringARB` | `GL+Arb`, `GL4+Arb` | No observer declared; reconcile feature scope before adding |
| `glDeleteObjectARB` | `GL+Arb` | Observer present; inspect per-name outcomes |
| `glDeleteProgram` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteProgramPipelines` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteProgramPipelinesEXT` | `GL+Ext`, `GL4+Ext` | No observer declared; reconcile feature scope before adding |
| `glDeleteProgramsARB` | `GL+Arb` | No observer declared; reconcile feature scope before adding |
| `glDeleteQueries` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteQueriesARB` | `GL+Arb` | Observer present; inspect per-name outcomes |
| `glDeleteRenderbuffers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteRenderbuffersEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteSamplers` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteSemaphoresEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteShader` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteSync` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteTextures` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteTexturesEXT` | `GL+Ext` | Observer present; inspect per-name outcomes |
| `glDeleteTransformFeedbacks` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteVertexArrays` | `GL`, `GL4` | Observer present; inspect per-name outcomes |
| `glDeleteVertexShaderEXT` | `GL+Ext` | No observer declared; reconcile feature scope before adding |

## Configuration and realization work still required

- For every retained factory, identify reservation versus immediate realization and enumerate all later
  realization commands from the same compiler-referenced bindings. Include bind-created objects and
  EXT named operations where their specifications permit realization; do not infer semantics from names.
- Join storage, parameter, attachment, view, executable and mapping effects to each canonical schema.
  The existing [API coverage](Rendering.GpuResourceHandles.ApiCoverage.md) and
  [acquisition analysis](Rendering.GpuResourceHandles.Analysis.md) locate current families and consumers;
  they do not establish complete creation coverage.
- Trace backend saved-original factories separately from external callbacks, including failure cleanup
  and retirement. A missing external observer does not mean the managed factory lacks publication.
- Reconcile platform imports and extension-created sync objects against supported RHI operations.
  Do not expand support solely because an eligible binding owner exposes an entry point.
- Qualify exact ABI/output handling, zero/failed results, delayed realization, deletion/name reuse and
  context lifetime before treating any table row as implemented coverage.

No implementation checklist markers are completed by this inventory.
