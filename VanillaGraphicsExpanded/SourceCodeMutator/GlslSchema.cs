using System;
using System.Collections.Generic;
using System.Linq;

using TinyTokenizer;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

#region GLSL Keyword Definitions

/// <summary>
/// GLSL keyword categories for use with TinyTokenizer's keyword system.
/// </summary>
public static class GlslKeywords
{
    /// <summary>
    /// GLSL scalar types: float, int, uint, bool, double
    /// </summary>
    public static readonly string[] ScalarTypes =
    [
        "float", "int", "uint", "bool", "double"
    ];

    /// <summary>
    /// GLSL vector types: vec2-4, ivec2-4, uvec2-4, bvec2-4, dvec2-4
    /// </summary>
    public static readonly string[] VectorTypes =
    [
        "vec2", "vec3", "vec4",
        "ivec2", "ivec3", "ivec4",
        "uvec2", "uvec3", "uvec4",
        "bvec2", "bvec3", "bvec4",
        "dvec2", "dvec3", "dvec4"
    ];

    /// <summary>
    /// GLSL matrix types: mat2-4, mat2x3, mat2x4, mat3x2, mat3x4, mat4x2, mat4x3
    /// </summary>
    public static readonly string[] MatrixTypes =
    [
        "mat2", "mat3", "mat4",
        "mat2x2", "mat2x3", "mat2x4",
        "mat3x2", "mat3x3", "mat3x4",
        "mat4x2", "mat4x3", "mat4x4"
    ];

    /// <summary>
    /// GLSL sampler types for texture sampling.
    /// </summary>
    public static readonly string[] SamplerTypes =
    [
        "sampler1D", "sampler2D", "sampler3D", "samplerCube",
        "sampler1DArray", "sampler2DArray", "samplerCubeArray",
        "sampler2DRect", "samplerBuffer",
        "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow",
        "sampler1DArrayShadow", "sampler2DArrayShadow", "samplerCubeArrayShadow",
        "sampler2DRectShadow",
        "isampler1D", "isampler2D", "isampler3D", "isamplerCube",
        "isampler1DArray", "isampler2DArray", "isamplerCubeArray",
        "isampler2DRect", "isamplerBuffer",
        "usampler1D", "usampler2D", "usampler3D", "usamplerCube",
        "usampler1DArray", "usampler2DArray", "usamplerCubeArray",
        "usampler2DRect", "usamplerBuffer"
    ];

    /// <summary>
    /// GLSL image types for compute shaders (image load/store).
    /// </summary>
    public static readonly string[] ImageTypes =
    [
        "image1D", "image2D", "image3D", "imageCube",
        "image1DArray", "image2DArray", "imageCubeArray",
        "image2DRect", "imageBuffer",
        "iimage1D", "iimage2D", "iimage3D", "iimageCube",
        "iimage1DArray", "iimage2DArray", "iimageCubeArray",
        "iimage2DRect", "iimageBuffer",
        "uimage1D", "uimage2D", "uimage3D", "uimageCube",
        "uimage1DArray", "uimage2DArray", "uimageCubeArray",
        "uimage2DRect", "uimageBuffer"
    ];

    /// <summary>
    /// GLSL special types: void, atomic counters, etc.
    /// </summary>
    public static readonly string[] SpecialTypes =
    [
        "void", "atomic_uint"
    ];

    /// <summary>
    /// GLSL storage qualifiers.
    /// </summary>
    public static readonly string[] StorageQualifiers =
    [
        "const", "in", "out", "inout", "uniform", "buffer", "shared",
        "attribute", "varying", "centroid", "flat", "smooth", "noperspective",
        "patch", "sample", "coherent", "volatile", "restrict", "readonly", "writeonly",
        "layout", "precision", "highp", "mediump", "lowp"
    ];

    /// <summary>
    /// All GLSL type keywords combined.
    /// </summary>
    public static readonly string[] AllTypes =
    [
        .. ScalarTypes,
        .. VectorTypes,
        .. MatrixTypes,
        .. SamplerTypes,
        .. ImageTypes,
        .. SpecialTypes
    ];
}

#endregion

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

/// <summary>
/// A GLSL uniform declaration: uniform type name [array]? ;
/// Pattern: "uniform" + type + identifier + optional[size] + ";"
/// Example: uniform mat4 modelViewMatrix;
/// Example: uniform vec4 lights[16];
/// </summary>
public sealed class GlUniformNode : SyntaxNode, INamedNode
{
    protected GlUniformNode(CreationContext context)
        : base(context) { }

