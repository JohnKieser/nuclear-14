using Content.Server.Administration;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Vehicles;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Vehicles;

public partial class MotorbikeSystem : EntitySystem
{

    [Dependency] private MetaDataSystem _metaSystem = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedVehicleSystem _vehicle = default!;
    [Dependency] private IGameTiming _time = default!;
    [Dependency] private DamageableSystem _dmg = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;

    private static readonly ProtoId<DamageTypePrototype> DmgType = "EngineDamage";
    private static DamageTypePrototype _engineDmg = default!;
    private static readonly ProtoId<DamageGroupPrototype> DmgGroup = "InternalMechanical";
    private static DamageGroupPrototype _engineGroupDmg = default!;

    private EntityQueryEnumerator<MotorbikeComponent, ActiveEntityComponent> _runningBikesQuery;
    public override void Initialize()
    {
        base.Initialize();

        _runningBikesQuery = EntityQueryEnumerator<MotorbikeComponent, ActiveEntityComponent>();
        _engineDmg = _protoMan.Index(DmgType);
        _engineGroupDmg = _protoMan.Index(DmgGroup);

        SubscribeLocalEvent<MotorbikeComponent, ComponentInit>(OnInit);
        // SubscribeLocalEvent<MotorbikeComponent, EntInsertedIntoContainerMessage>(OnKeyInserted, after: [typeof(VehicleSystem)]);
        //SubscribeLocalEvent<MotorbikeComponent, InteractUsingEvent>(OnInteractUsing, before: [typeof(FlammableSystem), typeof(RepairableSystem)]);

        SubscribeLocalEvent<MotorbikeComponent, SolutionTransferAttemptEvent>(OnSolutionTransferAttempt);

        SubscribeLocalEvent<MotorbikeComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MotorbikeComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<MotorbikeComponent, GetVerbsEvent<Verb>>(OnGetVerbs);

        SubscribeLocalEvent<MotorbikeComponent, EngineOffEvent>(OnEngineOff);
        SubscribeLocalEvent<MotorbikeComponent, EngineOnEvent>(OnEngineOn);

        //SubscribeLocalEvent<MotorbikeComponent, EngineTryOffEvent>(OnTryEngineOff);
        SubscribeLocalEvent<MotorbikeComponent, EngineTryOnEvent>(OnTryEngineOn);

        SubscribeLocalEvent<DisabledEntityComponent, EngineTryOnEvent>(OnTryDisabledEngineOn);

    }
    // private void OnTryEngineOff(Entity<MotorbikeComponent> ent, ref EngineTryOffEvent args)
    // {
    //
    // }
    private void OnTryDisabledEngineOn(Entity<DisabledEntityComponent> ent, ref EngineTryOnEvent args)
    {
        args.Handled = true;
        args.Cancel = true;
        //_popup.PopupEntity(Loc.GetString(locMsg), uid);
        _popup.PopupEntity("The engine hisses and whines terribly. It's never coming back on again", ent);
    }
    private void OnTryEngineOn(Entity<MotorbikeComponent> ent, ref EngineTryOnEvent args)
    {
        if (args.Handled)
            return;
        args.Cancel =
            !(_solution.TryGetSolution(ent.Owner, ent.Comp.FuelSolution, out _, out var fuelSolution) &&
                                        fuelSolution.Volume > FixedPoint2.Zero);

    }
    private void OnEngineOff(Entity<MotorbikeComponent> ent, ref EngineOffEvent args)
    {
        RemCompDeferred<ActiveEntityComponent>(ent);
    }
    private void OnEngineOn(Entity<MotorbikeComponent> ent, ref EngineOnEvent args)
    {
        EnsureComp<ActiveEntityComponent>(ent);
    }
    // Adds a Rename verb to motorbikes, allowing players to give them a custom name.
    private void OnGetVerbs(Entity<MotorbikeComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        var player = actor.PlayerSession;

        var rename = new Verb
        {
            Text = "Rename",
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/rename.svg.192dpi.png")),
            Act = () =>
            {
                _quickDialog.OpenDialog(player, "Rename", "Name", (string newName) =>
                {
                    _metaSystem.SetEntityName(ent.Owner, newName);
                });
            },
            Impact = LogImpact.Low,
        };

        args.Verbs.Add(rename);
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // misfits: server tps is usually 30. So as an example
        //          this runs on the 30th tick. Runs every second
        var tickToRun = _time.CurTick.Value % MathF.Round(_time.TickRate);
        if (tickToRun != 0)
            return;


        while (_runningBikesQuery.MoveNext(out var uid, out var motorbike, out _))
        {
            if (!TryConsumeFuel(uid, motorbike))
                FuelTryStopEngine(uid);
        }
    }

