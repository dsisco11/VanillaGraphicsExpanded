using VanillaGraphicsExpanded;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>
/// Unit tests for the UniformExtractor utility class.
/// These tests verify AST-based uniform extraction from GLSL source code.
/// </summary>
public class UniformExtractorTests
{
    #region Basic Extraction Tests

    [Fact]
    public void ExtractUniforms_SimpleUniform_ReturnsCorrectDeclaration()
    {
        const string source = """
            #version 330 core
            uniform mat4 modelViewMatrix;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal("modelViewMatrix", uniforms[0].Name);
        Assert.Equal("mat4", uniforms[0].TypeName);
        Assert.False(uniforms[0].IsArray);
        Assert.Null(uniforms[0].ArraySize);
    }

    [Fact]
    public void ExtractUniforms_MultipleUniforms_ReturnsAll()
    {
        const string source = """
            #version 330 core
            uniform mat4 projectionMatrix;
            uniform vec3 lightPosition;
            uniform sampler2D diffuseTexture;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Equal(3, uniforms.Count);
        Assert.Contains(uniforms, u => u.Name == "projectionMatrix" && u.TypeName == "mat4");
        Assert.Contains(uniforms, u => u.Name == "lightPosition" && u.TypeName == "vec3");
        Assert.Contains(uniforms, u => u.Name == "diffuseTexture" && u.TypeName == "sampler2D");
    }

    [Fact]
    public void ExtractUniforms_ArrayUniform_ExtractsArraySize()
    {
        const string source = """
            #version 330 core
            uniform vec4 lights[16];
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal("lights", uniforms[0].Name);
        Assert.Equal("vec4", uniforms[0].TypeName);
        Assert.True(uniforms[0].IsArray);
        Assert.Equal(16, uniforms[0].ArraySize);
    }

    [Fact]
    public void ExtractUniforms_MixedArrayAndNonArray_ExtractsCorrectly()
    {
        const string source = """
            #version 330 core
            uniform mat4 viewMatrix;
            uniform vec4 lightColors[8];
            uniform float intensity;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

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

    [Theory]
    [InlineData("float", "testFloat")]
    [InlineData("int", "testInt")]
    [InlineData("uint", "testUint")]
    [InlineData("bool", "testBool")]
    [InlineData("double", "testDouble")]
    public void ExtractUniforms_ScalarTypes_Recognized(string typeName, string varName)
    {
        var source = $$"""
            #version 330 core
            uniform {{typeName}} {{varName}};
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal(varName, uniforms[0].Name);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

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
        var source = $$"""
            #version 330 core
            uniform {{typeName}} testVector;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

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
        var source = $$"""
            #version 330 core
            uniform {{typeName}} testMatrix;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

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
        var source = $$"""
            #version 330 core
            uniform {{typeName}} testSampler;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal(typeName, uniforms[0].TypeName);
    }

    #endregion

    #region Deduplication Tests

    [Fact]
    public void ExtractUniforms_DuplicateNames_DeduplicatesFirstWins()
    {
        const string source = """
            #version 330 core
            uniform mat4 transform;
            uniform mat3 transform;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        // Should only return one entry (first occurrence wins)
        Assert.Single(uniforms);
        Assert.Equal("transform", uniforms[0].Name);
        Assert.Equal("mat4", uniforms[0].TypeName);
    }

    [Fact]
    public void ExtractUniformNames_ReturnsUniqueNames()
    {
        const string source = """
            #version 330 core
            uniform mat4 modelMatrix;
            uniform mat4 viewMatrix;
            uniform mat4 projMatrix;
            void main() { }
            """;

        var names = UniformExtractor.ExtractUniformNames(source);

        Assert.Equal(3, names.Count);
        Assert.Contains("modelMatrix", names);
        Assert.Contains("viewMatrix", names);
        Assert.Contains("projMatrix", names);
    }

    #endregion

    #region Multiple Sources Tests

    [Fact]
    public void ExtractUniformNamesFromMultiple_CombinesAllSources()
    {
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

        var names = UniformExtractor.ExtractUniformNamesFromMultiple(vertexShader, fragmentShader);

        Assert.Equal(4, names.Count);
        Assert.Contains("modelViewMatrix", names);
        Assert.Contains("projectionMatrix", names);
        Assert.Contains("diffuseTexture", names);
        Assert.Contains("lightPosition", names);
    }

    [Fact]
    public void ExtractUniformNamesFromMultiple_DeduplicatesAcrossSources()
    {
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

        var names = UniformExtractor.ExtractUniformNamesFromMultiple(vertexShader, fragmentShader);

        Assert.Equal(2, names.Count);
        Assert.Contains("viewMatrix", names);
        Assert.Contains("lightDir", names);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ExtractUniforms_EmptySource_ReturnsEmpty()
    {
        var uniforms = UniformExtractor.ExtractUniformsList("");

        Assert.Empty(uniforms);
    }

    [Fact]
    public void ExtractUniforms_NoUniforms_ReturnsEmpty()
    {
        const string source = """
            #version 330 core
            in vec3 position;
            out vec4 fragColor;
            void main() { fragColor = vec4(1.0); }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Empty(uniforms);
    }

    [Fact]
    public void ExtractUniforms_UniformInComment_NotExtracted()
    {
        const string source = """
            #version 330 core
            // uniform mat4 commentedUniform;
            uniform mat4 realUniform;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal("realUniform", uniforms[0].Name);
    }

    [Fact]
    public void ExtractUniforms_UniformInMultilineComment_NotExtracted()
    {
        const string source = """
            #version 330 core
            /*
            uniform mat4 commentedUniform;
            */
            uniform mat4 realUniform;
            void main() { }
            """;

        var uniforms = UniformExtractor.ExtractUniformsList(source);

        Assert.Single(uniforms);
        Assert.Equal("realUniform", uniforms[0].Name);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void UniformDeclaration_ToString_FormatsCorrectly()
    {
        var simple = new UniformDeclaration("myUniform", "vec3");
        var array = new UniformDeclaration("myArray", "vec4", 16);

        Assert.Equal("uniform vec3 myUniform", simple.ToString());
        Assert.Equal("uniform vec4 myArray[16]", array.ToString());
    }

    #endregion

    #region CompareWithGLLocations Tests

    [Fact]
    public void CompareWithGLLocations_CategorizesCorrectly()
    {
        const string source = """
            #version 330 core
            uniform mat4 usedUniform;
            uniform mat4 unusedUniform;
            void main() { }
            """;

        // Simulate GL returning -1 for unused uniform (optimized out)
        static int MockGetLocation(string name) => name == "usedUniform" ? 0 : -1;

        var (active, optimizedOut) = UniformExtractor.CompareWithGLLocations(source, MockGetLocation);

        Assert.Single(active);
        Assert.Equal("usedUniform", active[0].Name);

        Assert.Single(optimizedOut);
        Assert.Equal("unusedUniform", optimizedOut[0].Name);
    }

    #endregion
}
