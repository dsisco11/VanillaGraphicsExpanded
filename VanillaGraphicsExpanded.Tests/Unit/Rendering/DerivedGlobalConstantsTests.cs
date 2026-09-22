using ShaderBuildTool.Spirv;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Guards syntax-aware global initializer rewriting against comments and nested declaration scopes.</summary>
public sealed class DerivedGlobalConstantsTests
{
    #region Initializer rewriting
    /// <summary>Only global parenthesized initializers become mutable; nested syntax, comments and local constants survive.</summary>
    [Fact]
    public void RewritesGlobalCallsWithoutTouchingLocalOrSpecializationConstants()
    {
        const string source = """
            #version 450 core
            // const commented = vec3(0); { pretend scope }
            /* const block = max(1, 2); */
            layout(constant_id = 7) const float specialized = 2.0;
            const float literal = 3.0;
            const float grouped = (2.0 + (3.0 * 4.0));
            const vec3 nested = vec3(max(specialized, 1.0), /* keep comma, ) */ 2.0, 3.0);
            void main() {
                const vec3 local = vec3(max(1.0, 2.0));
                if (true) { const float inner = float(3); }
            }
            """;
        string result = DerivedGlobalConstants.Apply(source);
        Assert.Contains("// const commented = vec3(0); { pretend scope }", result);
        Assert.Contains("/* const block = max(1, 2); */", result);
        Assert.Contains("layout(constant_id = 7) const float specialized = 2.0;", result);
        Assert.Contains("const float literal = 3.0;", result);
        Assert.DoesNotContain("const float grouped", result);
        Assert.Contains("float grouped = (2.0 + (3.0 * 4.0));", result);
        Assert.DoesNotContain("const vec3 nested", result);
        Assert.Contains("vec3(max(specialized, 1.0), /* keep comma, ) */ 2.0, 3.0)", result);
        Assert.Contains("const vec3 local = vec3(max(1.0, 2.0));", result);
        Assert.Contains("const float inner = float(3);", result);
    }

    /// <summary>Multiline and comment-separated layouts remain specialization declarations even with constructor initializers.</summary>
    [Fact]
    public void PreservesMultilineSpecializationAndPreprocessorText()
    {
        const string source = """
            #define CALL_MACRO(x) const float macroValue = float(x)
            layout(
                constant_id = 2
            ) /* between layout and declaration */ const float specialized = float(1);
            const /* retain modifier comment */ float ordinary = float(2);
            """;
        string result = DerivedGlobalConstants.Apply(source);
        Assert.Contains("#define CALL_MACRO(x) const float macroValue = float(x)", result);
        Assert.Contains("const float specialized = float(1)", result);
        Assert.Contains("retain modifier comment", result);
        Assert.DoesNotContain("const /* retain modifier comment */ float ordinary", result);
    }
    /// <summary>A completed interface declaration cannot lend its layout to a later ordinary global initializer.</summary>
    [Theory]
    [InlineData("layout(location=0) uniform float value;")]
    [InlineData("layout(location=0) in vec3 value;")]
    public void InterfaceLayoutDoesNotLeakIntoFollowingGlobal(string declaration)
    {
        string result = DerivedGlobalConstants.Apply(declaration + "\nconst float derived = float(2);\n");
        Assert.Contains(declaration, result);
        Assert.DoesNotContain("const float derived", result);
        Assert.Contains("float derived = float(2);", result);
    }
    /// <summary>Comment punctuation cannot create an initializer call or prematurely terminate a declaration.</summary>
    [Fact]
    public void CommentPunctuationDoesNotDriveDeclarationAnalysis()
    {
        const string source = "const float untouched = 1.0 /* ( fake call ) */;\nconst /* comment with ; and } */ float actual=float(2);";
        string result = DerivedGlobalConstants.Apply(source);
        Assert.Contains("const float untouched = 1.0 /* ( fake call ) */;", result);
        Assert.Contains("/* comment with ; and } */", result);
        Assert.Contains("/* comment with ; and } */ float actual=float(2);", result);
        Assert.Equal(1, result.Split("/* comment with ; and } */", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("const /* comment with ; and } */ float actual", result);
    }
    #endregion
}
