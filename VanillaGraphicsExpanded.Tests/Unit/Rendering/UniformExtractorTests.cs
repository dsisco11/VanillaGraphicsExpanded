using VanillaGraphicsExpanded;
using TinyTokenizer.Ast;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>
/// Unit tests for the UniformExtractor utility class.
/// These tests verify AST-based uniform extraction from GLSL source code.
/// </summary>
public class UniformExtractorTests
{
    #region Public API

    #region Basic Extraction Tests

    /// <summary>Initialized scalar uniforms retain their authored constant initializer in the AST.</summary>
    [Fact]
    public void ParseUniform_InitializedScalar_PreservesInitializerExpression()
    {
        // Arrange
        var tree = SyntaxTree.Parse("uniform float shadowIntensity = 1.0;", GlslSchema.Instance);

        // Act
        var uniform = Assert.Single(tree.Root.Children.OfType<GlUniformNode>());
        string initializer = string.Concat(uniform.InitializerNodes.Select(static node => node.ToText())).Trim();

        // Assert
        Assert.Equal("shadowIntensity", uniform.Name);
        Assert.Equal("float", uniform.TypeName);
        Assert.Equal("1.0", initializer);
    }

    /// <summary>Nested constructor nodes preserve the complete authored uniform initializer expression.</summary>
    [Fact]
    public void ParseUniform_ConstructorInitializer_PreservesExpressionNodes()
    {
        // Arrange
        const string expression = "vec3(1.0, mix(0.25, 0.75, 0.5), 0.0)";
        var tree = SyntaxTree.Parse($"uniform vec3 tint = {expression};", GlslSchema.Instance);

        // Act
        var uniform = Assert.Single(tree.Root.Children.OfType<GlUniformNode>());
        string initializer = string.Concat(uniform.InitializerNodes.Select(static node => node.ToText())).Trim();

        // Assert
        Assert.Equal(expression, initializer);
    }

    /// <summary>Uninitialized scalar uniforms expose no initializer nodes.</summary>
    [Fact]
    public void ParseUniform_WithoutInitializer_ExposesEmptyInitializer()
    {
        // Arrange
        var tree = SyntaxTree.Parse("uniform int dontWarpVertices;", GlslSchema.Instance);

        // Act
        var uniform = Assert.Single(tree.Root.Children.OfType<GlUniformNode>());

        // Assert
        Assert.Empty(uniform.InitializerNodes);
        Assert.False(uniform.IsArray);
    }

    /// <summary>Symbolic array extents retain array shape even when their element count is not a literal.</summary>
    [Fact]
    public void ParseUniform_SymbolicArrayExtent_RetainsArrayShape()
    {
        // Arrange
        var tree = SyntaxTree.Parse("uniform vec4 palette[MAX_PALETTE_SIZE];", GlslSchema.Instance);

        // Act
        var uniform = Assert.Single(tree.Root.Children.OfType<GlUniformNode>());

        // Assert
        Assert.Equal("palette", uniform.Name);
        Assert.True(uniform.IsArray);
        Assert.Null(uniform.ArraySize);
    }

    /// <summary>A single uniform is returned with its declared name, type, and scalar shape.</summary>
    [Fact]
    public void ExtractUniforms_SimpleUniform_ReturnsCorrectDeclaration()
    {
        // Arrange: Declare one mat4 uniform named modelViewMatrix.
        const string source = """
            #version 330 core
            uniform mat4 modelViewMatrix;
            void main() { }
            """;

        // Act: Extract the declaration list from the GLSL source.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: Preserve the name, mat4 type, and non-array shape.
        Assert.Single(uniforms);
        Assert.Equal("modelViewMatrix", uniforms[0].Name);
        Assert.Equal("mat4", uniforms[0].TypeName);
        Assert.False(uniforms[0].IsArray);
        Assert.Null(uniforms[0].ArraySize);
    }

    /// <summary>Every declaration in one shader source is returned.</summary>
    [Fact]
    public void ExtractUniforms_MultipleUniforms_ReturnsAll()
    {
        // Arrange: Declare a matrix, vector, and sampler in one shader source.
        const string source = """
            #version 330 core
            uniform mat4 projectionMatrix;
            uniform vec3 lightPosition;
            uniform sampler2D diffuseTexture;
            void main() { }
            """;

        // Act: Extract every uniform from the source.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: All three declarations retain their authored names and types.
        Assert.Equal(3, uniforms.Count);
        Assert.Contains(uniforms, u => u.Name == "projectionMatrix" && u.TypeName == "mat4");
        Assert.Contains(uniforms, u => u.Name == "lightPosition" && u.TypeName == "vec3");
        Assert.Contains(uniforms, u => u.Name == "diffuseTexture" && u.TypeName == "sampler2D");
    }

