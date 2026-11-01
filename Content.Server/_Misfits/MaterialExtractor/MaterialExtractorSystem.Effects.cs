using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._Misfits.MaterialExtractor;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Power.Generator;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Utility;


namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>
/// stuff like visuals, lights, sound ect...
/// </summary>
public sealed partial class MaterialExtractorSystem
{
    private void ExtractorVFXInit()
    {
        SubscribeLocalEvent<MaterialExtractorComponent, GetVerbsEvent<AlternativeVerb>>(GetAlternativeVerb);
        SubscribeLocalEvent<MaterialExtractorComponent, ExaminedEvent>(OnExamined);
    }

    private void UpdateExtractorEffects(EntityUid uid, MaterialExtractorComponent extractor)
    {
        //UpdateUi(uid,extractor);
        PopupMessages(uid,extractor);
        if (_timing.CurTime >= extractor.NextPulse)
            TogglePulseEffect(uid, extractor);

        if ((extractor.State & ExtractorState.ExtractorMobWaveStopped) == 0 && _timing.CurTime > extractor.NextSubWaveWarning)
            WaveWarningEffect(uid, extractor);
    }
    private void SetBeacon(EntityUid uid, MaterialExtractorComponent extractor, bool enabled)
    {
        extractor.BeaconOn = enabled;
        _lights.SetEnabled(uid, enabled);
    }
    private void PopupMessages(EntityUid uid, MaterialExtractorComponent extractor)
    {
        LowFuelMsgToggle(uid, extractor);
    }
    private void OnExamined(Entity<MaterialExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("material-extractor-examine", ("quality", ent.Comp.DepositQuality.ToLowerInvariant())));
    }
    private void GetAlternativeVerb(Entity<MaterialExtractorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if ((ent.Comp.State & ExtractorState.Broken) != 0x0 || !args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => _ui.OpenUi(ent.Owner, MaterialExtractorUiKey.Key, user),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/zap.svg.192dpi.png")),
            Text = "Material extractor controls",
        });
    }
    private void WaveWarningEffect(EntityUid uid,MaterialExtractorComponent comp)
    {

        _audio.PlayPvs(ThumpSound, uid,
            AudioParams.Default.WithVolume(-3f).WithMaxDistance(30f));

        _chat.TrySendInGameICMessage(uid,
            Loc.GetString("material-extractor-rumble"),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);
    }
    private void TogglePulseEffect(EntityUid uid,MaterialExtractorComponent comp)
    {
        SetBeacon(uid, comp, !comp.BeaconOn);
        comp.NextPulse = _timing.CurTime + comp.PulseIntervalSeconds;
        if (!comp.BeaconOn)
            return;
        _audio.PlayPvs(ThumpSound, uid,
            AudioParams.Default.WithVolume(-7f).WithMaxDistance(22f));
    }



    private void SendLifecycleEmote(EntityUid uid, string localizationId)
    {
        _chat.SendAreaEmote(uid, Loc.GetString(localizationId), LifecycleEmoteRange);
    }
    private void UpdateUi(EntityUid uid, MaterialExtractorComponent extractor)
    {
        if (!_ui.IsUiOpen(uid, MaterialExtractorUiKey.Key) || !TryComp<FuelGeneratorComponent>(uid, out var fuelComp) ||
            !TryComp<DamageableComponent>(uid, out var dmgComp) || !TryComp<DestructibleComponent>(uid, out var destructibleComp))
            return;


        var hp = extractor.HealthThreshhold / dmgComp.TotalDamage;
        var canShutdown = (extractor.State & ExtractorState.ExtractorBlockUserShutdown) != 0x0;
        var running = fuelComp.On;
        var fuel = _generator.GetFuel(uid);
        var capacity = 0f;
        var fuelDepleted = fuel <= 0f;

        if (_solution.TryGetSolution(uid, extractor.FuelSolution, out _, out var tank))
            capacity = tank.MaxVolume.Float();


        _ui.SetUiState(uid, MaterialExtractorUiKey.Key,
            new MaterialExtractorUiState
            {
                CanShutdown = canShutdown,
                Running = running,
                Fuel = fuel,
                FuelCapacity = capacity,
                HealthPercent = hp.Float(),
                // Unattended = unattended,
                FuelDepleted = fuelDepleted,
            });
    }
}
