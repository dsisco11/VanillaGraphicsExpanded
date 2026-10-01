using System;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns publication of persistent shader inputs at activation.</summary>
public abstract partial class GpuProgram
{
    private bool submittingInputs;

    #region Public API
    /// <summary>Rejects recursive executable work while retained inputs are being submitted.</summary>
    internal void RequireOutsideSubmission()
    {
        if (submittingInputs) throw new InvalidOperationException("Cannot activate a shader during its submission.");
    }

    /// <summary>Rejects mutation during publication or after terminal owner retirement.</summary>
    protected void RequireInputMutation()
    {
        if (IsRetired) throw new ObjectDisposedException(GetType().Name);
        if (submittingInputs) throw new InvalidOperationException("Cannot change shader inputs during submission.");
    }

    /// <summary>Publishes the complete retained state; binding contracts generate this implementation.</summary>
    protected abstract void Submit();
    #endregion

    #region Private
    /// <summary>Keeps publication non-reentrant without requiring a separate editing scope.</summary>
    private void SubmitPreparedInputs()
    {
        RequireOutsideSubmission();
        submittingInputs = true;
        try { Submit(); }
        finally { submittingInputs = false; }
    }
    #endregion
}
