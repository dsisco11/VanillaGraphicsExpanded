namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns the immutable set of declared choices for one numerical shader domain.</summary>
internal sealed class GpuObservationDomain(string name, (int Choice, uint Inputs)[] choices, int[]? orderedScalars = null)
{
    private readonly Dictionary<int, uint> declared = choices.ToDictionary(choice => choice.Choice, choice => choice.Inputs);
    private readonly HashSet<int> ordered = orderedScalars?.ToHashSet() ?? [];
    #region Internal API
    /// <summary>Identifies the declared precompiled domain asset.</summary>
    internal string Name { get; } = name;
    /// <summary>Rejects unregistered choices before any observation resources or selectors change.</summary>
    internal bool Contains(int choice) => declared.ContainsKey(choice);
    /// <summary>Identifies serial scalar policies owned by this numerical domain.</summary>
    internal bool IsOrderedScalar(int choice) => declared.ContainsKey(choice) && ordered.Contains(choice);
    /// <summary>Returns the source requirements owned by the validated domain declaration.</summary>
    internal uint RequiredInputs(int choice) => declared[choice];
    #endregion
}
