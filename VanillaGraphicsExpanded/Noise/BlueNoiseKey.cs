namespace VanillaGraphicsExpanded.Noise;

public readonly record struct BlueNoiseKey(
    int Width,
    int Height,
    int Slices,
    bool Tileable,
    uint Seed,
    BlueNoiseAlgorithm Algorithm,
    float Sigma,
    float InitialFillRatio,
    int MaxIterations,
    int StagnationLimit)
{
    public static BlueNoiseKey FromConfig(in BlueNoiseConfig config) => new(
        config.Width,
        config.Height,
        config.Slices,
        config.Tileable,
        config.Seed,
        config.Algorithm,
        config.Sigma,
        config.InitialFillRatio,
        config.MaxIterations,
        config.StagnationLimit);
}
