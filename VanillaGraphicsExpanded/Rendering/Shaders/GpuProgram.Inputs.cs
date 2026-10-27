namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Captures typed desired inputs without executing commands or following replacement generations.</summary>
public abstract partial class GpuProgram
{
    #region Internal API
    /// <summary>Rejects recursive executable work while retained inputs are being submitted.</summary>
    internal void RequireOutsideSubmission() => lifetime.RequireOutsidePublication();
    #endregion

    #region Protected API
    /// <summary>Rejects retained-input writes after retirement or during capture.</summary>
    protected internal void RequireInputMutation() => lifetime.RequireMutation();
    /// <summary>Publishes the complete retained state; binding contracts generate this implementation.</summary>
    protected abstract void Submit();
    #endregion
    #region Private
    /// <summary>Shares the asset lifetime with preparation and retirement policy.</summary>
    GpuProgramLifetime IGpuProgram.Lifetime => lifetime;
    /// <summary>Invokes authored and generated overrides through their protected family contract.</summary>
    void IGpuProgram.PublishInputs() => Submit();
    #endregion
}
