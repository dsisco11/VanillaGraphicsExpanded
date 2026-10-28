using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenGLInterceptionGenerator;

/// <summary>Uses C# overload resolution to select backend-required OpenTK marshalling signatures.</summary>
internal static class DriverCallDiscovery
{
    #region Internal API
    /// <summary>Recognizes calls to the generated driver boundary, including supported extension owners.</summary>
    internal static bool IsCall(SyntaxNode node) => node is InvocationExpressionSyntax invocation
        && invocation.Expression is MemberAccessExpressionSyntax member
        && member.Expression.ToString().Contains("OpenGLDriverCalls");

    /// <summary>Binds the same argument list against supported OpenTK roots in its original lexical scope.</summary>
    internal static DriverCall? Resolve(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var member = (MemberAccessExpressionSyntax)invocation.Expression;
        string owner = member.Expression.ToString();
        string family = owner.EndsWith(".Arb", StringComparison.Ordinal) ? "Arb"
            : owner.EndsWith(".Ext", StringComparison.Ordinal) ? "Ext" : "";
        foreach (string root in new[] { "OpenTK.Graphics.OpenGL4.GL", "OpenTK.Graphics.OpenGL.GL" })
        {
            var target = SyntaxFactory.ParseExpression("global::" + root + (family.Length == 0 ? "" : "." + family) + "." + member.Name);
            var candidate = invocation.WithExpression(target);
            var symbol = context.SemanticModel.GetSpeculativeSymbolInfo(invocation.SpanStart, candidate, SpeculativeBindingOption.BindAsExpression).Symbol;
            if (symbol is IMethodSymbol method) return new(method, family, invocation.GetLocation());
        }
        return null;
    }
    #endregion
}
