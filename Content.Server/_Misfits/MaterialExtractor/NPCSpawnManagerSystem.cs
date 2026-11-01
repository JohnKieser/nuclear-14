using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems;


namespace Content.Server._Misfits.MaterialExtractor;


public sealed partial class NPCSpawnManagerSystem : EntitySystem
{

    public override void Initialize()
    {
        SubscribeLocalEvent<SpawnChunkComponent,WorldChunkLoadedEvent>(OnChunkLoaded);
        SubscribeLocalEvent<SpawnChunkComponent,SpawnChunkReloaded>(OnChunkReloaded);
        SubscribeLocalEvent<SpawnChunkComponent,WorldChunkUnloadedEvent>(OnChunkUnloaded);

    }
    /// <summary>
    /// redundant indirection incase I wanna change things with WorldControllerSystem
    /// </summary>
    public void OnChunkReloaded(Entity<SpawnChunkComponent> ent, ref SpawnChunkReloaded args)
    {
        var ev = new SpawnChunkInPVS(args.Chunk, args.Coords, args.Loaders);
        RaiseLocalEvent(ent.Owner, ref ev);
    }
    public void OnChunkLoaded(Entity<SpawnChunkComponent> ent, ref WorldChunkLoadedEvent args)
    {
        if (!TryComp<LoadedChunkComponent>(ent, out var comp) || comp.Loaders is null)
            return;

        var ev = new SpawnChunkInPVS(args.Chunk, args.Coords, comp.Loaders);
        RaiseLocalEvent(ent.Owner, ref ev);
    }

    public void OnChunkUnloaded(Entity<SpawnChunkComponent> ent, ref WorldChunkUnloadedEvent args)
    {
        var ev = new SpawnChunkOutsidePVS(args.Chunk, args.Coords);
        RaiseLocalEvent(ent.Owner, ref ev);
    }
}


[ByRefEvent]
public readonly record struct SpawnChunkReloaded(EntityUid Chunk, Vector2i Coords, List<EntityUid> Loaders);
[ByRefEvent]
public readonly record struct SpawnChunkInPVS(EntityUid Chunk, Vector2i Coords, List<EntityUid> Loaders);
[ByRefEvent]
public readonly record struct SpawnChunkOutsidePVS(EntityUid Chunk, Vector2i Coords);
