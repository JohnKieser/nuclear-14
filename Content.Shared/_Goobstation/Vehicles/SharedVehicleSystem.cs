using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Melee.Events;


namespace Content.Shared.Vehicles;
/// <summary>
/// misfits current iteration. Depends on entity with vehicle comp having a strap comp.
/// Very basically this just relays input from the strapped entity to the 'vehicle' entity so it moves
/// does this on strap/buckle. Then handles other vehicle things like visuals, engine logic(cant move da car if it isnt on), ect...
/// </summary>

public abstract partial class SharedVehicleSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public static readonly EntProtoId HornActionId = "ActionHorn";
    public static readonly EntProtoId SirenActionId = "ActionSiren";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<VehicleComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<VehicleComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<VehicleComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<VehicleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<VehicleComponent, VirtualItemDeletedEvent>(OnDropped);
        SubscribeLocalEvent<VehicleComponent, MeleeHitEvent>(OnMeleeHit);

        SubscribeLocalEvent<VehicleComponent, EntInsertedIntoContainerMessage>(OnInsert);
        SubscribeLocalEvent<VehicleComponent, EntRemovedFromContainerMessage>(OnEject);

        SubscribeLocalEvent<VehicleComponent, HornActionEvent>(OnHorn);
        SubscribeLocalEvent<VehicleComponent, SirenActionEvent>(OnSiren);
        SubscribeLocalEvent<VehicleComponent, ItemSlotEjectAttemptEvent>(OnItemSlotEject);
        SubscribeLocalEvent<VehicleComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
    }

    private void OnInit(EntityUid uid, VehicleComponent component, ComponentInit args)
    {
        _appearance.SetData(uid, VehicleState.Animated, component.EngineRunning);
        _appearance.SetData(uid, VehicleState.DrawOver, false);
    }

    private void OnMeleeHit(Entity<VehicleComponent> ent, ref MeleeHitEvent args)
    {
        var buckledEnts = Comp<StrapComponent>(ent.Owner).BuckledEntities;
        if (args.User == ent.Comp.Driver || buckledEnts.Contains(args.User))
            args.Handled = true;
    }

    private void OnRemove(EntityUid uid, VehicleComponent component, ComponentRemove args)
    {
        if (component.Driver == null)
            return;
        //_buckle.TryUnbuckle(component.Driver.Value, component.Driver.Value);
        Dismount(component.Driver.Value, (uid,component));
    }
