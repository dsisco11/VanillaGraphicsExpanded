using System.Reflection;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Non-null chunk presence marker; traversal must not query chunk internals.</summary>
internal class LoadedChunkSentinel : DispatchProxy
{
    /// <summary>Fails loudly if the production adapter begins requiring additional chunk behavior.</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => throw new InvalidOperationException($"Unexpected chunk call: {targetMethod?.Name}");
}
