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
