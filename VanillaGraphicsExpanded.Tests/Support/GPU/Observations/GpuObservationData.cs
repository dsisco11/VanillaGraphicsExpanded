namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Contains verified result transport and its original selected domain.</summary>
internal sealed record GpuObservationData(uint[] Counters, int Width, int Height, int Depth, int Kind, string? ExpectedActual = null);
