namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Bridges authored retained-input publication into the common program workflow.</summary>
public abstract partial class GpuProgram
{
    #region Internal API
    /// <summary>Rejects recursive executable work while retained inputs are being submitted.</summary>
    internal void RequireOutsideSubmission() => lifetime.RequireOutsidePublication();
    #endregion

    #region Protected API
    /// <summary>Rejects mutation during publication or after terminal owner retirement.</summary>
    protected internal void RequireInputMutation() => lifetime.RequireMutation();
    /// <summary>Publishes the complete retained state; binding contracts generate this implementation.</summary>
    protected abstract void Submit();
    #endregion
    #region Private
    /// <summary>Exposes stable owner state only at the internal workflow boundary.</summary>
    GpuProgramLifetime IGpuProgram.Lifetime => lifetime;
    /// <summary>Invokes authored and generated overrides through their protected family contract.</summary>
    void IGpuProgram.PublishInputs() => Submit();
    #endregion
}
