using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.DebugView;

public sealed class DebugViewRegistry
{
    private static readonly Lazy<DebugViewRegistry> LazyInstance = new(() => new DebugViewRegistry());

    public static DebugViewRegistry Instance => LazyInstance.Value;

    private readonly object gate = new();
    private readonly Dictionary<string, DebugViewDefinition> viewsById = new(StringComparer.Ordinal);

    public Action<string>? LogWarning { get; set; }

    public event Action? Changed;

    internal DebugViewRegistry() { }

    public bool Register(DebugViewDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        bool changed;
        lock (gate)
        {
            if (viewsById.ContainsKey(definition.Id))
            {
                LogWarning?.Invoke($"[VGE] DebugViewRegistry: duplicate id '{definition.Id}', rejecting registration.");
                return false;
            }

            viewsById.Add(definition.Id, definition);
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return true;
    }

    public bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        bool removed;
        lock (gate)
        {
            removed = viewsById.Remove(id);
        }

        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    public bool TryGet(string id, out DebugViewDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            definition = null;
            return false;
        }

        lock (gate)
        {
            if (viewsById.TryGetValue(id, out DebugViewDefinition? d))
            {
                definition = d;
                return true;
            }
        }

        definition = null;
        return false;
    }

    public DebugViewDefinition[] GetAll()
    {
        lock (gate)
        {
            if (viewsById.Count == 0)
            {
                return [];
            }

            return viewsById.Values
                .OrderBy(v => v.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
