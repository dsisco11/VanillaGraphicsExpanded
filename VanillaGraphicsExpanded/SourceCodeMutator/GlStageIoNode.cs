using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>A named stage input or output, with an optional array dimension.</summary>
public sealed class GlStageIoNode : GlInterfaceDeclarationNode
{
    #region Construction
    /// <summary>Wraps a stage declaration recognized by the GLSL schema.</summary>
    internal GlStageIoNode(CreationContext context) : base(context) { }
    #endregion

    #region Declaration properties
    /// <inheritdoc/>
    public override string Name => GetTypedChild<SyntaxToken>(StorageIndex + 2).Text;
    /// <summary>Index of the storage token after an optional interpolation qualifier.</summary>
    private int StorageIndex => GetTypedChild<SyntaxToken>(0).Text is "in" or "out" ? 0 : 1;
    /// <summary>Declared scalar or vector type.</summary>
    public string ValueType => GetTypedChild<SyntaxToken>(StorageIndex + 1).Text;
    /// <summary>Explicit interpolation qualifier, or empty for the GLSL default.</summary>
    public string Interpolation => StorageIndex == 0 ? "" : GetTypedChild<SyntaxToken>(0).Text;
    /// <inheritdoc/>
    public override GlInterfaceStorage Storage => GetTypedChild<SyntaxToken>(StorageIndex).Kind == GlslSchema.Instance.GetKeywordKind("in")
        ? GlInterfaceStorage.Input : GlInterfaceStorage.Output;
    #endregion
}
