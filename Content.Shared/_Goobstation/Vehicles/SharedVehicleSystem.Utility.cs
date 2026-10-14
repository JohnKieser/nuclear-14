


using System.Runtime.CompilerServices;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;


namespace Content.Shared.Vehicles;
public abstract partial class SharedVehicleSystem : EntitySystem
{
    private static EngineOffEvent EngineOff;
    private static EngineOnEvent EngineOn;
    private static EngineTryOffEvent EngineTryOff;
    private static EngineTryOnEvent EngineTryOn;
    public void TryTurnOnEngine(Entity<VehicleComponent> vehicle)
    {

        EngineTryOff = new(vehicle.Comp);
        RaiseLocalEvent(vehicle,ref EngineTryOn);
        if (!EngineTryOn.Cancel)
            TurnOnEngine(vehicle);

    }
    public void TryTurnOffEngine(Entity<VehicleComponent> vehicle)
    {

        EngineTryOff = new(vehicle.Comp);
        RaiseLocalEvent(vehicle,ref EngineTryOff);
        if (!EngineTryOff.Cancel)
            TurnOffEngine(vehicle);

    }
    public void TurnOnEngine(Entity<VehicleComponent> vehicle)
    {
        DebugTools.Assert(!vehicle.Comp.EngineRunning);
        vehicle.Comp.EngineRunning = true;
        _appearance.SetData(vehicle.Owner, VehicleState.Animated, true);
        _ambientSound.SetAmbience(vehicle.Owner, true);

        RaiseLocalEvent(vehicle,ref EngineOn);

        if (vehicle.Comp.Driver is not null)
            DriverStart(vehicle);
    }

    public void TurnOffEngine(Entity<VehicleComponent> vehicle)
    {
        DebugTools.Assert(vehicle.Comp.EngineRunning);
        vehicle.Comp.EngineRunning = false;
        _appearance.SetData(vehicle.Owner, VehicleState.Animated, false);
        _ambientSound.SetAmbience(vehicle.Owner, false);

        RaiseLocalEvent(vehicle,ref EngineOff);

        if (vehicle.Comp.Driver is not null)
            DriverEnd(vehicle);
    }

    /// <summary>
    /// Can be seen as a wrapper for <see cref="OnUnstrapped"/> but also
    /// calls any other handlers to keep consistent unbuckle behavior
    /// for the given entity
    /// </summary>
    private void Dismount(EntityUid dismounter, Entity<VehicleComponent> vehicle, EntityUid? user = null)
    {
        var buckle = (dismounter, Comp<BuckleComponent>(dismounter));
        var strap = (vehicle,Comp<StrapComponent>(vehicle));
        // trigger event calls
        _buckle.Unbuckle(buckle, strap, user);
    }

    private void DriverStart(Entity<VehicleComponent>  vehicle)
    {
        DebugTools.Assert(vehicle.Comp.Driver != null);
        AddVehicleActions(vehicle.Comp.Driver.Value, vehicle);
        SetupDriverOverlay(vehicle);
        _mover.SetRelay(vehicle.Comp.Driver.Value, vehicle);
    }

    private void DriverEnd(Entity<VehicleComponent> vehicle)
    {
        DebugTools.Assert(vehicle.Comp.Driver != null);

        RemoveVehicleActions(vehicle.Comp.Driver!.Value, vehicle);
        RemComp<RelayInputMoverComponent>(vehicle.Comp.Driver!.Value);
        TryQueueDel(vehicle.Comp.ActiveOverlay);
        vehicle.Comp.ActiveOverlay = null;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetupDriverOverlay(Entity<VehicleComponent> ent)
    {
        if (ent.Comp.OverlayPrototype == null)
            return;

        // // Misfits server spawns the overlay & networks it a predicted client spawn would leave duplicate that never gets deleted (cuz its not networked)
        // if (_net.IsClient)
        //     return;
        var overlay = Spawn(ent.Comp.OverlayPrototype, Transform(ent).Coordinates);
        _transform.SetParent(overlay, ent);
        _transform.SetLocalPosition(overlay, Vector2.Zero);
        _transform.SetLocalRotation(overlay, Angle.Zero);
        ent.Comp.ActiveOverlay = overlay;
    }
}
