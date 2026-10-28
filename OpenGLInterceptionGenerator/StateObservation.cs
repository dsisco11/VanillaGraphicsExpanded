using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace OpenGLInterceptionGenerator;

/// <summary>Validates domain withdrawal/classification methods and emits context-safe state delivery.</summary>
internal sealed class StateObservation
{
    private const string Backend = "global::VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.";
    private readonly string withdrawal;
    private readonly string? classifier;

    #region Internal API
    /// <summary>Builds a checked declaration without interpreting native command semantics.</summary>
    internal StateObservation(IMethodSymbol method, AttributeData attribute, string[] arguments)
    {
        string? withdraw = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Withdraw").Value.Value as string;
        withdrawal = ResolveMethod(method, withdraw, "Withdrawal", "void",
            new[] { "VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGlStateCache" }.Concat(arguments).ToArray());
        string? classify = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Classify").Value.Value as string;
        if (classify != null)
            classifier = ResolveMethod(method, classify, "Classifier",
                "VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLObservedEffect", arguments);
    }

    /// <summary>Emits direct domain calls with one originating context and field-specific failure recovery.</summary>
    internal string Emit(string observer, string arguments)
    {
        string effect = classifier == null ? Backend + "OpenGLObservedEffect.Changed" : classifier + "(" + arguments + ")";
        string stateArguments = "cache" + (arguments.Length == 0 ? "" : ", " + arguments);
        return $$"""
                var cache = {{Backend}}OpenGlStateCache.Current;
                var origin = cache.KnowledgeContext;
                try
                {
                    if (!{{Backend}}OpenGLObservationContext.TryGet(out cache, out origin, requireCapabilities: false)) return;
                    var effect = {{effect}};
                    if (effect == {{Backend}}OpenGLObservedEffect.Rejected) return;
                    if (!cache.OwnsKnowledgeFor(origin)
                        || !global::VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) return;
                    {{withdrawal}}({{stateArguments}});
                    if (effect == {{Backend}}OpenGLObservedEffect.Changed && cache.OwnsKnowledgeFor(origin)
                        && global::VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                        {{observer}}({{stateArguments}});
                }
                catch (global::System.Exception)
                {
                    // A delivery fault cannot certify the affected field or withdraw a foreign namespace.
                    try { if (cache.OwnsKnowledgeFor(origin)
                        && global::VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                            {{withdrawal}}({{stateArguments}}); }
                    catch (global::System.Exception) { /* Recovery cannot escape native dispatch. */ }
                }
        """;
    }
    #endregion

    #region Private
    /// <summary>Resolves an exact directly callable domain method without runtime lookup or cache-schema knowledge.</summary>
    private static string ResolveMethod(IMethodSymbol observer, string? name, string contract, string result, string[] arguments)
    {
        if (string.IsNullOrEmpty(name)) throw new FormatException(contract + " method must be specified.");
        var matches = observer.ContainingType.GetMembers(name!).OfType<IMethodSymbol>().Where(candidate =>
            candidate.IsStatic && !candidate.IsGenericMethod && candidate.DeclaredAccessibility is Accessibility.Internal or Accessibility.Public
            && candidate.ReturnType.ToDisplayString() == result
            && candidate.Parameters.Length == arguments.Length && candidate.Parameters.Select((p, i) =>
                p.RefKind == RefKind.None && p.Type.ToDisplayString() == arguments[i]).All(valid => valid)).ToArray();
        if (matches.Length != 1)
            throw new FormatException(contract + " must be an accessible static method matching the required arguments and return type.");
        return "global::" + observer.ContainingType.ToDisplayString() + ".@" + name;
    }
    #endregion
}
