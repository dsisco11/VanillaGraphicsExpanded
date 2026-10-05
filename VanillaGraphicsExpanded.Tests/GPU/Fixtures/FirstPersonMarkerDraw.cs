using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models the engine's marker-writing MRT draw without changing inherited blend or depth state.</summary>
internal sealed class FirstPersonMarkerDraw : IDisposable
{
    private readonly int program;
    private readonly int vao;
    private readonly int framebuffer;

    #region Public API
    /// <summary>Compiles a real raster draw writing overlay color, sampled depth and negative metadata.</summary>
    public FirstPersonMarkerDraw()
    {
        int vertex = Compile(ShaderType.VertexShader, """
            #version 430 core
            void main() {
                vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
                gl_Position = vec4(p * 2.0 - 1.0, -0.98, 1.0);
            }
            """);
        int fragment = Compile(ShaderType.FragmentShader, """
            #version 430 core
            layout(location=0) out vec4 color;
            layout(location=1) out vec4 depth;
            layout(location=4) out vec4 normal;
            layout(location=5) out vec4 material;
            void main() {
                color=vec4(1,0,0,1); depth=vec4(0.01,0,0,1);
                normal=vec4(0.5,0.5,1,-1); material=vec4(0.5,0,0,0);
            }
            """);
        program = GL.CreateProgram();
        GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
        GL.DeleteShader(vertex); GL.DeleteShader(fragment);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        Assert.True(linked != 0, GL.GetProgramInfoLog(program));
        vao = GL.GenVertexArray(); framebuffer = GL.GenFramebuffer();
    }

    /// <summary>Draws into the actual engine and VGE images, retaining inherited indexed blend enables.</summary>
    public void Draw(EngineTerrainBuffers terrain, GBufferManager buffers)
    {
        int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int priorProgram = GL.GetInteger(GetPName.CurrentProgram);
        int priorVao = GL.GetInteger(GetPName.VertexArrayBinding);
        int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, framebuffer);
            GL.FramebufferTexture(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, terrain.Color.TextureId, 0);
            GL.FramebufferTexture(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment1, terrain.Depth.TextureId, 0);
            GL.FramebufferTexture(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment4, buffers.NormalTextureId, 0);
            GL.FramebufferTexture(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment5, buffers.MaterialTextureId, 0);
            GL.DrawBuffers(6, [DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.None,
                DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment4, DrawBuffersEnum.ColorAttachment5]);
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer));
            GL.Viewport(0, 0, terrain.Primary.Width, terrain.Primary.Height);
            GL.UseProgram(program); GL.BindVertexArray(vao); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
        finally
        {
            GL.UseProgram(priorProgram); GL.BindVertexArray(priorVao);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
            GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
        }
    }

    /// <summary>Reads the actual marker attachment after rasterization.</summary>
    public float ReadMarker(GBufferManager buffers)
    {
        using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, buffers.NormalTextureId);
        float[] pixels = new float[4];
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
        return pixels[3];
    }

    /// <summary>Releases only this simulated engine draw's temporary objects.</summary>
    public void Dispose()
    {
        GL.DeleteProgram(program); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer);
    }
    #endregion

    #region Private
    /// <summary>Compiles one driver shader and reports its diagnostic on failure.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
        return shader;
    }
    #endregion
}
