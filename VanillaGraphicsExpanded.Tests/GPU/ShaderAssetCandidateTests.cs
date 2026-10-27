using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks asset-level cache selection, exact ownership transfer and abandoned RHI candidate cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderAssetCandidateTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Asset preparation persists a ready executable and reuses its opaque bytes for a new owned generation.</summary>
    [Fact]
    public void CacheSelectionRetainsIndependentReadyOwnership()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        string directory = Path.Combine(Path.GetTempPath(), "VGE-ShaderAsset-" + Guid.NewGuid().ToString("N"));
        using var policy = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory));
        using var assets = new BinaryShaderApiFixture();
        try
        {
            var plan = Plan();
            using var first = new ShaderAssetCandidate(plan, Read,
                () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain));
            first.Complete(TestContext.Current.CancellationToken);
            Assert.False(first.Cached);
            using var original = first.TakeShader();
            Assert.Throws<InvalidOperationException>(() => first.TakeShader());
            first.Dispose();
            original.RequireUsable();
            using var second = new ShaderAssetCandidate(plan, Read,
                () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain));
            second.Complete(TestContext.Current.CancellationToken);
            Assert.True(second.Cached);
            using var restored = second.TakeShader();
            Assert.NotEqual(original.Program.ProgramId, restored.Program.ProgramId);
            Assert.Equal(original.Interface.PreparedBindings.Entries.Count, restored.Interface.PreparedBindings.Entries.Count);
            original.Dispose();
            restored.RequireUsable();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>Ready but unconsumed creation remains candidate-owned and is retired when the asset request is abandoned.</summary>
    [Fact]
    public void AbandonedReadyAssetRetiresItsExecutable()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var policy = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        try
        {
            using var candidate = new ShaderAssetCandidate(Plan(), Read,
                () => ShaderDigestIndexCache.ForAssets(assets.Api.Assets, ShaderImportsSystem.DefaultDomain));
            candidate.Complete(TestContext.Current.CancellationToken);
            Assert.True(candidate.IsReady);
            candidate.Dispose();
            Assert.False(candidate.IsReady);
            Assert.Throws<ObjectDisposedException>(() => candidate.TakeShader());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Selects a canonical existing compute contract and its typed defaults.</summary>
    private static ShaderLoadPlan Plan() => new(new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1")));
    /// <summary>Reads the canonical precompiled fixture payload.</summary>
    private static ReadOnlySpan<byte> Read(string path) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path));
    #endregion
}
