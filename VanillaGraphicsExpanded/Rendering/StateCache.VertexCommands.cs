using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes bound vertex configuration through the common revision publication boundary.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Executes EnableVertexAttribArray and invalidates retained VAO helper assumptions.</summary>
    internal void EnableVertexAttribArray(int attribute)
    {
        var observation = BeginNativeVertexMetadata(0, named: false, elementBinding: false);
        bool returned = false;
        try
        {
            GL.EnableVertexAttribArray(attribute);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes DisableVertexAttribArray and invalidates retained VAO helper assumptions.</summary>
    internal void DisableVertexAttribArray(int attribute)
    {
        var observation = BeginNativeVertexMetadata(0, named: false, elementBinding: false);
        bool returned = false;
        try
        {
            GL.DisableVertexAttribArray(attribute);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexAttribPointer and invalidates retained VAO helper assumptions.</summary>
    internal void VertexAttribPointer(int attribute, int size, VertexAttribPointerType type, bool normalized, int stride, nint offset)
    {
        var observation = BeginNativeVertexMetadata(0, named: false, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexAttribPointer(attribute, size, type, normalized, stride, offset);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexAttribIPointer and invalidates retained VAO helper assumptions.</summary>
    internal void VertexAttribIPointer(int attribute, int size, VertexAttribIntegerType type, int stride, nint offset)
    {
        var observation = BeginNativeVertexMetadata(0, named: false, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexAttribIPointer(attribute, size, type, stride, offset);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexAttribDivisor and invalidates retained VAO helper assumptions.</summary>
    internal void VertexAttribDivisor(int attribute, int divisor)
    {
        var observation = BeginNativeVertexMetadata(0, named: false, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexAttribDivisor(attribute, divisor);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }
    #endregion
}

