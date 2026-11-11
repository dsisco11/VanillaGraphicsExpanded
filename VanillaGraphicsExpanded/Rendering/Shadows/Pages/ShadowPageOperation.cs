namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Bounded GPU table commands; allocation never certifies unfinished depth.</summary>
internal enum ShadowPageOperation : uint
{
    Lookup = 0, Allocate = 1, Publish = 2, Require = 3, RetireKey = 4,
    RetireSource = 5, RetireDimension = 6, RetireAll = 7, Collect = 8, CancelReplacement = 9, FreezeWrites = 10,
    Invalidate = 11
}
