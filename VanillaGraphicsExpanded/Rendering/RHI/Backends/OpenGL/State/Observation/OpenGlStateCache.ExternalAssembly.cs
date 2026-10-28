using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates defined external assembly effects into the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Commits the defined PatchParameter effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPatchParameter(PatchParameterInt parameter, int value)
    {
        assembly.PatchVertices = value; assemblyKnown |= PrimitiveAssemblyStateKnowledge.PatchVertices;
    }
    /// <summary>Commits the defined PrimitiveRestartIndex effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPrimitiveRestartIndex(uint index)
    {
        assembly.RestartIndex = unchecked((uint)index); assemblyKnown |= PrimitiveAssemblyStateKnowledge.RestartIndex;
    }
    /// <summary>Commits the defined PrimitiveRestartIndex effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalPrimitiveRestartIndex(int index)
    {
        assembly.RestartIndex = unchecked((uint)index); assemblyKnown |= PrimitiveAssemblyStateKnowledge.RestartIndex;
    }
    #endregion
}