    private void OnInit(Entity<MotorbikeComponent> ent, ref ComponentInit args)
    {
        _appearance.SetData(ent, MotorbikeVisuals.Burning, ent.Comp.Burning);
    }

    // private void OnKeyInserted(Entity<MotorbikeComponent> ent, ref EntInsertedIntoContainerMessage args)
    // {
    //     if (!TryComp(ent, out VehicleComponent? vehicle) ||
    //         args.Container.ID != vehicle.KeySlot ||
    //         !vehicle.EngineRunning)
    //     {
    //         return;
    //     }
    //
    //     if (ent.Comp.Burning || !HasUsableFuel(ent, ent.Comp))
    //         StopEngine(ent, vehicle, popup: !ent.Comp.Burning);
    // }

    private void OnSolutionTransferAttempt(Entity<MotorbikeComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (Comp<VehicleComponent>(ent).Driver is not null)
            args.Cancel("The driver is in the way of the fuel port!!");
    }

    private void OnDamageChanged(Entity<MotorbikeComponent> ent, ref DamageChangedEvent args)
    {

        if (args.Damageable.Damage.TryGetDamageInGroup(_engineGroupDmg, out var dmg) && dmg > 50.0)
            DestroyEngine(ent);

    }

    private void OnExamined(Entity<MotorbikeComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange ||
            !_solution.TryGetSolution(ent.Owner, ent.Comp.FuelSolution, out _, out var fuelSolution))
        {
            return;
        }

        var current = fuelSolution.GetTotalPrototypeQuantity(ent.Comp.FuelReagent);
        args.PushMarkup(Loc.GetString("motorbike-examine-fuel", ("current", current), ("max", fuelSolution.MaxVolume)));
    }



    private bool TryConsumeFuel(EntityUid uid, MotorbikeComponent motorbike)
    {

        if (!_solution.TryGetSolution(uid, motorbike.FuelSolution, out var fuelSoln, out var fuelSolution))
        {
            // debug here
            return false;
        }

        var roundedNum = motorbike.FuelUsePerSecond.Float();
        var fuelToUse = FixedPoint2.Min(FixedPoint2.New(roundedNum),fuelSolution.Volume);
        var consumed = _solution.SplitSolution(fuelSoln.Value, fuelToUse);

        var validFuelUsed = consumed.GetTotalPrototypeQuantity(motorbike.FuelReagent);

        var shouldRun = validFuelUsed / fuelToUse > .5f &&
                        fuelSolution.GetTotalPrototypeQuantity(motorbike.FuelReagent) > 0;

        consumed.RemoveReagent(motorbike.FuelReagent, validFuelUsed);
        var damage = consumed.Volume * 10;

        if (damage >= 1)
            _dmg.TryChangeDamage(uid,new(_engineDmg, damage), true);
        return shouldRun;

    }

    private void FuelTryStopEngine(EntityUid uid)
    {
        Entity<VehicleComponent> bike = (uid,Comp<VehicleComponent>(uid));
        if (bike.Comp.EngineRunning)
            _vehicle.TurnOffEngine(bike);

    }
    private void FuelStartEngine(EntityUid uid)
    {
        var bike = (uid,Comp<VehicleComponent>(uid));
        _vehicle.TurnOnEngine(bike);
    }
    private void DestroyEngine(EntityUid uid)
    {

        var bike = (uid,Comp<VehicleComponent>(uid));
        _vehicle.TurnOffEngine(bike);
        EnsureComp<DisabledEntityComponent>(uid);

        //_popup.PopupEntity(Loc.GetString(locMsg), uid);
    }
}
