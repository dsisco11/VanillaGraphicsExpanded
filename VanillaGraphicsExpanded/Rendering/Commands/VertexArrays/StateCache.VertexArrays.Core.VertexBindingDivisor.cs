using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core VertexBindingDivisor dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexBindingDivisor(int @bindingindex, int @divisor)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexBindingDivisor(@bindingindex, @divisor);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexBindingDivisor(uint @bindingindex, uint @divisor)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexBindingDivisor(@bindingindex, @divisor);
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
