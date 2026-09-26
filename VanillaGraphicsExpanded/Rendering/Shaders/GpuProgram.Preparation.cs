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
    private bool retired;

    #region Preparation
    /// <summary>Reports whether an explicit preload must prepare a new generation.</summary>
    internal bool RequiresPreparation => reloadRequired || !IsLinked || NeedsRecompile;

    /// <summary>Distinguishes final owner retirement from the engine's base-class reload disposal.</summary>
    internal bool IsRetired => retired;

    /// <summary>Excludes a known failed selection from batch submission until inputs or assets change.</summary>
    internal bool CanSubmitPreparation => failedPreparation == null ||
        !failedPreparation.SameInputs(new ShaderLoadPlan(RequestedSettings));

    /// <summary>Prepares current settings synchronously without publishing an incomplete or incompatible program.</summary>
    public bool EnsureReady()
    {
        if (capi == null) throw new InvalidOperationException("Initialize the shader before preparation.");
        if (retired) return false;
        if (Disposed && !registerWhenReady) return false;
        if (Disposed) registeredWithEngine = false;
        var settings = RequestedSettings;
        if (!reloadRequired && IsLinked && (ReferenceEquals(readySettings, settings) || !NeedsRecompile))
        {
            readySettings = settings;
            return true;
        }
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

    /// <summary>Invalidates assets while preserving requested settings and any valid installed generation.</summary>
    internal void InvalidateAssets()
    {
        reloadRequired = true;
        failedPreparation = null;
        if (Disposed) registeredWithEngine = false;
    }

    /// <summary>Marks a declared application owner for engine registration after successful preparation.</summary>
    internal void RegisterOnPreparation() => registerWhenReady = true;

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

    #region Lifetime
    /// <summary>Ensures direct engine-compatible activation cannot bind an unprepared generation.</summary>
    public new void Use()
    {
        if (!EnsureReady()) throw new InvalidOperationException($"Shader {PassName} could not be prepared.");
        base.Use();
    }

    /// <summary>Releases linked resources safely, including declarations with no GL stage ownership.</summary>
    public new void Dispose()
    {
        retired = true;
        if (ProgramId == 0)
        {
            VertexShader = null;
            FragmentShader = null;
            GeometryShader = null;
            // Never-linked declarations own no GL objects, so retirement needs no current context.
            EngineDisposed(this) = true;
            return;
        }
        base.Dispose();
    }
    #endregion
}

