namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes VAO-owned element assignments and the installed ordinary engine mesh draw.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Observes a direct-state-access element-buffer assignment without changing VAO selection.</summary>
    internal void NotifyVertexArrayElementBuffer(int vao, int buffer) => elementArrayBufferByVao[vao] = buffer;

    /// <summary>Withdraws the ordinary upload's array/VAO knowledge while preserving unrelated resource slots.</summary>
    internal void ForgetEngineMeshUpload(int? vao)
    {
        // Upload creates a new VAO; only its association can change, including publication queries.
        currentVao = null;
        bufferBindingByTarget.Remove(OpenTK.Graphics.OpenGL.BufferTarget.ArrayBuffer);
        if (vao.HasValue) elementArrayBufferByVao.Remove(vao.Value);
    }

    /// <summary>Records the helper's VAO/EBO unbinds, or forgets only its uncertain bindings on failure.</summary>
    internal void ObserveEngineMeshDraw(int vao, bool completed)
    {
        if (completed)
        {
            elementArrayBufferByVao[vao] = 0;
            currentVao = 0;
        }
        else
        {
            elementArrayBufferByVao.Remove(vao);
            currentVao = null;
        }
    }
    #endregion
}
