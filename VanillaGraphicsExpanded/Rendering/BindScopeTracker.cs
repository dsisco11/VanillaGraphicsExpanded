using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for binding "scope tracker" implementations.
/// </summary>
/// <remarks>
/// Implementations provide the current binding getter and the binding mutator.
/// The tracker maintains a per-thread stack of previous bindings to support nested scopes and future scope-stack/PSO integration.
/// </remarks>
internal abstract class BindScopeTracker<TTracker> where TTracker : BindScopeTracker<TTracker>, new()
{
    private static readonly TTracker Instance = new();

    [ThreadStatic]
    private static int[]? stack;

    [ThreadStatic]
    private static int stackCount;

    /// <summary>
    /// Returns the currently-bound id for this binding category.
    /// </summary>
    protected abstract int GetCurrent();

    /// <summary>
    /// Binds the given id for this binding category.
    /// </summary>
    protected abstract void Bind(int id);

    /// <summary>
    /// Begins a nested binding scope, binding <paramref name="id"/> and returning the previous binding.
    /// </summary>
    public static int Begin(int id)
    {
        int previous = Instance.GetCurrent();
        Push(previous);

        if (id != previous)
        {
            Instance.Bind(id);
        }

        return previous;
    }

    /// <summary>
    /// Ends a binding scope, restoring <paramref name="previousId"/> (best-effort).
    /// </summary>
    public static void End(int previousId)
    {
        int popped = PopOrDefault();
        if (popped != previousId)
        {
            // Out-of-order disposal: reset the stack to keep future scopes sane.
            ResetStack();
        }

        int current = Instance.GetCurrent();
        if (previousId != current)
        {
            Instance.Bind(previousId);
        }
    }

    /// <summary>
    /// Clears the per-thread scope stack for this tracker.
    /// </summary>
    public static void ClearThreadStack()
    {
        ResetStack();
    }

    private static void Push(int value)
    {
        if (stack is null)
        {
            stack = new int[8];
        }
        else if (stackCount >= stack.Length)
        {
            Array.Resize(ref stack, stack.Length * 2);
        }

        stack[stackCount++] = value;
    }

    private static int PopOrDefault()
    {
        if (stackCount <= 0 || stack is null)
        {
            return default;
        }

        return stack[--stackCount];
    }

    private static void ResetStack()
    {
        stackCount = 0;
    }
}

