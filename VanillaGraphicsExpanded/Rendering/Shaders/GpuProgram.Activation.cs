using System;
namespace VanillaGraphicsExpanded.Rendering.Shaders;
/// <summary>Publishes graphics inputs through VGE's context-aware program lifetime.</summary>
public abstract partial class GpuProgram
{
    #region Public API
    /// <summary>Prepares and publishes every activation through the shared workflow.</summary>
    public new void Use() => ((IGpuProgram)this).Activate();
    /// <summary>Stops only this owner's current admitted activation.</summary>
    public new void Stop() => StateCache.Current.StopProgram(this);
    #endregion
    #region Internal API
    /// <summary>Keeps the temporary engine activation hook from bypassing VGE publication.</summary>
    internal bool IsActivatingEngine => false;
    #endregion
    #region Private
    /// <summary>Admits the installed generation in the existing program tracker.</summary>
    void IGpuProgram.BindExecutable() => StateCache.Current.ActivateProgram(this);
    /// <summary>Withdraws incomplete inputs without affecting an unrelated owner.</summary>
    void IGpuProgram.ClearActivation(bool bindingEntered) => ClearFailedActivation();
    /// <summary>Routes temporary engine-interface activation through VGE publication.</summary>
    void Vintagestory.API.Client.IShaderProgram.Use() => Use();
    /// <summary>Routes temporary engine-interface stopping through scoped ownership.</summary>
    void Vintagestory.API.Client.IShaderProgram.Stop() => Stop();
    /// <summary>Clears only this owner's admitted activation after failed preparation or submission.</summary>
    private void ClearFailedActivation() => StateCache.Current.StopProgram(this);
    #endregion
}
