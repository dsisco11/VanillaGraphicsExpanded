using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext VertexArrayVertexBindingDivisor dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexArrayVertexBindingDivisor(int @vaobj, int @bindingindex, int @divisor)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexBindingDivisor(@vaobj, @bindingindex, @divisor);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteExtVertexArrayVertexBindingDivisor(uint @vaobj, uint @bindingindex, uint @divisor)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexBindingDivisor(@vaobj, @bindingindex, @divisor);
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
