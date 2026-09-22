using System;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Pairs production fragment binaries with the test framebuffer's fullscreen vertex interface.</summary>
internal static class PbrShaderPrograms
{
    /// <summary>Loads the direct-lighting fragment with a vertex stage that generates UVs from positions.</summary>
    public static int CompilePbrDirectLightingProgram(ShaderTestHelper helper)
    {
        var result = helper.CompileAndLink("tests/fullscreen_uv.vsh", "pbr_direct_lighting.fsh");
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        return result.ProgramId;
    }
    /// <summary>Loads the existing composite stage pair through built binaries.</summary>
    public static int CompilePbrCompositeProgram(ShaderTestHelper helper)
    {
        var result = helper.CompileAndLink("pbr_composite.vsh", "pbr_composite.fsh");
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        return result.ProgramId;
    }
}
