using Content.Server.Administration;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
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

public sealed partial class MotorbikeSystem : EntitySystem
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

    // private static readonly ProtoId<DamageTypePrototype> DmgType = "EngineDamage";
    // private static DamageTypePrototype _engineDmg = default!;
    // private static readonly ProtoId<DamageGroupPrototype> DmgGroup = "InternalMechanical";
    // private static DamageGroupPrototype _engineGroupDmg = default!;


//TODO: finish and make into general fuel consumer system/comp

    private EntityQueryEnumerator<ActiveEntityComponent,MotorbikeComponent,SolutionComponent> _runningBikesQuery;
    public override void Initialize()
    {
        base.Initialize();
        _solutionTickEv = new();
        // _engineDmg = _protoMan.Index(DmgType);
        // _engineGroupDmg = _protoMan.Index(DmgGroup);

        SubscribeLocalEvent<MotorbikeComponent, ComponentInit>(OnInit);
        // SubscribeLocalEvent<MotorbikeComponent, EntInsertedIntoContainerMessage>(OnKeyInserted, after: [typeof(VehicleSystem)]);
        //SubscribeLocalEvent<MotorbikeComponent, InteractUsingEvent>(OnInteractUsing, before: [typeof(FlammableSystem), typeof(RepairableSystem)]);

        SubscribeLocalEvent<MotorbikeComponent, SolutionTransferAttemptEvent>(OnSolutionTransferAttempt);

        //SubscribeLocalEvent<MotorbikeComponent, DamageChangedEvent>(OnDamageChanged);
        //SubscribeLocalEvent<MotorbikeComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<MotorbikeComponent, GetVerbsEvent<Verb>>(OnGetVerbs);

        SubscribeLocalEvent<MotorbikeComponent, EngineOffEvent>(OnEngineOff);
        SubscribeLocalEvent<MotorbikeComponent, EngineOnEvent>(OnEngineOn);

        SubscribeLocalEvent<MotorbikeComponent, EngineTryOnEvent>(OnTryEngineOn);

        SubscribeLocalEvent<DisabledEntityComponent, EngineTryOnEvent>(OnTryDisabledEngineOn);

        SubscribeLocalEvent<SolutionTickEv>(ProcessSolutionConsumption);
        SubscribeLocalEvent<EngineFuelComponent,SolutionConsumedEv>(OnEngineFuelTick);

    }

    private void OnEngineFuelTick(Entity<EngineFuelComponent> ent, ref SolutionConsumedEv ev)
    {
        var badQuant = ev.SolutionSource.Volume.Value;

        foreach (var sol in ev.AllowedReagents)
        {
            ReagentId id = new(sol, null);
            badQuant -= ev.SolutionSource.GetReagent(id).Quantity.Value;
        }

        badQuant = ent.Comp.DmgPerInvalidReagent*ev.AmountToConsume.Value*(badQuant / ev.SolutionSource.Volume.Value);
        var dmg = FixedPoint2.FromCents(badQuant);
        var dmgType = _protoMan.Index(ent.Comp.DmgType);
        _dmg.TryChangeDamage(ent,new(dmgType, dmg), true);
    }

    public sealed class SolutionTickEv : EntityEventArgs;
    private static SolutionTickEv _solutionTickEv = default!;
    [ByRefEvent]
    public record struct SolutionConsumedEv(Solution SolutionSource, FixedPoint2 AmountToConsume,
                        List<ProtoId<ReagentPrototype>>  AllowedReagents, bool Handled = false);

    private static SolutionConsumedEv _solutionConsumedEv = default!;
    private void ProcessSolutionConsumption(SolutionTickEv ev)
    {
        _runningBikesQuery = EntityQueryEnumerator<ActiveEntityComponent,MotorbikeComponent,SolutionComponent>();
        while (_runningBikesQuery.MoveNext(out var uid, out _, out var motorbike, out var sol))
        {
            var fuelSolution = sol.Solution;
            var roundedNum = motorbike.FuelUsePerSecond.Float();
            var solutionToConsume = FixedPoint2.Min(FixedPoint2.New(roundedNum),fuelSolution.Volume);
            _solutionConsumedEv = new(fuelSolution, solutionToConsume, motorbike.AllowedReagents);
            RaiseLocalEvent(uid, ref _solutionConsumedEv);

            if (_solutionConsumedEv.Handled)
                continue;
            var fuelSolEnt = (uid, sol);
            _solution.SplitSolution(fuelSolEnt, solutionToConsume);
        }

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

        args.Cancel = !_solution.TryGetSolution(ent.Owner, ent.Comp.FuelSolution, out _, out var fuelSolution) ||
                        !(fuelSolution.Volume > FixedPoint2.Zero);

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
        QueueLocalEvent(_solutionTickEv);

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

    // private void OnDamageChanged(Entity<MotorbikeComponent> ent, ref DamageChangedEvent args)
    // {
    //
    //     if (args.Damageable.Damage.TryGetDamageInGroup(_engineGroupDmg, out var dmg) && dmg > 50.0)
    //         DestroyEngine(ent);
    //
    // }
    // private void DestroyEngine(EntityUid uid)
    // {
    //
    //     var bike = (uid,Comp<VehicleComponent>(uid));
    //     _vehicle.TryTurnOffEngine(bike);
    //     EnsureComp<DisabledEntityComponent>(uid);
    //
    //     //_popup.PopupEntity(Loc.GetString(locMsg), uid);
    // }
    // private void OnExamined(Entity<MotorbikeComponent> ent, ref ExaminedEvent args)
    // {
    //     if (!args.IsInDetailsRange ||
    //         !_solution.TryGetSolution(ent.Owner, ent.Comp.FuelSolution, out _, out var fuelSolution))
    //     {
    //         return;
    //     }
    //
    //     var current = fuelSolution.GetTotalPrototypeQuantity(ent.Comp.FuelReagent);
    //     args.PushMarkup(Loc.GetString("motorbike-examine-fuel", ("current", current), ("max", fuelSolution.MaxVolume)));
    // }

    private void FuelTryStopEngine(EntityUid uid)
    {
        Entity<VehicleComponent> bike = (uid,Comp<VehicleComponent>(uid));
        if (bike.Comp.EngineRunning)
            _vehicle.TryTurnOffEngine(bike);

    }
    private void FuelStartEngine(EntityUid uid)
    {
        var bike = (uid,Comp<VehicleComponent>(uid));
        _vehicle.TryTurnOnEngine(bike);
    }




}
