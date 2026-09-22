using System;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Pairs production fragment binaries with the test framebuffer's fullscreen vertex interface.</summary>
internal static class PbrShaderPrograms
{
    /// <summary>Loads the direct-lighting fragment with a vertex stage that generates UVs from positions.</summary>
    public static int CompilePbrDirectLightingProgram(ShaderTestHelper helper)
    {
        var result = helper.CompileProgram("tests/pbr_direct_fullscreen");
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        return result.ProgramId;
    }
    /// <summary>Loads the existing composite stage pair through built binaries.</summary>
    public static int CompilePbrCompositeProgram(ShaderTestHelper helper)
    {
        var result = helper.CompileProgram("pbr_composite");
        if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
        return result.ProgramId;
    }
}
