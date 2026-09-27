using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>A uniform, storage or stage interface block header, including preprocessing-separated bodies.</summary>
public sealed class GlInterfaceBlockHeaderNode : GlInterfaceDeclarationNode
{
    #region Construction
    /// <summary>Wraps a block header recognized by the GLSL schema.</summary>
    internal GlInterfaceBlockHeaderNode(CreationContext context) : base(context) { }
    #endregion

    #region Declaration properties
    /// <inheritdoc/>
    public override string Name => GetTypedChild<SyntaxToken>(1).Text;
    /// <inheritdoc/>
    public override GlInterfaceStorage Storage => GetTypedChild<SyntaxToken>(0).Text switch
    {
        "uniform" => GlInterfaceStorage.Uniform,
        "buffer" => GlInterfaceStorage.Buffer,
        "in" => GlInterfaceStorage.Input,
        "out" => GlInterfaceStorage.Output,
        _ => throw new System.InvalidOperationException("Unsupported GLSL interface block storage.")
    };
    /// <inheritdoc/>
    public override bool IsBlock => true;
    #endregion
}
