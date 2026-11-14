using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Shadows;

/// <summary>Validates the shared surface interface once after native program linking, retaining managed executable identity.</summary>
internal static class SurfaceShadowPrograms
{
    #region Public API
    /// <summary>Lists native forward material families; opaque terrain receives shadows in the owned deferred pass.</summary>
    internal static bool Participates(string? pass)=>pass is "standard" or "entityanimated" or "instanced" or "chunktransparent";

    /// <summary>Withdraws the interface before either successful or failed shader replacement.</summary>
    internal static void Remove(ShaderProgramBase program)
    {ShaderCapabilities.Remove(program,ShaderCapability.SharedSurfaceShadows);}

    /// <summary>Publishes only linked programs with every required typed binding at the declared baseline slots.</summary>
    internal static void Register(ShaderProgram program)
    {
        Remove(program);
        if(program.AssetDomain==Constants.ModId || !Participates(program.PassName) || program.ProgramId==0)return;
        var layout=GpuProgramLayout.TryBuild(program.ProgramId);
        if(!layout.TryGetUniformBlockBinding("SurfaceShadowParameters",out int parameters)||parameters!=18 ||
            !layout.TryGetShaderStorageBlockBinding("SurfaceShadowRequests",out int requests)||requests!=0 ||
            !layout.TryGetShaderStorageBlockBinding("PageEntries",out int entries)||entries!=1 ||
            !layout.TryGetShaderStorageBlockBinding("SurfaceShadowReadiness",out int readiness)||readiness!=2 ||
            !layout.TryGetShaderStorageBlockBinding("SurfaceShadowDiagnostics",out int diagnostics)||diagnostics!=5 ||
            !layout.TryGetSamplerUnit("shadowDepthPool",out int sampler)||sampler!=33)return;
        ShaderCapabilities.Publish(program,ShaderCapability.SharedSurfaceShadows);
    }

    /// <summary>Checks managed linked identity without reflecting resources during native draw submission.</summary>
    internal static bool Has(ShaderProgramBase program)=>ShaderCapabilities.Has(program,ShaderCapability.SharedSurfaceShadows);

    /// <summary>Requires the completed owned executable's active interface, rather than optional declarations optimized out by another variant.</summary>
    internal static bool HasOwned(GpuProgram program)
    {
        var facts=program.ProgramLayout.BinaryInterface;
        if(facts is null)return false;
        return Match(ShaderBindingKind.UniformBlock,"SurfaceShadowParameters",18) &&
            Match(ShaderBindingKind.StorageBlock,"SurfaceShadowRequests",0) && Match(ShaderBindingKind.StorageBlock,"PageEntries",1) &&
            Match(ShaderBindingKind.StorageBlock,"SurfaceShadowReadiness",2) && Match(ShaderBindingKind.StorageBlock,"SurfaceShadowDiagnostics",5) &&
            Match(ShaderBindingKind.Sampler,"shadowDepthPool",33);

        /// <summary>Reads only prepared activity and slot facts belonging to this exact executable.</summary>
        bool Match(ShaderBindingKind kind,string name,int slot)=>facts.ActiveBindings(kind).TryGetValue(name,out int value)&&value==slot;
    }
    #endregion
}
