# VGE Shader Programs (GpuProgram)

This mod compiles all VGE-owned shaders through a unified pipeline implemented by `GpuProgram` + `ShaderSourceCode`.

## Add a new shader program

1. Add stage files under:
   - `VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/shaders/`
   - Names must match the program name: `{PassName}.vsh` and `{PassName}.fsh` (optional `{PassName}.gsh`).

2. Create a C# program inheriting `GpuProgram`.

3. Register it using **memory shader registration** (important):

```csharp
public static void Register(ICoreClientAPI api)
{
    var instance = new MyShaderProgram
    {
        PassName = "my_shader",
        AssetDomain = "vanillagraphicsexpanded"
    };

    api.Shader.RegisterMemoryShaderProgram("my_shader", instance);
    instance.Initialize(api);
    instance.CompileAndLink();
}
```

Why `RegisterMemoryShaderProgram`?

- VGE builds stage sources itself (imports + define injection), so we don’t want the engine to load raw files again.
- The Harmony hook (`ShaderIncludesHook`) is intended for vanilla shaders; VGE shaders compile through `ShaderSourceCode`.

## Imports

Use AST-aware `@import` directives in shader stages:

```glsl
@import "./includes/pbrfunctions.glsl"
```

- VGE resolves `./includes/...` relative to the stage source (i.e. `shaders/{PassName}.fsh`).
- Place shared includes in `assets/vanillagraphicsexpanded/shaders/includes/`.

## Defines

At runtime, use `SetDefine(name, value)` / `RemoveDefine(name)` on the shader program.

- Defines are injected **after** the `#version` directive (AST-aware, not a string prepend).
- Because of that, every VGE shader stage must start with a `#version ...` line.
- A `null` value means `#define NAME` (no explicit value).

Define changes trigger a recompile on the main thread (GL context safety). If you cache uniform locations or similar program-dependent state, override `OnAfterCompile()` to refresh it.

## Generated GPU binding contracts

Declare strongly typed partial properties in the owning shader's main class file.
The marker attribute owns the GLSL name, resource kind, index and applicable stages.
The generator implements resource setters through the owner's linked program layout:

```csharp
[ShaderProgram("Contract", "my_shader", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "my_shader.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "my_shader.fsh")]
public partial class MyShaderProgram : GpuProgram
{
    #region Public API
    /// <summary>Binds the parameter buffer used by both stages.</summary>
    [ShaderBinding("MyBlockUBO", ShaderBindingKind.UniformBlock,
        GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    public partial GpuUniformBuffer Parameters { set; }

    /// <summary>Binds the fragment stage's albedo texture.</summary>
    [ShaderBinding("albedoTex", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    public partial GpuTexture Albedo { set; }
    #endregion
}
```

Sampler setters accept `GpuTexture`, image setters accept `GpuTexture` or `GpuTextureBinding`,
uniform-block setters accept `GpuUniformBuffer`, and storage-block setters accept
`GpuShaderStorageBuffer`. Image assignments specify access, mip level, layering,
layer and optional format, for example
`shader.Output = new(texture, Access: TextureAccess.WriteOnly, Layered: true)`.
An image property typed as `GpuTexture` uses the same defaults as `new GpuTextureBinding(texture)`:
read-only access, mip zero, layer zero, non-layered binding and the texture's internal
format. Use a `GpuTextureBinding` property when callers need write access or other view
settings. This value describes a binding; it does not create an OpenGL texture-view object.
Assignments require the owning program to be linked; bind the program before dispatch
or drawing. The setters resolve linked resource activity and skip optimized-away
resources. They do not upload data or retain resource ownership. Compute wrappers
use their owning `GpuComputePipeline` field named `pipeline`.

Shared layouts and contract-only owners use static get-only partial properties of
`ShaderSamplerBinding`, `ShaderImageBinding`, `ShaderUniformBlockBinding`, or
`ShaderStorageBlockBinding`. Location declarations use the corresponding
`ShaderUniformLocationBinding`, `ShaderVaryingLocationBinding`, or
`ShaderFragmentOutputLocationBinding` descriptor. These descriptors expose name,
index and required-resource policy without referencing engine resource types.
Unsupported types, accessors and non-partial declarations produce `VGEGEN001`.

`UniformLocation`, `Sampler`, `Image`, `UniformBlock` and `StorageBlock` use
independent index namespaces. A sampler's uniform location differs from its texture
unit. Shared stage I/O also supports `VaryingLocation` and `FragmentOutputLocation`.
Indices are nonnegative and must fit the target GPU's limits; compile-time validation
does not query driver capabilities. GLSL arrays and matrices still need enough
explicit location space in the shader interface.

