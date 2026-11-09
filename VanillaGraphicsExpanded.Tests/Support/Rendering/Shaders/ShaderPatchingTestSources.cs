using System;

namespace VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;

/// <summary>Provides immutable shader inputs shared across parser and preprocessing tests.</summary>
internal static class ShaderPatchingTestSources
{
    #region Public API
    /// <summary>Normalizes shader text before comparing tokenizer output across platforms.</summary>
    internal static string NormalizeLineEndings(string text) =>
        text.ReplaceLineEndings("\n");

    /// <summary>
    /// Sample GLSL shader for testing function and directive recognition.
    /// </summary>
    internal const string SampleShader = """
        #version 330 core
        #extension GL_ARB_explicit_attrib_location: enable

        layout(location = 0) out vec4 outColor;
        layout(location = 1) out vec4 outGlow;
        #if SSAOLEVEL > 0
        in vec4 gnormal;
        layout(location = 2) out vec4 outGNormal;
        layout(location = 3) out vec4 outGPosition;
        #endif

        uniform sampler2D tex;
        in vec2 uv;
        out vec4 fragColor;

        // Helper function
        vec4 sampleTexture(vec2 coord) {
            return texture(tex, coord);
        }

        void main() {
            vec4 color = sampleTexture(uv);
            fragColor = color;
        }
        """;

    /// <summary>
    /// Sample shader with @import directive.
    /// </summary>
    internal const string ShaderWithImport = """
        #version 330 core
        #extension GL_ARB_explicit_attrib_location: enable

        layout(location = 0) out vec4 outColor;
        layout(location = 1) out vec4 outGlow;
        #if SSAOLEVEL > 0
        in vec4 gnormal;
        layout(location = 2) out vec4 outGNormal;
        layout(location = 3) out vec4 outGPosition;
        #endif

        @import "./includes/shared.glsl"
        void main() {
            fragColor = vec4(1.0);
        }
        """;

    /// <summary>
    /// Sample include file for preprocessing tests.
    /// </summary>
    internal const string SharedInclude = """
        // Shared code
        float PI = 3.14159;
        """;
    #endregion
}
