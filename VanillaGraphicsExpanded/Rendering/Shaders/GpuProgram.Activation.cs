using System;
namespace VanillaGraphicsExpanded.Rendering.Shaders;
/// <summary>Publishes graphics inputs through VGE's context-aware program lifetime.</summary>
public abstract partial class GpuProgram
{
    #region Public API
    /// <summary>Prepares and publishes every activation through the shared workflow.</summary>
    public void Use() => ((IGpuProgram)this).Activate();
    /// <summary>Stops only this owner's current admitted activation.</summary>
    public void Stop() => StateCache.Current.StopProgram(this);
    #endregion
    #region Private
    /// <summary>Admits the installed generation in the existing program tracker.</summary>
    void IGpuProgram.BindExecutable() => StateCache.Current.ActivateProgram(this);
    /// <summary>Withdraws incomplete inputs without affecting an unrelated owner.</summary>
    void IGpuProgram.ClearActivation(bool bindingEntered) => ClearFailedActivation();
    /// <summary>Clears only this owner's admitted activation after failed preparation or submission.</summary>
    private void ClearFailedActivation() => StateCache.Current.StopProgram(this);
    #endregion
}
