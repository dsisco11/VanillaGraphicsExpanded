using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;


namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies manager metadata behavior for failed native dispatch and foreign-context observations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A managed wrapper failure before native dispatch preserves the unchanged executable metadata.</summary>
    [Fact]
    public void FailureBeforeDispatchPreservesMetadata()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetUnits));
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(program)!;
            var layout=PrepareArrayLayout(program);
            var prepared=layout.BinaryInterface;
            ulong revision=entry.MetadataRevision;
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.ProgramUniform1),new[]{typeof(int),typeof(int),typeof(int)}),prefix:new HarmonyMethod(typeof(GpuProgramMetadataInterceptionTests),nameof(ThrowDispatch)));
            // Act
            Assert.Throws<ApplicationException>(()=>SetUnits(3,program.ProgramId,location,7));
            // Assert
            Assert.Same(prepared,layout.BinaryInterface);
            Assert.True(manager.IsResourceMetadataAvailable(program));
            Assert.Equal(revision,entry.MetadataRevision);
            harmony.UnpatchAll(harmony.Id);
            Assert.True(manager.RefreshMetadata((GpuResourceMetadataEntry<GpuProgramMetadata>)entry,GpuMetadataChange.None)); Assert.True(manager.IsResourceMetadataAvailable(program));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Observation under a foreign manager context cannot mutate the original program registration.</summary>
    [Fact]
    public void ForeignContextPreventsObservation()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.RetireResourceServices(); var origin=VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current(); var current=origin;
        using var manager=new RHIResourceManager(()=>current,static _=>true); RHIResourceManagerSystem.Initialize(manager);
        try
        {
            using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var entry=manager.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
            current=(origin.Item1,origin.Item2+1);
            // Act
            OpenGlStateCache.ObserveProgramMetadata(program.ProgramId,true,false,-1,1);
            current=origin;
            // Assert
            Assert.Equal(revision,entry.MetadataRevision); Assert.True(manager.IsResourceMetadataAvailable(program));
            Assert.True(manager.RefreshMetadata((GpuResourceMetadataEntry<GpuProgramMetadata>)entry,GpuMetadataChange.None)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { current=origin; manager.Dispose(); RHIResourceManagerSystem.Shutdown(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    #region Test doubles
    /// <summary>Models a native boundary failure before the command reaches the driver.</summary>
    private static void ThrowDispatch() => throw new ApplicationException("native program dispatch failure");

    #endregion

    #region Program construction
    /// <summary>Compiles a linked program with the sampler and block facts used by the metadata checks.</summary>
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
    /// <summary>Prepares the same authored sampler-array and buffer-block contract the migrated case queries.</summary>
    private static GpuProgramLayout PrepareArrayLayout(GpuProgramObject program)
    {
        int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]"); GL.ProgramUniform1(program.ProgramId,location,2,new[]{0,1});
        var manager=RHIResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
        Assert.True(manager.RefreshMetadata(entry,GpuMetadataChange.Metadata));
        var contract=new GpuBindingContract(); contract.Samplers.Add("inputs",new GpuBindingContract.Binding(0,true,2)); contract.RegisterUniformBlockBinding("Parameters",0); contract.RegisterShaderStorageBlockBinding("Output",0);
        var layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) };
        layout.RegisterContract(contract); program.SetPreparedLayout(layout); return layout;
    }
    #endregion

    #region Native call adapters
    /// <summary>Creates the interception owner used by the native setter call site.</summary>
    private static Harmony Patch(string method) => new("VGE.Tests.ProgramMetadata."+method);
    /// <summary>Preserves the native setter overloads exercised by these metadata cases.</summary>
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
    #endregion
    #endregion
}
