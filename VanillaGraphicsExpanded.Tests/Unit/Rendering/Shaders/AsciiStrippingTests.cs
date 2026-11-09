using VanillaGraphicsExpanded;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>
/// Tests for the SIMD-optimized ASCII stripping functionality in SourceCodeImportsProcessor.
/// </summary>
public class AsciiStrippingTests
{
    #region Public API

    /// <summary>Creates the test class with the output sink supplied by xUnit.</summary>
    public AsciiStrippingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region StripNonAscii Basic Tests

    /// <summary>Null input produces a null result.</summary>
    [Fact]
    public void StripNonAscii_NullInput_ReturnsNull()
    {
        // Arrange: establish the input shape for this stripping contract.
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string? result = SourceCodeImportsProcessor.StripNonAscii(null!);
        // Assert: only the expected ASCII content remains.
        Assert.Null(result);
    }

    /// <summary>An empty input remains empty.</summary>
    [Fact]
    public void StripNonAscii_EmptyString_ReturnsEmpty()
    {
        // Arrange: establish the input shape for this stripping contract.
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii("");
        // Assert: only the expected ASCII content remains.
        Assert.Equal("", result);
    }

    /// <summary>An ASCII shader snippet remains unchanged and keeps its reference.</summary>
    [Fact]
    public void StripNonAscii_AllAscii_ReturnsSameString()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "void main() { gl_FragColor = vec4(1.0); }";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(input, result);
        // Should return the same reference for all-ASCII strings (optimization)
        Assert.Same(input, result);
    }

    /// <summary>ASCII newline characters and all other source text remain unchanged.</summary>
    [Fact]
    public void StripNonAscii_AsciiWithNewlines_PreservesNewlines()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "#version 330 core\n\nvoid main() {\n    // comment\n}\n";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(input, result);
    }

    /// <summary>ASCII tab characters and all other source text remain unchanged.</summary>
    [Fact]
    public void StripNonAscii_AsciiWithTabs_PreservesTabs()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "void main() {\n\tfloat x = 1.0;\n}";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(input, result);
    }

    #endregion

    #region StripNonAscii Unicode Removal Tests

    /// <summary>A single non-ASCII character is removed from surrounding ASCII text.</summary>
    [Fact]
    public void StripNonAscii_SingleUnicodeChar_RemovesIt()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "hello═world";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("helloworld", result);
    }

    /// <summary>Box-drawing separators are stripped from comment lines.</summary>
    [Fact]
    public void StripNonAscii_BoxDrawingChars_RemovesThem()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "// ═══════════════════\n// Header\n// ═══════════════════";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("// \n// Header\n// ", result);
    }

    /// <summary>A leading non-ASCII code unit is removed.</summary>
    [Fact]
    public void StripNonAscii_UnicodeAtStart_RemovesIt()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "║void main() {}";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("void main() {}", result);
    }

    /// <summary>A trailing non-ASCII code unit is removed.</summary>
    [Fact]
    public void StripNonAscii_UnicodeAtEnd_RemovesIt()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "void main() {}║";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("void main() {}", result);
    }

    /// <summary>Consecutive non-ASCII code units are all removed.</summary>
    [Fact]
    public void StripNonAscii_ConsecutiveUnicode_RemovesAll()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "start═══end";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("startend", result);
    }

    /// <summary>Mixed Greek and ASCII text retains only its ASCII code units.</summary>
    [Fact]
    public void StripNonAscii_MixedUnicodeAndAscii_KeepsOnlyAscii()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "α = β + γ; // Greek letters";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(" =  + ; // Greek letters", result);
    }

    /// <summary>A supplementary emoji is removed as a surrogate pair while neighboring ASCII remains.</summary>
    [Fact]
    public void StripNonAscii_Emoji_RemovesThem()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "// TODO: Fix this 🐛 bug";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Emoji are multi-byte, both surrogate pairs should be removed
        // Assert: only the expected ASCII content remains.
        Assert.DoesNotContain("🐛", result);
        Assert.Contains("// TODO: Fix this", result);
    }

    /// <summary>CJK characters are removed while ASCII comments are retained.</summary>
    [Fact]
    public void StripNonAscii_CJKCharacters_RemovesThem()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "// 日本語コメント comment";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("//  comment", result);
    }

    /// <summary>Cyrillic characters are removed while surrounding ASCII remains.</summary>
    [Fact]
    public void StripNonAscii_CyrillicCharacters_RemovesThem()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "// Привет world";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("//  world", result);
    }

    #endregion

    #region StripNonAscii Real Shader Code Tests

    /// <summary>Box-drawing decoration is removed without losing the shader's ASCII source.</summary>
    [Fact]
    public void StripNonAscii_ShaderWithBoxDrawingRegionMarkers_RemovesThem()
    {
        // Arrange: establish the input shape for this stripping contract.
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

        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);

        // Should preserve all the ASCII parts
        // Assert: only the expected ASCII content remains.
        Assert.Contains("#version 330 core", result);
        Assert.Contains("// LumOn Common Utility Functions", result);
        Assert.Contains("const float PI = 3.14159;", result);
        Assert.Contains("void main()", result);
        Assert.Contains("gl_FragColor = vec4(1.0);", result);

        // Should not contain box drawing chars
        Assert.DoesNotContain("═", result);
    }

    /// <summary>ASCII shader region comments and source remain unchanged.</summary>
    [Fact]
    public void StripNonAscii_ShaderWithRegionComments_PreservesRegions()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = """
            #version 330 core
            
            // #region Constants
            const float PI = 3.14159;
            // #endregion
            
            void main() {}
            """;

        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(input, result);
    }

    /// <summary>ASCII include guards and shader declarations remain unchanged.</summary>
    [Fact]
    public void StripNonAscii_IncludeGuardsPreserved()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = """
            #ifndef LUMON_COMMON_ASH
            #define LUMON_COMMON_ASH
            
            float getValue() { return 1.0; }
            
            #endif // LUMON_COMMON_ASH
            """;

        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(input, result);
    }

    #endregion

    #region StripNonAscii Performance/Edge Case Tests

    /// <summary>A large ASCII-only string keeps its original reference.</summary>
    [Fact]
    public void StripNonAscii_LargeAsciiString_ReturnsSameReference()
    {
        // Arrange: establish the input shape for this stripping contract.
        // Generate a large ASCII-only string
        string input = new string('x', 100_000);
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        
        // Should return same reference (fast path)
        // Assert: only the expected ASCII content remains.
        Assert.Same(input, result);
    }

    /// <summary>One non-ASCII character is removed from a large string without losing either side.</summary>
    [Fact]
    public void StripNonAscii_LargeStringWithSingleUnicode_StripsIt()
    {
        // Arrange: establish the input shape for this stripping contract.
        // Large string with one unicode char in the middle
        string prefix = new string('a', 50_000);
        string suffix = new string('b', 50_000);
        string input = prefix + "═" + suffix;
        
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        
        // Assert: only the expected ASCII content remains.
        Assert.Equal(prefix + suffix, result);
        Assert.Equal(100_000, result.Length);
    }

    /// <summary>Input containing only non-ASCII code units produces an empty result.</summary>
    [Fact]
    public void StripNonAscii_OnlyUnicode_ReturnsEmpty()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "═══║║║═══";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("", result);
    }

    /// <summary>Alternating ASCII and Unicode input retains each ASCII character in order.</summary>
    [Fact]
    public void StripNonAscii_AlternatingAsciiAndUnicode_KeepsAscii()
    {
        // Arrange: establish the input shape for this stripping contract.
        const string input = "a═b║c═d║e";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("abcde", result);
    }

    /// <summary>An isolated high surrogate is removed from the UTF-16 string.</summary>
    [Fact]
    public void StripNonAscii_HighSurrogateOnly_RemovesIt()
    {
        // Arrange: establish the input shape for this stripping contract.
        // Isolated high surrogate (invalid UTF-16, but should still be handled)
        char highSurrogate = '\uD800';
        string input = "test" + highSurrogate + "end";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("testend", result);
    }

    /// <summary>ASCII code unit 127 remains in the output.</summary>
    [Fact]
    public void StripNonAscii_Char127_IsPreserved()
    {
        // Arrange: establish the input shape for this stripping contract.
        // DEL character (127) is technically ASCII
        string input = "a" + "\x7F" + "b";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal(input, result);
    }

    /// <summary>The first code unit above ASCII is removed.</summary>
    [Fact]
    public void StripNonAscii_Char128_IsRemoved()
    {
        // Arrange: establish the input shape for this stripping contract.
        // 128 is the first non-ASCII character
        string input = "a" + "\x80" + "b";
        // Act: strip all non-ASCII UTF-16 code units from the input.
        string result = SourceCodeImportsProcessor.StripNonAscii(input);
        // Assert: only the expected ASCII content remains.
        Assert.Equal("ab", result);
    }

    #endregion

    #endregion

    #region Private

    private readonly ITestOutputHelper _output;

    #endregion
}
