using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns PrimitiveAssembly transitions and independent field validity.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes PatchVertices, suppressing a known identical transition.</summary>
    public void SetPatchVertices(int count)
    {
        ValidateBoundaryMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices);
        // Invalid patch sizes must not be published as known state after a native error.
        EnsurePatchLimit();
        if (count <= 0 || count > GpuSupport.MaxPatchVertices) throw new System.ArgumentOutOfRangeException(nameof(count));
        if (assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices) && assembly.PatchVertices == count) return;
        assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PatchVertices;
        GL.PatchParameter(PatchParameterInt.PatchVertices, count);
        FixedFunctionCalls++;
        assembly.PatchVertices = count;
        assemblyKnown |= PrimitiveAssemblyStateKnowledge.PatchVertices;
    }
    #endregion
    #region Private
    /// <summary>Resolves the immutable patch limit before scoped managed draws.</summary>
    private static void EnsurePatchLimit()
    {
        GpuSupport.EnsureCurrentContext();
        if (GpuSupport.MaxPatchVertices <= 0) throw new System.InvalidOperationException("No current patch capability.");
    }
    #endregion
}
