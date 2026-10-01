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

Declare ordinary typed properties on binding interfaces beside their shader owners.
The marker attribute owns the GLSL name, resource kind, index and applicable stages.
The generator implements resource setters through the owner's linked program layout:

```csharp
/// <summary>Declares the shader's parameter buffer and albedo input.</summary>
internal interface IMyShaderBindings
{
    #region Public API
    /// <summary>Binds the parameter buffer used by both stages.</summary>
    [ShaderBinding("MyBlockUBO", ShaderBindingKind.UniformBlock,
        GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }

    /// <summary>Binds the fragment stage's albedo texture.</summary>
    [ShaderBinding("albedoTex", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    GpuTexture Albedo { set; }
    #endregion
}

/// <summary>Consumes the generated binding API.</summary>
[ShaderProgram("Contract", "my_shader", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "my_shader.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "my_shader.fsh")]
internal partial class MyShaderProgram : GpuProgram, IMyShaderBindings { }
```

Implement `IMyShaderBindings` on a partial `GpuProgram` subclass to generate its setters.
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

Shared layouts and contract-only owners import interfaces with get-only properties of
`ShaderSamplerBinding`, `ShaderImageBinding`, `ShaderUniformBlockBinding`, or
`ShaderStorageBlockBinding`. Location declarations use the corresponding
`ShaderUniformLocationBinding`, `ShaderVaryingLocationBinding`, or
`ShaderFragmentOutputLocationBinding` descriptor. These descriptors expose name,
index and required-resource policy without referencing engine resource types.
Unsupported types/accessors and class-based binding declarations produce `VGEGEN001`.

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

### Interface binding declarations

Interfaces can own the binding API and its metadata. Implement the interface on a
partial shader owner to generate its missing properties through the existing runtime
layout. Interface properties are ordinary public instance properties; they do not
need `partial`. For example:

```csharp
/// <summary>Declares the shared cache input.</summary>
internal interface ICacheBindings
{
    /// <summary>Binds the cache texture.</summary>
    [ShaderBinding("cache", ShaderBindingKind.Sampler, 18,
        ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuTexture Cache { set; }
}

/// <summary>Consumes the generated cache API.</summary>
[ShaderProgram("Contract", "my_shader", 1)]
[ShaderStage("Contract", ShaderStageKind.Fragment, "my_shader.fsh")]
public partial class MyShaderProgram : GpuProgram, ICacheBindings { }
```

The generated setter is named `Cache` and is callable directly or through the
interface. `GpuTextureBinding` image views and all existing resource setter types
remain supported. Descriptor types such as `ShaderSamplerBinding` and
`ShaderUniformLocationBinding` use get-only instance interface properties; their
generated implementations return typed binding metadata without engine calls.

Interface inheritance composes declarations, and diamonds retain each original
property once. A derived interface can explicitly redeclare a property with `new`
and its own `ShaderBinding` attribute to override its index, required policy or
applicability. The C# property name/type and GLSL name/kind must remain the same.
A composite redeclaration can resolve matching API names from multiple parents;
otherwise unrelated interfaces claiming the same API name are diagnosed as ambiguous.
Different properties claiming the same GLSL name or index remain conflicts.

An existing concrete implementation, including an explicit interface implementation,
keeps its authored body and accessibility. Default interface bodies are also preserved
and are called through the interface when C# requires it. Neither receives a duplicate
generated setter. Their code remains responsible for applying the binding behavior;
the interface attribute still supplies the offline/runtime contract. An unannotated
defining partial property is completed from interface metadata and must retain the
interface accessor shape. Layout overrides belong on derived interfaces, and concrete
properties must not repeat binding attributes. Concrete abstract
properties require an authored body or a defining partial declaration instead.

Nullable `GpuTexture?` sampler properties preserve their authored null/unbind behavior.
Existing engine texture-ID setters can declare `int` sampler properties on the interface;
they require an authored setter selecting the texture target and sampler policy. New
generated setters use GPU resource types. Liquid keeps its seven engine-facing setters
internal through explicit interface implementations. `GpuUniformBuffer`,
`GpuShaderStorageBuffer` and their shared `GpuBufferObject` base are public resource
types, so generated buffer setters on public shaders are directly callable. Internal
descriptor types still use explicit interface implementations on public shader owners.

`ShaderBindingSet(typeof(ICacheBindings))` imports only metadata. This allows static
contract owners and fixtures to consume resource interfaces without implementing
instance members or accessing GPU resources. Interfaces themselves can import other
sets. `Defaults`, `Stages`, `Program`, and property `Programs` filters follow the same
consumer filtering rules. Interface properties are validated even when unused;
runtime resource types and interface bodies never enter generated offline shells.
Owners and binding interfaces must be top-level and nongeneric; runtime owners remain
partial classes. Class-based binding declarations and class imports are rejected.

### Metadata-only binding imports

```csharp
/// <summary>Owns the shared surface-cache lookup interface.</summary>
internal interface ICacheMetadata
{
    #region Public API
    /// <summary>Declares the shared cache sampler.</summary>
    [ShaderBinding("cache", ShaderBindingKind.Sampler, 18,
        ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    ShaderSamplerBinding Cache { get; }
    #endregion
}

/// <summary>Consumes shared metadata without creating a resource API.</summary>
[ShaderBindingSet(typeof(ICacheMetadata), Stages = new[] { ShaderStageKind.Fragment })]
internal static partial class MyFixtureShader { }
```

Sets can reference other sets; base-class and interface declarations are inherited at compile time.
Diamond references to the same property retain one declaration. Cycles, duplicated names
from different properties, conflicting indices, unsupported properties, invalid names/stages,
and incompatible complete layouts for a shared stage identity produce compiler errors.
A set's `Program` selects the consumer member. Program restrictions on imported properties
are evaluated against that consumer member too; use unrestricted properties for general
shared layouts.

`Defaults = true` supplies declarations only where an explicit declaration has not
supplied that kind/name. `IShaderInterfaceLocations` owns the existing stable global
locations and intentional interface aliases. `IShaderIncludeBindings` owns optional
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
