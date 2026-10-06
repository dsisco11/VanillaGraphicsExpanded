using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached assembly enable transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes PrimitiveRestart, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPrimitiveRestartEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart) && assembly.PrimitiveRestart == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PrimitiveRestart;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.PrimitiveRestart); else GL.Disable(EnableCap.PrimitiveRestart);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        assembly.PrimitiveRestart = enabled;
        assemblyKnown |= PrimitiveAssemblyStateKnowledge.PrimitiveRestart;
    }

    /// <summary>Establishes PrimitiveRestartFixedIndex, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPrimitiveRestartFixedIndexEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (!GpuSupport.Graphics.FixedIndexRestart)
        {
            if (enabled) throw new NotSupportedException("PrimitiveRestartFixedIndex is unavailable.");
            return;
        }
        if (assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex) && assembly.PrimitiveRestartFixedIndex == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.PrimitiveRestartFixedIndex); else GL.Disable(EnableCap.PrimitiveRestartFixedIndex);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        assembly.PrimitiveRestartFixedIndex = enabled;
        assemblyKnown |= PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
    }
    #endregion
}
