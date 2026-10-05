using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Tracks explicitly shader-owned CPU blocks separately from borrowed binding sources.</summary>
public abstract partial class GpuProgram
{
    private List<CpuUniformBuffer>? ownedUniforms;

    #region Protected API
    /// <summary>Registers one owned instance and attaches the shader's existing write guard.</summary>
    protected T OwnUniformBuffer<T>(T buffer) where T : CpuUniformBuffer
    {
        System.ObjectDisposedException.ThrowIf(retired, this);
        ownedUniforms ??= new();
        if (!ownedUniforms.Contains(buffer)) ownedUniforms.Add(buffer);
        buffer.SetWriteGuard(RequireInputMutation);
        return buffer;
    }
    #endregion

    #region Private
    /// <summary>Releases owned publication versions on terminal retirement, never on executable reload.</summary>
    private void ReleaseOwnedUniforms()
    {
        if (ownedUniforms is null) return;
        foreach (var buffer in ownedUniforms) buffer.Dispose();
        ownedUniforms.Clear();
    }
    #endregion
}
