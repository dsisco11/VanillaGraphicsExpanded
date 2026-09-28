using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Reports shader patch failures locally, retaining startup notifications until chat is available.</summary>
internal sealed class ShaderPatchErrors : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private bool ready;
    private bool disposed;

    #region Lifetime
    /// <summary>Subscribes to the client world lifetime before initial shader loading.</summary>
    internal ShaderPatchErrors(ICoreClientAPI api)
    {
        this.api = api;
        api.Event.LevelFinalize += OnReady;
        api.Event.LeaveWorld += OnLeave;
    }

    /// <summary>Removes event handlers and releases queued startup failures.</summary>
    public void Dispose()
    {
        disposed = true;
        api.Event.LevelFinalize -= OnReady;
        api.Event.LeaveWorld -= OnLeave;
        pending.Clear();
    }
    #endregion

    #region Reporting
    /// <summary>Logs full diagnostics and schedules a concise local chat notification.</summary>
    internal void Report(string shader, string details)
    {
        api.Logger.Error($"[VGE] Shader patch failed for '{shader}': {details}");
        api.Event.EnqueueMainThreadTask(() =>
        {
            if (disposed) return;
            if (ready) Show(shader);
            else pending.Add(shader);
        }, "vge-shader-patch-error");
    }

    /// <summary>Delivers errors collected while startup shaders compiled before the chat UI was ready.</summary>
    private void OnReady()
    {
        ready = true;
        foreach (string shader in pending) Show(shader);
        pending.Clear();
    }

    /// <summary>Defers notifications during the next world load.</summary>
    private void OnLeave() => ready = false;

    /// <summary>Displays the shader identity while leaving lengthy compiler output in the client log.</summary>
    private void Show(string shader) => api.ShowChatMessage($"[VGE] Error: shader patch failed for '{shader}'. See client-main.log for details.");
    #endregion
}
