using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks shared engine-generation mode independently of mutable queued configuration.</summary>
[Collection("CPU state")]
public sealed class PbrShaderLightingModeTests
{
    #region Public API
    /// <summary>Configuration changes wait for reload entry, and the chosen target persists until another generation starts.</summary>
    [Fact]
    public void QueuedChangesDoNotChangeExistingGeneration()
    {
        // Arrange: preserve global configuration and generation state for unconditional cleanup.
        bool originalConfig = ConfigModSystem.Config.LumOn.Enabled;
        bool? originalGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        bool originalSuppression = ShaderRegistry.SupressShaderAndBufferReloads;
        try
        {
            // Act and assert: configuration is queued until reload entry, which establishes each new generation immediately.
            ShaderRegistry.SupressShaderAndBufferReloads = false;
            ConfigModSystem.Config.LumOn.Enabled = false;
            PbrShaderLightingMode.GenerationLumOnEnabled = null;
            Assert.False(PbrShaderLightingMode.LumOnEnabled);
            ConfigModSystem.Config.LumOn.Enabled = true;
            Assert.False(PbrShaderLightingMode.LumOnEnabled);
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            Assert.True(PbrShaderLightingMode.LumOnEnabled);
            // Reload entry establishes the new generation; there is no success-only commit or old-program rollback.
            ConfigModSystem.Config.LumOn.Enabled = false;
            Assert.True(PbrShaderLightingMode.LumOnEnabled);
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            Assert.False(PbrShaderLightingMode.LumOnEnabled);
        }
        finally
        {
            ShaderRegistry.SupressShaderAndBufferReloads = originalSuppression;
            ConfigModSystem.Config.LumOn.Enabled = originalConfig;
            PbrShaderLightingMode.GenerationLumOnEnabled = originalGeneration;
        }
    }

    /// <summary>An engine-suppressed reload keeps both compiled shaders and their lighting mode.</summary>
    [Fact]
    public void SuppressedReloadPreservesExistingGeneration()
    {
        // Arrange: preserve the shared mode state and prepare a suppressed engine reload.
        bool originalConfig = ConfigModSystem.Config.LumOn.Enabled;
        bool? originalGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        bool originalSuppression = ShaderRegistry.SupressShaderAndBufferReloads;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = false;
            ConfigModSystem.Config.LumOn.Enabled = true;
            ShaderRegistry.SupressShaderAndBufferReloads = true;
            // Act and assert: suppressed reload keeps the current generation's lighting mode.
            ShaderDigestReloadHook.Prefix(out _);
            ShaderDigestReloadHook.Finalizer(true);
            Assert.False(PbrShaderLightingMode.LumOnEnabled);
        }
        finally
        {
            ShaderRegistry.SupressShaderAndBufferReloads = originalSuppression;
            ConfigModSystem.Config.LumOn.Enabled = originalConfig;
            PbrShaderLightingMode.GenerationLumOnEnabled = originalGeneration;
        }
    }
    #endregion
}
