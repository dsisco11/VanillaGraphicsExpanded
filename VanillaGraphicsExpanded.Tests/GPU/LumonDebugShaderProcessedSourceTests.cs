using System;
using System.IO;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonDebugShaderProcessedSourceTests : RenderTestBase
{
    public LumonDebugShaderProcessedSourceTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void LumonDebugFragmentShader_ContainsLumonSceneDebugHelpers_AndCompiles()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        var result = helper.CompileShader("lumon_debug.fsh", ShaderType.FragmentShader);
        Assert.True(result.IsSuccess, result.ErrorMessage);

        string src = GetShaderSource(result.ShaderId);
        Assert.Contains("renderLumonSceneIrradianceDebug", src);
        Assert.Contains("VgeLumonSceneTrySampleIrradiance_NearFieldV1", src);
    }

    [Fact]
    public void LumonDebugGBufferFragmentShader_ContainsLumonSceneDebugHelpers_AndCompiles()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        var result = helper.CompileShader("lumon_debug_gbuffer.fsh", ShaderType.FragmentShader);
        Assert.True(result.IsSuccess, result.ErrorMessage);

        string src = GetShaderSource(result.ShaderId);
        Assert.Contains("renderLumonSceneIrradianceDebug", src);
        Assert.Contains("VgeLumonSceneTrySampleIrradiance_NearFieldV1", src);
    }

    private static string GetShaderSource(int shaderId)
    {
        GL.GetShader(shaderId, ShaderParameter.ShaderSourceLength, out int len);
        if (len <= 0) return string.Empty;
        GL.GetShaderSource(shaderId, len, out _, out string src);
        return src ?? string.Empty;
    }

    private static ShaderTestHelper CreateShaderHelperOrSkip()
    {
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.Skip("Shader assets not available - test output content may be missing");
        }

        return new ShaderTestHelper(shaderPath, includePath);
    }
}
