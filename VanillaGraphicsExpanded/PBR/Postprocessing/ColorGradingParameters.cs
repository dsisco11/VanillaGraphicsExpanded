namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Names the display transform controls independently of uniform storage.</summary>
/// <param name="Gamma">Relative native gamma control.</param>
/// <param name="ExtraGamma">Additional authored gamma adjustment.</param>
/// <param name="Brightness">Linear grading brightness control.</param>
/// <param name="Contrast">Additional authored contrast.</param>
internal readonly record struct ColorGradingParameters(float Gamma, float ExtraGamma, float Brightness, float Contrast);
