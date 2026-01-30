using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VanillaGraphicsExpanded.DebugView;

public sealed class DebugViewController : IDisposable
{
    private static readonly Lazy<DebugViewController> LazyInstance = new(() => new DebugViewController(DebugViewRegistry.Instance));

    public static DebugViewController Instance => LazyInstance.Value;

    private readonly object gate = new();
    private readonly DebugViewRegistry registry;

    private DebugViewActivationContext? context;

    private string? activeExclusiveViewId;
    private IDisposable? activeExclusiveHandle;

    private readonly Dictionary<string, IDisposable> activeToggleHandles = new(StringComparer.Ordinal);

    private bool subscribedToRegistry;

    public Action<string>? LogWarning { get; set; }

    public event Action? StateChanged;

    private int persistQueued;
    private int suppressPersist;

    public string? ActiveExclusiveViewId
    {
        get
        {
            lock (gate)
            {
                return activeExclusiveViewId;
            }
        }
    }

    public string[] GetActiveToggleViewIds()
    {
        lock (gate)
        {
            if (activeToggleHandles.Count == 0)
            {
                return [];
            }

            return activeToggleHandles.Keys.ToArray();
        }
    }

    public DebugViewController(DebugViewRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void Initialize(DebugViewActivationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        lock (gate)
        {
            this.context = context;

            if (!subscribedToRegistry)
            {
                registry.Changed += OnRegistryChanged;
                subscribedToRegistry = true;
            }
        }

        // After the controller is initialized and views are registered, restore persisted activation state.
        RestoreActivationStateFromConfig(context);
    }

    public bool IsActive(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (gate)
        {
            if (string.Equals(activeExclusiveViewId, id, StringComparison.Ordinal))
            {
                return true;
            }

            return activeToggleHandles.ContainsKey(id);
        }
    }

    public bool TryActivate(string id, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(id))
        {
            error = "Invalid debug view id.";
            return false;
        }

        DebugViewActivationContext? ctx;
        lock (gate)
        {
            ctx = context;
        }

        if (ctx is null)
        {
            error = "Debug view controller is not initialized.";
            return false;
        }

        if (!registry.TryGet(id, out DebugViewDefinition? definition) || definition is null)
        {
            error = $"Debug view '{id}' is not registered.";
            return false;
        }

        DebugViewAvailability availability = definition.GetAvailability(ctx);
        if (!availability.IsAvailable)
        {
            error = string.IsNullOrWhiteSpace(availability.Reason) ? "Debug view is unavailable." : availability.Reason;
            return false;
        }

        if (definition.ActivationMode == DebugViewActivationMode.Toggle)
        {
            return TryToggleInternal(definition, ctx, out error);
        }

        return TryActivateExclusiveInternal(definition, ctx, out error);
    }

    public bool TryDeactivateExclusive(out string? error)
    {
        error = null;

        DebugViewActivationContext? ctx;
        lock (gate)
        {
            ctx = context;
        }

        IDisposable? handle;
        lock (gate)
        {
            if (activeExclusiveViewId is null || activeExclusiveHandle is null)
            {
                return true;
            }

            handle = activeExclusiveHandle;
            activeExclusiveViewId = null;
            activeExclusiveHandle = null;
        }

        SafeDispose(handle, context: "exclusive deactivate");

        if (ctx is not null)
        {
            PersistActivationState(ctx);
        }
        StateChanged?.Invoke();
        return true;
    }

    public void DeactivateAll()
    {
        DebugViewActivationContext? ctx;
        lock (gate)
        {
            ctx = context;
        }

        IDisposable? exclusive;
        Dictionary<string, IDisposable>? toggles;

        lock (gate)
        {
            exclusive = activeExclusiveHandle;
            activeExclusiveViewId = null;
            activeExclusiveHandle = null;

            if (activeToggleHandles.Count > 0)
            {
                toggles = new Dictionary<string, IDisposable>(activeToggleHandles, StringComparer.Ordinal);
                activeToggleHandles.Clear();
            }
            else
            {
                toggles = null;
            }
        }

        SafeDispose(exclusive, context: "exclusive deactivate all");

        if (toggles is not null)
        {
            foreach (var (id, handle) in toggles)
            {
                SafeDispose(handle, context: $"toggle deactivate all ({id})");
            }
        }

        if (ctx is not null)
        {
            PersistActivationState(ctx);
        }
        StateChanged?.Invoke();
    }

