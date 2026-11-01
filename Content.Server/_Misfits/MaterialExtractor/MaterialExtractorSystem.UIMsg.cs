using Content.Shared._Misfits.MaterialExtractor;
using Content.Shared.Chat;
using Content.Shared.Power.Generator;


namespace Content.Server._Misfits.MaterialExtractor;


public sealed partial class MaterialExtractorSystem
{
    private void UIMessagesInit()
    {
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorStartMessage>(OnStartMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorStopMessage>(OnStopMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorEjectFuelMessage>(OnEjectFuelMessage);
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
        if ((ent.Comp.State & ExtractorState.ExtractorBlockUserShutdown) == 0x0
            && TryComp<FuelGeneratorComponent>(ent, out var generator))
            _generator.SetFuelGeneratorOn(ent, false, generator);
    }

    private void OnEjectFuelMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorEjectFuelMessage args)
    {
        if((ent.Comp.State & ExtractorState.ExtractorBlockUserShutdown) == 0x0)
            _generator.EmptyGenerator(ent);
    }

    private void LowFuelMsgToggle(EntityUid uid, MaterialExtractorComponent extractor)
    {
        if (!_solution.TryGetSolution(uid, extractor.FuelSolution, out _, out var fuelTank))
            return;

        if (fuelTank.FillFraction <= extractor.LowFuelWarningThreshold &&
            !extractor.LowFuelWarningIssued)
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
}
