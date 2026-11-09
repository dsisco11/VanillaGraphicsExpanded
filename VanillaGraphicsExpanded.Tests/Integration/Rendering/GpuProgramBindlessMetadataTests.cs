using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies native opaque sampler and image assignments against driver state and metadata withdrawal.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramBindlessMetadataTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    #region Public API
    /// <summary>Real opaque sampler and image handles pass through native setters while withdrawing unit-based preparation.</summary>
    [Theory]
    [InlineData(false,0)] [InlineData(false,1)] [InlineData(false,2)] [InlineData(false,3)] [InlineData(true,0)] [InlineData(true,1)]
    public void NativeOpaqueHandleAssignmentWithdrawsUnits(bool image,int variant)
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetOpaqueHandle)); int shader=0,native=0;
        try
        {
            bool supported=Enumerable.Range(0,GL.GetInteger(GetPName.NumExtensions)).Any(index=>GL.GetString(StringNameIndexed.Extensions,index)=="GL_ARB_bindless_texture");
            Assert.SkipUnless(supported,"GL_ARB_bindless_texture is unavailable.");
            GpuProgramObject program;
            if(image)
            {
                shader=GL.CreateShader(ShaderType.ComputeShader);
                GL.ShaderSource(shader,"#version 430\nlayout(local_size_x=1) in; layout(rgba32f,binding=3) readonly uniform image2D images[2]; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=imageLoad(images[0],ivec2(0))+imageLoad(images[1],ivec2(0)); }");
                GL.CompileShader(shader); native=GL.CreateProgram(); GL.AttachShader(native,shader); GL.LinkProgram(native);
                GL.GetProgram(native,GetProgramParameterName.LinkStatus,out int linked); Assert.Equal(1,linked); program=GpuProgramObject.Adopt(native); native=0;
            }
            else program=CreateProgram(out shader,true);
            using(program)
            using(var texture=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())
            {
                var manager=RHIResourceManagerSystem.CurrentManager!;
                GpuProgramLayout layout;
                if(image)
                {
                    var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)manager.GetResourceRegistration(program)!;
                    var contract=new GpuBindingContract(); contract.Images.Add("images",new(3,true,2)); contract.RegisterShaderStorageBlockBinding("Output",0);
                    layout=new GpuProgramLayout { BinaryInterface=new GpuProgramInterface(program.ProgramId,new[]{contract},metadata:entry.Metadata) }; layout.RegisterContract(contract); program.SetPreparedLayout(layout);
                }
                else layout=PrepareArrayLayout(program);
                using var alias=new ProgramAlias(program.ProgramId); RHIResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
                ulong handle=unchecked((ulong)(image?GL.Arb.GetImageHandle((uint)texture.TextureId,0,false,0,(PixelFormat)SizedInternalFormat.Rgba32f):GL.Arb.GetTextureHandle((uint)texture.TextureId)));
                Assert.NotEqual(0UL,handle); Assert.Equal(ErrorCode.NoError,GL.GetError());
                output.WriteLine($"Native {(image?"image":"sampler")} handle=0x{handle:X16}; no residency needed for assignment/readback (no shader execution).");
                OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId); int location=GL.GetUniformLocation(program.ProgramId,image?"images[0]":"inputs[0]");
                // Act
                SetOpaqueHandle(variant,program.ProgramId,location,1,handle); Assert.Equal(ErrorCode.NoError,GL.GetError());
                // Assert
                GL.Arb.GetUniform((uint)program.ProgramId,location,out ulong actual); Assert.Equal(handle,actual); Assert.Equal(ErrorCode.NoError,GL.GetError());
                Assert.False(manager.IsResourceMetadataAvailable(program)); Assert.False(manager.IsResourceMetadataAvailable(alias)); Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
                using var replacement=CreateProgram(out int replacementShader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, replacementShader)); Assert.True(manager.IsResourceMetadataAvailable(replacement));
            }
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); if(shader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); if(native!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, native)); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Leaves exact opaque-handle overloads at the production native-slot interception boundary.</summary>
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
    /// <summary>Creates the Harmony owner used by the native opaque-handle dispatch wrapper.</summary>
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
