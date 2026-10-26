namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Describes optional explicit semaphore import; omission leaves an unimported native semaphore.</summary>
public readonly record struct RHISemaphoreDescription(RHIExternalHandle? Import = null);
