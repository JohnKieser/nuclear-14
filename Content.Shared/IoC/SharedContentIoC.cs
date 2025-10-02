using Content.Shared.Humanoid.Markings;
using Content.Shared.Localizations;
using Content.Shared.Tag;
using Content.Shared.Whitelist;

namespace Content.Shared.IoC;

public static class SharedContentIoC
{

    public static void Register(IDependencyCollection deps)
    {
        deps.Register<MarkingManager, MarkingManager>();
        deps.Register<ContentLocalizationManager, ContentLocalizationManager>();
        deps.Register<TagSystem>();
        deps.Register<EntityWhitelistSystem>();
    }
}

