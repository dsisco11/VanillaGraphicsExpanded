using System;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns requested settings separately from the last successfully installed shader generation.</summary>
public abstract partial class GpuProgram
{
    private readonly object settingsLock = new();
    private ShaderLoadPlan? requestedPlan;
    private ShaderLoadPlan? installedPlan;

    #region Settings access
    /// <summary>Returns the immutable requested snapshot, including inactive user selections.</summary>
    internal ShaderSettings RequestedSettings { get { lock (settingsLock) return RequestedPlan.Settings; } }

    /// <summary>Returns the snapshot used for the last successful link, or null before installation.</summary>
    internal ShaderSettings? InstalledSettings { get { lock (settingsLock) return Disposed ? null : installedPlan?.Settings; } }

    /// <summary>Initializes defaults after the derived owner has established its contract.</summary>
    private ShaderLoadPlan RequestedPlan => requestedPlan ??= new(new ShaderSettings(ProgramContract));

    /// <summary>Reads a typed selection directly from the requested immutable snapshot.</summary>
    internal T GetShaderOption<T>(ShaderOption<T> option) where T : struct
    {
        lock (settingsLock) return ShaderOptionAccess.Get(RequestedPlan.Settings, option);
    }

    /// <summary>Validates and retains a typed value, scheduling only changed effective stage inputs.</summary>
    /// <returns>Whether effective shader inputs changed.</returns>
    internal bool SetShaderOption<T>(ShaderOption<T> option, T value) where T : struct =>
        UpdateSettings(settings => settings.With(option, value));

    /// <summary>Normalizes compatibility names and aliases; null restores the declaration default.</summary>
    public bool SetDefine(string name, string? value) => UpdateSettings(settings => settings.With(name, value));

    /// <summary>Restores the canonical option default through either its name or declared alias.</summary>
    public bool RemoveDefine(string name) => SetDefine(name, null);

    /// <summary>Atomically publishes requested selections before notifying the existing coalescing scheduler.</summary>
    private bool UpdateSettings(Func<ShaderSettings, ShaderSettings> update)
    {
        bool changed;
        lock (settingsLock)
        {
            var prior = RequestedPlan;
            var next = new ShaderLoadPlan(update(prior.Settings));
            changed = !prior.SameInputs(next);
            requestedPlan = next;
        }
        if (changed) RequestRecompile();
        return changed;
    }
    #endregion
}
