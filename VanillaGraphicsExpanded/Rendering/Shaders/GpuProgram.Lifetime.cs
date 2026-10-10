using System;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Coordinates terminal graphics cleanup independently of engine executable invalidation.</summary>
public abstract partial class GpuProgram
{
    private readonly GpuProgramLifetime lifetime = new();

    #region Public API
    /// <summary>Releases linked resources safely, including declarations with no GL stage ownership.</summary>
    public new void Dispose() => ((IGpuProgram)this).Retire();

    #endregion

    #region Protected API
    /// <summary>Releases optional graphics resources after terminal admission closes.</summary>
    protected virtual void ReleaseResources() { }
    #endregion

    #region Private
    /// <summary>Routes IDisposable through terminal VGE ownership rather than engine reload disposal.</summary>
    void IDisposable.Dispose() => Dispose();
    /// <summary>Bridges family resource cleanup to the shared lifetime workflow.</summary>
    void IGpuProgram.ReleaseResources() => ReleaseResources();

    /// <summary>Retires the temporary engine-owned graphics executable exactly once.</summary>
    void IGpuProgram.ReleaseExecutable()
    {
        if (ProgramId == 0)
        {
            VertexShader = null;
            FragmentShader = null;
            GeometryShader = null;
            EngineDisposed(this) = true;
            return;
        }
        base.Dispose();
    }
    #endregion
}