    private bool TryActivateExclusiveInternal(DebugViewDefinition definition, DebugViewActivationContext ctx, out string? error)
    {
        error = null;

        IDisposable? prevHandle = null;
        bool alreadyActive;

        lock (gate)
        {
            alreadyActive = string.Equals(activeExclusiveViewId, definition.Id, StringComparison.Ordinal);
            if (!alreadyActive)
            {
                prevHandle = activeExclusiveHandle;
                activeExclusiveViewId = null;
                activeExclusiveHandle = null;
            }
        }

        if (alreadyActive)
        {
            return true;
        }

        SafeDispose(prevHandle, context: "exclusive switch (prev)");

        try
        {
            IDisposable? handle = definition.RegisterRenderer(ctx);
            if (handle is null)
            {
                error = "Activation failed: renderer registration returned null.";
                return false;
            }

            lock (gate)
            {
                activeExclusiveViewId = definition.Id;
                activeExclusiveHandle = handle;
            }

            PersistActivationState(ctx);
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            LogWarning?.Invoke($"[VGE] DebugViewController: failed to activate '{definition.Id}': {ex}");
            error = ex.Message;
            return false;
        }
    }

    private bool TryToggleInternal(DebugViewDefinition definition, DebugViewActivationContext ctx, out string? error)
    {
        error = null;

        IDisposable? toDispose = null;
        bool wasActive;

        lock (gate)
        {
            if (activeToggleHandles.TryGetValue(definition.Id, out IDisposable? existing))
            {
                wasActive = true;
                toDispose = existing;
                activeToggleHandles.Remove(definition.Id);
            }
            else
            {
                wasActive = false;
            }
        }

        if (wasActive)
        {
            SafeDispose(toDispose, context: $"toggle off ({definition.Id})");
            PersistActivationState(ctx);
            StateChanged?.Invoke();
            return true;
        }

        try
        {
            IDisposable? handle = definition.RegisterRenderer(ctx);
            if (handle is null)
            {
                error = "Activation failed: renderer registration returned null.";
                return false;
            }

            lock (gate)
            {
                activeToggleHandles[definition.Id] = handle;
            }

            PersistActivationState(ctx);
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            LogWarning?.Invoke($"[VGE] DebugViewController: failed to toggle on '{definition.Id}': {ex}");
            error = ex.Message;
            return false;
        }
    }

    private void OnRegistryChanged()
    {
        // If an active view was removed, best-effort deactivate it.
        string? exclusiveId;
        List<string>? togglesToRemove;

        lock (gate)
        {
            exclusiveId = activeExclusiveViewId;

            if (activeToggleHandles.Count > 0)
            {
                togglesToRemove = new List<string>(activeToggleHandles.Count);
                foreach (string id in activeToggleHandles.Keys)
                {
                    togglesToRemove.Add(id);
                }
            }
            else
            {
                togglesToRemove = null;
            }
        }

        bool changed = false;

        if (exclusiveId is not null && !registry.TryGet(exclusiveId, out _))
        {
            IDisposable? handle;
            lock (gate)
            {
                if (!string.Equals(activeExclusiveViewId, exclusiveId, StringComparison.Ordinal))
                {
                    handle = null;
                }
                else
                {
                    handle = activeExclusiveHandle;
                    activeExclusiveViewId = null;
                    activeExclusiveHandle = null;
                }
            }

            SafeDispose(handle, context: "exclusive removed");
            changed = true;
        }

        if (togglesToRemove is not null)
        {
            foreach (string id in togglesToRemove)
            {
                if (registry.TryGet(id, out _))
                {
                    continue;
                }

                IDisposable? handle;
                lock (gate)
                {
                    if (!activeToggleHandles.TryGetValue(id, out handle))
                    {
                        continue;
                    }

                    activeToggleHandles.Remove(id);
                }

                SafeDispose(handle, context: $"toggle removed ({id})");
                changed = true;
            }
        }

        if (changed)
        {
            if (context is not null)
            {
                PersistActivationState(context);
            }
            StateChanged?.Invoke();
        }
    }

