using System;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// A VGE shader program that can load vertex/fragment stages from different asset base-names.
///
/// Intended for multi-pass pipelines that share a common fullscreen vertex stage.
/// </summary>
internal class VgeStageNamedShaderProgram : GpuProgram
{
    private readonly string vertexStageShaderName;
    private readonly string fragmentStageShaderName;
    private readonly string geometryStageShaderName;

    protected override string VertexStageShaderName => vertexStageShaderName;

    protected override string FragmentStageShaderName => fragmentStageShaderName;

    protected override string GeometryStageShaderName => geometryStageShaderName;

    public VgeStageNamedShaderProgram(
        string passName,
        string vertexStageShaderName,
        string fragmentStageShaderName,
        string assetDomain,
        string? geometryStageShaderName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexStageShaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentStageShaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetDomain);

        PassName = passName;
        AssetDomain = assetDomain;

        this.vertexStageShaderName = vertexStageShaderName;
        this.fragmentStageShaderName = fragmentStageShaderName;
        this.geometryStageShaderName = geometryStageShaderName ?? passName;
    }

}
