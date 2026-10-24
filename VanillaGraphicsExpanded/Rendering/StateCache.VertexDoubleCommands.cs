using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes double vertex interpretation changes through VAO metadata observation.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Forwards the native double attribute command and publishes its VAO revision.</summary>
    internal void VertexArrayAttribLFormat(int vao, int attribute, int size, VertexAttribDoubleType type, int offset)
    {
        var observation = BeginNativeVertexMetadata(vao, true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(vao, attribute, size, type, offset);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }
    /// <summary>Forwards the native double attribute command and publishes its VAO revision.</summary>
    internal void VertexAttribLPointer(int attribute, int size, VertexAttribDoubleType type, int stride, nint offset)
    {
        var observation = BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribLPointer(attribute, size, type, stride, offset);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }
    #endregion
}
