using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached primitive restart transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes RestartIndex while suppressing a known identical native transition.</summary>
    internal void SetRestartIndex(uint index)
    {
        
        ValidateCompleteMutation();
        if (assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex) && assembly.RestartIndex == index) return;
        assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.RestartIndex;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PrimitiveRestartIndex(index);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        assembly.RestartIndex = index;
        assemblyKnown |= PrimitiveAssemblyStateKnowledge.RestartIndex;
    }
    #endregion
}
