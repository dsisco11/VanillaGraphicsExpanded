using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns PrimitiveAssembly transitions and independent field validity.</summary>
internal sealed partial class StateCache
{
    private int maxPatchVertices;
    #region Public API
    /// <summary>Establishes PatchVertices, suppressing a known identical transition.</summary>
    public void SetPatchVertices(int count)
    {
        ValidateBoundaryMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices);
        SynchronizeContext();
        // Invalid patch sizes must not be published as known state after a native error.
        EnsurePatchLimit();
        if (count <= 0 || count > maxPatchVertices) throw new System.ArgumentOutOfRangeException(nameof(count));
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
    private void EnsurePatchLimit()
    {
        if (maxPatchVertices != 0) return;
        int limit = QueryCapability(() => GL.GetInteger(GetPName.MaxPatchVertices));
        if (limit <= 0) throw new System.InvalidOperationException("No current patch capability.");
        maxPatchVertices = limit;
        CapabilityQueries++;
    }
    #endregion
}
