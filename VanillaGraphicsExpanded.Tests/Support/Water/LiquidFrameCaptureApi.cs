using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Support.Water;

/// <summary>Builds controlled engine API responses for liquid frame-input capture tests.</summary>
internal static class LiquidFrameCaptureApi
{
    #region Public API
    /// <summary>Returns API adapters exposing the supplied uniforms and projection without creating frame resources.</summary>
    /// <param name="uniforms">The engine uniform snapshot returned to the shader capture path.</param>
    /// <param name="projection">The projection matrix returned by the render API adapter.</param>
    /// <returns>A controlled client API whose render and atlas queries match the capture contract.</returns>
    internal static ICoreClientAPI Create(DefaultShaderUniforms uniforms, float[] projection)
    {
        var render = RuntimeRenderEvents.Adapt<IRenderAPI>((method, _) => method.Name switch
        {
            "get_ShaderUniforms" => uniforms,
            "get_CurrentProjectionMatrix" => projection,
            "get_FrameWidth" => 512,
            "get_FrameHeight" => 512,
            _ => throw new NotSupportedException(method.Name)
        });
        var size = new Size2i(1024, 1024);
        var atlas = RuntimeRenderEvents.Adapt<IBlockTextureAtlasAPI>((method, _) => method.Name == "get_Size"
            ? size : throw new NotSupportedException(method.Name));
        return RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, _) => method.Name switch
        {
            "get_Render" => render,
            "get_BlockTextureAtlas" => atlas,
            _ => throw new NotSupportedException(method.Name)
        });
    }
    #endregion
}
