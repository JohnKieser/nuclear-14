using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;



namespace Content.Shared.Vehicles;


public sealed partial class InputRelayControllerSystem : EntitySystem
{

    [Robust.Shared.IoC.Dependency] private SharedBuckleSystem _buckle = default!;
    [Robust.Shared.IoC.Dependency] private SharedMoverController _mover = default!;
    private static InputRelayTrySetEv _trySetEv;
    private static InputRelaySetEv _setEv;
    private static InputRelayRemovedEv _removedEv;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DriveOnBuckleComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<DriveOnBuckleComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<DriveOnBuckleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<DriveOnBuckleComponent, VirtualItemDeletedEvent>(OnDropped);

    }

    private static void OnRemove(EntityUid uid, DriveOnBuckleComponent component, ComponentRemove args)
    {
        if (component.Pilots.Count == 0)
            return;
        component.Pilots.Clear();
    }

    private void OnStrapped(Entity<DriveOnBuckleComponent> car, ref StrappedEvent args)
    {
        TrySetRelay(car,args.Buckle);
    }


    private void OnUnstrapped(Entity<DriveOnBuckleComponent> car, ref UnstrappedEvent args)
    {
        RemoveRelay(car,args.Buckle);
    }


    private void TrySetRelay(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        _trySetEv = new(pilot);
        RaiseLocalEvent(car ,ref _trySetEv);
        if (_trySetEv.Cancel)
            return;
        RaiseLocalEvent(pilot ,ref _trySetEv);
        if (_trySetEv.Cancel)
            return;
        _mover.SetRelay(pilot, car);
        car.Comp.Pilots.Add(pilot);
        _setEv = new(pilot);
        RaiseLocalEvent(car ,ref _setEv);
        RaiseLocalEvent(pilot ,ref _setEv);
    }

    private void RemoveRelay(Entity<DriveOnBuckleComponent> car, EntityUid pilot)
    {
        RemComp<RelayInputMoverComponent>(pilot);
        car.Comp.Pilots.Remove(pilot);
        _removedEv = new(pilot);
        RaiseLocalEvent(car ,ref _removedEv);
        RaiseLocalEvent(pilot ,ref _removedEv);
    }
    private void OnDropped(EntityUid car, DriveOnBuckleComponent comp, VirtualItemDeletedEvent args)
    {
        //misfits: edge case discovered during server shutdown that virtual item deletion(_virtualItem.DeleteInHandsMatching()) causes event chain where TryDrop is still called
        //          this results in an infinite throwing event loop maybe because of vehicle entity being raised on is in the middle of deletion
         if (TerminatingOrDeleted(car))
            return;
         var dismounter = args.User;
         var buckle = (dismounter, Comp<BuckleComponent>(dismounter));
         var strap = (uid: car,Comp<StrapComponent>(car));

         _buckle.Unbuckle(buckle, strap, dismounter);
    }


    [ByRefEvent]
    public record struct InputRelaySetEv(EntityUid Driver);

    [ByRefEvent]
    public record struct InputRelayRemovedEv(EntityUid Driver);

    [ByRefEvent]
    public record struct InputRelayTrySetEv(EntityUid Driver, bool  Cancel = false);

    [ByRefEvent]
    public record struct EngineTryOffEvent(VehicleComponent Comp, bool Cancel = false, bool Handled = false);

    [ByRefEvent]
    public record struct EngineTryOnEvent(VehicleComponent Comp, bool Cancel = false, bool Handled = false);


}


