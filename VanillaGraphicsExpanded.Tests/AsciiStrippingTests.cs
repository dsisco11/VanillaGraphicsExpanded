using VanillaGraphicsExpanded;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>
/// Tests for the SIMD-optimized ASCII stripping functionality in SourceCodeImportsProcessor.
/// </summary>
public class AsciiStrippingTests
{
    private readonly ITestOutputHelper _output;

    public AsciiStrippingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region StripNonAscii Basic Tests

    [Fact]
    public void StripNonAscii_NullInput_ReturnsNull()
    {
        string? result = SourceCodeImportsProcessor.StripNonAscii(null!);
        Assert.Null(result);
    }

    [Fact]
    public void StripNonAscii_EmptyString_ReturnsEmpty()
    {
        string result = SourceCodeImportsProcessor.StripNonAscii("");
        Assert.Equal("", result);
    }

    [Fact]
    public void StripNonAscii_AllAscii_ReturnsSameString()
    {
        const string input = "void main() { gl_FragColor = vec4(1.0); }";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(input, result);
        // Should return the same reference for all-ASCII strings (optimization)
        Assert.Same(input, result);
    }

    [Fact]
    public void StripNonAscii_AsciiWithNewlines_PreservesNewlines()
    {
        const string input = "#version 330 core\n\nvoid main() {\n    // comment\n}\n";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void StripNonAscii_AsciiWithTabs_PreservesTabs()
    {
        const string input = "void main() {\n\tfloat x = 1.0;\n}";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(input, result);
    }

    #endregion

    #region StripNonAscii Unicode Removal Tests

    [Fact]
    public void StripNonAscii_SingleUnicodeChar_RemovesIt()
    {
        const string input = "hello═world";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("helloworld", result);
    }

    [Fact]
    public void StripNonAscii_BoxDrawingChars_RemovesThem()
    {
        const string input = "// ═══════════════════\n// Header\n// ═══════════════════";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("// \n// Header\n// ", result);
    }

    [Fact]
    public void StripNonAscii_UnicodeAtStart_RemovesIt()
    {
        const string input = "║void main() {}";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("void main() {}", result);
    }

    [Fact]
    public void StripNonAscii_UnicodeAtEnd_RemovesIt()
    {
        const string input = "void main() {}║";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("void main() {}", result);
    }

    [Fact]
    public void StripNonAscii_ConsecutiveUnicode_RemovesAll()
    {
        const string input = "start═══end";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("startend", result);
    }

    [Fact]
    public void StripNonAscii_MixedUnicodeAndAscii_KeepsOnlyAscii()
    {
        const string input = "α = β + γ; // Greek letters";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(" =  + ; // Greek letters", result);
    }

    [Fact]
    public void StripNonAscii_Emoji_RemovesThem()
    {
        const string input = "// TODO: Fix this 🐛 bug";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Emoji are multi-byte, both surrogate pairs should be removed
        Assert.DoesNotContain("🐛", result);
        Assert.Contains("// TODO: Fix this", result);
    }

    [Fact]
    public void StripNonAscii_CJKCharacters_RemovesThem()
    {
        const string input = "// 日本語コメント comment";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("//  comment", result);
    }

    [Fact]
    public void StripNonAscii_CyrillicCharacters_RemovesThem()
    {
        const string input = "// Привет world";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("//  world", result);
    }

    #endregion

    #region StripNonAscii Real Shader Code Tests

    [Fact]
    public void StripNonAscii_ShaderWithBoxDrawingRegionMarkers_RemovesThem()
    {
        const string input = """
            #version 330 core
            // ═══════════════════════════════════════════════════════════════════════════
            // LumOn Common Utility Functions
            // ═══════════════════════════════════════════════════════════════════════════
            
            const float PI = 3.14159;
            
            void main() {
                gl_FragColor = vec4(1.0);
            }
            """;

        string result = SourceCodeImportsProcessor.StripNonAscii(input);

        // Should preserve all the ASCII parts
        Assert.Contains("#version 330 core", result);
        Assert.Contains("// LumOn Common Utility Functions", result);
        Assert.Contains("const float PI = 3.14159;", result);
        Assert.Contains("void main()", result);
        Assert.Contains("gl_FragColor = vec4(1.0);", result);

        // Should not contain box drawing chars
        Assert.DoesNotContain("═", result);
    }

    [Fact]
    public void StripNonAscii_ShaderWithRegionComments_PreservesRegions()
    {
        const string input = """
            #version 330 core
            
            // #region Constants
            const float PI = 3.14159;
            // #endregion
            
            void main() {}
            """;

        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void StripNonAscii_IncludeGuardsPreserved()
    {
        const string input = """
            #ifndef LUMON_COMMON_ASH
            #define LUMON_COMMON_ASH
            
            float getValue() { return 1.0; }
            
            #endif // LUMON_COMMON_ASH
            """;

        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(input, result);
    }

    #endregion

    #region StripNonAscii Performance/Edge Case Tests

    [Fact]
    public void StripNonAscii_LargeAsciiString_ReturnsSameReference()
    {
        // Generate a large ASCII-only string
        string input = new string('x', 100_000);
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        
        // Should return same reference (fast path)
        Assert.Same(input, result);
    }

    [Fact]
    public void StripNonAscii_LargeStringWithSingleUnicode_StripsIt()
    {
        // Large string with one unicode char in the middle
        string prefix = new string('a', 50_000);
        string suffix = new string('b', 50_000);
        string input = prefix + "═" + suffix;
        
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        
        Assert.Equal(prefix + suffix, result);
        Assert.Equal(100_000, result.Length);
    }

    [Fact]
    public void StripNonAscii_OnlyUnicode_ReturnsEmpty()
    {
        const string input = "═══║║║═══";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("", result);
    }

    [Fact]
    public void StripNonAscii_AlternatingAsciiAndUnicode_KeepsAscii()
    {
        const string input = "a═b║c═d║e";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("abcde", result);
    }

    [Fact]
    public void StripNonAscii_HighSurrogateOnly_RemovesIt()
    {
        // Isolated high surrogate (invalid UTF-16, but should still be handled)
        char highSurrogate = '\uD800';
        string input = "test" + highSurrogate + "end";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("testend", result);
    }

    [Fact]
    public void StripNonAscii_Char127_IsPreserved()
    {
        // DEL character (127) is technically ASCII
        string input = "a" + "\x7F" + "b";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void StripNonAscii_Char128_IsRemoved()
    {
        // 128 is the first non-ASCII character
        string input = "a" + "\x80" + "b";
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        Assert.Equal("ab", result);
    }

    #endregion
}
