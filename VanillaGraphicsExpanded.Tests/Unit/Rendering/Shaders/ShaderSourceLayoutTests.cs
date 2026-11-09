using ShaderBuildTool.Spirv;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks source layout rewriting and syntax-tree inspection of shader declarations.</summary>
public sealed class ShaderSourceLayoutTests
{
    #region Public API
    #region Source declarations
    /// <summary>VGE's existing uniform specialization remains distinct from stage inputs and ordinary declarations.</summary>
    [Fact]
    public void SharedSchemaDistinguishesUniformsAndStageInterfaces()
    {
        // Arrange and Act
        var tree = SyntaxTree.Parse("uniform sampler2D surface; in vec2 uv; const float ordinary = 1.0;", GlslSchema.Instance);
        // Assert
        Assert.Equal("surface", Assert.Single(tree.Root.Children.OfType<GlUniformNode>()).Name);
        Assert.Equal(GlInterfaceStorage.Input, Assert.Single(tree.Root.Children.OfType<GlStageIoNode>()).Storage);
    }

    /// <summary>Contract values replace old qualifiers without losing image format or access qualifiers.</summary>
    [Fact]
    public void ImageLayoutsPreserveFormatAndAccess()
    {
        // Arrange
        var contract = new GpuBindingContract();
        contract.RegisterImageUnit("target", 3);
        // Act
        string source = ShaderSourceLayout.Apply("#version 450 core\nlayout(binding = OLD_SLOT, rgba16f) writeonly uniform image2D target;", "csh", contract);
        // Assert
        Assert.Contains("rgba16f", source);
        Assert.Contains("binding = 3", source);
        Assert.DoesNotContain("location", source);
        Assert.Contains("writeonly", source);
        Assert.DoesNotContain("OLD_SLOT", source);
    }

    /// <summary>Nested argument commas and comments remain syntax rather than being split as layout entries.</summary>
    [Fact]
    public void LayoutExpressionsAndCommentsRemainIntact()
    {
        // Arrange
        var contract = new GpuBindingContract();
        contract.RegisterImageUnit("target", 3);
        // Act
        string source = ShaderSourceLayout.Apply("""
            #version 450 core
            layout(binding=OLD, rgba16f, offset=ALIGN(4, /* nested, = */ 8)) writeonly uniform image2D target;
            """, "csh", contract);
        // Assert
        Assert.Contains("binding = 3", source);
        Assert.Contains("offset=ALIGN(4, /* nested, = */ 8)", source);
        var layout = Assert.Single(SyntaxTree.Parse(source, GlslSchema.Instance).Root.Children.OfType<GlLayoutNode>());
        Assert.Equal(3, layout.Qualifiers.Count());
    }

    /// <summary>Separate location and binding clauses merge without leaving an empty layout qualifier.</summary>
    [Fact]
    public void MultipleLayoutClausesAreConsolidated()
    {
        // Arrange
        var contract = new GpuBindingContract();
        contract.UniformLocations.Add("surface", 7);
        // Act
        string source = ShaderSourceLayout.Apply("layout(location=1) layout(location=5) uniform float surface;", "fsh", contract);
        // Assert
        var layout = Assert.Single(SyntaxTree.Parse(source, GlslSchema.Instance).Root.Children.OfType<GlLayoutNode>());
        Assert.Contains("location = 7", layout.ToText());
        Assert.DoesNotContain("binding", layout.ToText());
    }

    /// <summary>Atomic counters retain their own binding and offset syntax even if a regular uniform shares the name.</summary>
    [Fact]
    public void AtomicCountersDoNotReceiveOrdinaryUniformLocations()
    {
        // Arrange
        var contract = new GpuBindingContract();
        contract.UniformLocations.Add("counter", 7);
        const string declaration = "layout(binding=0, offset=4) uniform atomic_uint counter;";
        // Act and Assert
        Assert.Equal(declaration, ShaderSourceLayout.Apply(declaration, "csh", contract));
    }

    #endregion

    #region Block and stage interfaces
    /// <summary>Both conditional block headers receive bindings while block members remain unchanged.</summary>
    [Fact]
    public void ConditionalBlockHeadersUseTheSameContract()
    {
        // Arrange
        var contract = new GpuBindingContract();
        contract.RegisterUniformBlockBinding("Params", 7);
        // Act
        string source = ShaderSourceLayout.Apply("""
            #version 450 core
            #ifdef FIRST
            layout(std140, binding=1) uniform Params
            #else
            layout(std140) uniform Params
            #endif
            { vec4 color; };
            """, "fsh", contract);
        // Assert
        Assert.Equal(2, source.Split("binding = 7").Length - 1);
        Assert.Contains("{ vec4 color; }", source);
    }

    /// <summary>Stage varyings receive locations; identically named parameters and explicit attachments keep their declarations.</summary>
    [Fact]
    public void VaryingLayoutsDoNotChangeFunctionsOrAttachments()
    {
        // Arrange
        var contract = new GpuBindingContract();
        contract.VaryingLocations.Add("value", 4);
        contract.FragmentOutputLocations.Add("color", 0);
        // Act
        string source = ShaderSourceLayout.Apply("""
            #version 450 core
            flat in uint value;
            layout(location=2) out vec4 color;
            void copyValue(out uint value) { value = 0u; }
            """, "fsh", contract);
        // Assert
        Assert.Contains("location = 4", source);
        Assert.Contains("flat", source);
        Assert.Contains("location=2", source);
        Assert.Contains("void copyValue(out uint value) { value = 0u; }", source);
    }
    #endregion
    #endregion
}
