namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Represents one independent scalar interval at an original linear texel and channel.</summary>
internal readonly record struct GpuScalarInterval(int Pixel, int Channel, double Lower, double Upper);
