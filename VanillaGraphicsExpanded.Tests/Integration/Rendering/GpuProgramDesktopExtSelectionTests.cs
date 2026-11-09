using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks desktop EXT separate-program selectors independently of ARB pipeline selectors.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramDesktopExtSelectionTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{

    #region Public API
    /// <summary>Signed and unsigned active-program selectors direct bound uniform publication to the actual selected program.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopExtActiveProgramSelectsUniformOwner(bool unsigned)
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        RequireDesktopExt();
        var harmony = Patch(nameof(DesktopSelect));
        var units = Patch(nameof(SetUnits));
        try
        {
            using var a = CreateProgram(out int shaderA);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shaderA));
            using var b = CreateProgram(out int shaderB);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shaderB));
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entryA = manager.GetResourceRegistration(a)!;
            var entryB = manager.GetResourceRegistration(b)!;
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, a.ProgramId);
            ulong revisionA = entryA.MetadataRevision;
            ulong revisionB = entryB.MetadataRevision;

            // Act
            DesktopSelect(unsigned, false, b.ProgramId);
            int location = GL.GetUniformLocation(b.ProgramId, "inputs[0]");
            SetUnits(0, b.ProgramId, location, 3);
            // Assert
            Assert.Equal(b.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            GL.GetUniform(b.ProgramId, location, out int unit);
            Assert.Equal(3, unit);
            Assert.Equal(revisionA, entryA.MetadataRevision);
            Assert.True(entryB.MetadataRevision > revisionB);

            // Uniform observation recovers the active selector, but cannot certify all installed stages.
            using var nativeCalls = new BindingCallCapture();
            Assert.Equal(b.ProgramId, OpenGlStateCache.Current.GetCurrentProgram());
            // Act: request the same program again after observation; the uncertain cache requires a native restore.
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, b.ProgramId);
            // Assert: restoration reaches the native UseProgram call site.
            Assert.Equal(1, nativeCalls.Calls(nameof(GL.UseProgram)));

            // Act: move to a second owner before submitting the invalid unlinked program.
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, a.ProgramId);
            int unlinked = GL.CreateProgram();
            try
            {
                DesktopSelect(unsigned, false, unlinked);
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                Assert.Equal(a.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            }
            finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, unlinked)); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            DrainGlErrors();
            harmony.UnpatchAll(harmony.Id);
            units.UnpatchAll(units.Id);
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Stage-only selection requires full restoration even after a query recovers the unchanged active-program selector.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopExtStageSelectionForcesFullProgramRestore(bool unsigned)
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        RequireDesktopExt();
        var harmony = Patch(nameof(DesktopSelect));
        int native = CreateDesktopVertexProgram();
        try
        {
            using var program = CreateProgram(out int shader);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            // Act
            DesktopSelect(unsigned, true, native);
            // Assert
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(program.ProgramId, OpenGlStateCache.Current.GetCurrentProgram());

            using var nativeCalls = new BindingCallCapture();
            // Act: selecting the known program must restore the full selector after EXT stage-only mutation.
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            // Assert: the first request emits a native restore, and the repeated request is cached.
            Assert.Equal(1, nativeCalls.Calls(nameof(GL.UseProgram)));
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            Assert.Equal(1, nativeCalls.Calls(nameof(GL.UseProgram)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            DrainGlErrors();
            harmony.UnpatchAll(harmony.Id);
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Clears pending native errors during failure cleanup after selector assertions.</summary>
    private static void DrainGlErrors()
    {
        while (GL.GetError() != ErrorCode.NoError) { }
    }

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
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, program));
            throw;
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, vertex)); }
    }

    /// <summary>Compiles the sampler-array executable used to verify selector ownership.</summary>
    private static GpuProgramObject CreateProgram(out int shader,bool prepared=false)
    {
        const string source="#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; uniform int numericValue; layout(std140,binding=0) uniform Parameters { vec4 parameter; }; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)+vec4(numericValue)+parameter; }";
        shader=0;
        int program=0;
        int candidateShader=0;
        try
        {
            candidateShader=GL.CreateShader(ShaderType.ComputeShader);
            GL.ShaderSource(candidateShader,prepared?source.Replace("uniform int numericValue;","").Replace("+vec4(numericValue)",""):source);
            GL.CompileShader(candidateShader);
            GL.GetShader(candidateShader,ShaderParameter.CompileStatus,out int compiled);
            Assert.Equal(1,compiled);
            program=GL.CreateProgram();
            GL.AttachShader(program,candidateShader);
            GL.LinkProgram(program);
            GL.GetProgram(program,GetProgramParameterName.LinkStatus,out int linked);
            Assert.Equal(1,linked);
            var owner=GpuProgramObject.Adopt(program);
            shader=candidateShader;
            program=0;
            candidateShader=0;
            return owner;
        }
        catch
        {
            if(program!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program,program));
            if(candidateShader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader,candidateShader));
            throw;
        }
    }
    /// <summary>Creates a uniquely named Harmony owner for the native selector interception wrapper.</summary>
    private static Harmony Patch(string method) => new("VGE.Tests.ProgramMetadata."+method);
    /// <summary>Preserves the native integer setter overloads used to observe the selected program.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void SetUnits(int variant,int program,int location,int value)
    {
        switch(variant)
        {
            case 0: GL.Uniform1(location,value); break;
            case 1: GL.Uniform1(location,1,new[]{value}); break;
            case 2: GL.Uniform1(location,1,ref value); break;
            case 3: GL.ProgramUniform1(program,location,value); break;
            case 4: GL.ProgramUniform1((uint)program,location,1,new[]{value}); break;
            case 5: GL.ProgramUniform1(program,location,1,&value); break;
            case 6: GL.Ext.ProgramUniform1(program,location,value); break;
            case 7: GL.Ext.ProgramUniform1(program,location,1,ref value); break;
        }
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

    #endregion
}
