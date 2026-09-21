using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Prevents singleton PBR readiness changes from overlapping any other test collection.</summary>
[CollectionDefinition("LocalTraceMaterialCapture", DisableParallelization = true)]
public sealed class LocalTraceMaterialCaptureCollection : ICollectionFixture<HeadlessGLFixture> { }