    /// <summary>The "uniform" keyword token.</summary>
    public SyntaxToken UniformKeyword => GetTypedChild<SyntaxToken>(0);

    /// <summary>The type token (e.g., "mat4", "vec3", "sampler2D").</summary>
    public SyntaxToken TypeNode => GetTypedChild<SyntaxToken>(1);

    /// <summary>The type name as text.</summary>
    public string TypeName => TypeNode.Text;

    /// <summary>The uniform name token.</summary>
    public SyntaxToken NameNode => GetTypedChild<SyntaxToken>(2);

    /// <summary>The uniform name as text (implements INamedNode).</summary>
    public string Name => NameNode.Text;

    /// <summary>
    /// The array size if this is an array uniform, otherwise null.
    /// Extracts the integer from the bracket block, e.g., "[16]" -> 16
    /// </summary>
    public int? ArraySize
    {
        get
        {
            // Check if we have a bracket block after the name (child at index 3)
            // The children are: uniform(0), type(1), name(2), optional[bracket](3), semicolon(last)
            var childList = Children.ToList();
            if (childList.Count > 3 && childList[3] is SyntaxBlock { Kind: NodeKind.BracketBlock } bracket)
            {
                // Extract the numeric content from the bracket block
                var innerText = bracket.InnerText.Trim();
                if (int.TryParse(innerText, out int size))
                    return size;
            }
            return null;
        }
    }

    /// <summary>
    /// Returns true if this is an array uniform declaration.
    /// </summary>
    public bool IsArray => ArraySize.HasValue;

    /// <inheritdoc/>
    public override string ToString() =>
        IsArray ? $"uniform {TypeName} {Name}[{ArraySize}]" : $"uniform {TypeName} {Name}";
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
    /// Includes GLSL type keywords and uniform declaration parsing.
    /// </summary>
    public static Schema Instance { get; } = Schema.Create()
        .WithCommentStyles(CommentStyle.CStyleSingleLine, CommentStyle.CStyleMultiLine)
        .WithOperators(CommonOperators.CFamily)
        .WithTagPrefixes('#', '@')
        // Define GLSL type keywords
        .DefineKeywordCategory("GlslScalarTypes", GlslKeywords.ScalarTypes)
        .DefineKeywordCategory("GlslVectorTypes", GlslKeywords.VectorTypes)
        .DefineKeywordCategory("GlslMatrixTypes", GlslKeywords.MatrixTypes)
        .DefineKeywordCategory("GlslSamplerTypes", GlslKeywords.SamplerTypes)
        .DefineKeywordCategory("GlslImageTypes", GlslKeywords.ImageTypes)
        .DefineKeywordCategory("GlslSpecialTypes", GlslKeywords.SpecialTypes)
        .DefineKeywordCategory("GlslStorageQualifiers", GlslKeywords.StorageQualifiers)
        // Uniform declaration: uniform type name; or uniform type name[size];
        // Must match before function definition (higher priority)
        .DefineSyntax(Syntax.Define<GlUniformNode>("glUniform")
            .Match(
                Query.Keyword("uniform"),                      // The "uniform" keyword
                Query.AnyOf(Query.AnyKeyword, Query.AnyIdent), // Type (keyword or custom type)
                Query.AnyIdent,                                // Name
                Query.BracketBlock.Optional(),                 // Optional array [size]
                Query.Symbol(";")
            )
            .WithPriority(15)
            .Build())
        // Function definition: type name(params) { body }
        // Return type can be a keyword (void, float, vec3) or identifier (custom type)
        .DefineSyntax(Syntax.Define<GlFunctionNode>("glFunction")
            .Match(
                Query.AnyOf(Query.AnyKeyword, Query.AnyIdent), // Return type
                Query.AnyIdent,                                // Function name
                Query.ParenBlock,                              // Parameters
                Query.BraceBlock                               // Body
            )
            .WithPriority(10)
            .Build())
        // Directive: #import or @import followed by tokens until newline
        .DefineSyntax(Syntax.Define<GlImportNode>("glImport")
            .Match(
                Query.TaggedIdent("@import"),
                Query.Any.Until(Query.Newline)
            )
            .WithPriority(1)
            .Build())
        // Directive: #tag or @tag followed by tokens until newline
        .DefineSyntax(Syntax.Define<GlDirectiveNode>("glDirective")
            .Match(
                Query.AnyTaggedIdent,
                Query.Any.Until(Query.Newline)
            )
            .WithPriority(0)
            .Build())
        .Build();
}

#endregion

