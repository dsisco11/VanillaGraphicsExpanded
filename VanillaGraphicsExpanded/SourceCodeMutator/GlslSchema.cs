using System.Collections.Generic;
using System.Linq;

using TinyTokenizer;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

#region Custom Syntax Nodes for GLSL

/// <summary>
/// A GLSL function definition: type name(params) { body }
/// Pattern: Ident + Ident + ParenBlock + BraceBlock
/// Example: void main() { ... }
/// </summary>
public sealed class GlslFunctionSyntax : SyntaxNode, INamedNode, IBlockContainerNode
{
    public GlslFunctionSyntax(GreenSyntaxNode green, RedNode? parent, int position)
        : base(green, parent, position) { }

    /// <summary>Return type node (e.g., "void", "vec4").</summary>
    public RedLeaf ReturnTypeNode => GetTypedChild<RedLeaf>(0);

    /// <summary>Return type as text.</summary>
    public string ReturnType => ReturnTypeNode.Text;

    /// <summary>Function name node.</summary>
    public RedLeaf NameNode => GetTypedChild<RedLeaf>(1);

    /// <summary>Function name as text.</summary>
    public string Name => NameNode.Text;

    /// <summary>Parameter list block (parentheses).</summary>
    public RedBlock Parameters => GetTypedChild<RedBlock>(2);

    /// <summary>Function body block (braces).</summary>
    public RedBlock Body => GetTypedChild<RedBlock>(3);

    #region IBlockContainerNode

    /// <inheritdoc/>
    public IReadOnlyList<string> BlockNames => ["body", "params"];

    /// <inheritdoc/>
    public RedBlock GetBlock(string? name = null) => name switch
    {
        "body" or null => Body,
        "params" => Parameters,
        _ => throw new System.ArgumentException($"Unknown block name: {name}. Valid names are: {string.Join(", ", BlockNames)}")
    };

    #endregion
}

/// <summary>
/// A GLSL tagged directive: #version, #define, etc.
/// Pattern: TaggedIdent + rest of line
/// </summary>
public sealed class GlslDirectiveSyntax : SyntaxNode, INamedNode
{
    public GlslDirectiveSyntax(GreenSyntaxNode green, RedNode? parent, int position)
        : base(green, parent, position) { }

    /// <summary>The directive tag node (e.g., "#version").</summary>
    public RedLeaf DirectiveNode => GetTypedChild<RedLeaf>(0);

    /// <summary>The directive name without # or @ (e.g., "version", "import").</summary>
    public string Name => DirectiveNode.Text.TrimStart('#', '@');

    /// <summary>
    /// Gets all children after the directive tag (the arguments).
    /// </summary>
    public IEnumerable<RedNode> Arguments => Children.Skip(1);

    /// <summary>
    /// Gets the arguments as a string.
    /// </summary>
    public string ArgumentsText => string.Concat(Arguments.Select(a => a.ToString()));
}

#endregion

#region GLSL Schema

/// <summary>
/// Schema and syntax definitions for GLSL shader code.
/// </summary>
public static class GlslSchema
{
    /// <summary>
    /// Creates the GLSL schema with function and directive recognition.
    /// </summary>
    public static Schema Instance { get; } = Schema.Create()
        .WithCommentStyles(CommentStyle.CStyleSingleLine, CommentStyle.CStyleMultiLine)
        .WithOperators(CommonOperators.CFamily)
        .WithTagPrefixes('#', '@')
        // Function definition: type name(params) { body }
        .DefineSyntax(Syntax.Define<GlslFunctionSyntax>("GlslFunction")
            .Match(Query.AnyIdent, Query.AnyIdent, Query.ParenBlock, Query.BraceBlock)
            .WithPriority(10)
            .Build())
        // Directive: #tag or @tag followed by tokens until newline
        .DefineSyntax(Syntax.Define<GlslDirectiveSyntax>("GlslDirective")
            .Match(Query.AnyTaggedIdent, Query.Any.Until(Query.Newline))
            .Build())
        .Build();
}

#endregion

