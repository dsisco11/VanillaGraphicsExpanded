namespace VanillaGraphicsExpanded.PBR;

/// <summary>Assigns current deferred receiver textures without borrowing native cascade resources.</summary>
public sealed partial class DirectLightingRenderer
{
    #region Private
    /// <summary>Retains receiver inputs; shared surface bindings supply the coherent light and readiness publication.</summary>
    private void AssignInputs(PBRDirectLightingShaderProgram shader)
    {
        shader.PrimaryScene = gBufferManager.PrimaryColorTexture;
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, gBufferManager.PrimaryDepthTexture);
        shader.GBufferSurface = gBufferManager.SurfaceTexture;
        shader.GBufferPosition = gBufferManager.PositionTexture;
    }
    #endregion
}
