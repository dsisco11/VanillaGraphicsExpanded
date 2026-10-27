using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.Rendering.Integration;
/// <summary>Coordinates reload participants and ordered shutdown for one client rendering lifetime.</summary>
internal sealed class ClientRenderingLifetime
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, ClientRenderingLifetime> clients = new();
    private static ClientRenderingLifetime? active;
    private readonly GpuShaderPrograms registry;
    private readonly List<(Action Validate, Action Invalidate)> compute = [];
    private Action? shutdown;
    private readonly List<Action> publications = [];
    private readonly List<Action> preloads = [];
    internal Exception? ReloadFailure { get; private set; }
    private bool closed;
    internal bool Reloading { get; private set; }
    internal long Epoch => registry.Epoch;
    internal bool IsClosed => closed;
    #region Public API
    #region Composition
    /// <summary>Associates explicit composition with the API while preserving the registry's independent identity.</summary>
    internal static ClientRenderingLifetime Create(ICoreClientAPI api, GpuShaderPrograms registry)
    {
        if (clients.TryGetValue(api, out var old)) return old;
        var owner = new ClientRenderingLifetime(registry);
        clients.Add(api, owner);
        return owner;
    }
    /// <summary>Locates an existing lifetime without creating an owner.</summary>
    internal static ClientRenderingLifetime? Find(ICoreClientAPI api) => clients.TryGetValue(api, out var owner) ? owner : null;
    /// <summary>Publishes the actual client integration owner and its ordered cleanup callback.</summary>
    internal void Attach(Action shutdown)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (this.shutdown is not null) throw new InvalidOperationException("Client rendering lifetime is already attached.");
        this.shutdown = shutdown;
        active = this;
        RenderContextRegistry.Retiring += OnContextRetiring;
        context = RenderContextRegistry.Current();
    }
    #endregion
    #region Participants
    /// <summary>Registers feature-owned compute replacement without moving declarations into the graphics registry.</summary>
    internal IDisposable RegisterCompute(Action validate, Action invalidate)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        var participant = (validate, invalidate);
        compute.Add(participant);
        return new Registration(() => compute.Remove(participant));
    }
    /// <summary>Registers feature publication withdrawal at the synchronous native reload admission boundary.</summary>
    internal IDisposable RegisterPublication(Action withdraw)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        publications.Add(withdraw);
        return new Registration(() => publications.Remove(withdraw));
    }
    /// <summary>Registers an already configured mandatory selection to prepare after native refresh.</summary>
    internal IDisposable RegisterPreload(Action preload)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        preloads.Add(preload);
        return new Registration(() => preloads.Remove(preload));
    }
    #endregion
    #region Native reload
    /// <summary>Withdraws all readiness synchronously before native assets are refreshed.</summary>
    internal static void BeginNativeReload()
    {
        var owner = active;
        if (owner is null || owner.closed) return;
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        if (owner.context != default && owner.context != RenderContextRegistry.Current())
            throw new InvalidOperationException("Native shader reload requires the client rendering context.");
        foreach (var participant in owner.compute) participant.Validate();
        owner.registry.BeginReload();
        owner.Reloading = true;
        foreach (var participant in owner.compute.ToArray()) participant.Invalidate();
        owner.ReloadFailure = null;
        foreach (var withdraw in owner.publications.ToArray())
            try { withdraw(); } catch (Exception error) { owner.ReloadFailure = error; }
    }
    /// <summary>Reopens lazy preparation even when native compilation failed or threw.</summary>
    internal static void CompleteNativeReload()
    {
        if (active is not { } owner) return;
        owner.registry.CompleteReload();
        owner.Reloading = false;
        if (owner.closed || owner.context != default && owner.context != RenderContextRegistry.Current()) return;
        foreach (var preload in owner.preloads.ToArray())
            try { preload(); } catch (Exception error) { owner.ReloadFailure = error; }
    }
    #endregion
    #region World and client lifetime
    /// <summary>Captures world admission without allowing a delayed callback to enter another world or client lifetime.</summary>
    internal Action GuardWorldCallback(Action callback)
    {
        long epoch = Epoch;
        return () => { if (!closed && Epoch == epoch) callback(); };
    }
    /// <summary>Rejects old world callbacks while keeping reusable declarations and native context identity.</summary>
    internal void LeaveWorld() { if (!closed) registry.LeaveWorld(); }
    /// <summary>Closes callback admission before ordered borrower and resource cleanup.</summary>
    internal void Shutdown()
    {
        if (closed) return;
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        registry.RequireRetirementBoundary();
        foreach (var participant in compute) participant.Validate();
        closed = true;
        registry.CloseSubmissionAdmission();
        RenderContextRegistry.Retiring -= OnContextRetiring;
        if (ReferenceEquals(active, this)) active = null;
        try { shutdown?.Invoke(); }
        finally
        {
            try { registry.Dispose(); }
            finally
            {
                compute.Clear(); publications.Clear(); preloads.Clear();
            }
        }
    }
    #endregion
    #endregion
    #region Private
    private (nint Handle, long Generation) context;
    /// <summary>Retains one independent registry lifetime.</summary>
    private ClientRenderingLifetime(GpuShaderPrograms registry) => this.registry = registry;
    /// <summary>Retires rendering before the window registration is withdrawn or its handle reused.</summary>
    private void OnContextRetiring((nint Handle, long Generation) retiring)
    {
        if (retiring != context || closed) return;
        OpenGlStateCache.Current.AbandonEngineBoundary(context);
        Shutdown();
    }
    /// <summary>Releases a feature registration exactly once.</summary>
    private sealed class Registration(Action release) : IDisposable
    {
        private Action? callback = release;
        /// <summary>Removes only the original participant.</summary>
        public void Dispose() { var action = callback; callback = null; action?.Invoke(); }
    }
    #endregion
}
