using System;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Adapts graphics activation to VGE workflows while retaining the engine handoff.</summary>
public abstract partial class GpuProgram
{
    private bool activatingEngine;

    #region Public API
    /// <summary>Prepares and publishes every activation through the common program workflow.</summary>
    public new void Use() => ((IGpuProgram)this).Activate();
    #endregion

    #region Internal API
    /// <summary>Allows underlying engine activation only within the owned submission workflow.</summary>
    internal bool IsActivatingEngine => activatingEngine;
    #endregion

    #region Private
    /// <summary>Temporarily establishes engine ownership until graphics activation is isolated.</summary>
    void IGpuProgram.BindExecutable()
    {
        if (!ReferenceEquals(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram, this))
        {
            activatingEngine = true;
            try { base.Use(); }
            finally { activatingEngine = false; }
        }
        StateCache.Current.UseProgram(ProgramId);
        StateCache.Current.NotifyProgramBound(ProgramId);
    }

    /// <summary>Clears only this owner's failed graphics activation.</summary>
    void IGpuProgram.ClearActivation(bool bindingEntered) => ClearFailedActivation();

    /// <summary>Routes engine-interface activation through the same readiness and submission boundary.</summary>
    void Vintagestory.API.Client.IShaderProgram.Use() => Use();

    /// <summary>Stops a failed owner so a caller cannot accidentally draw with incomplete submitted resources.</summary>
    private void ClearFailedActivation()
    {
        if (ReferenceEquals(Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram, this))
        {
            try { Stop(); }
            finally { StateCache.Current.UnbindProgram(); }
        }
    }

    #endregion
}
