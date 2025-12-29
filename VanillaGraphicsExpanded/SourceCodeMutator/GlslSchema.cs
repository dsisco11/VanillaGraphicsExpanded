using System.Collections.Immutable;

using TinyTokenizer;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Semantic node representing a GLSL function definition: returnType functionName(params) { body }
/// </summary>
public sealed class GlslFunctionNode : SemanticNode
{
    public GlslFunctionNode(NodeMatch match, NodeKind kind) : base(match, kind) { }

    /// <summary>
    /// Gets the return type identifier node.
    /// </summary>
    public RedLeaf ReturnType => (RedLeaf)Parts[0];

    /// <summary>
    /// Gets the function name identifier node.
    /// </summary>
    public RedLeaf NameNode => (RedLeaf)Parts[1];

    /// <summary>
    /// Gets the function name as a string.
    /// </summary>
    public string Name => NameNode.Text;

    /// <summary>
    /// Gets the parameter list block (parentheses).
    /// </summary>
    public RedBlock Parameters => (RedBlock)Parts[2];

    /// <summary>
    /// Gets the function body block (braces).
    /// </summary>
    public RedBlock Body => (RedBlock)Parts[3];
}

/// <summary>
/// Semantic node representing a GLSL #version directive: #version 450
/// </summary>
public sealed class GlslVersionDirectiveNode : SemanticNode
{
    public GlslVersionDirectiveNode(NodeMatch match, NodeKind kind) : base(match, kind) { }

    /// <summary>
    /// Gets the #version tagged identifier node.
    /// </summary>
    public RedLeaf Directive => (RedLeaf)Parts[0];

    /// <summary>
    /// Gets the version number node.
    /// </summary>
    public RedLeaf Version => (RedLeaf)Parts[1];

    /// <summary>
    /// Gets the version number as a string.
    /// </summary>
    public string VersionNumber => Version.Text;
}

/// <summary>
/// Semantic node representing a GLSL @import directive: @import "filename.glsl"
/// </summary>
public sealed class GlslImportDirectiveNode : SemanticNode
{
    public GlslImportDirectiveNode(NodeMatch match, NodeKind kind) : base(match, kind) { }

    /// <summary>
    /// Gets the @import tagged identifier node.
    /// </summary>
    public RedLeaf Directive => (RedLeaf)Parts[0];

    /// <summary>
    /// Gets the filename string node (includes quotes).
    /// </summary>
    public RedLeaf FileNameNode => (RedLeaf)Parts[1];

    /// <summary>
    /// Gets the imported filename without quotes.
    /// </summary>
    public string FileName
    {
        get
        {
            var text = FileNameNode.Text;
            // Remove surrounding quotes
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            {
                return text[1..^1];
            }
            return text;
        }
    }
}

/// <summary>
/// Schema and semantic definitions for GLSL shader code.
/// </summary>
public static class GlslSchema
{
    /// <summary>
    /// Semantic definition for GLSL function definitions.
    /// Pattern: returnType functionName(params) { body }
    /// </summary>
    public static readonly SemanticNodeDefinition<GlslFunctionNode> FunctionDefinition =
        Semantic.Define<GlslFunctionNode>("GlslFunction")
            .Match(p => p.Ident().Ident().ParenBlock().BraceBlock())
            .Create((match, kind) => new GlslFunctionNode(match, kind))
            .Build();

    /// <summary>
    /// Semantic definition for #version directives.
    /// Pattern: #version %number%
    /// </summary>
    public static readonly SemanticNodeDefinition<GlslVersionDirectiveNode> VersionDirective =
        Semantic.Define<GlslVersionDirectiveNode>("GlslVersionDirective")
            .Match(p => p.MatchQuery(Query.TaggedIdent.WithText("#version")).Numeric())
            .Create((match, kind) => new GlslVersionDirectiveNode(match, kind))
            .Build();

    /// <summary>
    /// Semantic definition for @import directives.
    /// Pattern: @import "...filename..."
    /// </summary>
    public static readonly SemanticNodeDefinition<GlslImportDirectiveNode> ImportDirective =
        Semantic.Define<GlslImportDirectiveNode>("GlslImportDirective")
            .Match(p => p.MatchQuery(Query.TaggedIdent.WithText("@import")).String())
            .Create((match, kind) => new GlslImportDirectiveNode(match, kind))
            .Build();

    /// <summary>
    /// The GLSL schema with all semantic definitions registered.
    /// </summary>
    public static readonly Schema Instance = Schema.Create()
        .WithCommentStyles(CommentStyle.CStyleSingleLine, CommentStyle.CStyleMultiLine)
        .WithTagPrefixes('#', '@')
        .Define(FunctionDefinition)
        .Define(VersionDirective)
        .Define(ImportDirective)
        .Build();

    /// <summary>
    /// TokenizerOptions derived from the GLSL schema for use with SyntaxEditor.
    /// </summary>
    public static readonly TokenizerOptions TokenizerOptions = TokenizerOptions.Default
        .WithCommentStyles(CommentStyle.CStyleSingleLine, CommentStyle.CStyleMultiLine)
        .WithTagPrefixes('#', '@');
}

