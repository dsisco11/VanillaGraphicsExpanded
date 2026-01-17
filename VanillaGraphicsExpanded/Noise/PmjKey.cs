namespace VanillaGraphicsExpanded.Noise;

public readonly record struct PmjKey(
    int SampleCount,
    uint Seed,
    PmjVariant Variant,
    bool OwenScramble,
    uint Salt)
{
    public static PmjKey FromConfig(in PmjConfig config)
    {
        // Note: Centering/output kind are conversion concerns and do not affect the canonical sequence.
        return new PmjKey(
            SampleCount: config.SampleCount,
            Seed: config.Seed,
            Variant: config.Variant,
            OwenScramble: config.OwenScramble,
            Salt: config.Salt);
    }
}
