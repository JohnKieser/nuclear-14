namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>
/// Tracks chunks relavent for intelligent spawning of things.
/// At first used for extractor to spawn NPCs outside PVS. Soon will be used for POIs by a manager prolly
/// </summary>
[RegisterComponent]
public sealed partial class SpawnChunkComponent : Component
{
    public EntityUid SpawnManager = EntityUid.Invalid;
}
