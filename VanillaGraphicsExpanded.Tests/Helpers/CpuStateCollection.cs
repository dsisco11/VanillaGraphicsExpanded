namespace VanillaGraphicsExpanded.Tests.Helpers;

/// <summary>Serializes CPU tests touching process-wide engine or Harmony state without creating a graphics context.</summary>
[CollectionDefinition("CPU state", DisableParallelization = true)]
public sealed class CpuStateCollection { }
