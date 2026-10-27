using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks explicit and bound uniform assignment against native values and canonical program metadata.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramUniformMetadataTests(HeadlessGLFixture fixture)
{
    private static int nativeCalls;
    #region Public API
    /// <summary>Sampler units publish metadata while numeric contents preserve revisions and caller program selections.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitAssignmentsPreserveBindingsAndPublishOnlyMetadata(bool direct)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var program = CreateProgram();
            using var sentinel = CreateProgram();
            using var pipeline = GpuProgramPipeline.Create();
            using var capability = new SeparateShaderObjectsCapabilityScope(direct);
            OpenGLProgramPipelineSelection.Bind(StateCache.Current, pipeline.PipelineId);
            OpenGLProgramSelection.Select(StateCache.Current, sentinel.ProgramId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(program));
            int sampler = GL.GetUniformLocation(program.ProgramId, "inputTexture");
            int numeric = GL.GetUniformLocation(program.ProgramId, "numericValue");
            Assert.True(sampler >= 0 && numeric >= 0);
            ulong previous = entry.MetadataRevision;
            OpenGLProgramAssignments.SetUnit(program.ProgramId, sampler, 3);
            GL.GetUniform(program.ProgramId, sampler, out int actualUnit);
            Assert.Equal(3, actualUnit);
            Assert.Equal(previous + 1, entry.MetadataRevision);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(pipeline.PipelineId, GL.GetInteger(GetPName.ProgramPipelineBinding));
            previous = entry.MetadataRevision;
            OpenGLProgramAssignments.SetUnit(program.ProgramId, numeric, 42);
            GL.GetUniform(program.ProgramId, numeric, out int actualNumeric);
            Assert.Equal(42, actualNumeric);
            Assert.Equal(previous, entry.MetadataRevision);
            Assert.Equal(sentinel.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(pipeline.PipelineId, GL.GetInteger(GetPName.ProgramPipelineBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            OpenGLProgramSelection.Select(StateCache.Current, 0);
            OpenGLProgramPipelineSelection.Bind(StateCache.Current, 0);
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Available canonical sampler units suppress stable authored assignments while numeric content and recovery still dispatch.</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void StableCanonicalUnitsAvoidNativeWork(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=new Harmony("VGE.Tests.StableUnits");
        try
        {
            using var program=CreateProgram(); using var capability=new SeparateShaderObjectsCapabilityScope(direct); int sampler=GL.GetUniformLocation(program.ProgramId,"inputTexture"),numeric=GL.GetUniformLocation(program.ProgramId,"numericValue");
            var entry=(GpuResourceMetadataEntry<GpuProgramMetadata>)GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(program)!;
            OpenGLProgramAssignments.SetUnit(program.ProgramId,sampler,3);
            var reader=new CountingReader(entry.Reader!); entry.Reader=reader;
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.Uniform1),[typeof(int),typeof(int)]),prefix:new HarmonyMethod(typeof(GpuProgramUniformMetadataTests),nameof(CountNative)));
            harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL),nameof(GL.ProgramUniform1),[typeof(int),typeof(int),typeof(int)]),prefix:new HarmonyMethod(typeof(GpuProgramUniformMetadataTests),nameof(CountNative)));
            nativeCalls=0; ulong revision=entry.MetadataRevision;
            OpenGLProgramAssignments.SetUnit(program.ProgramId,sampler,3);
            Assert.Equal(0,nativeCalls); Assert.Equal(0,reader.Reads); Assert.Equal(revision,entry.MetadataRevision);
            OpenGLProgramAssignments.SetUnit(program.ProgramId,numeric,3); Assert.Equal(1,nativeCalls); Assert.Equal(0,reader.Reads); Assert.Equal(revision,entry.MetadataRevision);
            entry.BeginRefresh(GpuMetadataChange.Metadata); OpenGLProgramAssignments.SetUnit(program.ProgramId,sampler,3);
            Assert.Equal(2,nativeCalls); Assert.Equal(1,reader.Reads); Assert.True(entry.IsUsable); Assert.True(entry.MetadataRevision>revision); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); OpenGLProgramSelection.Select(StateCache.Current, 0); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Pipeline creation restores selectors and explicit configuration never binds the target pipeline.</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PipelineCreationAndExplicitConfigurationPreserveSelectors(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=new Harmony("VGE.Tests.PipelineExplicit");
        try
        {
            using var program=CreateProgram(); using var sentinel=GpuProgramPipeline.Create(); OpenGLProgramPipelineSelection.Bind(StateCache.Current, sentinel.PipelineId); OpenGLProgramSelection.Select(StateCache.Current, program.ProgramId);
            using var capabilities=new DirectStateAccessCapabilityScope(direct); using var target=GpuProgramPipeline.Create();
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(target); Assert.NotNull(entry); Assert.True(GL.IsProgramPipeline(target.PipelineId));
            Assert.Equal(sentinel.PipelineId,GL.GetInteger(GetPName.ProgramPipelineBinding)); Assert.Equal(program.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            harmony.Patch(AccessTools.Method(typeof(GL),nameof(GL.BindProgramPipeline),[typeof(int)]),prefix:new HarmonyMethod(typeof(GpuProgramUniformMetadataTests),nameof(CountNative))); nativeCalls=0;
            GL.UseProgramStages(target.PipelineId,ProgramStageMask.ComputeShaderBit,program.ProgramId); GL.ActiveShaderProgram(target.PipelineId,program.ProgramId);
            Assert.Equal(0,nativeCalls); Assert.Equal(sentinel.PipelineId,GL.GetInteger(GetPName.ProgramPipelineBinding)); Assert.Equal(program.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            GL.GetProgramPipeline(target.PipelineId,ProgramPipelineParameter.ComputeShader,out int stage); GL.GetProgramPipeline(target.PipelineId,ProgramPipelineParameter.ActiveProgram,out int active); Assert.Equal(program.ProgramId,stage); Assert.Equal(program.ProgramId,active);
            int id=target.PipelineId; VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.ProgramPipeline, id)); Assert.False(target.IsValid); Assert.False(GL.IsProgramPipeline(id)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); OpenGLProgramSelection.Select(StateCache.Current, 0); OpenGLProgramPipelineSelection.Bind(StateCache.Current, 0); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Counts exact observed native dispatches without changing their behavior.</summary>
    private static void CountNative() => nativeCalls++;
    /// <summary>Counts canonical reader calls while retaining native acquisition semantics.</summary>
    private sealed class CountingReader(IGpuResourceMetadataReader<GpuProgramMetadata> inner):IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool IsResource(nint id)=>inner.IsResource(id);
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuProgramMetadata metadata) { Reads++; return inner.TryRead(id,out metadata); }
    }
    /// <summary>Links a small native candidate and transfers its sole deletion ownership into the common manager.</summary>
    private static GpuProgramObject CreateProgram()
    {
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        int program = GL.CreateProgram();
        try
        {
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputTexture; uniform int numericValue; layout(std430,binding=0) buffer Output { vec4 result; }; void main() { result=texelFetch(inputTexture,ivec2(0),0)+vec4(numericValue); }");
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
            GL.ProgramParameter(program,ProgramParameterName.ProgramSeparable,1);
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.DetachShader(program, shader);
            var owner = GpuProgramObject.Adopt(program);
            program = 0;
            return owner;
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            if (program != 0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, program));
        }
    }
    #endregion
}

