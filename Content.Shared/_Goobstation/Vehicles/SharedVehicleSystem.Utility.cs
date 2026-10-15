


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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TryTurnOnEngine(Entity<VehicleComponent> vehicle)
    {
        EngineTryOff = new(vehicle.Comp);
        RaiseLocalEvent(vehicle,ref EngineTryOn);
        DebugTools.Assert(!vehicle.Comp.EngineRunning,"redundantly tried turning ON engine");
        if (!EngineTryOn.Cancel && !vehicle.Comp.EngineRunning)
            TurnOnEngine(vehicle);

    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TryTurnOffEngine(Entity<VehicleComponent> vehicle)
    {
        EngineTryOff = new(vehicle.Comp);
        RaiseLocalEvent(vehicle,ref EngineTryOff);
        DebugTools.Assert(vehicle.Comp.EngineRunning, "redundantly tried turning OFF engine");
        if (!EngineTryOff.Cancel && vehicle.Comp.EngineRunning)
            TurnOffEngine(vehicle);

    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TurnOnEngine(Entity<VehicleComponent> vehicle)
    {
        DebugTools.Assert(!vehicle.Comp.EngineRunning, "ALREADY RUNNING VEHICLE FORCED ON. MIGHT CAUSE ISSUES");
        vehicle.Comp.EngineRunning = true;
        _appearance.SetData(vehicle.Owner, VehicleState.Animated, true);
        _ambientSound.SetAmbience(vehicle.Owner, true);
        RaiseLocalEvent(vehicle,ref EngineOn);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TurnOffEngine(Entity<VehicleComponent> vehicle)
    {
        vehicle.Comp.EngineRunning = false;
        _appearance.SetData(vehicle.Owner, VehicleState.Animated, false);
        _ambientSound.SetAmbience(vehicle.Owner, false);
        RaiseLocalEvent(vehicle,ref EngineOff);
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DriverEnable(Entity<VehicleComponent>  vehicle)
    {

        AddVehicleActions(vehicle.Comp.Driver!.Value, vehicle);
        SetupDriverOverlay(vehicle);

    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DriverDisable(Entity<VehicleComponent> vehicle)
    {

        RemoveVehicleActions(vehicle.Comp.Driver!.Value, vehicle);
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
