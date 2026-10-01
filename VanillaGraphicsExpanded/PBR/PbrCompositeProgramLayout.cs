using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns the installed binding layout and retained packed inputs.</summary>
internal sealed class PbrCompositeProgramLayout : GpuProgramLayout
{
    public PbrCompositeParamsUbo Params { get; } = new();

    public PbrCompositeProgramLayout()
    {
        RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("pbr_composite"));
    }

}
