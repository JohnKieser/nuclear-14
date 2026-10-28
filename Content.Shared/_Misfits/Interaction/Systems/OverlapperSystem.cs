using System.Runtime.CompilerServices;
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
    [Robust.Shared.IoC.Dependency] private SharedInteractionSystem _interact = default!;
    [Robust.Shared.IoC.Dependency] private SharedPhysicsSystem _physics = default!;
    [Robust.Shared.IoC.Dependency] private SharedMapSystem _map = default!;
    [Robust.Shared.IoC.Dependency] private SharedTransformSystem _xform = default!;
    [Robust.Shared.IoC.Dependency] private SharedDestructibleSystem _destroy = default!;
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
        if (ent.Comp.Parent == EntityUid.Invalid || !HasComp<OverlapParentComponent>(ent.Comp.Parent))
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
    /// <summary>
    /// Boilerplate for setCoordinate to get all the needed params.
    /// Would just use setParent, but it unanchors the overlapper.
    /// I looked but there doesnt seem? to be an 'in-engine' or current method to do this
    /// </summary>
    /// <param name="ent"></param>
    /// <param name="parent"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetOverlapperParent(EntityUid ent, EntityUid parent)
    {
        var entXform = Transform(ent);
        var oldParentXform = Transform(entXform.ParentUid);

        Entity<TransformComponent, MetaDataComponent> entity = (ent, entXform, MetaData(ent));
        var parentXform = Transform(parent);
        var newRot = _xform.GetWorldRotation(parent);
        var newCoords = parentXform.Coordinates;

        _xform.SetCoordinates(entity, newCoords, newRot, false,parentXform,oldParentXform);
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
            if (layer != overlapLayer || entToParent == ent.Owner || !entToParent.Valid)
                continue;
            SetOverlapperParent(ent, entToParent);
            ent.Comp.Parent = entToParent;
            EnsureComp<OverlapParentComponent>(entToParent);
            Dirty(ent);
            DebugTools.Assert(HasComp<OverlapParentComponent>(entToParent));
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

        var(layer, mask) = _physics.GetHardCollision(ent);
        // calling the normal method results in infinite event call loop
        var xform = Transform(ent);
        var coord = xform.Coordinates;
        var rot = xform.LocalRotation;
        args.InRange = _interact.InRangeUnobstructed(args.User, args.Target,coord, rot, args.Range,(CollisionGroup) mask,IgnoreParent);
        args.Handled = args.InRange;
    }

    private void OnRangeParent(Entity<OverlapParentComponent> ent, ref InRangeOverrideEvent args)
    {
        args.InRange = false;
        args.Handled = true;
    }
}
