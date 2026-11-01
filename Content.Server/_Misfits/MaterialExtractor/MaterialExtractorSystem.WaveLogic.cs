using Content.Shared._Misfits.Expeditions;
using Content.Shared._NC14.DayNightCycle;
using Content.Shared.Random.Helpers;
using Robust.Shared.Player;
using Robust.Shared.Utility;


namespace Content.Server._Misfits.MaterialExtractor;


public sealed partial class MaterialExtractorSystem
{
    private MobWaveThemePrototype NewWaveTheme(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        var isNight = IsNight(extractorUid, extractor);
        Dictionary<MobWaveThemePrototype, float> themes = new(extractor.WaveThemes.Count);

        foreach (var t in extractor.WaveThemes)
        {
            var theme = _proto.Index(t);
            if (!isNight && theme.NightOnly)
                continue;

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

    private void SetUpNextWaveTransition(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        ExtractorReward(extractor);
        extractor.WaveTheme = NewWaveTheme(extractorUid, extractor);
        extractor.WaveThemeID = extractor.WaveTheme.ID;

        var extractorPosition = _transform.GetWorldPosition(extractorUid);
        var maxPvsBox = new Box2(0,0,25,25).Translated(extractorPosition);
        var center = -maxPvsBox.Size/2f;
        maxPvsBox.BottomLeft += center;
        maxPvsBox.TopRight += center;

        HashSet<Entity<ActorComponent>> playersInRange = new(100);
        var extractorXform = Transform(extractorUid);
        _lookup.GetEntitiesIntersecting(extractorXform.MapID,maxPvsBox,playersInRange, LookupFlags.Approximate);
        extractor.Participants = playersInRange.Count;

        CountNextWaveTransition(extractor);
        UpdateDifficulty(extractor);
        extractor.State |= ExtractorState.OnWaveTransition;
    }

    private const float SubWaveWarningMult = .75f;
    private const float WaveTransitionHalfWayMsg = .5f;
    private void CountNextSubWave(MaterialExtractorComponent extractor)
    {
        extractor.SubWavesInARow++;
        extractor.SubWavesTotal++;
    }

    private void SetSubWaveTimers(MaterialExtractorComponent extractor)
    {
        DebugTools.Assert(extractor.SubWavesInARow <= extractor.SubWavePerWave);
        var subWaveInterval = _random.Next(extractor.SubWaveMinTime, extractor.SubWaveMaxTime);
        extractor.NextSubWave = _timing.CurTime + subWaveInterval;
        extractor.NextSubWaveWarning = _timing.CurTime + subWaveInterval * SubWaveWarningMult;
    }

    private void CountNextWaveTransition(MaterialExtractorComponent extractor)
    {
        extractor.WavesInARow++;
        extractor.WavesTotal++;
        extractor.SubWavesInARow = 0;

        var waveInterval = _random.Next(extractor.WaveMinTime, extractor.WaveMaxTime);

        extractor.NextWave = _timing.CurTime + waveInterval;
        extractor.NextWaveTransitionWarning = _timing.CurTime + waveInterval * WaveTransitionHalfWayMsg;
    }
}
