using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>Layout assignments owned by GPU interface contracts.</summary>
public enum GlLayoutQualifierKind
{
    Other,
    Binding,
    Location
}

/// <summary>A GLSL layout qualifier whose entries retain their syntax nodes and nested expressions.</summary>
public sealed class GlLayoutNode : SyntaxNode
{
    /// <summary>A single layout entry; nested expression blocks remain intact.</summary>
    public sealed record Qualifier(GlLayoutQualifierKind Kind, IReadOnlyList<SyntaxNode> Nodes)
    {
        /// <summary>Serializes the original syntax, retaining comments and expression structure.</summary>
        public string ToText() => string.Concat(Nodes.Select(n => n.ToText()));
    }

    #region Construction
    /// <summary>Wraps a layout keyword and its argument block recognized by the GLSL schema.</summary>
    internal GlLayoutNode(CreationContext context) : base(context) { }
    #endregion

    #region Qualifier syntax
    /// <summary>The argument block containing layout entries.</summary>
    public SyntaxBlock Arguments => GetTypedChild<SyntaxBlock>(1);

    /// <summary>Separates entries at top-level comma tokens, never at commas inside expressions or comments.</summary>
    public IEnumerable<Qualifier> Qualifiers
    {
        get
        {
            var nodes = new List<SyntaxNode>();
            foreach (var node in Arguments.InnerChildren)
            {
                if (Query.Symbol(",").Matches(node))
                {
                    if (nodes.Count != 0) yield return CreateQualifier(nodes);
                    nodes = new();
                }
                else nodes.Add(node);
            }
            if (nodes.Count != 0) yield return CreateQualifier(nodes);
        }
    }

    /// <summary>Classifies contract-owned assignment syntax while leaving other layout entries opaque.</summary>
    private static Qualifier CreateQualifier(List<SyntaxNode> nodes)
    {
        var kind = GlLayoutQualifierKind.Other;
        if (nodes.Count >= 3 && Query.Operator("=").Matches(nodes[1]))
        {
            if (Query.Ident("binding").Matches(nodes[0])) kind = GlLayoutQualifierKind.Binding;
            else if (Query.Ident("location").Matches(nodes[0])) kind = GlLayoutQualifierKind.Location;
        }
        return new(kind, nodes);
    }

    /// <summary>Formats typed layout assignments for insertion by a syntax editor.</summary>
    public static string FormatAssignments(IReadOnlyDictionary<GlLayoutQualifierKind, int> values)
    {
        return string.Join(", ", values.Select(pair => (pair.Key switch
        {
            GlLayoutQualifierKind.Binding => "binding",
            GlLayoutQualifierKind.Location => "location",
            _ => throw new ArgumentOutOfRangeException(nameof(values), "Only owned layout assignments can be generated.")
        }) + " = " + pair.Value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Formats a complete layout qualifier from typed assignments.</summary>
    public static string Format(IReadOnlyDictionary<GlLayoutQualifierKind, int> values) => "layout(" + FormatAssignments(values) + ") ";
    #endregion
}
