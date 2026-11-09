using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies opaque-handle interception and managed metadata withdrawal at a controlled native boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramBindlessMetadataTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    private static ulong capturedHandle;
    private static int capturedCount;
    #region Public API
    /// <summary>Controlled native boundaries receive all 64 bits while the original unit snapshot remains withdrawn.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public unsafe void OpaqueHandleDispatchWithdrawsOriginalProgram(int variant)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetOpaqueHandle));
        try
        {
            bool supported=Enumerable.Range(0,GL.GetInteger(GetPName.NumExtensions)).Any(index=>GL.GetString(StringNameIndexed.Extensions,index)=="GL_ARB_bindless_texture");
            output.WriteLine("Native GL_ARB_bindless_texture="+supported+"; this case uses a controlled native boundary, not native handle residency.");
            using var boundary = CaptureOpaqueBoundary(variant);
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var layout=PrepareArrayLayout(program); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            using var alias=new ProgramAlias(program.ProgramId); RHIResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
            var snapshot=entry.Metadata; int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            const ulong bits=0xFEDCBA9876543210UL; capturedHandle=0; capturedCount=-1;
            // Act
            SetOpaqueHandle(variant,program.ProgramId,location,1,bits);
            // Assert
            Assert.Equal(bits,capturedHandle); Assert.Equal(1,capturedCount);
            Assert.False(manager.IsResourceMetadataAvailable(program)); Assert.False(manager.IsResourceMetadataAvailable(alias)); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
            Assert.False(manager.RefreshMetadata(entry,GpuMetadataChange.None)); Assert.False(manager.RefreshMetadata(entry,GpuMetadataChange.Metadata));
            Assert.Throws<InvalidOperationException>(()=>manager.PublishMetadata(entry,snapshot,GpuMetadataChange.Metadata));
            using var replacement=CreateProgram(out int replacementShader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, replacementShader));
            Assert.True(manager.IsResourceMetadataAvailable(replacement)); Assert.NotSame(entry,manager.GetResourceRegistration(replacement));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Ignored locations, empty vectors and ordinary numeric destinations do not withdraw unit metadata.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OpaqueHandleNonResourceDestinationsDoNotInvalidate(int mode)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetOpaqueHandle));
        try
        {
            using var boundary = CaptureOpaqueBoundary(2); using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
            int location=mode==0?-1:GL.GetUniformLocation(program.ProgramId,mode==1?"numericValue":"inputs[0]");
            // Act
            SetOpaqueHandle(2,program.ProgramId,location,mode==2?0:1,ulong.MaxValue);
            // Assert
            Assert.True(manager.IsResourceMetadataAvailable(program)); Assert.Equal(revision,entry.MetadataRevision); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Substitutes a typed native oracle without requiring valid resident driver handles.</summary>
    private static unsafe NativeDriverCapture CaptureOpaqueBoundary(int variant)
    {
        var native = new NativeDriverCapture();
        switch (variant)
        {
            case 0:
                native.Add("glUniformHandleui64ARB", new BoundHandle((location, bits) =>
                    native.Observe(() => { capturedHandle = bits; capturedCount = 1; })));
                break;
            case 1:
                native.Add("glProgramUniformHandleui64ARB", new NamedHandle((program, location, bits) =>
                    native.Observe(() => { capturedHandle = bits; capturedCount = 1; })));
                break;
            case 2:
                native.Add("glUniformHandleui64vARB", new BoundHandles((location, count, bits) =>
                    native.Observe(() => { capturedCount = count; if (count > 0) capturedHandle = *bits; })));
                break;
            default:
                native.Add("glProgramUniformHandleui64vARB", new NamedHandles((program, location, count, bits) =>
                    native.Observe(() => { capturedCount = count; if (count > 0) capturedHandle = *bits; })));
                break;
        }
        native.Install();
        return native;
    }
    /// <summary>Matches scalar bound opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void BoundHandle(int location, ulong bits);
    /// <summary>Matches scalar named opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void NamedHandle(uint program, int location, ulong bits);
    /// <summary>Matches vector bound opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void BoundHandles(int location, int count, ulong* bits);
    /// <summary>Matches vector named opaque assignment.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void NamedHandles(uint program, int location, int count, ulong* bits);
    /// <summary>Leaves exact native signed, unsigned, array and pointer overloads through the production native-slot interception.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void SetOpaqueHandle(int variant,int program,int location,int count,ulong bits)
    {
        if(variant==0) GL.Arb.UniformHandle(location,unchecked((long)bits));
        else if(variant==1) GL.Arb.ProgramUniformHandle((uint)program,location,bits);
        else if(variant==2) GL.Arb.UniformHandle(location,count,new[]{unchecked((long)bits)});
        else GL.Arb.ProgramUniformHandle((uint)program,location,count,&bits);
    }
    /// <summary>Compiles the sampler-array program used to verify canonical metadata withdrawal.</summary>
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
    /// <summary>Creates the Harmony owner used by the native opaque-handle dispatch wrappers.</summary>
    private static Harmony Patch(string method) => new("VGE.Tests.ProgramMetadata."+method);
    /// <summary>Publishes the sampler-array contract from the program's current canonical metadata.</summary>
    private static GpuProgramLayout PrepareArrayLayout(GpuProgramObject program)
    {
        int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]"); GL.ProgramUniform1(program.ProgramId,location,2,new[]{0,1});
        var manager=RHIResourceManagerSystem.CurrentManager!; var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
        Assert.True(manager.RefreshMetadata(entry,GpuMetadataChange.Metadata));
        var contract=new GpuBindingContract(); contract.Samplers.Add("inputs",new GpuBindingContract.Binding(0,true,2)); contract.RegisterUniformBlockBinding("Parameters",0); contract.RegisterShaderStorageBlockBinding("Output",0);
        var layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) };
        layout.RegisterContract(contract); program.SetPreparedLayout(layout); return layout;
    }
    /// <summary>Receives a canonical metadata snapshot without taking program deletion ownership.</summary>
    private sealed class ProgramAlias(int id):GpuResource<GpuProgramMetadata>
    {
        #region Public API
        public GpuProgramMetadata Snapshot=>metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }=id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind=>GpuResourceKind.Program;
        /// <inheritdoc />
        protected override bool OwnsResource=>false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
        #endregion
    }
    #endregion
}