    /// <summary>An array declaration retains its declared element count.</summary>
    [Fact]
    public void ExtractUniforms_ArrayUniform_ExtractsArraySize()
    {
        // Arrange: Declare a vec4 array with sixteen elements.
        const string source = """
            #version 330 core
            uniform vec4 lights[16];
            void main() { }
            """;

        // Act: Extract the array declaration.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: Preserve the base name, vec4 type, array flag, and element count.
        Assert.Single(uniforms);
        Assert.Equal("lights", uniforms[0].Name);
        Assert.Equal("vec4", uniforms[0].TypeName);
        Assert.True(uniforms[0].IsArray);
        Assert.Equal(16, uniforms[0].ArraySize);
    }

    /// <summary>Array and non-array declarations retain distinct shapes.</summary>
    [Fact]
    public void ExtractUniforms_MixedArrayAndNonArray_ExtractsCorrectly()
    {
        // Arrange: Mix a scalar, a matrix, and an eight-element vector array.
        const string source = """
            #version 330 core
            uniform mat4 viewMatrix;
            uniform vec4 lightColors[8];
            uniform float intensity;
            void main() { }
            """;

        // Act: Extract the mixed declaration list.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: Only lightColors is an array; the other declarations remain scalar-shaped.
        Assert.Equal(3, uniforms.Count);

        var viewMatrix = uniforms.First(u => u.Name == "viewMatrix");
        Assert.False(viewMatrix.IsArray);

        var lightColors = uniforms.First(u => u.Name == "lightColors");
        Assert.True(lightColors.IsArray);
        Assert.Equal(8, lightColors.ArraySize);

        var intensity = uniforms.First(u => u.Name == "intensity");
        Assert.False(intensity.IsArray);
    }

    #endregion

    #region GLSL Type Coverage Tests

