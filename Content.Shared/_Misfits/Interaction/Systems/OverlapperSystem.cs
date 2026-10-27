using Content.Shared._Misfits.Interaction.Components;
using Content.Shared.Coordinates;
using Content.Shared.Destructible;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;


namespace Content.Shared._Misfits.Interaction.Systems;


public sealed partial class OverlapperSystem : EntitySystem
{
    [Dependency] private SharedInteractionSystem _interact = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedDestructibleSystem _destroy = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OverlapperComponent, EntParentChangedMessage> (OnParentChange, after: new[]{typeof(SharedTransformSystem)});
        SubscribeLocalEvent<OverlapperComponent, ComponentShutdown>(OnCompShutdown);
        SubscribeLocalEvent<OverlapperComponent,InRangeOverrideEvent>(OnRangeChild);
        SubscribeLocalEvent<OverlapperComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OverlapParentComponent,InRangeOverrideEvent>(OnRangeParent);

    }
    public void OnCompShutdown(Entity<OverlapperComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Parent == EntityUid.Invalid)
            return;
        RemComp<OverlapParentComponent>(ent.Comp.Parent);
    }
    public void OnParentChange(Entity<OverlapperComponent> ent, ref EntParentChangedMessage args)
    {
        if (TerminatingOrDeleted(ent) || ent.Comp.Parent == EntityUid.Invalid ||
            ent.Comp.Parent == Transform(ent).ParentUid)
            return;
        _destroy.DestroyEntity(ent);
    }

    public void OnMapInit(Entity<OverlapperComponent> ent, ref MapInitEvent args)
    {
        var overlapCoords = ent.Owner.ToCoordinates();
        if (!overlapCoords.TryGetTileRef(out var tileRef) || tileRef.Value.IsSpace() ||
            !TryComp<MapGridComponent>(tileRef.Value.GridUid, out var gridComp))
        {
            RemComp<OverlapperComponent>(ent);
            return;
        }

        var (overlapLayer, _) = _physics.GetHardCollision(ent);
        var anchored = _map.GetAnchoredEntities(tileRef.Value.GridUid, gridComp, tileRef.Value.GridIndices);
        foreach (var entToParent in anchored)
        {
            var (layer, _) = _physics.GetHardCollision(entToParent);
            if (layer != overlapLayer || entToParent == ent.Owner)
                continue;

            ent.Comp.Parent = entToParent;
            _xform.SetParent(ent, entToParent);
            var parentComp = EnsureComp<OverlapParentComponent>(ent);

            DebugTools.Assert(Transform(ent).ParentUid != tileRef.Value.GridUid ||
                Transform(ent).ParentUid != _xform.GetMap(Transform(ent).Coordinates));
            return;
        }

        RemComp<OverlapperComponent>(ent);
    }
    public bool IgnoreParent(EntityUid uid)
    {
        return HasComp<OverlapParentComponent>(uid);
    }
    private void OnRangeChild(Entity<OverlapperComponent> ent, ref InRangeOverrideEvent args)
    {
        if (args.InRange || ent.Comp.Parent == EntityUid.Invalid)
            return;

        var(layer, mask) = _physics.GetHardCollision(Transform(ent.Owner).ParentUid);
        args.InRange = _interact.InRangeUnobstructed(args.User, args.Target, args.Range,(CollisionGroup) mask,IgnoreParent);
        args.Handled = args.InRange;
    }

    private void OnRangeParent(Entity<OverlapParentComponent> ent, ref InRangeOverrideEvent args)
    {
        args.InRange = false;
        args.Handled = true;
    }
}
