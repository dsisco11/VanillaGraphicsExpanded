using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns one backend-realized vertex array while canonical identity and deletion remain manager-controlled.</summary>
internal sealed class GpuVao : GpuResource<GpuVertexArrayMetadata>
{
    private int vertexArrayId;
    private string? debugName;

    #region Public API
    /// <summary>Gets the original native name solely for backend configuration and execution.</summary>
    public int VertexArrayId=>vertexArrayId;
    /// <summary>Gets the diagnostic label without native discovery.</summary>
    public string? DebugName=>debugName;
    /// <inheritdoc />
    public override nint ResourceId { get=>vertexArrayId;protected set=>vertexArrayId=(int)value; }
    /// <summary>Stores the label and applies native diagnostics only in debug builds.</summary>
    public override void SetDebugName(string? name)
    {
        debugName=name;
#if DEBUG
        if(vertexArrayId!=0)GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray,vertexArrayId,name);
#endif
    }
    #endregion

    #region Protected API
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind=>GpuResourceKind.VertexArray;
    #endregion

    #region Internal API
    /// <summary>Declares an unpublished backend recipient without allocating native storage.</summary>
    internal GpuVao() { }
    #endregion
}
