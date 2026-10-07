using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
namespace ShaderBuildTool.Spirv;
/// <summary>Packages compiler declarations without restricting ordinary numeric specialization values.</summary>
internal static class ShaderInterfaceExtraction
{
    internal const string ExtractorIdentity = "spirv-cross-2.23.0;interface-v2";
    #region Public API
    /// <summary>Associates declarations with the exact stage, structural selection and configuration.</summary>
    internal static PackagedShaderInterface Extract(byte[] binary, ShaderStageSelection selection)
    {
        using var reflection = new ShaderInterfaceReflection(binary, selection.Stage);
        return new(PackagedShaderInterface.CurrentVersion, ExtractorIdentity, selection.Stage.Kind,
            selection.Stage.EntryPoint, selection.Key, ShaderCompilerProcess.GenerateDebugInfo ? "Debug" : "Release", reflection.Read());
    }
    #endregion
}
