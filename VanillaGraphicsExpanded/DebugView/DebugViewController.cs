using System;
using System.Collections.Generic;
using System.Linq;

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
        StateChanged?.Invoke();
        return true;
    }

    public void DeactivateAll()
    {
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
            StateChanged?.Invoke();
        }
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
