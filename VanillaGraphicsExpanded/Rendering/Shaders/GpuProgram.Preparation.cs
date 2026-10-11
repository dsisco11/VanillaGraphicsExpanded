using System;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Prepares requested graphics generations only at explicit preload or use boundaries.</summary>
public abstract partial class GpuProgram
{
    private bool reloadRequired = true;
    private bool registeredWithEngine;
    private bool registerWhenReady;
    private ShaderLoadPlan? failedPreparation;
    private ShaderSettings? readySettings;

    #region Public API
    /// <summary>Prepares current settings synchronously through the guarded program workflow.</summary>
    public bool EnsureReady() => ((IGpuProgram)this).Prepare();
    #endregion

    #region Internal API
    /// <summary>Supplies replacement admission to the common activation workflow.</summary>
    bool IGpuProgram.RequiresPreparation => RequiresPreparation;

    /// <summary>Reports whether an explicit preload must prepare a new generation.</summary>
    internal bool RequiresPreparation => reloadRequired || !IsLinked || NeedsRecompile;

    /// <summary>Distinguishes final owner retirement from the engine's base-class reload disposal.</summary>
    internal bool IsRetired => lifetime.IsRetired;

    /// <summary>Excludes a known failed selection from batch submission until inputs or assets change.</summary>
    internal bool CanSubmitPreparation => failedPreparation == null ||
        !failedPreparation.SameInputs(new ShaderLoadPlan(RequestedSettings));

    /// <summary>Invalidates assets while preserving requested settings and any valid installed generation.</summary>
    internal void InvalidateAssets()
    {
        reloadRequired = true;
        failedPreparation = null;
        if (Disposed) registeredWithEngine = false;
    }

    /// <summary>Marks a declared application owner for engine registration after successful preparation.</summary>
    internal void RegisterOnPreparation() => registerWhenReady = true;

    #endregion

    #region Private
    /// <summary>Preserves graphics settings and engine reload preparation during ownership migration.</summary>
    bool IGpuProgram.PrepareExecutable()
    {
        RequireOutsideSubmission();
        if (capi == null) throw new InvalidOperationException("Initialize the shader before preparation.");
        if (lifetime.IsRetired) return false;
        if (Disposed && !registerWhenReady) return false;
        if (Disposed) registeredWithEngine = false;
        var settings = RequestedSettings;
        if (!reloadRequired && IsLinked && (ReferenceEquals(readySettings, settings) || !NeedsRecompile))
        {
            readySettings = settings;
            return true;
        }
        lifetime.RequireUnborrowed();
        var plan = new ShaderLoadPlan(settings);
        if (failedPreparation?.SameInputs(plan) == true) return false;
        if (!CompileAndLink())
        {
            // Keep a previous executable alive, but never draw it for incompatible requested inputs.
            failedPreparation = plan;
            return false;
        }
        return true;
    }

    /// <summary>Records a coherent installed generation and restores registration after engine teardown.</summary>
    private void CompletePreparation()
    {
        reloadRequired = false;
        failedPreparation = null;
        readySettings = InstalledSettings;
        if (registerWhenReady && !registeredWithEngine)
        {
            capi!.Shader.RegisterMemoryShaderProgram(PassName, this);
            registeredWithEngine = true;
        }
    }
    #endregion

}
