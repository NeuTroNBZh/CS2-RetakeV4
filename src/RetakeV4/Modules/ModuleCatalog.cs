using RetakeV4.Modules.Admin;
using RetakeV4.Modules.Allocation;
using RetakeV4.Modules.Announcements;
using RetakeV4.Modules.Api;
using RetakeV4.Modules.Core;
using RetakeV4.Modules.Hud;
using RetakeV4.Modules.InstaDefuse;
using RetakeV4.Modules.Links;
using RetakeV4.Modules.MapCleanup;
using RetakeV4.Modules.Plant;
using RetakeV4.Modules.RoundTypes;
using RetakeV4.Modules.Spawns;
using RetakeV4.Modules.Teams;

namespace RetakeV4.Modules;

// Every module of the plugin, shared by the plugin itself and the default-config export.
public static class ModuleCatalog
{
    public static IReadOnlyList<IRetakeModule> CreateAll() => new IRetakeModule[]
    {
        new CoreModule(),
        new HudModule(),
        new RoundTypesModule(),
        new TeamsModule(),
        new SpawnsModule(),
        new AllocationModule(),
        new PlantModule(),
        new InstaDefuseModule(),
        new AdminModule(),
        new LinksModule(),
        new AnnouncementsModule(),
        new MapCleanupModule(),
        new ApiModule(),
    };
}
