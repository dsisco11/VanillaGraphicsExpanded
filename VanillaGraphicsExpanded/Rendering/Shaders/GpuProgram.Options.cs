using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns requested settings separately from the last successfully installed shader generation.</summary>
public abstract partial class GpuProgram
{
    private readonly object settingsLock = new();
    private ShaderLoadPlan? requestedPlan;
    private ShaderLoadPlan? installedPlan;
    private ShaderSettingsEditor? settingsEditor;
    private bool settingsEditFailed;

    #region Settings access
    /// <summary>Returns the immutable requested snapshot, including inactive user selections.</summary>
    internal ShaderSettings RequestedSettings { get { lock (settingsLock) return RequestedPlan.Settings; } }

    /// <summary>Returns the snapshot used for the last successful link, or null before installation.</summary>
    internal ShaderSettings? InstalledSettings { get { lock (settingsLock) return Disposed ? null : installedPlan?.Settings; } }

    /// <summary>Initializes defaults after the derived owner has established its contract.</summary>
    private ShaderLoadPlan RequestedPlan => requestedPlan ??= new(new ShaderSettings(ProgramContract));

    /// <summary>Reads the last published selection. Pending configuration edits are not visible until publication.</summary>
    internal T GetShaderOption<T>(ShaderOption<T> option) where T : struct
    {
        lock (settingsLock) return ShaderOptionAccess.Get(RequestedPlan.Settings, option);
    }

    /// <summary>Collects typed edits and publishes one validated snapshot and effective load plan.</summary>
    internal bool SetShaderOptions(Action<ShaderSettingsEditor> configure) => UpdateSettings(configure);

    /// <summary>Applies declared names or aliases atomically; null restores a default and later aliases win.</summary>
    public bool SetDefines(IReadOnlyDictionary<string, string?> values) => UpdateSettings(editor =>
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var pair in values) editor.Set(pair.Key, pair.Value);
    });

    /// <summary>
    /// Batches generated option-property assignments and bulk setters. Reads see the prior published snapshot.
    /// Nested scopes join the outer batch; only the outer call reports whether effective inputs changed.
    /// Exceptions discard all edits. The callback must only configure options, not load or render the program.
    /// </summary>
    public bool ConfigureOptions(Action configure) => UpdateSettings(_ =>
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure();
    });

    /// <summary>Compatibility single-option update; prefer a typed batch or generated accessors inside ConfigureOptions.</summary>
    [Obsolete("Use SetShaderOptions or generated option properties inside ConfigureOptions.")]
    internal bool SetShaderOption<T>(ShaderOption<T> option, T value) where T : struct =>
        SetShaderOptions(editor => editor.Set(option, value));

    /// <summary>Compatibility name update; null restores the declaration default.</summary>
    [Obsolete("Use SetDefines to publish a complete batch.")]
    public bool SetDefine(string name, string? value) => SetDefines(new Dictionary<string, string?> { [name] = value });

    /// <summary>Compatibility default restoration through a name or declared alias.</summary>
    [Obsolete("Use SetDefines with null values to restore defaults in one batch.")]
    public bool RemoveDefine(string name) => SetDefines(new Dictionary<string, string?> { [name] = null });

    /// <summary>Collects edits under the settings lock and schedules only after the complete batch is published.</summary>
    private bool UpdateSettings(Action<ShaderSettingsEditor> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        bool changed;
        lock (settingsLock)
        {
            // Reentrant generated setters contribute to the current edit; other threads wait for its publication.
            if (settingsEditor != null)
            {
                try { update(settingsEditor); }
                catch { settingsEditFailed = true; throw; }
                return false;
            }
            var prior = RequestedPlan;
            settingsEditor = new(prior.Settings);
            settingsEditFailed = false;
            try
            {
                update(settingsEditor);
                // A callback cannot swallow an invalid nested edit and accidentally publish a partial batch.
                if (settingsEditFailed) throw new InvalidOperationException("A shader option edit failed; the batch was discarded.");
                var next = new ShaderLoadPlan(settingsEditor.Complete());
                changed = !prior.SameInputs(next);
                requestedPlan = next;
            }
            finally { settingsEditor = null; settingsEditFailed = false; }
        }
        if (changed) RequestRecompile();
        return changed;
    }
    #endregion
}
