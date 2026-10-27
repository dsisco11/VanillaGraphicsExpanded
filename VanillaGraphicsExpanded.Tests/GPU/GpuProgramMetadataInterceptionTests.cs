using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact native executable and interface commands against canonical program publication.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed partial class GpuProgramMetadataInterceptionTests(HeadlessGLFixture fixture,ITestOutputHelper output)
{
    #region Public API
    /// <summary>Integer payload overloads update sampler arrays while ordinary numeric content leaves metadata untouched.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ExactUnitPayloadsPublishActualUnits(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=Patch(nameof(SetUnits));
        try
        {
            using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            using var sentinel=CreateProgram(out int sentinelShader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, sentinelShader));
            using var pipeline=GpuProgramPipeline.Create(); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, pipeline.PipelineId);
            bool bound=variant<3;
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, bound?program.ProgramId:sentinel.ProgramId);
            var manager=GpuResourceManagerSystem.CurrentManager!;
            using var alias=new ProgramAlias(program.ProgramId); GpuResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            var entry=manager.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            SetUnits(variant,program.ProgramId,location,3);
            GL.GetUniform(program.ProgramId,location,out int actual); Assert.Equal(3,actual);
            Assert.Equal(revision+1,entry.MetadataRevision); Assert.Contains(alias.Snapshot.TextureUnits,item=>item.Location==location && item.Unit==3);
            revision=entry.MetadataRevision;
            int numeric=GL.GetUniformLocation(program.ProgramId,"numericValue");
            SetUnits(variant,program.ProgramId,numeric,9);
            GL.GetUniform(program.ProgramId,numeric,out actual); Assert.Equal(9,actual); Assert.Equal(revision,entry.MetadataRevision);
            Assert.Equal(bound?program.ProgramId:sentinel.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(pipeline.PipelineId,GL.GetInteger(GetPName.ProgramPipelineBinding)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, 0); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Bound uniform assignment resolves a pipeline's active program when no monolithic program is selected.</summary>
    [Fact]
    public void PipelineActiveProgramReceivesBoundUnit()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetUnits));
        try
        {
            using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            using var pipeline=GpuProgramPipeline.Create(); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, pipeline.PipelineId); GL.ActiveShaderProgram(pipeline.PipelineId,program.ProgramId); OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            var entry=GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]"); SetUnits(0,program.ProgramId,location,5);
            GL.GetUniform(program.ProgramId,location,out int actual); Assert.Equal(5,actual); Assert.Equal(revision+1,entry.MetadataRevision);
            Assert.Equal(0,GL.GetInteger(GetPName.CurrentProgram)); Assert.Equal(pipeline.PipelineId,GL.GetInteger(GetPName.ProgramPipelineBinding)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, 0); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Block assignments publish measured bindings and rejected indices retain real values and errors.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BlockBindingsPublishActualFacts(bool storage)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetBlock));
        try
        {
            using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(program)!;
            var kind=storage?ProgramInterface.ShaderStorageBlock:ProgramInterface.UniformBlock;
            int index=GL.GetProgramResourceIndex(program.ProgramId,kind,storage?"Output":"Parameters");
            ulong revision=entry.MetadataRevision; SetBlock(storage,program.ProgramId,index,4);
            Assert.Equal(revision+1,entry.MetadataRevision);
            Assert.Equal(4,entry.Metadata.Resources.Single(item=>item.Interface==kind && item.Index==index).Properties[ProgramProperty.BufferBinding][0]);
            SetBlock(storage,program.ProgramId,int.MaxValue,7); Assert.Equal(ErrorCode.InvalidValue,GL.GetError());
            Assert.Equal(4,entry.Metadata.Resources.Single(item=>item.Interface==kind && item.Index==index).Properties[ProgramProperty.BufferBinding][0]); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Failed and recovered links replace canonical executable facts instead of retaining the prior interface.</summary>
    [Fact]
    public void LinkFailureAndRecoveryPublishAliases()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(Link)); int shader=0;
        try
        {
            using var program=CreateProgram(out shader);
            using var alias=new ProgramAlias(program.ProgramId); GpuResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            GL.ShaderSource(shader,"invalid shader"); GL.CompileShader(shader); Link(program.ProgramId);
            Assert.False(alias.Snapshot.ExecutableAvailable); Assert.Empty(alias.Snapshot.Resources);
            GL.ShaderSource(shader,Source); GL.CompileShader(shader); Link(program.ProgramId);
            Assert.True(alias.Snapshot.ExecutableAvailable); Assert.NotEmpty(alias.Snapshot.TextureUnits); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { if(shader!=0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader)); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Native dispatch exceptions withdraw existing facts until an explicit successful refresh.</summary>
    [Fact]
    public void DispatchFailureWithdrawsMetadata()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SetUnits));
        try
        {
            using var program=CreateProgram(out int shader,true); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(program)!;
            var layout=PrepareArrayLayout(program);
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]");
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.ProgramUniform1),new[]{typeof(int),typeof(int),typeof(int)}),prefix:new HarmonyMethod(typeof(GpuProgramMetadataInterceptionTests),nameof(ThrowDispatch)));
            Assert.Throws<ApplicationException>(()=>SetUnits(3,program.ProgramId,location,7));
            Assert.Throws<InvalidOperationException>(()=>layout.BinaryInterface);
            Assert.False(manager.IsResourceMetadataAvailable(program));
            harmony.UnpatchAll(harmony.Id);
            Assert.True(manager.RefreshMetadata((GpuResourceMetadataEntry<GpuProgramMetadata>)entry,GpuMetadataChange.None)); Assert.True(manager.IsResourceMetadataAvailable(program));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Commands on a native program without a registration never create an implicit manager entry.</summary>
    [Fact]
    public void UntrackedLinkDoesNotCreateRegistration()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(Link)); int program=GL.CreateProgram();
        try
        {
            Link(program);
            Assert.Null(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Program,program));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, program)); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A context change between admission and completion prevents publication under a different manager context.</summary>
    [Fact]
    public void ContextChangePreventsCompletion()
    {
        fixture.EnsureContextValid(); var origin=VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current(); var current=origin;
        using var manager=new GpuResourceManager(()=>current,static _=>true); GpuResourceManagerSystem.Initialize(manager);
        try
        {
            using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            var entry=manager.GetResourceRegistration(program)!; ulong revision=entry.MetadataRevision;
            var token=OpenGlStateCache.BeginNativeProgramMetadata(program.ProgramId,true,false,-1,1);
            current=(origin.Item1,origin.Item2+1); token.Complete(true); current=origin;
            Assert.Equal(revision,entry.MetadataRevision); Assert.False(manager.IsResourceMetadataAvailable(program));
            Assert.True(manager.RefreshMetadata((GpuResourceMetadataEntry<GpuProgramMetadata>)entry,GpuMetadataChange.None)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { current=origin; manager.Dispose(); GpuResourceManagerSystem.Shutdown(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Exact external program selectors leave bound units targeting the actual program after rejected selection.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ProgramSelectorsRecoverActualTarget(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SelectProgram)); var units=Patch(nameof(SetUnits));
        try
        {
            using var first=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            using var second=CreateProgram(out shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, first.ProgramId);
            var manager=GpuResourceManagerSystem.CurrentManager!; var a=manager.GetResourceRegistration(first)!; var b=manager.GetResourceRegistration(second)!;
            ulong ar=a.MetadataRevision,br=b.MetadataRevision;
            SelectProgram(variant,second.ProgramId); Assert.Equal(second.ProgramId,OpenGlStateCache.Current.GetCurrentProgram());
            SelectProgram(variant,int.MaxValue); Assert.Equal(ErrorCode.InvalidValue,GL.GetError());
            int location=GL.GetUniformLocation(second.ProgramId,"inputs[0]"); SetUnits(0,second.ProgramId,location,6);
            GL.GetUniform(second.ProgramId,location,out int actual); Assert.Equal(6,actual);
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br+1,b.MetadataRevision);
            Assert.Equal(second.ProgramId,OpenGlStateCache.Current.GetCurrentProgram()); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); units.UnpatchAll(units.Id); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Unsigned and EXT pipeline selectors preserve the actual active-program target after rejected rebinding.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PipelineSelectorsRecoverActualTarget(bool extension)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(SelectPipeline)); var units=Patch(nameof(SetUnits));
        try
        {
            using var program=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            using var first=GpuProgramPipeline.Create(); using var second=GpuProgramPipeline.Create(); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, first.PipelineId);
            GL.ActiveShaderProgram(second.PipelineId,program.ProgramId); OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            SelectPipeline(extension,second.PipelineId); Assert.Equal(second.PipelineId,OpenGlStateCache.Current.GetCurrentProgramPipeline());
            SelectPipeline(extension,int.MaxValue); Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            int location=GL.GetUniformLocation(program.ProgramId,"inputs[0]"); SetUnits(0,program.ProgramId,location,5);
            GL.GetUniform(program.ProgramId,location,out int actual); Assert.Equal(5,actual);
            Assert.Equal(second.PipelineId,OpenGlStateCache.Current.GetCurrentProgramPipeline()); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, 0); units.UnpatchAll(units.Id); harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A real driver binary reinstalls executable facts; an invalid format preserves native executable state.</summary>
    [Fact]
    public unsafe void BinaryReinstallationPublishesActualExecutable()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(nameof(InstallBinary));
        try
        {
            using var source=CreateProgram(out int shader); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            GL.GetProgram(source.ProgramId,GetProgramParameterName.ProgramBinaryLength,out int length); Assert.True(length>0);
            var binary=new byte[length]; BinaryFormat format;
            fixed(byte* pointer=binary) GL.GetProgramBinary(source.ProgramId,length,out length,out format,(IntPtr)pointer);
            using var destination=GpuProgramObject.Create(); var manager=GpuResourceManagerSystem.CurrentManager!;
            using var alias=new ProgramAlias(destination.ProgramId); GpuResourceManager.AcquireWrapperMetadata(alias,GpuProgramMetadataReader.Instance);
            InstallBinary(destination.ProgramId,format,binary,length); Assert.True(alias.Snapshot.ExecutableAvailable); Assert.NotEmpty(alias.Snapshot.Resources);
            var entry=manager.GetResourceRegistration(destination)!; ulong revision=entry.MetadataRevision;
            InstallBinary(destination.ProgramId,(BinaryFormat)int.MaxValue,binary,length);
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError()); GL.GetProgram(destination.ProgramId,GetProgramParameterName.LinkStatus,out int linked);
            Assert.Equal(linked!=0,alias.Snapshot.ExecutableAvailable); Assert.Equal(revision+1,entry.MetadataRevision); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Models a native boundary failure before the command reaches the driver.</summary>
    private static void ThrowDispatch() => throw new ApplicationException("native program dispatch failure");
    /// <summary>Preserves exact unsigned core and ARB selectors for the production transpiler.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SelectProgram(int variant,int program) { if(variant==0) GL.UseProgram((uint)program); else GL.Arb.UseProgramObject(program); }
    /// <summary>Preserves exact unsigned core and EXT pipeline selectors.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SelectPipeline(bool extension,int pipeline) { if(extension) GL.Ext.BindProgramPipeline(pipeline); else GL.BindProgramPipeline((uint)pipeline); }
    /// <summary>Forwards an actual native binary payload unchanged through interception.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void InstallBinary(int program,BinaryFormat format,byte[] binary,int length)
    { fixed(byte* pointer=binary) GL.ProgramBinary(program,format,(IntPtr)pointer,length); }
    private const string Source="#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; uniform int numericValue; layout(std140,binding=0) uniform Parameters { vec4 parameter; }; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){ result=texelFetch(inputs[0],ivec2(0),0)+texelFetch(inputs[1],ivec2(0),0)+vec4(numericValue)+parameter; }";
    /// <summary>Compiles a retained shader so subsequent relinking exercises the same owned native program.</summary>
    private static GpuProgramObject CreateProgram(out int shader,bool prepared=false)
    {
        shader=GL.CreateShader(ShaderType.ComputeShader); GL.ShaderSource(shader,prepared?Source.Replace("uniform int numericValue;","").Replace("+vec4(numericValue)",""):Source); GL.CompileShader(shader);
        int program=GL.CreateProgram(); GL.AttachShader(program,shader); GL.LinkProgram(program);
        GL.GetProgram(program,GetProgramParameterName.LinkStatus,out int linked); Assert.Equal(1,linked);
        return GpuProgramObject.Adopt(program);
    }
    /// <summary>Installs the production command transpiler on a concrete native caller.</summary>
    private static Harmony Patch(string method)
    {
        var harmony=new Harmony("VGE.Tests.ProgramMetadata."+method);
        harmony.Patch(AccessTools.Method(typeof(GpuProgramMetadataInterceptionTests),method),transpiler:new HarmonyMethod(typeof(EngineStateSwitchingHook),nameof(EngineStateSwitchingHook.Transpiler)));
        return harmony;
    }
    /// <summary>Preserves exact scalar, managed-vector, reference and pointer payload signatures.</summary>
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
    /// <summary>Forwards UBO and SSBO assignment through their actual native interfaces.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetBlock(bool storage,int program,int index,int binding)
    { if(storage) GL.ShaderStorageBlockBinding(program,index,binding); else GL.UniformBlockBinding(program,index,binding); }
    /// <summary>Retains a direct native executable-link command for interception.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Link(int program) => GL.LinkProgram(program);
    /// <summary>Receives shared canonical snapshots without owning the program.</summary>
    private sealed class ProgramAlias(int id):GpuResource<GpuProgramMetadata>
    {
        public GpuProgramMetadata Snapshot=>metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }=id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind=>GpuResourceKind.Program;
        /// <inheritdoc />
        protected override bool OwnsResource=>false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}








