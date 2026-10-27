using System.Runtime.InteropServices;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Updates resource-specific binding snapshots after native deletion.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Other resource bindings






    #endregion
    #endregion

    #region Private
    /// <summary>Forgets only references to a deleted buffer; other VAOs may retain attached storage.</summary>
    private void ForgetDeletedBuffer(int buffer)
    {
        RecordBoundaryRetirement(EPipelineState.BufferBindings, buffer);
        if (buffer == 0) return;
        InvalidateVertexConfigurationsForBuffer(buffer);
        foreach (var key in indexedBufferBindings.Where(pair => pair.Value.Buffer == buffer).Select(pair => pair.Key).ToArray())
            indexedBufferBindings.Remove(key);
        foreach (var key in bufferBindingByTarget.Where(pair => pair.Value == buffer).Select(pair => pair.Key).ToArray())
            bufferBindingByTarget.Remove(key);
        foreach (var key in elementArrayBufferByVao.Where(pair => pair.Value == buffer).Select(pair => pair.Key).ToArray())
            elementArrayBufferByVao.Remove(key);
    }
    #endregion
}
