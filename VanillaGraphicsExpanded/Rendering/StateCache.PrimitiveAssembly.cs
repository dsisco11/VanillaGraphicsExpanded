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
        SynchronizeContext();
        // Invalid patch sizes must not be published as known state after a native error.
        if (maxPatchVertices == 0)
        {
            maxPatchVertices = GL.GetInteger(GetPName.MaxPatchVertices);
            CapabilityQueries++;
        }
        if (count <= 0 || count > maxPatchVertices) throw new System.ArgumentOutOfRangeException(nameof(count));
        if (assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices) && assembly.PatchVertices == count) return;
        assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PatchVertices;
        GL.PatchParameter(PatchParameterInt.PatchVertices, count);
        FixedFunctionCalls++;
        assembly.PatchVertices = count;
        assemblyKnown |= PrimitiveAssemblyStateKnowledge.PatchVertices;
    }
    #endregion
}
