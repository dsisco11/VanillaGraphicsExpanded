using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class PbrShaderPrograms
{
    public static int CompilePbrDirectLightingProgram(ShaderTestHelper shaderHelper)
    {
        ArgumentNullException.ThrowIfNull(shaderHelper);

        string? processedFragment = shaderHelper.GetProcessedSource("pbr_direct_lighting.fsh");
        if (processedFragment == null)
        {
            throw new InvalidOperationException("Processed shader source not found: pbr_direct_lighting.fsh");
        }

        processedFragment = SourceCodeImportsProcessor.StripNonAscii(processedFragment);

        const string vertexSource = "#version 330 core\n" +
                                    "layout(location = 0) in vec2 position;\n" +
                                    "out vec2 uv;\n" +
                                    "void main(){ gl_Position = vec4(position, 0.0, 1.0); uv = position * 0.5 + 0.5; }\n";

        return CompileProgramFromSource(vertexSource, processedFragment);
    }

    public static int CompilePbrCompositeProgram(ShaderTestHelper shaderHelper)
    {
        ArgumentNullException.ThrowIfNull(shaderHelper);

        string? processedFragment = shaderHelper.GetProcessedSource("pbr_composite.fsh");
        if (processedFragment == null)
        {
            throw new InvalidOperationException("Processed shader source not found: pbr_composite.fsh");
        }

        processedFragment = SourceCodeImportsProcessor.StripNonAscii(processedFragment);

        const string vertexSource = "#version 330 core\n" +
                                    "layout(location = 0) in vec2 position;\n" +
                                    "void main(){ gl_Position = vec4(position, 0.0, 1.0); }\n";

        return CompileProgramFromSource(vertexSource, processedFragment);
    }

    private static int CompileProgramFromSource(string vertexSource, string fragmentSource)
    {
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);
        GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vStatus);
        if (vStatus == 0)
        {
            var log = GL.GetShaderInfoLog(vertexShader);
            GL.DeleteShader(vertexShader);
            throw new InvalidOperationException($"Vertex shader compile error: {log}");
        }

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);
        GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fStatus);
        if (fStatus == 0)
        {
            var log = GL.GetShaderInfoLog(fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            throw new InvalidOperationException($"Fragment shader compile error: {log}");
        }

        int program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int lStatus);
        if (lStatus == 0)
        {
            var log = GL.GetProgramInfoLog(program);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteProgram(program);
            throw new InvalidOperationException($"Program link error: {log}");
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return program;
    }
}
