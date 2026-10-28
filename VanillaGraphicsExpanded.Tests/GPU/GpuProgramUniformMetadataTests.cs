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
    #region Public API
    /// <summary>Content-only and ignored assignments preserve active certification; represented unit changes interrupt it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void IrrelevantUniformWritesDoNotInterruptMetadata(bool core)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var program = CreateProgram();
            GL.UseProgram(program.ProgramId);
            Assert.True(GL.GetError() == ErrorCode.NoError, "UseProgram setup");
            var entry = (GpuResourceMetadataEntry<GpuProgramMetadata>)GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(program)!;
            var snapshot = entry.Metadata;
            var reader = new CountingReader(entry.Reader!);
            entry.Reader = reader;
            int numeric = GL.GetUniformLocation(program.ProgramId, "numericValue");
            int unsigned = GL.GetUniformLocation(program.ProgramId, "unsignedValue");
            int sampler = GL.GetUniformLocation(program.ProgramId, "inputTexture");
            Assert.True(numeric >= 0 && unsigned >= 0 && sampler >= 0);
            ulong revision = entry.MetadataRevision;
            using (var operation = new GpuResourceManager.MetadataOperation<GpuProgramMetadata>(program, GpuMetadataChange.Metadata))
            {
                if (core)
                {
                    OpenTK.Graphics.OpenGL4.GL.Uniform1(numeric, 7);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "core bound signed");
                    OpenTK.Graphics.OpenGL4.GL.Uniform1(unsigned, 9u);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "core bound unsigned");
                    OpenTK.Graphics.OpenGL4.GL.ProgramUniform1(program.ProgramId, numeric, 8);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "core named signed");
                    OpenTK.Graphics.OpenGL4.GL.ProgramUniform1((uint)program.ProgramId, unsigned, 10u);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "core named unsigned");
                    OpenTK.Graphics.OpenGL4.GL.Uniform1(-1, 11);
                }
                else
                {
                    GL.Uniform1(numeric, 7);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "bound signed");
                    GL.Uniform1(unsigned, 9u);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "bound unsigned");
                    GL.ProgramUniform1(program.ProgramId, numeric, 8);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "named signed");
                    GL.ProgramUniform1((uint)program.ProgramId, unsigned, 10u);
                    Assert.True(GL.GetError() == ErrorCode.NoError, "named unsigned");
                    GL.Uniform1(-1, 11);
                }
                Assert.Equal(ErrorCode.NoError, GL.GetError());
                // A count cannot turn a numeric starting location into a unit assignment.
                // GL rejects multiple elements for this non-array uniform; preserve that error.
                GL.Uniform1(numeric, 2, new[] { 1, 2 });
                Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
                // EXT unsigned setters also remain ordinary driver dispatch, with no metadata observer.
                var table = OpenGLBindingTable.For(typeof(GL));
                nint scalar = table.Read("glUniform1uiEXT"), vector = table.Read("glUniform1uivEXT");
                if (scalar != 0) ((delegate* unmanaged[Stdcall]<int, uint, void>)scalar)(unsigned, 12);
                uint value = 13;
                if (vector != 0) ((delegate* unmanaged[Stdcall]<int, int, uint*, void>)vector)(unsigned, 1, &value);
                Assert.Equal(revision, entry.MetadataRevision);
                Assert.Equal(0, reader.Reads);
                Assert.False(entry.CompletionInterrupted);
                operation.Commit(snapshot);
            }
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.True(entry.IsUsable);
            GL.GetUniform(program.ProgramId, numeric, out int actual);
            Assert.Equal(8, actual);
            using (var operation = new GpuResourceManager.MetadataOperation<GpuProgramMetadata>(program, GpuMetadataChange.Metadata))
            {
                GL.Uniform1(sampler, 2);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(snapshot));
            }
            Assert.Equal(0, reader.Reads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.UseProgram(0); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
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
            using var pipeline = NativeResourceSetup.CreateProgramPipeline();
            using var capability = new SeparateShaderObjectsCapabilityScope(direct);
            OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, pipeline.PipelineId);
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, sentinel.ProgramId);
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
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
            OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, 0);
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
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
            using var native = new BindingCallCapture();
            int NativeCalls() => native.Calls(nameof(GL.Uniform1)) + native.Calls(nameof(GL.ProgramUniform1));
            ulong revision=entry.MetadataRevision;
            OpenGLProgramAssignments.SetUnit(program.ProgramId,sampler,3);
            Assert.Equal(0,NativeCalls()); Assert.Equal(0,reader.Reads); Assert.Equal(revision,entry.MetadataRevision);
            OpenGLProgramAssignments.SetUnit(program.ProgramId,numeric,3); Assert.Equal(1,NativeCalls()); Assert.Equal(0,reader.Reads); Assert.Equal(revision,entry.MetadataRevision);
            entry.BeginRefresh(GpuMetadataChange.Metadata); OpenGLProgramAssignments.SetUnit(program.ProgramId,sampler,3);
            Assert.Equal(2,NativeCalls()); Assert.Equal(1,reader.Reads); Assert.True(entry.IsUsable); Assert.True(entry.MetadataRevision>revision); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Pipeline creation restores selectors and explicit configuration never binds the target pipeline.</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PipelineCreationAndExplicitConfigurationPreserveSelectors(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=new Harmony("VGE.Tests.PipelineExplicit");
        try
        {
            using var program=CreateProgram(); using var sentinel=NativeResourceSetup.CreateProgramPipeline(); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, sentinel.PipelineId); OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
            using var capabilities=new DirectStateAccessCapabilityScope(direct); using var target=NativeResourceSetup.CreateProgramPipeline();
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(target); Assert.NotNull(entry); Assert.True(GL.IsProgramPipeline(target.PipelineId));
            Assert.Equal(sentinel.PipelineId,GL.GetInteger(GetPName.ProgramPipelineBinding)); Assert.Equal(program.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            using var native = new BindingCallCapture();
            GL.UseProgramStages(target.PipelineId,ProgramStageMask.ComputeShaderBit,program.ProgramId); GL.ActiveShaderProgram(target.PipelineId,program.ProgramId);
            Assert.Equal(0,native.Calls(nameof(GL.BindProgramPipeline))); Assert.Equal(sentinel.PipelineId,GL.GetInteger(GetPName.ProgramPipelineBinding)); Assert.Equal(program.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            GL.GetProgramPipeline(target.PipelineId,ProgramPipelineParameter.ComputeShader,out int stage); GL.GetProgramPipeline(target.PipelineId,ProgramPipelineParameter.ActiveProgram,out int active); Assert.Equal(program.ProgramId,stage); Assert.Equal(program.ProgramId,active);
            int id=target.PipelineId; VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.ProgramPipeline, id)); Assert.False(target.IsValid); Assert.False(GL.IsProgramPipeline(id)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, 0); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
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
            GL.ShaderSource(shader, "#version 430\nlayout(local_size_x=1) in; uniform sampler2D inputTexture; uniform int numericValue; uniform uint unsignedValue; layout(std430,binding=0) buffer Output { vec4 result; }; void main() { result=texelFetch(inputTexture,ivec2(0),0)+vec4(numericValue+int(unsignedValue)); }");
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

