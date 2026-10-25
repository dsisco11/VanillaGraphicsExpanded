using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Arb VertexAttribPointer dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteArbVertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    #endregion
}