    /// <summary>Supported scalar types map to their uniform declarations.</summary>
    [Theory]
    [InlineData("float", "testFloat")]
    [InlineData("int", "testInt")]
    [InlineData("uint", "testUint")]
    [InlineData("bool", "testBool")]
    [InlineData("double", "testDouble")]
    public void ExtractUniforms_ScalarTypes_Recognized(string typeName, string varName)
    {
        // Arrange: Insert the selected GLSL scalar type and variable name into a uniform declaration.
        var source = $$"""
            #version 330 core
            uniform {{typeName}} {{varName}};
            void main() { }
            """;

        // Act: Extract the parameterized scalar declaration.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: The extracted entry retains the supplied name and scalar type.
        Assert.Single(uniforms);
        Assert.Equal(varName, uniforms[0].Name);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

    /// <summary>Supported vector types map to their uniform declarations.</summary>
    [Theory]
    [InlineData("vec2")]
    [InlineData("vec3")]
    [InlineData("vec4")]
    [InlineData("ivec2")]
    [InlineData("ivec3")]
    [InlineData("ivec4")]
    [InlineData("uvec2")]
    [InlineData("uvec3")]
    [InlineData("uvec4")]
    [InlineData("bvec2")]
    [InlineData("bvec3")]
    [InlineData("bvec4")]
    [InlineData("dvec2")]
    [InlineData("dvec3")]
    [InlineData("dvec4")]
    public void ExtractUniforms_VectorTypes_Recognized(string typeName)
    {
        // Arrange: Declare testVector using the selected GLSL vector type.
        var source = $$"""
            #version 330 core
            uniform {{typeName}} testVector;
            void main() { }
            """;

        // Act: Extract the vector declaration.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: The single result reports the selected vector type.
        Assert.Single(uniforms);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

    /// <summary>Supported matrix types map to their uniform declarations.</summary>
    [Theory]
    [InlineData("mat2")]
    [InlineData("mat3")]
    [InlineData("mat4")]
    [InlineData("mat2x3")]
    [InlineData("mat2x4")]
    [InlineData("mat3x2")]
    [InlineData("mat3x4")]
    [InlineData("mat4x2")]
    [InlineData("mat4x3")]
    public void ExtractUniforms_MatrixTypes_Recognized(string typeName)
    {
        // Arrange: Declare testMatrix using the selected square or rectangular GLSL matrix type.
        var source = $$"""
            #version 330 core
            uniform {{typeName}} testMatrix;
            void main() { }
            """;

        // Act: Extract the matrix declaration.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: The result preserves the selected matrix type.
        Assert.Single(uniforms);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

    /// <summary>Supported sampler types map to their uniform declarations.</summary>
    [Theory]
    [InlineData("sampler2D")]
    [InlineData("sampler3D")]
    [InlineData("samplerCube")]
    [InlineData("sampler2DArray")]
    [InlineData("sampler2DShadow")]
    [InlineData("isampler2D")]
    [InlineData("usampler2D")]
    public void ExtractUniforms_SamplerTypes_Recognized(string typeName)
    {
        // Arrange: Declare testSampler using the selected sampler type.
        var source = $$"""
            #version 330 core
            uniform {{typeName}} testSampler;
            void main() { }
            """;

        // Act: Extract the sampler declaration.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: The result preserves the selected sampler type.
        Assert.Single(uniforms);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

    #endregion

    #region Deduplication Tests

    /// <summary>The first declaration wins when a source repeats a uniform name.</summary>
    [Fact]
    public void ExtractUniforms_DuplicateNames_DeduplicatesFirstWins()
    {
        // Arrange: Declare transform first as mat4, then repeat its name as mat3.
        const string source = """
            #version 330 core
            uniform mat4 transform;
            uniform mat3 transform;
            void main() { }
            """;

        // Act: Extract and deduplicate the repeated name.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Should only return one entry (first occurrence wins)
        // Assert: One transform entry remains and uses the first declaration's mat4 type.
        Assert.Single(uniforms);
        Assert.Equal("transform", uniforms[0].Name);
        Assert.Equal("mat4", uniforms[0].TypeName);
    }

    /// <summary>Uniform-name extraction returns unique names.</summary>
    [Fact]
    public void ExtractUniformNames_ReturnsUniqueNames()
    {
        // Arrange: Declare model, view, and projection matrix uniforms.
        const string source = """
            #version 330 core
            uniform mat4 modelMatrix;
            uniform mat4 viewMatrix;
            uniform mat4 projMatrix;
            void main() { }
            """;

        // Act: Extract uniform names without declaration details.
        var names = UniformExtractor.ExtractUniformNames(source);

        // Assert: Return each of the three authored names once.
        Assert.Equal(3, names.Count);
        Assert.Contains("modelMatrix", names);
        Assert.Contains("viewMatrix", names);
        Assert.Contains("projMatrix", names);
    }

    #endregion

    #region Multiple Sources Tests

    /// <summary>Uniform names from several sources are combined.</summary>
    [Fact]
    public void ExtractUniformNamesFromMultiple_CombinesAllSources()
    {
        // Arrange: Split matrix uniforms into a vertex source and sampler/vector uniforms into a fragment source.
        const string vertexShader = """
            #version 330 core
            uniform mat4 modelViewMatrix;
            uniform mat4 projectionMatrix;
            void main() { }
            """;

        const string fragmentShader = """
            #version 330 core
            uniform sampler2D diffuseTexture;
            uniform vec3 lightPosition;
            void main() { }
            """;

        // Act: Collect uniform names across both sources.
        var names = UniformExtractor.ExtractUniformNamesFromMultiple(vertexShader, fragmentShader);

        // Assert: The combined result contains all four authored names.
        Assert.Equal(4, names.Count);
        Assert.Contains("modelViewMatrix", names);
        Assert.Contains("projectionMatrix", names);
        Assert.Contains("diffuseTexture", names);
        Assert.Contains("lightPosition", names);
    }

    /// <summary>Repeated names across sources appear only once.</summary>
    [Fact]
    public void ExtractUniformNamesFromMultiple_DeduplicatesAcrossSources()
    {
        // Arrange: Declare viewMatrix in both stages and lightDir only in the fragment stage.
        const string vertexShader = """
            #version 330 core
            uniform mat4 viewMatrix;
            void main() { }
            """;

        const string fragmentShader = """
            #version 330 core
            uniform mat4 viewMatrix;
            uniform vec3 lightDir;
            void main() { }
            """;

        // Act: Collect and deduplicate names across the two sources.
        var names = UniformExtractor.ExtractUniformNamesFromMultiple(vertexShader, fragmentShader);

        // Assert: viewMatrix appears once alongside lightDir.
        Assert.Equal(2, names.Count);
        Assert.Contains("viewMatrix", names);
        Assert.Contains("lightDir", names);
    }

    #endregion

    #region Edge Cases

    /// <summary>An empty source produces no declarations.</summary>
    [Fact]
    public void ExtractUniforms_EmptySource_ReturnsEmpty()
    {
        // Arrange an empty shader source to exercise extraction when no declarations exist.
        // Act: Extract declarations from an empty string.
        var uniforms = UniformExtractor.ExtractUniformsList("");

        // Assert: No uniform declarations are returned.
        Assert.Empty(uniforms);
    }

    /// <summary>A valid source without uniforms produces no declarations.</summary>
    [Fact]
    public void ExtractUniforms_NoUniforms_ReturnsEmpty()
    {
        // Arrange: Provide a valid shader with vertex inputs and fragment outputs but no uniforms.
        const string source = """
            #version 330 core
            in vec3 position;
            out vec4 fragColor;
            void main() { fragColor = vec4(1.0); }
            """;

        // Act: Extract declarations from the valid uniform-free source.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: The result contains no declarations.
        Assert.Empty(uniforms);
    }

    /// <summary>A declaration-looking line comment is ignored.</summary>
    [Fact]
    public void ExtractUniforms_UniformInComment_NotExtracted()
    {
        // Arrange: Place one uniform-looking line comment beside a real mat4 declaration.
        const string source = """
            #version 330 core
            // uniform mat4 commentedUniform;
            uniform mat4 realUniform;
            void main() { }
            """;

        // Act: Extract declarations while the comment remains in the source text.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: Only realUniform is returned; commentedUniform is ignored.
        Assert.Single(uniforms);
        Assert.Equal("realUniform", uniforms[0].Name);
    }

    /// <summary>A declaration inside a block comment is ignored.</summary>
    [Fact]
    public void ExtractUniforms_UniformInMultilineComment_NotExtracted()
    {
        // Arrange: Place a uniform-looking declaration inside a block comment before a real declaration.
        const string source = """
            #version 330 core
            /*
            uniform mat4 commentedUniform;
            */
            uniform mat4 realUniform;
            void main() { }
            """;

        // Act: Extract declarations from the source containing the block comment.
        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Assert: Only realUniform is returned from active GLSL text.
        Assert.Single(uniforms);
        Assert.Equal("realUniform", uniforms[0].Name);
    }

    #endregion

    #region ToString Tests

    /// <summary>The declaration formatter preserves the expected type and name.</summary>
    [Fact]
    public void UniformDeclaration_ToString_FormatsCorrectly()
    {
        // Arrange: Create one scalar declaration and one 16-element array declaration.
        var simple = new UniformDeclaration("myUniform", "vec3");
        var array = new UniformDeclaration("myArray", "vec4", 16);

        // Act: format scalar and array declarations.
        // Assert: Formatting includes the uniform keyword, type, name, and array extent.
        Assert.Equal("uniform vec3 myUniform", simple.ToString());
        Assert.Equal("uniform vec4 myArray[16]", array.ToString());
    }

    #endregion

    #region CompareWithGLLocations Tests

    /// <summary>Linked locations separate active uniforms from optimized-out declarations.</summary>
    [Fact]
    public void CompareWithGLLocations_CategorizesCorrectly()
    {
        // Arrange: Declare usedUniform and unusedUniform and make the location callback return -1 for the latter.
        const string source = """
            #version 330 core
            uniform mat4 usedUniform;
            uniform mat4 unusedUniform;
            void main() { }
            """;

        // Simulate GL returning -1 for unused uniform (optimized out)
        static int MockGetLocation(string name) => name == "usedUniform" ? 0 : -1;

        // Act: Classify declarations using the supplied mock location lookup.
        var (active, optimizedOut) = UniformExtractor.CompareWithGLLocations(source, MockGetLocation);

        // Assert: usedUniform is active and unusedUniform is categorized as optimized out.
        Assert.Single(active);
        Assert.Equal("usedUniform", active[0].Name);

        Assert.Single(optimizedOut);
        Assert.Equal("unusedUniform", optimizedOut[0].Name);
    }

    #endregion

    #endregion
}
