using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

internal interface ILiveConfigurable
{
    void OnConfigReloaded(ICoreAPI api);
}
