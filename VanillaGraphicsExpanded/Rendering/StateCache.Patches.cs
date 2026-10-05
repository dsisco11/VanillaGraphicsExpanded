using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Resolves primitive and provoking values for tessellation adapters.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Resolves provoking convention only while unknown.</summary>
    internal ProvokingVertexMode ProvokingVertex
    {
        get
        {
            if (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProvokingVertex))
            {
                RejectUnsupportedBoundaryMutation();
                rasterizer.ProvokingVertex = (ProvokingVertexMode)GL.GetInteger(GetPName.ProvokingVertex);
                rasterizerKnown |= RasterizerStateKnowledge.ProvokingVertex;
            }
            return rasterizer.ProvokingVertex;
        }
    }
    /// <summary>Resolves patch size only while unknown.</summary>
    internal int PatchVertices
    {
        get
        {
            if (!assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PatchVertices))
            {
                RejectUnsupportedBoundaryMutation();
                assembly.PatchVertices = GL.GetInteger(GetPName.PatchVertices);
                assemblyKnown |= PrimitiveAssemblyStateKnowledge.PatchVertices;
            }
            return assembly.PatchVertices;
        }
    }
    #endregion
}
