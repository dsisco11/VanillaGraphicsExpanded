using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes named vertex configuration through the common revision publication boundary.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Executes VertexArrayElementBuffer and invalidates retained VAO helper assumptions.</summary>
    internal void VertexArrayElementBuffer(int vao, int buffer)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: true);
        bool returned = false;
        try
        {
            GL.VertexArrayElementBuffer(vao, buffer);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexArrayVertexBuffer and invalidates retained VAO helper assumptions.</summary>
    internal void VertexArrayVertexBuffer(int vao, int binding, int buffer, nint offset, int stride)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexArrayVertexBuffer(vao, binding, buffer, offset, stride);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexArrayAttribBinding and invalidates retained VAO helper assumptions.</summary>
    internal void VertexArrayAttribBinding(int vao, int attribute, int binding)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribBinding(vao, attribute, binding);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexArrayAttribFormat and invalidates retained VAO helper assumptions.</summary>
    internal void VertexArrayAttribFormat(int vao, int attribute, int size, VertexAttribType type, bool normalized, int offset)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribFormat(vao, attribute, size, type, normalized, offset);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexArrayAttribIFormat and invalidates retained VAO helper assumptions.</summary>
    internal void VertexArrayAttribIFormat(int vao, int attribute, int size, VertexAttribIType type, int offset)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribIFormat(vao, attribute, size, type, offset);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes EnableVertexArrayAttrib and invalidates retained VAO helper assumptions.</summary>
    internal void EnableVertexArrayAttrib(int vao, int attribute)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.EnableVertexArrayAttrib(vao, attribute);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes DisableVertexArrayAttrib and invalidates retained VAO helper assumptions.</summary>
    internal void DisableVertexArrayAttrib(int vao, int attribute)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.DisableVertexArrayAttrib(vao, attribute);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes VertexArrayBindingDivisor and invalidates retained VAO helper assumptions.</summary>
    internal void VertexArrayBindingDivisor(int vao, int binding, int divisor)
    {
        var observation = BeginNativeVertexMetadata(vao, named: true, elementBinding: false);
        bool returned = false;
        try
        {
            GL.VertexArrayBindingDivisor(vao, binding, divisor);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }
    #endregion
}