/// <summary>
/// key access logic done here. If given thing inserted into slot was a key then turns on engine
/// </summary>
    private void OnInsert(Entity<VehicleComponent> vehicle, ref EntInsertedIntoContainerMessage args)
    {
        // i dunno what this is for
        //HasComp<InstantActionComponent>(args.Entity) ||
        if (args.Container.ID != vehicle.Comp.KeySlot
            || vehicle.Comp.IsBroken)
            return;

        TurnOnEngine(vehicle);
    }

    private void OnEject(Entity<VehicleComponent> vehicle, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != vehicle.Comp.KeySlot)
            return;
        TurnOffEngine(vehicle);
    }


    private void OnStrapAttempt(Entity<VehicleComponent> ent, ref StrapAttemptEvent args)
    {
        if (ent.Comp.Driver is not null || ent.Comp.RequiredHands == 0)
            return;


        for (var hands = 0; hands < ent.Comp.RequiredHands; hands++)
            if (!_virtualItem.TrySpawnVirtualItemInHand(ent.Owner, args.Buckle.Owner))
            {
                args.Cancelled = true;
                _virtualItem.DeleteInHandsMatching(args.Buckle.Owner, ent.Owner);
                return;
            }
    }

    /// <summary>
    /// Overwrites default buckling behavior
    /// and does calls to handle visuals, virtual items, and adding inputmover
    /// Driver assigned here. If allows for more than one, driver is first to buckle
    /// </summary>
    /// <remarks>
    /// Might alter in the future so buckling behavior from buckle/strap comp
    /// is modifiable enough to not need this
    /// </remarks>
    private void OnStrapped(Entity<VehicleComponent> vehicle, ref StrappedEvent args)
    {
        if (!HasComp<MobMoverComponent>(args.Buckle))
            return;

        args.Moved = true;
        vehicle.Comp.Driver ??= args.Buckle;

        _appearance.SetData(vehicle, VehicleState.DrawOver, true);
        var curSlot = args.Strap.Comp.BuckledEntities.Count;
        _buckle.BuckleToCoord(args.Buckle, args.Strap, args.Strap.Comp.BuckleOffset * curSlot );

        if (vehicle.Comp.Driver != args.Buckle)
            return;
        if (vehicle.Comp.EngineRunning)
        {
            DriverStart(vehicle);
            return;
        }

        if (!vehicle.Comp.RequireKey)
            TurnOnEngine(vehicle);
    }

    /// <summary>
    /// Overwrites default unbuckling behavior
    /// and does calls to handle visuals, virtual items, and removing inputmover
    /// </summary>
    /// <remarks>
    /// Might alter in the future so buckling behavior from buckle/strap comp
    /// is modifiable enough to not need this
    /// </remarks>
    private void OnUnstrapped(Entity<VehicleComponent> vehicle, ref UnstrappedEvent args)
    {
        args.Moved = true;
        _appearance.SetData(vehicle, VehicleState.DrawOver, false);
        _virtualItem.DeleteInHandsMatching(args.Buckle, vehicle);
        _buckle.UnbuckleToPos(args.Buckle,args.Strap, Vector2.Zero);

        if (vehicle.Comp.Driver != args.Buckle)
            return;

        DriverEnd(vehicle);
        vehicle.Comp.Driver = null;

        if (!vehicle.Comp.RequireKey)
            TurnOffEngine(vehicle);
    }

    private void OnDropped(EntityUid uid, VehicleComponent comp, VirtualItemDeletedEvent args)
    {
        //misfits: edge case discovered during server shutdown that virtual item deletion(_virtualItem.DeleteInHandsMatching()) causes event chain where TryDrop is still called
        //          this results in an infinite throwing event loop maybe because of vehicle entity being raised on is in the middle of deletion
         if (TerminatingOrDeleted(uid))
            return;

         Dismount(args.User, (uid,comp), args.User);
    }

    /// default behavior is to only let the driver eject the key
    private static void OnItemSlotEject(EntityUid vehicle, VehicleComponent comp, ref ItemSlotEjectAttemptEvent args)
    {
    if(comp.PreventEjectOfKey && args.Slot.ID == comp.KeySlot && args.User != comp.Driver)
        args.Cancelled = true;
    }

    /// <summary>
    /// For vehicles that trigger doors and maybe other access tied entities on collide.
    /// Will go by the driver's access perms <see cref="AccessReaderSystem"/>
    /// </summary>
    private static void OnGetAdditionalAccess(EntityUid vehicle, VehicleComponent component, ref GetAdditionalAccessEvent args)
    {
        var driver = component.Driver;
        if (driver == null)
            return;

        args.Entities.Add(driver.Value);
    }

    private void OnHorn(EntityUid uid, VehicleComponent component, InstantActionEvent args)
    {
        if (args.Handled || component.Driver != args.Performer || component.HornSound == null)
            return;

        _audio.PlayPvs(component.HornSound, uid);
        args.Handled = true;
    }

    private void OnSiren(EntityUid uid, VehicleComponent comp, InstantActionEvent args)
    {
        if (args.Handled || comp.Driver != args.Performer || comp.SirenSound == null)
            return;

        comp.SirenEnabled = !comp.SirenEnabled;
        comp.SirenStream = comp.SirenEnabled
            ? _audio.Stop(comp.SirenStream)
            : _audio.PlayPvs(comp.SirenSound, uid)?.Entity;
        args.Handled = true;
    }
}


public sealed partial class HornActionEvent : InstantActionEvent;

public sealed partial class SirenActionEvent : InstantActionEvent;
