namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Represents one independent scalar interval at an original linear texel and channel.</summary>
internal readonly record struct GpuScalarInterval(int Pixel, int Channel, double Lower, double Upper);
