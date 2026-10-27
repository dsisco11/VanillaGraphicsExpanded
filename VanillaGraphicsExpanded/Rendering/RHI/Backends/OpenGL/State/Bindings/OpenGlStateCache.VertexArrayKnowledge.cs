namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Supplies the single executed VAO selector to backend dispatch without owning a native command.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Gets established selection or unknown without querying native state.</summary>
    internal int? VertexArrayKnowledge => currentVao;
    /// <summary>Withdraws or establishes a completed selection independently of retained element-buffer associations.</summary>
    internal void SetVertexArrayKnowledge(int? vertexArray) => currentVao = vertexArray;
    #endregion
}
