
namespace VanillaGraphicsExpanded.Tests.Support.Policy;

/// <summary>Prevents singleton PBR readiness changes from overlapping any other test collection.</summary>
[CollectionDefinition("NearFieldMaterialCapture", DisableParallelization = true)]
public sealed class NearFieldMaterialCaptureCollection : ICollectionFixture<HeadlessGLFixture> { }