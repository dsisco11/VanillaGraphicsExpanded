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

## Uniform Blocks (UBOs)

VGE targets GLSL `#version 330`, so uniform-block bindings cannot rely on `layout(binding=...)` without extensions.

Preferred pattern:

- Define a program layout contract by overriding `CreateLayout()` with a shader-specific `GpuProgramLayout` subclass.
- Register expected UBO binding points (and optionally sampler/image units) inside that layout.
- After `Use()` (per pass), bind a buffer to the block by name:
  - `program.TryBindUniformBlock("MyBlockUBO", myUniformBuffer)`

Example:

```csharp
internal sealed class MyShaderLayout : GpuProgramLayout
{
    public MyShaderLayout()
    {
        RegisterUniformBlockBinding("MyBlockUBO", bindingIndex: 12, required: true);
        RegisterSamplerUnit("albedoTex", unit: 0, required: true);
    }
}

public sealed class MyShaderProgram : GpuProgram
{
    protected override GpuProgramLayout CreateLayout() => new MyShaderLayout();
}
```

This keeps binding indices centralized in the program wrapper and avoids renderers hardcoding `glUniformBlockBinding` calls.

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
