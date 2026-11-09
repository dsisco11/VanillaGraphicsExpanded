using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Qualifies observed executable lifetimes and supplied assignment publication through real native hooks.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuExecutableLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Creation and assignments
    /// <summary>Native factories publish complete creation facts and pipeline realization preserves its reserved identity.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoriesMaintainDefaultsWithoutDiscovery(bool core)
    {
        // Arrange: Enable executable observation and choose one generated binding root for native object factories.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        int shader = 0, program = 0, pipeline = 0;
        try
        {
            using var reads = new ExecutableDescriptorCapture();
            // Act: Create shader, program, and reserved pipeline names, then perform the pipeline's first-use bind.
            shader = core ? OpenTK.Graphics.OpenGL4.GL.CreateShader(OpenTK.Graphics.OpenGL4.ShaderType.ComputeShader) : GL.CreateShader(ShaderType.ComputeShader);
            program = core ? OpenTK.Graphics.OpenGL4.GL.CreateProgram() : GL.CreateProgram();
            pipeline = core ? OpenTK.Graphics.OpenGL4.GL.GenProgramPipeline() : GL.GenProgramPipeline();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            // Assert: Factories publish kind defaults and pipeline realization preserves its original registration without descriptor reads.
            var stage = Assert.IsType<GpuResourceMetadataEntry<GpuShaderMetadata>>(manager.FindResource(GpuResourceKind.Shader, shader));
            Assert.True(stage.IsUsable); Assert.Equal(ShaderStage.Compute, stage.Metadata.Type);
            var executable = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(manager.FindResource(GpuResourceKind.Program, program));
            Assert.True(executable.IsUsable); Assert.False(executable.Metadata.ExecutableAvailable);
            Assert.Empty(executable.Metadata.Resources); Assert.Empty(executable.Metadata.TextureUnits);
            var reserved = Assert.IsType<GpuResourceMetadataEntry<GpuProgramPipelineMetadata>>(manager.FindResource(GpuResourceKind.ProgramPipeline, pipeline));
            Assert.False(reserved.IsRealized); Assert.False(reserved.IsUsable);
            if (core) OpenTK.Graphics.OpenGL4.GL.BindProgramPipeline(pipeline); else GL.BindProgramPipeline(pipeline);
            Assert.Same(reserved, manager.FindResource(GpuResourceKind.ProgramPipeline, pipeline));
            Assert.True(reserved.IsUsable); Assert.True(reserved.IsRealized);
            Assert.Equal(0, reads.StageReads); Assert.Equal(0, reads.InterfaceReads); Assert.Equal(0, reads.UnitReads);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Program, int.MaxValue, GpuProgramMetadataReader.Instance));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindProgramPipeline(0);
            if (pipeline != 0) GL.DeleteProgramPipeline(pipeline);
            if (program != 0) GL.DeleteProgram(program);
            if (shader != 0) GL.DeleteShader(shader);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>Array values publish from pinned arguments; rejected values and withdrawn executables never trigger reflection.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AssignmentsAreQueryFreeAndCannotRecoverUnavailableExecutables(bool core, bool activeTail)
    {
        // Arrange: Compile a program with an optional active sampler-array tail and retain its initial executable snapshot.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.ComputeShader), program = GL.CreateProgram();
        try
        {
            string source = "#version 450\nlayout(local_size_x=1) in; uniform sampler2D inputs[2]; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){result=texelFetch(inputs[0],ivec2(0),0)"
                + (activeTail ? "+texelFetch(inputs[1],ivec2(0),0)" : "") + ";}";
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader); GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            int location = GL.GetUniformLocation(program, "inputs[0]");
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Program, program));
            Assert.True(entry.IsUsable); Assert.True(entry.Metadata.ExecutableAvailable);
            using var reads = new ExecutableDescriptorCapture();
            ulong revision = entry.MetadataRevision;
            // Act: Assign two units, reject an invalid unit, then withdraw the executable before another assignment.
            if (core) OpenTK.Graphics.OpenGL4.GL.ProgramUniform1(program, location, 2, new[] { 3, 5 });
            else GL.ProgramUniform1(program, location, 2, new[] { 3, 5 });
            // Assert: Accepted values publish once, rejection preserves revision, and unavailable state triggers no reflection.
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Contains(entry.Metadata.TextureUnits, value => value.Location == location && value.Unit == 3);
            if (activeTail) Assert.Contains(entry.Metadata.TextureUnits, value => value.Location == location + 1 && value.Unit == 5);
            revision = entry.MetadataRevision;
            GL.ProgramUniform1(program, location, int.MaxValue);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(revision, entry.MetadataRevision);
            entry.BeginRefresh(GpuMetadataChange.Executable);
            GL.ProgramUniform1(program, location, 4);
            Assert.False(entry.IsUsable); Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(0, reads.StageReads); Assert.Equal(0, reads.InterfaceReads); Assert.Equal(0, reads.UnitReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(shader); fixture.CleanupGpuResources(); }
    }

    /// <summary>Every specified pipeline initialization boundary realizes the same generated name without stage discovery.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PipelineRealizationAliasesPreserveOriginalIdentity(int operation)
    {
        // Arrange: Reserve a pipeline name and observe descriptor reads before selecting a realization operation.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = GL.GenProgramPipeline();
        try
        {
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = manager.FindResource(GpuResourceKind.ProgramPipeline, id)!;
            using var reads = new ExecutableDescriptorCapture();
            // Act: Realize through one stage, active-program, validation, or status-query boundary.
            if (operation == 0) GL.UseProgramStages(id, 0, 0);
            else if (operation == 1) GL.ActiveShaderProgram(id, 0);
            else if (operation == 2) GL.ValidateProgramPipeline(id);
            else GL.GetProgramPipeline(id, ProgramPipelineParameter.ActiveProgram, out _);
            // Assert: All operations retain the reserved identity and avoid stage/interface discovery.
            Assert.Same(original, manager.FindResource(GpuResourceKind.ProgramPipeline, id));
            Assert.True(original.IsRealized); Assert.True(original.IsUsable);
            Assert.Equal(0, reads.StageReads); Assert.Equal(0, reads.InterfaceReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteProgramPipeline(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Independent program and pipeline namespaces cannot reject a valid pipeline selection.</summary>
    [Fact]
    public void EqualNumericProgramAndPipelineNamesKeepIndependentSelection()
    {
        // Arrange: Ensure a program receipt shares the pipeline's numeric name without requiring driver allocator reuse.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = GL.GenProgramPipeline();
        try
        {
            var manager = RHIResourceManagerSystem.CurrentManager!;
            // Supply a deterministic program-factory receipt with the same number; the
            // driver allocators need not happen to reuse it during this test interval.
            if (manager.FindResource(GpuResourceKind.Program, id) is null)
                manager.ObserveExecutableCreation(GpuResourceKind.Program, id);
            // Act: Bind the pipeline name while a same-numbered program registration exists.
            GL.BindProgramPipeline(id);
            // Assert: Pipeline selection records the value in its own namespace and succeeds natively.
            Assert.Equal(id, OpenGlStateCache.Current.ProgramPipelineSelectionKnowledge);
            Assert.Equal(id, GL.GetInteger(GetPName.ProgramPipelineBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindProgramPipeline(0); GL.DeleteProgramPipeline(id); fixture.CleanupGpuResources(); }
    }

    #endregion
    #region Completion and admission
    /// <summary>A native completion query cannot take publication ownership from a pending RHI candidate.</summary>
    [Fact]
    public void NativeStatusQueryPreservesOwnedPendingCompletion()
    {
        // Arrange: Submit an owned pending executable candidate through the backend.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            var backend = new OpenGLBackend(OpenGlStateCache.Current);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            backend.SubmitProgramExecutable(owner);
            ulong revision = entry.MetadataRevision;
            // Act: Query native link status before the owner completes publication.
            GL.GetProgram(owner.ProgramId, GetProgramParameterName.LinkStatus, out _);
            // Assert: The query cannot publish the pending candidate; backend completion remains authoritative.
            Assert.False(entry.IsUsable); Assert.Equal(revision, entry.MetadataRevision);
            Assert.False(backend.CompleteProgramLink(owner, out _));
            Assert.True(entry.IsUsable); Assert.False(entry.Metadata.ExecutableAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>ARB floating-point completion output establishes the same failed-link snapshot without another submission.</summary>
    [Fact]
    public unsafe void ArbFloatStatusCompletesExternalExecutable()
    {
        // Arrange: Create an external program and install an ARB float-output alias backed by the core status getter.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int program = GL.CreateProgram();
        try
        {
            using var capture = new NativeDriverCapture();
            nint original = capture.Original("glGetProgramiv");
            // This deterministic alias boundary forwards the explicit caller query to its
            // equivalent core getter. It does not claim native optional ARB execution.
            capture.Add("glGetObjectParameterfvARB", new FloatProgramOutput((name, parameter, output) =>
            {
                int value = 0;
                ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)original)(name, parameter, &value);
                *output = value;
            }));
            capture.Install();
            WithoutCachedCapabilities(() => GL.LinkProgram(program));
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Program, program));
            Assert.False(entry.IsUsable);
            var query = (delegate* unmanaged[Stdcall]<uint, int, float*, void>)OpenGLBindingTable.For(typeof(GL)).Read("glGetObjectParameterfvARB");
            float linked = -1;
            // Act: Link without cached capability admission, then query status through the observed ARB alias.
            query(unchecked((uint)program), (int)GetProgramParameterName.LinkStatus, &linked);
            // Assert: Failed-link output completes the external executable snapshot without another submission.
            Assert.Equal(0, linked);
            Assert.True(entry.IsUsable); Assert.False(entry.Metadata.ExecutableAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteProgram(program); fixture.CleanupGpuResources(); }
    }

    /// <summary>External relinking invalidates the original pending request before reflection can advance a revision.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void UncompletedExternalLinkCannotBecomeOwnedCandidate(int failedCompletion)
    {
        // Arrange: Submit an owned candidate, then relink externally so its original completion receipt is interrupted.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = NativeResourceSetup.CreateProgram();
            var backend = new OpenGLBackend(OpenGlStateCache.Current);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner));
            backend.SubmitProgramExecutable(owner);
            ulong revision = entry.MetadataRevision;
            WithoutCachedCapabilities(() => GL.LinkProgram(owner.ProgramId));
            Assert.True(entry.CompletionInterrupted); Assert.Equal(revision, entry.MetadataRevision);
            // Act: Optionally fail status acquisition, then ask the original backend owner to complete the interrupted link.
            if (failedCompletion != 0)
            {
                entry.Reader = new RejectedProgramOutput(failedCompletion == 2);
                GL.GetProgram(owner.ProgramId, GetProgramParameterName.LinkStatus, out _);
                Assert.True(entry.CompletionInterrupted); Assert.Equal(revision, entry.MetadataRevision);
            }
            using var reads = new ExecutableDescriptorCapture();
            // Assert: Interrupted output stays unusable and no reflection query can turn it into an owned candidate.
            Assert.False(backend.CompleteProgramLink(owner, out _));
            Assert.False(entry.IsUsable);
            Assert.Equal(0, reads.ProgramReads); Assert.Equal(0, reads.InterfaceReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Missing cached limits withdraw a represented update without initializing capabilities or rereading units.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PreAdmissionUnitChangesWithdrawCertification(bool core)
    {
        // Arrange: Compile an executable with one sampler unit and observe reflection before capability registration.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.ComputeShader), program = GL.CreateProgram();
        try
        {
            GL.ShaderSource(shader, "#version 450\nlayout(local_size_x=1) in; uniform sampler2D inputTexture; layout(std430,binding=0) buffer Output { vec4 result; }; void main(){result=texelFetch(inputTexture,ivec2(0),0);}");
            GL.CompileShader(shader); GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.Equal(1, linked);
            int location = GL.GetUniformLocation(program, "inputTexture");
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Program, program));
            Assert.True(entry.IsUsable);
            using var reads = new ExecutableDescriptorCapture();
            // Act: Change the sampler unit through either binding root while cached capability admission is absent.
            WithoutCachedCapabilities(() =>
            {
                if (core) OpenTK.Graphics.OpenGL4.GL.ProgramUniform1(program, location, 3);
                else GL.ProgramUniform1(program, location, 3);
                Assert.False(GpuSupport.HasCachedCapabilitiesForCurrentContext);
            });
            // Assert: Certification is withdrawn without initializing capabilities or reading executable units.
            Assert.False(entry.IsUsable);
            Assert.Equal(0, reads.UnitReads); Assert.Equal(0, reads.InterfaceReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(shader); fixture.CleanupGpuResources(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Preserves the legacy ARB floating-point object parameter ABI.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private unsafe delegate void FloatProgramOutput(uint program, int parameter, float* output);
    /// <summary>Models a missing admission snapshot while preserving the native context and original resource identity.</summary>
    private static void WithoutCachedCapabilities(Action action)
    {
        var field = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "cachedRegistration");
        object? original = field.GetValue(null);
        field.SetValue(null, ((nint)0, 0L));
        try { action(); }
        finally { field.SetValue(null, original); }
    }

    /// <summary>Models unavailable or contained failed completion output without inventing a replacement executable.</summary>
    private sealed class RejectedProgramOutput(bool fail) : IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuProgramMetadata metadata)
        {
            metadata = default;
            if (fail) throw new InvalidOperationException("Unavailable executable output.");
            return false;
        }
    }
    #endregion
}
