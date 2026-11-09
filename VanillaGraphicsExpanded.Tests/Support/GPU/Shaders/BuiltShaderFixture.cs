using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

/// <summary>Loads explicit registered stage identities through the strict production binary loader.</summary>
internal static class BuiltShaderFixture
{
    private static readonly ShaderVariantResolver Fixtures = ShaderBuildTool.Spirv.TestShaderPrograms.Create();

    #region Public API
    #region Loading
    /// <summary>Loads a declared executable through validated asset caching and retains its ready owner.</summary>
    internal static RHIShader LoadProgram(ShaderLoadPlan plan)
    {
        using var candidate = new VanillaGraphicsExpanded.Rendering.ShaderCompilation.ShaderAssetCandidate(plan,
            path => TestAssetSnapshot.Built.Read("shaders/" + path),
            () => ShaderBinaryDigest.Parse(TestAssetSnapshot.Built.Copy("shaders/" + ShaderBinaryDigest.FileName)));
        candidate.Complete();
        return candidate.TakeShader();
    }

    /// <summary>Selects an explicit test-catalog graphics pair without compiling detached stages on cache hits.</summary>
    internal static RHIShader LoadFixtureProgram(string vertex, string fragment)
    {
        var contract = Fixtures.Programs.Values.Single(program =>
            program.Stages.Any(stage => stage.Identity == vertex) && program.Stages.Any(stage => stage.Identity == fragment));
        return LoadProgram(new(new ShaderSettings(contract)));
    }

    /// <summary>Loads a declared stage's requested selection and retains its native owner.</summary>
    public static GpuShaderModule Load(string identity, ShaderType type, IReadOnlyDictionary<string, string?>? defines = null)
    {
        var stage = GpuShaderContracts.Registry.FindStage(identity);
        if (!PrecompiledStageFixture.Matches(stage.Kind, type)) throw new ArgumentException("Stage kind mismatch: " + identity);
        var owner = GpuShaderContracts.Registry.Programs.Values.OrderBy(p => p.Identity, StringComparer.Ordinal).First(p => p.Stages.Any(s => s.Identity == identity));
        var selected = new ShaderLoadPlan(new ShaderSettings(owner, defines)).Stages.Single(s => s.Stage.Identity == identity);
        return PrecompiledStageFixture.Load(selected,
            path => TestAssetSnapshot.Built.Copy("shaders/" + path));
    }
    /// <summary>Loads a fixed test stage with source-authored numeric locations and retains its native owner.</summary>
    public static GpuShaderModule LoadFixture(string path, ShaderType type)
    {
        byte[] binary = TestAssetSnapshot.Built.Copy("shaders/" + path + ".spv");
        return VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRHIContext.CompileExternalStage(new(PrecompiledStageFixture.Stage(type), binary)).RequireResource();
    }
    #endregion
    #endregion
}
