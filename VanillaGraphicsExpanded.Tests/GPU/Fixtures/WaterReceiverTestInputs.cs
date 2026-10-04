namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Authors synthetic receiver pairs with the publication contract exercised separately by composite GPU tests.</summary>
internal static class WaterReceiverTestInputs
{
    #region Public API
    /// <summary>Marks unavailable or unrepresentable color samples with the full-source sky-depth sentinel.</summary>
    internal static void EncodeDepthValidity(float[] colors, float[] depths, int depthChannels = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depthChannels);
        if (colors.Length % 4 != 0 || depths.Length != colors.Length / 4 * depthChannels)
            throw new ArgumentException("Receiver color and depth arrays must describe the same pixels.");
        // Preserve authored depth/provenance defects for receiver validation tests;
        // this only reproduces the producer's color eligibility signal in depth.
        for (int pixel = 0; pixel < colors.Length / 4; pixel++)
        {
            bool valid = colors[pixel * 4 + 3] >= .5f;
            for (int channel = 0; channel < 4; channel++)
            {
                float value = colors[pixel * 4 + channel];
                valid &= float.IsFinite(value) && MathF.Abs(value) <= 65504f;
            }
            if (!valid) depths[pixel * depthChannels] = 1;
        }
    }
    #endregion
}
