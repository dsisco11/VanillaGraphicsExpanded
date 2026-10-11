namespace VanillaGraphicsExpanded.PBR.Liquids;
/// <summary>Exposes typed pool inputs to the integration translator.</summary>
internal sealed partial class LiquidShaderProgram
{
    #region Private
    /// <summary>Stages the camera-relative origin through retained draw storage.</summary>
    System.Numerics.Vector3 ILiquidPoolInputs.Origin { set => Origin = value; }
    /// <summary>Stages model-view transforms through the existing frame-aware conversion.</summary>
    float[] ILiquidPoolInputs.ModelViewMatrix { set => ModelViewMatrix = value; }
    /// <summary>Stages preview transparency through retained draw storage.</summary>
    float ILiquidPoolInputs.ForcedTransparency { set => ForcedTransparency = value; }
    #endregion
}
