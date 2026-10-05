using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace VanillaGraphicsExpanded.Rendering.Integration;
/// <summary>Registers native context lifetimes independently of device strings and thread identity.</summary>
internal static class RenderContextRegistry
{
    /// <summary>Associates an owner lifetime with a unique registration generation.</summary>
    private sealed record Registration(WeakReference<object> Owner, long Generation, Func<object, bool> IsAlive);
    private static readonly Dictionary<nint, Registration> registrations = new();
    private static long nextGeneration;
    #region Public API
    /// <summary>Registers a live owner once; explicit retirement permits handle reuse with a new generation.</summary>
    internal static unsafe long RegisterCurrent(object owner, Func<object, bool> isAlive)
    {
        nint handle = (nint)GLFW.GetCurrentContext();
        if (handle == 0 || !isAlive(owner)) throw new InvalidOperationException("A live current context is required.");
        lock (registrations)
        {
            if (registrations.TryGetValue(handle, out var old) && old.Owner.TryGetTarget(out var target)
                && ReferenceEquals(target, owner) && old.IsAlive(target)) return old.Generation;
            long generation = Interlocked.Increment(ref nextGeneration);
            registrations[handle] = new(new(owner), generation, isAlive);
            return generation;
        }
    }
    /// <summary>Retires all registrations belonging to this owner without touching native resources.</summary>
    internal static void Retire(object owner)
    {
        lock (registrations)
        {
            var retired = new List<nint>();
            foreach (var pair in registrations)
                if (!pair.Value.Owner.TryGetTarget(out var target) || ReferenceEquals(target, owner)) retired.Add(pair.Key);
            foreach (var handle in retired) registrations.Remove(handle);
        }
    }
    /// <summary>Returns only a registered, still-live current context; absent registration has no authority.</summary>
    internal static unsafe (nint Handle, long Generation) Current()
    {
        lock (registrations)
        {
            // Avoid calling GLFW before initialization in context-free tools.
            if (registrations.Count == 0) return default;
            nint handle = (nint)GLFW.GetCurrentContext();
            if (registrations.TryGetValue(handle, out var registration)
                && registration.Owner.TryGetTarget(out var owner) && registration.IsAlive(owner)) return (handle, registration.Generation);
            registrations.Remove(handle);
            return default;
        }
    }
    #endregion
}
