using Content.Server.Administration;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Vehicles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Vehicles;

public sealed partial class SolutionConsumerSystem : EntitySystem
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

    private EntityQueryEnumerator<ActiveSolutionConsumerComponent,SolutionConsumerComponent,SolutionComponent> _runningBikesQuery;
    public override void Initialize()
    {
        base.Initialize();
        _solutionTickEv = new();


        SubscribeLocalEvent<SolutionTickEv>(ProcessSolutionConsumption);
        SubscribeLocalEvent<DamageOnConsumeReagentComponent,SolutionConsumedEv>(OnEngineFuelTick);

    }

    private void OnEngineFuelTick(Entity<DamageOnConsumeReagentComponent> ent, ref SolutionConsumedEv ev)
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


    private static SolutionConsumedEv _solutionConsumedEv = default!;
    private void ProcessSolutionConsumption(SolutionTickEv ev)
    {
        _runningBikesQuery = EntityQueryEnumerator<ActiveSolutionConsumerComponent,SolutionConsumerComponent,SolutionComponent>();
        while (_runningBikesQuery.MoveNext(out var uid, out _, out var motorbike, out var sol))
        {
            var fuelSolution = sol.Solution;
            var roundedNum = motorbike.FuelUsePerSecond.Float();
            var solutionToConsume = FixedPoint2.Min(FixedPoint2.New(roundedNum),fuelSolution.Volume);
            _solutionConsumedEv = new(fuelSolution, solutionToConsume, motorbike.AllowedReagents);
            RaiseLocalEvent(uid, ref _solutionConsumedEv);

            if (_solutionConsumedEv.Handled || !(solutionToConsume > 0))
                continue;
            var fuelSolEnt = (uid, sol);
            _solution.SplitSolution(fuelSolEnt, solutionToConsume);
        }

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




}
