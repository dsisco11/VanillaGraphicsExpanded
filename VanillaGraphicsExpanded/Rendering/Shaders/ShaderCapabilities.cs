using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Tracks declared source features separately from capabilities of successfully linked executables.</summary>
internal static class ShaderCapabilities
{
    private static readonly ConditionalWeakTable<ShaderProgramBase, State> Programs = new();

    /// <summary>Associates source intent and linked features with one managed shader owner.</summary>
    private sealed class State
    {
        internal ShaderCapability Declared;
        internal ShaderCapability Linked;
        internal int ProgramId;
    }

    #region Source and executable lifetime
    /// <summary>Records features only after all candidate stage edits have been published.</summary>
    internal static void Declare(ShaderProgramBase program, ShaderCapability capabilities)
        => Programs.GetValue(program, static _ => new State()).Declared = capabilities;

    /// <summary>Publishes source capabilities after successful compilation of the final executable.</summary>
    internal static void PublishDeclared(ShaderProgramBase program)
    {
        if (Programs.TryGetValue(program, out var state)) Publish(program, state.Declared);
    }

    /// <summary>Registers a feature at its successful executable installation boundary.</summary>
    internal static void Publish(ShaderProgramBase program, ShaderCapability capabilities)
    {
        if (program.ProgramId == 0) return;
        var state = Programs.GetValue(program, static _ => new State());
        // An in-place replacement must not inherit the previous executable's capabilities.
        if (state.ProgramId != program.ProgramId) state.Linked = ShaderCapability.None;
        state.ProgramId = program.ProgramId;
        state.Linked |= capabilities;
    }

    /// <summary>Removes one installed feature while preserving independent features on the same executable.</summary>
    internal static void Remove(ShaderProgramBase program, ShaderCapability capabilities)
    {
        if (Programs.TryGetValue(program, out var state)) state.Linked &= ~capabilities;
    }

    /// <summary>Withdraws a failed executable without discarding its source declarations for a later compile.</summary>
    internal static void InvalidateLinked(ShaderProgramBase program)
    {
        if (!Programs.TryGetValue(program, out var state)) return;
        state.Linked = ShaderCapability.None;
        state.ProgramId = 0;
    }

    /// <summary>Invalidates source declarations and executable identity on reload, fallback, or disposal.</summary>
    internal static void Forget(ShaderProgramBase program) => Programs.Remove(program);
    #endregion

    #region Draw classification
    /// <summary>Requires both the managed shader owner and its current linked executable to match.</summary>
    internal static bool Has(ShaderProgramBase program, ShaderCapability capability)
        => program.ProgramId != 0 && Programs.TryGetValue(program, out var state)
            && state.ProgramId == program.ProgramId && (state.Linked & capability) == capability;
    #endregion
}
