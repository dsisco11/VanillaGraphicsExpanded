using System;

using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks expanded production traversal helpers remain at global GLSL scope.</summary>
public sealed class ShaderProcessedSourceDiagnosticsTests
{
    /// <summary>Expands the per-view near-field shader and checks the shared traversal definition.</summary>
    [Fact]
    public void LumonDebugFsh_ProcessedSource_HasTopLevelTraceSceneHelpers()
    {
        using var helper = new ShaderTestHelper(shaderBasePath: ".", includeBasePath: ".");
        var src = helper.GetProcessedSource("lumon_debug_view_near_field_geometry.fsh");
        Assert.NotNull(src);

        string source = src!;
        Assert.StartsWith("#version", source.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("#version 330 core", source, StringComparison.Ordinal);

        int idx = source.IndexOf("lumonTraceSceneEndpoint", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Expected lumonTraceSceneEndpoint to exist in processed source.");

        int line = 1;
        for (int i = 0; i < idx; i++)
        {
            if (source[i] == '\n') line++;
        }

        // Rough brace depth check (not comment-aware, but good enough to detect obvious nesting).
        int braceDepth = 0;
        for (int i = 0; i < idx; i++)
        {
            char c = source[i];
            if (c == '{') braceDepth++;
            else if (c == '}') braceDepth--;
        }

        Console.WriteLine($"lumonTraceSceneEndpoint appears at line {line}, braceDepth≈{braceDepth}");

        // Dump a small window around the target line to help diagnose shader compiler errors.
        var lines = source.Split('\n');
        int start = Math.Max(0, line - 8);
        int end = Math.Min(lines.Length - 1, line + 8);
        for (int i = start; i <= end; i++)
        {
            Console.WriteLine($"{i + 1}: {lines[i]}");
        }

        Assert.True(braceDepth == 0, "TraceScene helpers should be declared at top level (brace depth 0).");
    }
}
