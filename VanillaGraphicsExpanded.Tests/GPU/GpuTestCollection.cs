using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Collection definition for GPU tests.
/// All tests in this collection share the same HeadlessGLFixture and run serially.
/// This is necessary because OpenGL contexts are not thread-safe.
/// </summary>
[CollectionDefinition("GPU")]
public class GpuTestCollection : ICollectionFixture<Fixtures.HeadlessGLFixture>
{
    // This class has no code; it's just a marker for the collection definition.
}
