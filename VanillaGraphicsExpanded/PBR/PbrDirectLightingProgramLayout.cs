using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns the installed binding layout and retained packed inputs.</summary>
internal sealed class PbrDirectLightingProgramLayout : GpuProgramLayout
{
    public PbrDirectLightingParamsUbo Params { get; } = new();

    public PbrDirectLightingProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("pbr_direct_lighting"));
    }

}
