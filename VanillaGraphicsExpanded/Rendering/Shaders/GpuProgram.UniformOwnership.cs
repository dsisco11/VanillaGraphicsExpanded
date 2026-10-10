namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Adopts explicitly owned CPU blocks separately from borrowed binding sources.</summary>
public abstract partial class GpuProgram
{
    #region Protected API
    /// <summary>Registers one owned CPU block with shared mutation and terminal cleanup policy.</summary>
    protected T OwnUniformBuffer<T>(T buffer) where T : CpuUniformBuffer
        => ((IGpuProgram)this).RegisterUniform(buffer);
    #endregion
}
