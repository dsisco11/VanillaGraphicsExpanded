using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks executable candidate ownership, frozen inputs and fixed generation invalidation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHIShaderCreationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Backend extraction and installation preserve the prepared interface without sharing native ownership.</summary>
    [Fact]
    public void ExecutableBinaryRoundTripCreatesAnIndependentGeneration()
    {
        Start();
        try
        {
            var description = RHIShaderInputs.ComputeDescription();
            var retrievable = new RHIShaderDescription(description.Kind, description.Stages, description.Bindings,
                retrievableBinary: true);
            using var original = RHICommandContext.Current.CreateShader(retrievable).RequireResource();
            var binary = RHICommandContext.Current.ExportShaderBinary(original, 16 * 1024 * 1024);
            Assert.NotNull(binary);
            using var restored = RHICommandContext.Current.CreateShader(new(description.Kind, description.Stages,
                description.Bindings, binary)).RequireResource();
            Assert.NotEqual(original.Program.ProgramId, restored.Program.ProgramId);
            Assert.Equal(original.Interface.PreparedBindings.Entries.Count, restored.Interface.PreparedBindings.Entries.Count);
            original.Dispose();
            RHIShaderRef reference = restored;
            Assert.Same(restored, reference.RequireShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    /// <summary>Ready creation retains a prepared interface and rejects use after external executable mutation.</summary>
    [Fact]
    public void ReadyShaderRetainsOneFixedExecutableGeneration()
    {
        Start();
        try
        {
            using var shader = RHICommandContext.Current.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            RHIShaderRef reference = shader;
            Assert.Same(shader, reference.RequireShader());
            Assert.True(shader.Program.MetadataSnapshot.ExecutableAvailable);
            GL.GetProgram(shader.Program.ProgramId, GetProgramParameterName.AttachedShaders, out int attached);
            Assert.Equal(0, attached);
            // External relinking invalidates the captured executable even if a numeric name stays unchanged.
            OpenGLProgramCalls.ExecuteCoreLinkProgram(shader.Program.ProgramId);
            Assert.Throws<InvalidOperationException>(() => reference.RequireShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    /// <summary>Pending cancellation cannot expose an owner; successful completion transfers that owner exactly once.</summary>
    [Fact]
    public void PendingCreationCompletesOrCancelsWithoutExposingPartialOwnership()
    {
        Start();
        try
        {
            using var pending = RHICommandContext.Current.BeginCreateShader(RHIShaderInputs.ComputeDescription());
            Assert.False(pending.Result.IsSuccess && pending.Status != CompletionStatus.Ready);
            if (pending.Status == CompletionStatus.Pending)
            {
                pending.Cancel();
                Assert.Equal(CompletionStatus.Cancelled, pending.TryComplete());
                Assert.False(pending.Result.IsSuccess);
                pending.Dispose();
            }
            else using (pending.Result.RequireResource()) Assert.Equal(CompletionStatus.Ready, pending.Status);
            using var completed = RHICommandContext.Current.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
            RHIShaderRef reference = completed;
            completed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reference.RequireShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Private
    /// <summary>Starts an isolated native/manager interval for candidate ownership checks.</summary>
    private void Start() { fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); OpenGlStateCache.Current.InvalidateAll(); }
    /// <summary>Drains original-context ownership and clears fixture-external selector knowledge.</summary>
    private void Finish() { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    #endregion
}
