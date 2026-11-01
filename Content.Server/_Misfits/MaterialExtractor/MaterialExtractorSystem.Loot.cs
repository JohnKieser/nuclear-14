using Content.Shared._Misfits.EntityEffects;
using Content.Shared.Throwing;
using Robust.Shared.Random;


namespace Content.Server._Misfits.MaterialExtractor;


public sealed partial class MaterialExtractorSystem
{
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    private void ExtractorReward(MaterialExtractorComponent extractor)
    {
        var rewardSize = extractor.WavesInARow*10;
        Queue<string> protos = new(rewardSize);
        while (rewardSize > 0)
        {
            protos.Enqueue(_random.Pick(extractor.OutputWeights).Key);
            rewardSize--;
        }
        extractor.LootQueue = protos;
    }

    private void SpawnThrowLoot(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        var coord = Transform(extractorUid).Coordinates;
       var loot = SpawnAtPosition(extractor.LootQueue.Dequeue(), coord);
       coord = coord.Offset(_random.NextVector2(2f));
       _throwingSystem.TryThrow(loot, coord);
    }


}
