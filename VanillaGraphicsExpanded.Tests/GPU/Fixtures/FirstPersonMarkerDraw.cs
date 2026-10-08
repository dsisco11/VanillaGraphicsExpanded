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
    /// <summary>Loads a precompiled raster draw writing overlay color, sampled depth and negative metadata.</summary>
    public FirstPersonMarkerDraw()
    {
        int vertex = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/first-person.vsh", ShaderType.VertexShader);
        int fragment = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/first-person.fsh", ShaderType.FragmentShader);
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
            GL.FramebufferTextureLayer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment4, buffers.SurfaceTexture!.TextureId, 0, 0);
            GL.FramebufferTextureLayer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment5, buffers.SurfaceTexture.TextureId, 0, 1);
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
        return LayeredTestTexture.Read(buffers.SurfaceTexture!, 0)[3];
    }

    /// <summary>Releases only this simulated engine draw's temporary objects.</summary>
    public void Dispose()
    {
        GL.DeleteProgram(program); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer);
    }
    #endregion
}
