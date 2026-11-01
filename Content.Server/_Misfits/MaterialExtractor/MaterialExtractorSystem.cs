using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Power.Generator;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Gibbing.Systems;
using Content.Shared.Maps;
using Content.Shared.Random.Helpers;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;


namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>Runs the low-frequency seismic pulse and deposits raw materials in the extractor hopper.</summary>
public sealed partial class MaterialExtractorSystem : EntitySystem
{
    [Dependency] private ChunkEntitySystem _chunkSys = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private GeneratorSystem _generator = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BodySystem _bodySystem = default!;




    private static readonly SoundPathSpecifier ThumpSound = new("/Audio/Effects/Footsteps/largethud.ogg");
    private const float LifecycleEmoteRange = 30f;


    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(GeneratorSystem));
        SubscribeLocalEvent<MaterialExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MaterialExtractorComponent, ComponentShutdown>(OnCompShutdown);
        SubscribeLocalEvent<MaterialExtractorComponent, ComponentStartup>(OnCompStartup);
        SubscribeLocalEvent<MaterialExtractorComponent, GeneratorTurnedOn>(OnGeneratorTurnedOn);
        SubscribeLocalEvent<MaterialExtractorComponent, GeneratorTurnedOff>(OnGeneratorTurnedOff);
        SubscribeLocalEvent<MaterialExtractorComponent, TriggerEvent>(OnWaveFail); // explosion

        SubscribeLocalEvent<MaterialExtractorComponent, DoAfterAttemptEvent<SharedToolSystem.ToolDoAfterEvent>>(OnToolUse);
        ExtractorVFXInit();
        UIMessagesInit();
        ChunkSpawnersInit();
    }

    private void OnToolUse(
        Entity<MaterialExtractorComponent> ent,
        ref DoAfterAttemptEvent<SharedToolSystem.ToolDoAfterEvent> ev
    )
    {
        if (ev.Event.Used is null || !HasComp<WelderComponent>(ev.Event.Used) ||
            (ent.Comp.State & ExtractorState.Broken) == 0)
            return;

        ev.DoAfter.Args.Delay *= 10;
    }
    private void OnWaveFail(Entity<MaterialExtractorComponent> ent, ref TriggerEvent args)
    {
        ent.Comp.LootQueue.Clear();
        ShutdownSequence(ent);
        _generator.EmptyGenerator(ent.Owner);
        WaveReset(ent);
    }
    private void OnGeneratorTurnedOn(Entity<MaterialExtractorComponent> ent, ref GeneratorTurnedOn args)
    {
        DebugTools.Assert(ent.Comp.State != ExtractorState.Deleting || !TerminatingOrDeleted(ent));
        DebugTools.Assert((ent.Comp.State & ExtractorState.ExtractorBlockUI) == 0x0);

        ent.Comp.State = ExtractorState.Active;

    }
    private void OnGeneratorTurnedOff(Entity<MaterialExtractorComponent> ent, ref GeneratorTurnedOff args)
    {
        DebugTools.Assert(ent.Comp.State != ExtractorState.Deleting || !TerminatingOrDeleted(ent));
        var fuelExhausted = _generator.GetFuel(ent) <= 0f;
        SendLifecycleEmote(ent, fuelExhausted
            ? "material-extractor-fuel-depleted"
            : "material-extractor-stopped");
        ShutdownSequence(ent);
        ent.Comp.State = ExtractorState.Inactive;
    }

    private void ShutdownSequence(Entity<MaterialExtractorComponent> ent)
    {
        ent.Comp.State = ExtractorState.WaveShutdown;
        SetBeacon(ent.Owner,ent.Comp,false);
        ent.Comp.LowFuelWarningIssued = false;
    }

    private void WaveReset(Entity<MaterialExtractorComponent> ent)
    {
        ent.Comp.WavesInARow = 0;
        ent.Comp.SubWavesInARow = 0;
        foreach (var mob in ent.Comp.ActiveAttackers)
            _bodySystem.GibBody(mob, gibOrgans: true, launchGibs: true);

        ent.Comp.ActiveAttackers.Clear();
        ent.Comp.State = ExtractorState.Broken;

    }
    private void OnCompShutdown(EntityUid uid, MaterialExtractorComponent comp, ComponentShutdown args)
    {
        comp.State = ExtractorState.Deleting;
        foreach (var chunk in comp.SpawnChunks)
            RemComp<SpawnChunkComponent>(chunk.Key);
    }
    private void OnCompStartup(EntityUid uid, MaterialExtractorComponent comp, ComponentStartup args)
    {
        comp.WaveTheme = _proto.Index(comp.WaveThemeID);
        var trigger = Comp<DestructibleComponent>(uid).Thresholds.First().Trigger;

        if (trigger is not DamageTrigger dmgTrigger)
        {
            Del(uid);
            throw new InvalidOperationException($"Material Extractor of prototype: {MetaData(uid).EntityPrototype!.ID} has incorrect trigger for destructibleComp. Needs a damage trigger");
        }

        comp.HealthThreshhold = dmgTrigger.Damage;
    }



    private void OnMapInit(Entity<MaterialExtractorComponent> ent, ref MapInitEvent args)
    {
        if (!TryCacheSpawnChunks(ent.Owner, ent.Comp))
        {
            Del(ent);
            throw new InvalidOperationException($"Material Extractor of prototype: {MetaData(ent).EntityPrototype!.ID} could not cache spawn chunks");
        }
        RollOreQuality(ent);
        ent.Comp.State = ExtractorState.Inactive;
    }

    private void RollOreQuality(Entity<MaterialExtractorComponent> ent, float minRoll = 0f)
    {
        var roll = _random.NextFloat(minRoll,maxValue:1f);

        // why not just make these weights brah?
        if (roll < ent.Comp.PoorDepositChance)
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("POOR", ent.Comp.PoorYieldMultiplier);
        else if (roll > 1f - ent.Comp.RichDepositChance)
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("RICH", ent.Comp.RichYieldMultiplier);
        else
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("FAIR", 1f);

    }

    private const int WaveCheckInterval = 30;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MaterialExtractorComponent>();
        while (query.MoveNext(out var uid, out var extractor))
        {
            UpdateUi(uid,extractor);
            if (extractor.LootQueue.Count > 0)
                SpawnThrowLoot(uid, extractor);

            if ((extractor.State & ExtractorState.ExtractorOff) != 0x0)
                continue;

            extractor.ActiveAttackers.RemoveWhere(attacker =>
                Deleted(attacker) ||
                (TryComp<MobStateComponent>(attacker, out var state) &&
                    state.CurrentState == MobState.Dead));
            if (_timing.CurTime.Ticks % 10 == 0)
                UpdateExtractorEffects(uid, extractor);

            // we are in some transition phase
            if ((extractor.State & ExtractorState.ExtractorMobWaveStopped) == 0  && _timing.CurTime > extractor.NextSubWave)
                UpdateSubWaves(uid, extractor);

            if ((extractor.State & ExtractorState.AwaitingWaveEnd) != 0 && ShouldEndWave(extractor))
                SetUpNextWaveTransition(uid, extractor);

            if ((extractor.State & ExtractorState.OnWaveTransition) != 0 && _timing.CurTime > extractor.NextWave)
                extractor.State = ExtractorState.Active;

        }
    }

    private bool ShouldEndWave(MaterialExtractorComponent extractor)
    {
        if (extractor.ActiveAttackers.Count <= 0)
            return true;
        // todo failsafe logic
        return false;
    }
    private void UpdateSubWaves(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {

        SubwaveRNG(extractor);
        SpawnSubWave(extractor,extractor.WaveTheme.Mobs,extractor.DifficultySubWaveRNG);
        var bossWave = _random.Prob(extractor.BossWaveChance) || extractor.SubWavesInARow == extractor.SubWavePerWave;
        if (bossWave)
        {
            var bossCount = Math.Max(extractor.DifficultySubWaveRNG*1/5,1);
            SpawnSubWave(extractor,extractor.WaveTheme.Bosses,bossCount);
        }

        CountNextSubWave(extractor);
        if (extractor.SubWavesInARow == extractor.SubWavePerWave)
        {
            extractor.State = ExtractorState.AwaitingWaveEnd;
            return;
        }

        SetSubWaveTimers(extractor);


        // extractor.WarningSent = false;
        // SetBeacon(extractorUid, extractor, true);
    }


    // maybe this can be like cost instead where each unit is worth a certain value
    // and the base scale increasing also decides the weights of each unit on a curve
    // like in rimworld
    private void UpdateDifficulty(MaterialExtractorComponent extractor)
    {
        var theme = extractor.WaveTheme;
        var themeMult = Math.Max((theme.MinCount + theme.MaxCount)/2,1); // get an avg
        var playerMult = 3 * MathF.Log2(.2f * extractor.Participants - 1) + 1;
        var baseScale = 5 * MathF.Log2(.2f * extractor.WavesInARow - 1) + 1;
        var scaling = themeMult*playerMult*baseScale;

        extractor.Difficulty = (int) Math.Floor(scaling);

    }

    private void SubwaveRNG(MaterialExtractorComponent extractor)
    {
        var subRNG = _random.NextFloat(extractor.SubWaveMinVariance,extractor.SubWaveMaxVariance);
        extractor.DifficultySubWaveRNG = (int) MathF.Max(MathF.Floor(subRNG*extractor.Difficulty),1);
    }

    private void SpawnSubWave(MaterialExtractorComponent extractor,  Dictionary<EntProtoId,float> mobSpawnTable,int mobNum)
    {

        var spawnChunk = _random.Pick(extractor.ChunksOutsidePVS);
        var spawnTiles = extractor.SpawnChunks[spawnChunk].GetEnumerator();
        while (mobNum > 0)
        {
            if (!spawnTiles.MoveNext())
            {
                spawnTiles.Dispose();
                // enumorator reset isnt exposed for some reason
                spawnTiles = extractor.SpawnChunks[spawnChunk].GetEnumerator();
                continue;
            }
            mobNum--;
            var prototype = _random.Pick(mobSpawnTable);
            var attacker = Spawn(prototype);
            extractor.ActiveAttackers.Add(attacker);
        }
    }







}
