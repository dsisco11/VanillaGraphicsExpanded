using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns fixed-function patch size for scoped tessellated draws.</summary>
internal sealed partial class GlStateCache
{
    private int? patchVertices;
    private ProvokingVertexMode? provokingVertex;

    #region Patch state
    /// <summary>Reads the current provoking convention, which is mutable draw state rather than a GPU capability.</summary>
    internal ProvokingVertexMode ProvokingVertex => provokingVertex ??=
        (ProvokingVertexMode)GL.GetInteger(GetPName.ProvokingVertex);

    /// <summary>Reads the initial external state once after invalidation.</summary>
    internal int PatchVertices
    {
        get
        {
            if (patchVertices is null) { GL.GetInteger(GetPName.PatchVertices, out int value); patchVertices = value; }
            return patchVertices.Value;
        }
    }

    /// <summary>Changes patch size only when necessary; callers restore their saved value on exit.</summary>
    internal void SetPatchVertices(int count)
    {
        if (PatchVertices == count) return;
        GL.PatchParameter(PatchParameterInt.PatchVertices, count);
        patchVertices = count;
    }
    #endregion
}
