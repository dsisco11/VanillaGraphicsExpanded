using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks desktop EXT separate-program selectors independently of ARB pipeline selectors.</summary>
public sealed partial class GpuProgramMetadataInterceptionTests
{
    private static int restoredProgramCalls;

    #region Public API
    /// <summary>Signed and unsigned active-program selectors direct bound uniform publication to the actual selected program.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopExtActiveProgramSelectsUniformOwner(bool unsigned)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        RequireDesktopExt();
        var harmony = Patch(nameof(DesktopSelect));
        var units = Patch(nameof(SetUnits));
        try
        {
            using var a = CreateProgram(out int shaderA);
            using var b = CreateProgram(out int shaderB);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteShader(shaderA);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteShader(shaderB);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entryA = manager.GetResourceRegistration(a)!;
            var entryB = manager.GetResourceRegistration(b)!;
            OpenGLProgramSelection.Select(StateCache.Current, a.ProgramId);
            ulong revisionA = entryA.MetadataRevision;
            ulong revisionB = entryB.MetadataRevision;

            DesktopSelect(unsigned, false, b.ProgramId);
            int location = GL.GetUniformLocation(b.ProgramId, "inputs[0]");
            SetUnits(0, b.ProgramId, location, 3);
            Assert.Equal(b.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            GL.GetUniform(b.ProgramId, location, out int unit);
            Assert.Equal(3, unit);
            Assert.Equal(revisionA, entryA.MetadataRevision);
            Assert.True(entryB.MetadataRevision > revisionB);

            // Uniform observation recovers the active selector, but cannot certify all installed stages.
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.UseProgram), [typeof(int)]),
                prefix: new HarmonyMethod(typeof(GpuProgramMetadataInterceptionTests), nameof(CountFullProgramRestore)));
            restoredProgramCalls = 0;
            Assert.Equal(b.ProgramId, StateCache.Current.GetCurrentProgram());
            OpenGLProgramSelection.Select(StateCache.Current, b.ProgramId);
            Assert.Equal(1, restoredProgramCalls);

            OpenGLProgramSelection.Select(StateCache.Current, a.ProgramId);
            int unlinked = GL.CreateProgram();
            try
            {
                DesktopSelect(unsigned, false, unlinked);
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                Assert.Equal(a.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            }
            finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteProgram(unlinked); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            units.UnpatchAll(units.Id);
            OpenGLProgramSelection.Select(StateCache.Current, 0);
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Stage-only selection requires full restoration even after a query recovers the unchanged active-program selector.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopExtStageSelectionForcesFullProgramRestore(bool unsigned)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        RequireDesktopExt();
        var harmony = Patch(nameof(DesktopSelect));
        int native = CreateDesktopVertexProgram();
        try
        {
            using var program = CreateProgram(out int shader);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteShader(shader);
            OpenGLProgramSelection.Select(StateCache.Current, program.ProgramId);
            DesktopSelect(unsigned, true, native);
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(program.ProgramId, StateCache.Current.GetCurrentProgram());

            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.UseProgram), [typeof(int)]),
                prefix: new HarmonyMethod(typeof(GpuProgramMetadataInterceptionTests), nameof(CountFullProgramRestore)));
            restoredProgramCalls = 0;
            OpenGLProgramSelection.Select(StateCache.Current, program.ProgramId);
            Assert.Equal(1, restoredProgramCalls);
            OpenGLProgramSelection.Select(StateCache.Current, program.ProgramId);
            Assert.Equal(1, restoredProgramCalls);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            OpenGLProgramSelection.Select(StateCache.Current, 0);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteProgram(native);
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Builds a valid separable vertex executable with its required built-in interface declaration.</summary>
    private static int CreateDesktopVertexProgram()
    {
        int program = GL.CreateProgram();
        int vertex = GL.CreateShader(ShaderType.VertexShader);
        try
        {
            GL.ShaderSource(vertex, "#version 430\nout gl_PerVertex { vec4 gl_Position; };\nvoid main(){gl_Position=vec4(0,0,0,1);}");
            GL.CompileShader(vertex);
            GL.ProgramParameter(program, ProgramParameterName.ProgramSeparable, 1);
            GL.AttachShader(program, vertex);
            GL.LinkProgram(program);
            GL.DetachShader(program, vertex);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked == 1, GL.GetProgramInfoLog(program));
            return program;
        }
        catch
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteProgram(program);
            throw;
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteShader(vertex); }
    }

    /// <summary>Records native capability separately from correctness assertions.</summary>
    private void RequireDesktopExt()
    {
        bool supported = Enumerable.Range(0, GL.GetInteger(GetPName.NumExtensions))
            .Any(i => GL.GetString(StringNameIndexed.Extensions, i) == "GL_EXT_separate_shader_objects");
        output.WriteLine("Desktop EXT SSO=" + supported);
        Assert.SkipUnless(supported, "Desktop EXT separate shader objects unavailable.");
    }

    /// <summary>Preserves exact signed and unsigned desktop EXT signatures.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DesktopSelect(bool unsigned, bool stage, int program)
    {
        if (stage)
        {
            if (unsigned) GL.Ext.UseShaderProgram((ExtSeparateShaderObjects)ShaderType.VertexShader, (uint)program);
            else GL.Ext.UseShaderProgram((ExtSeparateShaderObjects)ShaderType.VertexShader, program);
        }
        else
        {
            if (unsigned) GL.Ext.ActiveProgram((uint)program);
            else GL.Ext.ActiveProgram(program);
        }
    }

    /// <summary>Counts actual full-program restoration rather than relying on cached selector equality.</summary>
    private static void CountFullProgramRestore() => restoredProgramCalls++;
    #endregion
}
