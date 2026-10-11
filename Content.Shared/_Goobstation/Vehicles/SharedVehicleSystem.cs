using System.Runtime.CompilerServices;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Melee.Events;


namespace Content.Shared.Vehicles;

public abstract partial class SharedVehicleSystem : EntitySystem
{
    [Robust.Shared.IoC.Dependency] private AccessReaderSystem _access = default!;
    [Robust.Shared.IoC.Dependency] private SharedActionsSystem _actions = default!;
    [Robust.Shared.IoC.Dependency] private SharedAmbientSoundSystem _ambientSound = default!;
    [Robust.Shared.IoC.Dependency] private SharedAppearanceSystem _appearance = default!;
    [Robust.Shared.IoC.Dependency] private SharedAudioSystem _audio = default!;
    [Robust.Shared.IoC.Dependency] private SharedBuckleSystem _buckle = default!;
    [Robust.Shared.IoC.Dependency] private SharedMoverController _mover = default!;
    [Robust.Shared.IoC.Dependency] private SharedVirtualItemSystem _virtualItem = default!;
    [Robust.Shared.IoC.Dependency] private SharedTransformSystem _transform = default!;

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
        SubscribeLocalEvent<BuckleComponent, MobStateChangedEvent>(OnRiderMobStateChanged);

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
        if (args.User == ent.Comp.Driver) // Don't hit your own vehicle
            args.Handled = true;
    }

    // #Cythisiax Fixed - Revert PR #1103: projectiles no longer pass through a ridden vehicle's fixture,
    // so the bike blocks shots and takes full damage again.
    private void OnRiderMobStateChanged(Entity<BuckleComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive ||
            ent.Comp.BuckledTo is not { } strap ||
            !HasComp<VehicleComponent>(strap))
        {
            return;
        }

        _buckle.Unbuckle((ent.Owner, ent.Comp), null);
    }

    private void OnRemove(EntityUid uid, VehicleComponent component, ComponentRemove args)
    {
        if (component.Driver == null)
            return;
        //_buckle.TryUnbuckle(component.Driver.Value, component.Driver.Value);
        Dismount(component.Driver.Value, (uid,component));
    }

    private void OnInsert(Entity<VehicleComponent> vehicle, ref EntInsertedIntoContainerMessage args)
    {
        if (HasComp<InstantActionComponent>(args.Entity)
            || args.Container.ID != vehicle.Comp.KeySlot
            || vehicle.Comp.IsBroken)
            return;

        TurnOnEngine(vehicle);


    }

    public void TurnOnEngine(Entity<VehicleComponent> vehicle)
    {
        vehicle.Comp.EngineRunning = true;
        _appearance.SetData(vehicle.Owner, VehicleState.Animated, true);
        _ambientSound.SetAmbience(vehicle.Owner, true);


        if (vehicle.Comp.Driver is null)
            return;
        DriverStart(vehicle.Comp.Driver.Value, vehicle);
    }
    private void TurnOffEngine(Entity<VehicleComponent> vehicle)
    {
        vehicle.Comp.EngineRunning = false;
        _appearance.SetData(vehicle.Owner, VehicleState.Animated, false);
        _ambientSound.SetAmbience(vehicle.Owner, false);
    }
    private void OnEject(Entity<VehicleComponent> vehicle, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != vehicle.Comp.KeySlot)
            return;
        TurnOffEngine(vehicle);
        // if (uid.Comp.Driver == null)
        //    return;

       // Dismount(uid.Comp.Driver.Value, uid);
    }

    private void OnHorn(EntityUid uid, VehicleComponent component, InstantActionEvent args)
    {
        if (args.Handled == true)
            return;

        if (component.Driver != args.Performer)
            return;

        if (component.HornSound == null)
            return;

        _audio.PlayPvs(component.HornSound, component.Owner);
        args.Handled = true;
    }

    private void OnSiren(EntityUid uid, VehicleComponent component, InstantActionEvent args)
    {
        if (args.Handled == true)
            return;

        if (component.Driver != args.Performer)
            return;

        if (component.SirenSound == null)
            return;

        if (component.SirenEnabled)
        {
            component.SirenStream = _audio.Stop(component.SirenStream);
        }
        else
        {
            component.SirenStream = _audio.PlayPvs(component.SirenSound, uid)?.Entity;
        }

        component.SirenEnabled = !component.SirenEnabled;
        args.Handled = true;
    }


    private void OnStrapAttempt(Entity<VehicleComponent> ent, ref StrapAttemptEvent args)
    {
        if (ent.Comp.Driver is not null || ent.Comp.RequiredHands == 0)
        {
           // args.Cancelled = true;
            return;
        }

        for (var hands = 0; hands < ent.Comp.RequiredHands; hands++)
            if (!_virtualItem.TrySpawnVirtualItemInHand(ent.Owner, args.Buckle.Owner))
            {
                args.Cancelled = true;
                _virtualItem.DeleteInHandsMatching(args.Buckle.Owner, ent.Owner);
                return;
            }
    }

    private void OnStrapped(Entity<VehicleComponent> ent, ref StrappedEvent args)
    {

        if (!HasComp<MobMoverComponent>(args.Buckle) || !ent.Comp.EngineRunning)
            return;

        ent.Comp.Driver ??= args.Buckle;
        _appearance.SetData(ent, VehicleState.DrawOver, true);

        if (ent.Comp.Driver == args.Buckle)
            DriverStart(args.Buckle, ent);

        var curSlot = args.Strap.Comp.BuckledEntities.Count;
        _buckle.BuckleToCoord(args.Buckle, args.Strap, args.Strap.Comp.BuckleOffset * curSlot );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetupOverlay(Entity<VehicleComponent> ent)
    {
        if (ent.Comp.OverlayPrototype == null)
            return;
        var overlay = Spawn(ent.Comp.OverlayPrototype, Transform(ent).Coordinates);
        _transform.SetParent(overlay, ent);
        _transform.SetLocalPosition(overlay, Vector2.Zero);
        _transform.SetLocalRotation(overlay, Angle.Zero);
        ent.Comp.ActiveOverlay = overlay;
    }

    private void OnUnstrapped(Entity<VehicleComponent> vehicle, ref UnstrappedEvent args)
    {
        Dismount(args.Strap,args.Buckle,vehicle.Comp);
        args.Moved = true;
    }

    private void OnDropped(EntityUid uid, VehicleComponent comp, VirtualItemDeletedEvent args)
    {
          // if (comp.Driver != args.User)
         //   return;
        //var user = (args.User, Comp<>(args.User)
        Dismount(args.User, (uid,comp), args.User);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddVehicleActions(EntityUid driver, Entity<VehicleComponent> vehicle)
    {
        _actions.AddAction(driver, ref vehicle.Comp.HornAction, HornActionId, vehicle);
        _actions.AddAction(driver, ref vehicle.Comp.SirenAction, SirenActionId, vehicle);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveVehicleActions(EntityUid driver, Entity<VehicleComponent> vehicle)
    {
        _actions.RemoveAction(driver, vehicle.Comp.HornAction);
        _actions.RemoveAction(driver, vehicle.Comp.SirenAction);
    }

    private void DriverStart(EntityUid driver, Entity<VehicleComponent>  vehicle)
    {
        AddVehicleActions(driver, vehicle);
        SetupOverlay(vehicle);
        _mover.SetRelay(driver, vehicle);
        // if (TryComp<AccessComponent>(vehicle, out var accessComp))
        // {
        //     var accessSources = _access.FindPotentialAccessItems(driver);
        //     var access = _access.FindAccessTags(driver, accessSources);
        //
        //     foreach (var tag in access)
        //         accessComp.Tags.Add(tag);
        // }
        //_buckle.BuckleToCoord(mounter, vehicle);
    }

    private void DriverEnd(Entity<VehicleComponent> vehicle)
    {
        DebugTools.Assert(vehicle.Comp.Driver != null);

        RemoveVehicleActions(vehicle.Comp.Driver!.Value, vehicle);
        RemComp<RelayInputMoverComponent>(vehicle.Comp.Driver!.Value);
        TryQueueDel(vehicle.Comp.ActiveOverlay);
        vehicle.Comp.ActiveOverlay = null;
    }
    private void Dismount(EntityUid dismounter, Entity<VehicleComponent> vehicle, EntityUid? user = null)
    {
        // we raise the unbuckle event here
        var buckle = (dismounter, Comp<BuckleComponent>(dismounter));
        var strap = (vehicle,Comp<StrapComponent>(vehicle));
        _buckle.Unbuckle(buckle, strap, user);

    }

    private void Dismount(Entity<StrapComponent> vehicle, Entity<BuckleComponent> dismounter, VehicleComponent comp)
    {
        if (comp.Driver == dismounter)
        {
            DriverEnd((vehicle,comp));
            comp.Driver = null;
        }
        _appearance.SetData(vehicle, VehicleState.DrawOver, false);
        _virtualItem.DeleteInHandsMatching(dismounter, vehicle);
        _buckle.UnbuckleToPos(dismounter,vehicle, Vector2.Zero);

    }
    private void OnItemSlotEject(EntityUid uid, VehicleComponent comp, ref ItemSlotEjectAttemptEvent args)
    {
        if (!comp.PreventEjectOfKey || comp.Driver == null || args.Slot.ID != comp.KeySlot || args.User == comp.Driver)
            return;

        args.Cancelled = true;
    }
    private void OnGetAdditionalAccess(EntityUid uid, VehicleComponent component, ref GetAdditionalAccessEvent args)
    {
        var driver = component.Driver;
        if (driver == null)
            return;

        args.Entities.Add(driver.Value);
    }
}



public sealed partial class HornActionEvent : InstantActionEvent;

public sealed partial class SirenActionEvent : InstantActionEvent;
