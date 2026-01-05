using System;
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
public sealed class GlFunctionNode : SyntaxNode, INamedNode, IBlockContainerNode
{
    protected GlFunctionNode(CreationContext context)
        : base(context) { }

    /// <summary>Return type node (e.g., "void", "vec4").</summary>
    public SyntaxToken ReturnTypeNode => GetTypedChild<SyntaxToken>(0);

    /// <summary>Return type as text.</summary>
    public string ReturnType => ReturnTypeNode.Text;

    /// <summary>Function name node.</summary>
    public SyntaxToken NameNode => GetTypedChild<SyntaxToken>(1);

    /// <summary>Function name as text.</summary>
    public string Name => NameNode.Text;

    /// <summary>Parameter list block (parentheses).</summary>
    public SyntaxBlock Parameters => GetTypedChild<SyntaxBlock>(2);

    /// <summary>Function body block (braces).</summary>
    public SyntaxBlock Body => GetTypedChild<SyntaxBlock>(3);

    #region IBlockContainerNode

    /// <inheritdoc/>
    public IReadOnlyList<string> BlockNames => ["body", "params"];

    /// <inheritdoc/>
    public SyntaxBlock GetBlock(string? name = null) => name switch
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
public sealed class GlDirectiveNode : SyntaxNode, INamedNode
{
    protected GlDirectiveNode(CreationContext context)
        : base(context) { }

    /// <summary>The directive tag node (e.g., "#version").</summary>
    public SyntaxToken DirectiveNode => GetTypedChild<SyntaxToken>(0);

    /// <summary>The directive name without # or @ (e.g., "version", "import").</summary>
    public string Name => DirectiveNode.Text.TrimStart(['#', '@']);

    /// <summary>
    /// Gets all children after the directive tag (the arguments).
    /// </summary>
    public IEnumerable<SyntaxNode> Arguments => Children.Skip(1);

    /// <summary>
    /// Gets the arguments as a string.
    /// </summary>
    public string ArgumentsText => string.Concat(Arguments.Select(static a => a.ToText()));
}


public sealed class GlImportNode : SyntaxNode, INamedNode
{
    protected GlImportNode(CreationContext context)
        : base(context) { }

    /// <summary>The directive tag node (e.g., "#version").</summary>
    public SyntaxToken DirectiveNode => GetTypedChild<SyntaxToken>(0);

    /// <summary>The directive name without # or @ (e.g., "version", "import").</summary>
    public string Name => DirectiveNode.Text.AsSpan()[1..].ToString();

    /// <summary>The import filename node (e.g., the string token with the filename).</summary>
    public SyntaxToken ImportStringNode => GetTypedChild<SyntaxToken>(1);

    /// <summary>The import filename as text.</summary>
    public string ImportString => ImportStringNode.TextSpan[1..^1].ToString();
}

// statement node ending with a semicolon
// e.g., variable declarations, function calls, etc.
public sealed class GlStatementNode : SyntaxNode
{
    protected GlStatementNode(CreationContext context)
        : base(context) { }
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
        .DefineSyntax(Syntax.Define<GlFunctionNode>("glFunction")
            .Match(Query.AnyIdent, Query.AnyIdent, Query.ParenBlock, Query.BraceBlock)
            .WithPriority(10)
            .Build())
        // Directive: #import or @import followed by tokens until newline
        .DefineSyntax(Syntax.Define<GlImportNode>("glImport")
            .Match(Query.TaggedIdent("@import"), Query.Any.Until(Query.Newline))
            .WithPriority(1)
            .Build())
        // Directive: #tag or @tag followed by tokens until newline
        .DefineSyntax(Syntax.Define<GlDirectiveNode>("glDirective")
            .Match(Query.AnyTaggedIdent, Query.Any.Until(Query.Newline))
            .WithPriority(0)
            .Build())
        // Statement ending with semicolon
        // .DefineSyntax(Syntax.Define<GlStatementNode>("glStatement")
        //     .Match(Query.Any.Until(Query.Symbol(";")), Query.Symbol(";"))
        //     .Build())
        .Build();
}

#endregion

