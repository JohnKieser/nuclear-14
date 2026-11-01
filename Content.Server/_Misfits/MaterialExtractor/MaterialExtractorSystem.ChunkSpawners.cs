using System.Collections.Frozen;
using System.Numerics;
using Content.Server.Worldgen.Components;
using Content.Shared.Ghost;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;


namespace Content.Server._Misfits.MaterialExtractor;


public sealed partial class MaterialExtractorSystem
{
    private void ChunkSpawnersInit()
    {
        SubscribeLocalEvent<MaterialExtractorComponent,SpawnChunkInPVS>(OnChunkEnterPVS);
        SubscribeLocalEvent<MaterialExtractorComponent,SpawnChunkOutsidePVS>(OnChunkExitPVS);
    }
    private void OnChunkEnterPVS(Entity<MaterialExtractorComponent> ent, ref SpawnChunkInPVS args)
    {
        DebugTools.Assert(ent.Comp.SpawnChunks.ContainsKey(args.Chunk));
        if (args.Loaders.All(HasComp<GhostComponent>))
            return;

        ent.Comp.ChunksInPVS.Add(args.Chunk);
        ent.Comp.ChunksOutsidePVS.Remove(args.Chunk);
    }

    private static void OnChunkExitPVS(Entity<MaterialExtractorComponent> ent, ref SpawnChunkOutsidePVS args)
    {
        DebugTools.Assert(ent.Comp.SpawnChunksTotal.Contains(args.Chunk));
        ent.Comp.ChunksInPVS.Remove(args.Chunk);
        ent.Comp.ChunksOutsidePVS.Add(args.Chunk);
    }

    private static readonly Angle[] SpawnAngles = new[] { new Angle(0),new(90), new(180), new Angle(270) };
    private const int ChunkSize = 256;
    private const int SpawnAreaScale = 2;

    private bool TryCacheSpawnChunks(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        DebugTools.Assert(extractor.WaveSpawnMinDistance < extractor.WaveSpawnMaxDistance &&
                          extractor is { WaveSpawnMinDistance: > 0, WaveSpawnMaxDistance: > 0, }); // jetbrains braining me

        var extractorXform = Transform(extractorUid);

        if(extractorXform.MapUid is null || extractorXform.MapID == MapId.Nullspace ||
            !TryComp<MapGridComponent>(extractorXform.GridUid, out var gridComp))
            return false;

        var spawnSideLength = (extractor.WaveSpawnMaxDistance - extractor.WaveSpawnMinDistance)*SpawnAreaScale;

        var chunkToTileDict =
            CacheSpawnChunksInRange(
            extractorUid,
            extractorXform.GridUid.Value,
            extractorXform.MapUid.Value,
            gridComp,
            extractor.WaveSpawnMinDistance,
            spawnSideLength);

        if (chunkToTileDict.Count <= 0)
            return false;

        extractor.SpawnChunks = chunkToTileDict.ToFrozenDictionary();
        RefreshAllChunkPVS(extractor);
        return true;
    }
    private bool ValidSpawnTile(TileRef tile)
    {
        return !_turf.IsTileBlocked(tile, CollisionGroup.MobMask);
    }

    private List<MapCoordinates> GetValidChunkTileMapCoords(EntityUid mapUid, MapGridComponent gridComp, EntityUid chunk)
    {
        List<MapCoordinates> coords = new();

        var xform = Transform(chunk);
        var coord = _transform.GetMapCoordinates(chunk, xform);
        var startPos = new Vector2(coord.X ,coord.Y);
        var endPos = new Vector2(coord.X+16 ,coord.Y+16);
        Box2 box = new(startPos, endPos);

        var tiles = _map.GetTilesIntersecting(mapUid,gridComp,box, true, ValidSpawnTile );
        coords = new(ChunkSize);
        foreach (var tile in tiles)
        {
            var worldPos = _map.GridTileToWorld(tile.GridUid, gridComp, tile.GridIndices);
            coords.Add(worldPos);
        }
        return coords;
    }
    private Dictionary<EntityUid, List<MapCoordinates>> CacheSpawnChunksInRange(
        EntityUid spawnerUid,
        EntityUid gridUid,
        EntityUid mapUid,
        MapGridComponent gridComp,
        float minSpawnDist,
        float spawnAreaLength
    )
    {
        DebugTools.Assert(minSpawnDist <= 0 || spawnAreaLength <= 0);
        var spawnerPos = _transform.GetWorldPosition(spawnerUid);
        var spawnArea = spawnAreaLength * spawnAreaLength;
        var numChunks = (int)MathF.Ceiling((spawnArea) / ChunkSize);

        // Tankies jetbrains
        var center = spawnerPos with { Y = spawnerPos.Y + minSpawnDist+spawnAreaLength/2f, };
        Dictionary<EntityUid, List<MapCoordinates>> chunkToTileDict = new(numChunks);


        foreach(var angle in SpawnAngles)
        {
            center = angle.RotateVec(center);
            var chunks = _chunkSys.GetChunksInRange(gridUid, center, spawnAreaLength);
            foreach (var chunk in chunks)
            {
                var tiles = GetValidChunkTileMapCoords(mapUid, gridComp, chunk);
                if (tiles.Count <= 0)
                    continue;
                EnsureComp<SpawnChunkComponent>(chunk).SpawnManager = spawnerUid;
                chunkToTileDict.TryAdd(chunk,tiles);
            }
        }

        return chunkToTileDict;
    }
    private void RefreshAllChunkPVS(MaterialExtractorComponent extractor)
    {
        extractor.ChunksInPVS = new(extractor.SpawnChunks.Count);
        extractor.ChunksOutsidePVS = new(extractor.SpawnChunks.Count);

        foreach (var chunk in extractor.SpawnChunks)
        {
            if (TryComp<LoadedChunkComponent>(chunk.Key, out var comp)
                && comp.Loaders is not null && comp.Loaders.Count > 0
                && !comp.Loaders.All(HasComp<GhostComponent>))
            {
                extractor.ChunksInPVS.Add(chunk.Key);
                continue;
            }
            extractor.ChunksOutsidePVS.Add(chunk.Key);
        }
    }
}