    private void RestoreActivationStateFromConfig(DebugViewActivationContext ctx)
    {
        // Suppress persistence while restoring (avoid writing the config file during startup).
        Interlocked.Increment(ref suppressPersist);

        try
        {
            string? exclusive = ctx.Config.Debug.DebugViews.ActiveExclusiveViewId;
            string[] toggles = ctx.Config.Debug.DebugViews.ActiveToggleViewIds ?? Array.Empty<string>();

            // Restore toggles first.
            foreach (string id in toggles)
            {
                if (!registry.TryGet(id, out DebugViewDefinition? def) || def is null)
                {
                    continue;
                }

                if (def.ActivationMode != DebugViewActivationMode.Toggle)
                {
                    continue;
                }

                _ = TryActivate(id, out _);
            }

            // Restore exclusive view last (it may set render-time config state, e.g. LumOn debug mode).
            if (!string.IsNullOrWhiteSpace(exclusive))
            {
                if (registry.TryGet(exclusive, out DebugViewDefinition? def) && def is not null)
                {
                    if (def.ActivationMode == DebugViewActivationMode.Exclusive)
                    {
                        _ = TryActivate(exclusive, out _);
                    }
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref suppressPersist);
        }
    }

    private void PersistActivationState(DebugViewActivationContext ctx)
    {
        WriteActivationStateToConfig(ctx);
        QueuePersistConfig(ctx);
    }

    private void WriteActivationStateToConfig(DebugViewActivationContext ctx)
    {
        string? exclusive;
        string[] toggles;

        lock (gate)
        {
            exclusive = activeExclusiveViewId;
            toggles = activeToggleHandles.Count > 0
                ? activeToggleHandles.Keys.OrderBy(static s => s, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        ctx.Config.Debug.DebugViews.ActiveExclusiveViewId = exclusive;
        ctx.Config.Debug.DebugViews.ActiveToggleViewIds = toggles;
        ctx.Config.Debug.DebugViews.Sanitize();
    }

    private void QueuePersistConfig(DebugViewActivationContext ctx)
    {
        if (Volatile.Read(ref suppressPersist) != 0)
        {
            return;
        }

        // The tests use proxy ICoreClientAPI instances without an event bus.
        if (ctx.Capi.Event is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref persistQueued, 1) != 0)
        {
            return;
        }

        ctx.Capi.Event.EnqueueMainThreadTask(() =>
        {
            Interlocked.Exchange(ref persistQueued, 0);

            try
            {
                ctx.Capi.StoreModConfig(ctx.Config, global::VanillaGraphicsExpanded.Constants.ConfigFileName);
            }
            catch (Exception ex)
            {
                LogWarning?.Invoke($"[VGE] DebugViewController: failed to persist config: {ex}");
            }
        }, "vge:persist-debug-views");
    }

    private void SafeDispose(IDisposable? handle, string context)
    {
        if (handle is null)
        {
            return;
        }

        try
        {
            handle.Dispose();
        }
        catch (Exception ex)
        {
            LogWarning?.Invoke($"[VGE] DebugViewController: dispose failed ({context}): {ex}");
        }
    }

    public void Dispose()
    {
        DeactivateAll();

        lock (gate)
        {
            context = null;
        }

        if (subscribedToRegistry)
        {
            registry.Changed -= OnRegistryChanged;
            subscribedToRegistry = false;
        }
    }
}
