# Generated shader binding state

The [prepared bindings and generated state proposal](Rendering.ShaderBindingState.Proposal.md) describes
the intended replacement for the location-dependent submission path. This audit records the existing
declarations and partial migration; moving locations onto sampler attributes does not complete that design.
Implementation and acceptance are tracked in the dedicated [task list](Rendering.ShaderBindingState.todo).

## Uniform-location audit

The initial audit found 117 `ShaderBindingKind.UniformLocation` declarations. Seven local sampler
location descriptors have since moved onto their sampler declarations, leaving 110 shared
declarations. They do not create additional GLSL uniforms and none stores a previous CPU binding value. Each records the
explicit `layout(location = N)` of an existing uniform so `GpuProgramInterface` can map a logical
resource name to the active numeric location of a linked SPIR-V program without relying on a
preserved debug name.

| Declarations | Resource contract | Runtime consumer | Finding |
| --- | --- | --- | --- |
| 110 in `Rendering/Contracts/IShaderInterfaceLocations.cs` (locations 0 through 109) | Shared standalone uniforms, including sampler and image names used by multiple shader families | `GpuProgramInterface` builds its `Uniforms` map from `GpuBindingContract.UniformLocations`; `GpuProgramLayout.ResolveUniformLocation` and `ShaderBindingSubmission` use that map to identify active resources | Required by the current SPIR-V name-to-location mechanism. They are broad shared interface metadata, not CPU state tracking. |
| 5 originally in `PBR/Liquids/ILiquidShaderProgramBindings.cs` (`terrainTex`, `depthTex`, `vge_materialParamsTex`, `vge_waterMediumIndices`, `vge_waterMediumRecords`; locations 100 through 104) | Samplers declared by the same liquid binding contract | The same `GpuProgramInterface` mapping path | Duplicate metadata. Migrated to each sampler's `UniformLocation` attribute; the five descriptor properties were removed. |
| 2 originally in `PBR/IPBRCompositeShaderProgramBindings.cs` (`vge_waterOpticalDepth`, `vge_waterSource`; locations 100 and 101) | Water-boundary samplers declared by the same composite contract | The same `GpuProgramInterface` mapping path | Duplicate metadata. Migrated to each sampler's `UniformLocation` attribute; the two descriptor properties were removed. |

The redundant pattern was separate C# descriptor properties for sampler locations, not extra
shader uniforms and not prior-value storage. Generated shader state now owns prior assigned values.
The seven local sampler locations now live on their sampler declarations, preserving the SPIR-V
logical-name map without descriptor properties. The shared 110-entry interface needs separate
migration evidence before any removal because it currently covers uniforms beyond a single shader's
resource contract.