The generator emits immutable `ShaderStageContract.Bindings` for both the mod and
`ShaderBuildTool`. Offline compilation reads C# declarations through Roslyn; runtime
uses direct generated references and never scans source or reflects over attributes.
`GpuProgramLayout.RegisterContract(Contract.Stages[1].Bindings)` consumes the same
fragment contract. Compute layouts use `Stages[0]`. Keep resource uploads in the
existing buffer methods; generated setters bind through the established layout
methods. Do not redeclare their slots there.

For a class declaring several programs, `Program = "Sky"` selects its generated
contract member, while `Programs = new[] { "Sky", "Lighting" }` shares one property
across a subset. Omission applies the property to every declared program using one of
its stages. Shader options and availability conditions continue to select variants;
a binding declaration describes their stable interface union. `Required = false`
suppresses missing-resource diagnostics. Optimized-away resources remain legal even
when required; an absent linked resource never causes a fallback slot assignment.

### Shared layouts

```csharp
/// <summary>Owns the shared surface-cache lookup interface.</summary>
internal static partial class CacheBindings
{
    #region Private
    /// <summary>Declares the shared cache sampler.</summary>
    [ShaderBinding("cache", ShaderBindingKind.Sampler, 18,
        ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    private static partial ShaderSamplerBinding Cache { get; }
    #endregion
}

[ShaderBindingSet(typeof(CacheBindings), Stages = new[] { ShaderStageKind.Fragment })]
public partial class MyShaderProgram { }
```

Sets can reference other sets; base-class declarations are inherited at compile time.
Diamond references to the same property retain one declaration. Cycles, duplicated names
from different properties, conflicting indices, unsupported properties, invalid names/stages,
and incompatible complete layouts for a shared stage identity produce compiler errors.
A set's `Program` selects the consumer member. Program restrictions on imported properties
are evaluated against that consumer member too; use unrestricted properties for general
shared layouts.

`Defaults = true` supplies declarations only where an explicit declaration has not
supplied that kind/name. `ShaderInterfaceLocations` owns the existing stable global
locations and intentional interface aliases. `ShaderIncludeBindings` owns optional
UBOs declared by common includes; several inactive block names can intentionally share
a reserved slot. Import these as defaults when using those production includes.
Ordinary resource sets should use the default strict mode. Different resource kinds
cannot claim the same GLSL resource name.

## Uniform Blocks (UBOs)

Put runtime parameters into shared `std140` schemas and upload them through the
existing ring-backed buffer methods, for example
`program.Parameters = myUniformBuffer`. The generated declaration
supplies the slot to offline SPIR-V layout emission and linked runtime diagnostics.

### UBO schemas (shared includes)

Define the `std140` block layout in a shared include under:

- `assets/vanillagraphicsexpanded/shaders/includes/`

Then `@import` that include from every stage that uses the block, so the schema stays consistent.

### Rules ("UBO-only" for non-opaque)

- Do not use standalone non-opaque uniforms (`float`, `vec*`, `mat*`) for runtime parameters.
- Put non-opaque runtime parameters into `std140` uniform blocks and update them via UBO uploads/bind-range.
- Opaque types cannot live in UBOs by GLSL rules:
  - Samplers (`sampler2D`, `sampler3D`, etc)
  - Images (`image2D`, etc)
    These must use explicit bindings where available, or a layout contract that assigns stable units once after link.

### Binding Registry

Global binding points/units are reserved and documented here:

- [docs/GpuBindingRegistry.md](GpuBindingRegistry.md)

Shader layouts should prefer these reserved binding points instead of inventing new ones.

### Runtime updates (UBO ring)

VGE supports a per-frame UBO ring allocator for high-churn blocks (object/material params). The intended cadence is:

- Frame/View UBO: update once per frame.
- Object/Material UBO: allocate+bind a range per draw/dispatch.

In C#, `CpuUniformBuffer` is CPU-only packing; binding happens through a ring-backed `BindTo(...)` call.

Notes on explicit bindings:

- Compute shaders (`#version 430+`) can use explicit `layout(binding=...)` directly.
- For graphics shaders that target `#version 330`, explicit `layout(binding=...)` may be available when the driver supports `GL_ARB_shading_language_420pack`.
- Regardless, VGE applies the layout contract once after link as a deterministic fallback (UBOs via `glUniformBlockBinding`, samplers/images via `glUniform1i`).

## Dev-mode contract validation

In `DEBUG` builds, VGE runs a validation pass after link to compare the expected layout contract against the linked program’s reflection:

- Missing required resources are logged once.
- Binding/unit mismatches are logged once.

This is intended to catch silent shader drift (renames, optimized-away required resources, incorrect binding decorations) early without crashing the client.
