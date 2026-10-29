using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.Power.Generator;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._Misfits.Expeditions;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Power.Generator;
using Content.Shared._Misfits.Expeditions;
using Content.Shared._Misfits.MaterialExtractor;
using Content.Shared._NC14.DayNightCycle;
using Content.Shared.Storage;
using Content.Shared.Spawning;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Random.Helpers;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared;
using Robust.Shared.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Systems;


namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>Runs the low-frequency seismic pulse and deposits raw materials in the extractor hopper.</summary>
public sealed partial class MaterialExtractorSystem : EntitySystem
{
    private static readonly SoundPathSpecifier ThumpSound = new("/Audio/Effects/Footsteps/largethud.ogg");
    private const float LifecycleEmoteRange = 30f;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StorageSystem _storage = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private OccluderSystem _occulud = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private GeneratorSystem _generator = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ExpeditionBossSystem _bosses = default!;

    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(GeneratorSystem));
        SubscribeLocalEvent<MaterialExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MaterialExtractorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorStartMessage>(OnStartMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorStopMessage>(OnStopMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorEjectFuelMessage>(OnEjectFuelMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, GetVerbsEvent<AlternativeVerb>>(GetAlternativeVerb);
    }

    private void GetAlternativeVerb(Entity<MaterialExtractorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => _ui.OpenUi(ent.Owner, MaterialExtractorUiKey.Key, user),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/zap.svg.192dpi.png")),
            Text = "Material extractor controls",
        });
    }

    private void OnStartMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorStartMessage args)
    {
        if (!Transform(ent).Anchored || !TryComp<FuelGeneratorComponent>(ent, out var generator)
            || generator.On || _generator.GetFuel(ent) <= 0f || _generator.GetIsClogged(ent))
            return;

        SendLifecycleEmote(ent, "material-extractor-started");
        _generator.SetFuelGeneratorOn(ent, true, generator);
    }

    private void OnStopMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorStopMessage args)
    {
        if (TryComp<FuelGeneratorComponent>(ent, out var generator))
            _generator.SetFuelGeneratorOn(ent, false, generator);
    }

    private void OnEjectFuelMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorEjectFuelMessage args)
    {
        _generator.EmptyGenerator(ent);
    }


    private void OnExamined(Entity<MaterialExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("material-extractor-examine", ("quality", ent.Comp.DepositQuality.ToLowerInvariant())));
    }

    private void OnMapInit(Entity<MaterialExtractorComponent> ent, ref MapInitEvent args)
    {
        var qualityRoll = _random.NextFloat();
        if (qualityRoll < ent.Comp.PoorDepositChance)
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("POOR", ent.Comp.PoorYieldMultiplier);
        }
        else if (qualityRoll > 1f - ent.Comp.RichDepositChance)
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("RICH", ent.Comp.RichYieldMultiplier);
        }
        else
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("FAIR", 1f);
        }
        _lights.SetEnabled(ent.Owner, false);
    }

    private void PopupMessages(EntityUid uid, MaterialExtractorComponent extractor, bool running)
    {
        if (running)
        {
            LowFuelMsgToggle(uid, extractor);
            return;
        }

        var fuelExhausted = _generator.GetFuel(uid) <= 0f;
        SendLifecycleEmote(uid, fuelExhausted
            ? "material-extractor-fuel-depleted"
            : "material-extractor-stopped");

    }


    private void UpdateExtractorVisuals(EntityUid uid, MaterialExtractorComponent extractor)
    {
        var fuelComp = Comp<FuelGeneratorComponent>(uid);
        //_generator.SetFuelGeneratorOn(uid,fuelComp.On);
        SetBeacon(uid,extractor, fuelComp.On);
        UpdateUi(uid,extractor);
        PopupMessages(uid,extractor, fuelComp.On);
        if (_timing.CurTick.Value >= extractor.NextPulse)
            TogglePulseEffect(uid, extractor);

        if (_timing.CurTick.Value+extractor.NextWave*2/3 >= extractor.NextWave)
            WaveWarningEffect(uid, extractor);
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MaterialExtractorComponent>();
        while (query.MoveNext(out var uid, out var extractor))
        {

            UpdateExtractorVisuals(uid, extractor);

            extractor.ActiveAttackers.RemoveWhere(attacker =>
                Deleted(attacker) ||
                TryComp<MobStateComponent>(attacker, out var state) && state.CurrentState == MobState.Dead);



            if (!extractor.WasRunning)
            {
                extractor.WasRunning = true;
                extractor.WarningSent = false;
                extractor.NextPulse = _timing.CurTick.Value + (uint)MathF.Floor(_timing.TickRate*extractor.PulseIntervalSeconds);

                var wavMin = (int)MathF.Floor(_timing.TickRate * extractor.WaveMinSeconds);
                var wavMax = (int)MathF.Floor(_timing.TickRate * extractor.WaveMaxSeconds);
                var waveInterval = (uint)_random.Next(wavMin, wavMax);
                extractor.NextWave = _timing.CurTick.Value + waveInterval;
            }


            if (!extractor.WaveActive && !extractor.WarningSent &&
                _timing.CurTick.Value >= extractor.NextWave - (uint) MathF.Round(_timing.TickRate*extractor.WaveWarningSeconds))
            {
                extractor.WarningSent = true;
                SetBeacon(uid, extractor, true);
                _audio.PlayPvs(ThumpSound, uid,
                    AudioParams.Default.WithVolume(-3f).WithMaxDistance(30f));
            }

            if (_timing.CurTick.Value >= extractor.NextWave)
                StartWave(uid, extractor);

        }
    }

    private void WaveWarningEffect(EntityUid uid,MaterialExtractorComponent comp)
    {
        _audio.PlayPvs(ThumpSound, uid,
            AudioParams.Default.WithVolume(-3f).WithMaxDistance(30f));

    }
    private void TogglePulseEffect(EntityUid uid,MaterialExtractorComponent comp)
    {
        SetBeacon(uid, comp, !comp.BeaconOn);
        _audio.PlayPvs(ThumpSound, uid,
            AudioParams.Default.WithVolume(-7f).WithMaxDistance(22f));

        var secondsToTicks = (uint) MathF.Round(_timing.TickRate*comp.PulseIntervalSeconds);
        comp.NextPulse = _timing.CurTick.Value + secondsToTicks;
    }
    private void UpdateUi(EntityUid uid, MaterialExtractorComponent extractor)
    {
        if (!_ui.IsUiOpen(uid, MaterialExtractorUiKey.Key) || !TryComp<FuelGeneratorComponent>(uid, out var fuelComp))
            return;
        var running = fuelComp.On;
        var fuel = _generator.GetFuel(uid);
        var capacity = 0f;
        var fuelDepleted = fuel <= 0f;
        if (_solution.TryGetSolution(uid, extractor.FuelSolution, out _, out var tank))
            capacity = tank.MaxVolume.Float();


        _ui.SetUiState(uid, MaterialExtractorUiKey.Key,
            new MaterialExtractorUiState
            {
                Running = running,
                Fuel = fuel,
                FuelCapacity = capacity,
                // Unattended = unattended,
                FuelDepleted = fuelDepleted,
            });
    }

    private void SendLifecycleEmote(EntityUid uid, string localizationId)
    {
        _chat.SendAreaEmote(uid, Loc.GetString(localizationId), LifecycleEmoteRange);
    }

    private void ExtractorReward(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        var rewardSize = extractor.ConsecutiveWaves*10;

        var output = Spawn(SelectOutput(extractor), Transform(extractorUid).Coordinates);
        if (_storage.Insert(extractorUid, output, out _, storageComp: storage, playSound: false))
            return;

        Del(output);
        SetBeacon(extractorUid, extractor, true);
    }

    private void LowFuelMsgToggle(EntityUid uid, MaterialExtractorComponent extractor)
    {
        if (!_solution.TryGetSolution(uid, extractor.FuelSolution, out _, out var fuelTank))
            return;
        //misfits removed: least ai generated line of code.
        //var fraction = fuelTank.GetTotalPrototypeQuantity(extractor.FuelReagent).Float() / fuelTank.MaxVolume.Float();

        if (fuelTank.FillFraction <= extractor.LowFuelWarningThreshold && !extractor.LowFuelWarningIssued)
        {
            extractor.LowFuelWarningIssued = true;
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("material-extractor-low-fuel", ("percent", $"Value: {fuelTank.FillFraction:P2}.")),
                InGameICChatType.Emote,
                ChatTransmitRange.Normal,
                ignoreActionBlocker: true);
            return;
        }

        extractor.LowFuelWarningIssued = fuelTank.FillFraction > extractor.LowFuelWarningThreshold;

    }


    private string SelectOutput(MaterialExtractorComponent extractor)
    {
        var totalWeight = 0;
        foreach (var weight in extractor.OutputWeights.Values)
            totalWeight += weight;

        if (totalWeight <= 0)
            throw new InvalidOperationException("Material extractor output weights must have a positive total.");

        var roll = _random.Next(totalWeight);
        string? fallback = null;
        foreach (var (prototype, weight) in extractor.OutputWeights)
        {
            fallback = prototype;
            roll -= weight;
            if (roll < 0)
                return prototype;
        }

        return fallback!;
    }


    private void StartWave(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        if (extractor.ConsecutiveWaves != 0 && extractor.ConsecutiveWaves % 5 == 0 &&
            extractor.ActiveAttackers.Count > 0)
        {
            extractor.NextWave = _timing.CurTick.Value + 300; // 10 seconds
            return;
        }

        var theme = _proto.Index(extractor.CurWaveTheme);
        if (extractor.ConsecutiveWaves % 5 == 0)
        {

            theme = SelectWaveTheme(extractorUid, extractor);
            extractor.CurWaveTheme = theme;
        }
        _chat.TrySendInGameICMessage(extractorUid,
            Loc.GetString("material-extractor-rumble"),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);

        // search operation first. This also counts all players in an area
        if (!TryFindWaveSpawnCoordinates(extractorUid, extractor, out var spawnCoordinates))
            spawnCoordinates = Transform(extractorUid).Coordinates.Offset(_random.NextVector2(10,15));

        extractor.WavesStartedTotal++;
        extractor.ConsecutiveWaves++;
        // maybe this can be like cost instead where each unit is worth a certain value
        // and the base scale increasing also decides the weights of each unit
        // like in rimworld
        var themeMult = Math.Max((theme.MinCount + theme.MaxCount)/2,1);
        var playerMult = 3 * Math.Log2(.2f * extractor.Participants - 1) + 1;
        var baseScale = 5 * Math.Log2(.2f * extractor.ConsecutiveWaves - 1) + 1;
        var scaling = themeMult*playerMult*baseScale;

        var count = (int)Math.Floor(_random.NextDouble(1f,1.25f)*scaling);
        // we spawn a base mob as position reference to spawn all the other mobs with SpawnNextToOrDrop()
        var baseMob = EntityManager.SpawnAtPosition(_random.Pick(theme.Mobs), spawnCoordinates);
        // ai set for all mobs
        var htn = Comp<HTNComponent>(baseMob);
        _npcFaction.AggroEntity(baseMob, extractorUid);
        _npc.SetBlackboard(baseMob, NPCBlackboard.FollowTarget,
            Transform(extractorUid).Coordinates, htn);
        _htn.Replan(htn);
        extractor.ActiveAttackers.Add(baseMob);

        SpawnMobWave(extractorUid, baseMob, extractor, theme, count);
        var bossWave =
            extractor.WavesStartedTotal + 1 >= extractor.BossWaveMinNumber &&
            theme.Bosses.Count > 0 &&
            _random.Prob(extractor.BossWaveChance);
        if (bossWave)
        {
            var bossScale = (int)(scaling / 5f) * extractor.BossWaveMinNumber;
            var bossCount = Math.Max(bossScale,1);
            SpawnMobWave(extractorUid, baseMob, extractor, theme, bossCount, bossWave);
        }

        extractor.NextWave = _timing.CurTick.Value + (uint) MathF.Round(_timing.TickRate*extractor.WaveMinSeconds);
        // extractor.WarningSent = false;
        // SetBeacon(extractorUid, extractor, true);
    }

    private void SpawnMobWave(EntityUid extractorUid, EntityUid baseMob, MaterialExtractorComponent extractor, MobWaveThemePrototype themePrototype, int count, bool bossWave = false)
    {
        for (var i = 0; i < count; i++)
        {
            var prototype = _random.Pick(themePrototype.Mobs);
            var attacker = EntityManager.SpawnNextToOrDrop(prototype,baseMob);
            extractor.ActiveAttackers.Add(attacker);

            if (bossWave)
            {
                var partySize = extractor.Participants;
                _bosses.ConfigureFinalGuardian(attacker,
                    GetBossFamily(themePrototype.Family, prototype),
                    prototype,
                    partySize,
                    new(_random.Next()));
            }

            var htn = Comp<HTNComponent>(attacker);
            _npcFaction.AggroEntity(attacker, extractorUid);
            _npc.SetBlackboard(attacker, NPCBlackboard.FollowTarget,
                Transform(extractorUid).Coordinates, htn);
            _htn.Replan(htn);
        }
    }

    private MobWaveThemePrototype SelectWaveTheme(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        var isNight = IsNight(extractorUid, extractor);
        var totalWeight = 0;
        Dictionary<MobWaveThemePrototype, float> themes = new(extractor.WaveThemes.Count);

        foreach (var t in extractor.WaveThemes)
        {
            var theme = _proto.Index(t);
            if (!isNight && theme.NightOnly)
                continue;

            totalWeight += theme.Weight;
            themes.Add(theme,theme.Weight);
        }
        return _random.Pick(themes);

    }

    private bool IsNight(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        if (Transform(extractorUid).MapUid is not { } mapUid ||
            !TryComp<DayNightCycleComponent>(mapUid, out var dayNight) ||
            dayNight.CycleDurationMinutes <= 0f)
            return false;

        var duration = dayNight.CycleDurationMinutes * 60f;
        var elapsed = MathF.Max(0f, (float) _timing.CurTime.TotalSeconds - dayNight.RoundStartTimeSeconds);
        var cycleTime = (elapsed + dayNight.StartOffset * duration) % duration / duration;
        return cycleTime >= extractor.NightStart && cycleTime < extractor.NightEnd;
    }


    private static ExpeditionMobFamily GetBossFamily(ExpeditionMobFamily themeFamily, string prototype)
    {
        if (prototype.Contains("Deathclaw", StringComparison.Ordinal))
            return ExpeditionMobFamily.Deathclaw;
        if (prototype.Contains("Radscorpion", StringComparison.Ordinal))
            return ExpeditionMobFamily.Radscorpion;

        return themeFamily;
    }


    private bool ValidSpawnTile(TileRef tile)
    {
        return !_turf.IsTileBlocked(tile, CollisionGroup.MobMask);
    }
    private bool TryFindWaveSpawnCoordinates(EntityUid extractorUid, MaterialExtractorComponent extractor, out EntityCoordinates spawnCoordinates)
    {
        spawnCoordinates = EntityCoordinates.Invalid;
        var extractorXform = Transform(extractorUid);

        if(extractorXform.MapUid is null || extractorXform.GridUid is null)
            return false;
        // MapGridComp of the map?
        if (!TryComp<MapGridComponent>(extractorXform.GridUid, out var gridComp))
            return false;

        var extractorPosition = _transform.GetWorldPosition(extractorUid);

        var maxPvsBox = new Box2(0,0,25,25).Translated(extractorPosition);
        var center = -maxPvsBox.Size/2f;
        maxPvsBox.BottomLeft += center;
        maxPvsBox.TopRight += center;

        var length = extractor.WaveSpawnMaxDistance - extractor.WaveSpawnMinDistance;
        HashSet<Entity<ActorComponent>> playersInRange = new(100);

        _lookup.GetEntitiesIntersecting(extractorXform.MapID,maxPvsBox,playersInRange, LookupFlags.Approximate);
        extractor.Participants = playersInRange.Count;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var angle = extractor.SearchPercent* MathF.Tau;
            extractor.SearchPercent += .1f;
            if (extractor.SearchPercent >= 1f)
                extractor.SearchPercent = 0f;

            var startPos = new Vector2(extractorPosition.X ,extractorPosition.Y + extractor.WaveSpawnMinDistance);
            var endPos = new Vector2(startPos.X, startPos.Y+length);
            Box2 box = new(startPos, endPos);
            Box2Rotated rayBox = new(box, angle,extractorPosition);

            var tiles = _map.GetTilesIntersecting(extractorXform.MapUid.Value,gridComp,rayBox, true, ValidSpawnTile );
            foreach(var tile in tiles)
            {
               var worldPos = _map.GridTileToWorld(tile.GridUid, gridComp, tile.GridIndices);
               if (IsInAnyPlayerPvs(worldPos, extractor.WaveSpawnPvsBuffer, playersInRange))
                   continue;
               // MapGridComp of the grid/tile??
               spawnCoordinates = _map.MapToGrid(tile.GridUid, worldPos);
               return true;
            }
        }

        return false;
    }

    private bool IsInAnyPlayerPvs(MapCoordinates candidate, float buffer, HashSet<Entity<ActorComponent>> players)
    {
        var basePvsRange = MathF.Sqrt( _cfg.GetCVar(CVars.NetMaxUpdateRange))/2f;
        foreach (var playerUid in players)
        {
            var pvsScale = TryComp<EyeComponent>(playerUid, out var eye) ? MathF.Max(eye.PvsScale, 1f) : 1f;
            var pvsRangeScaled = (basePvsRange+buffer) * pvsScale;
            if(_occulud.InRangeUnoccluded(candidate,_transform.GetMapCoordinates(playerUid), pvsRangeScaled, true))
                return true;
        }

        return false;
    }


    private void SetBeacon(EntityUid uid, MaterialExtractorComponent extractor, bool enabled)
    {
        extractor.BeaconOn = enabled;
        _lights.SetEnabled(uid, enabled);
    }
}
