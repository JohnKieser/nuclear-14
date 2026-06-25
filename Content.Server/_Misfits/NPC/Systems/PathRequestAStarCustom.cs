using System.Threading;
using Content.Server._Misfits.NPC.Components;
using Robust.Shared.Map;

namespace Content.Server.NPC.Pathfinding;

// just like AStarPathRequest, but depends on a comp for yaml customization
public sealed class AStarCustomPathRequest : PathRequest
{

    public EntityCoordinates End;

    /// <summary>
    /// How close we need to be to the end node to be considered as arrived.
    /// </summary>
    public float Distance;

    // Misfit Change: comp
    // further custom stuff can be added here
    public PathfindingCustomizableComponent Comp;
    public AStarCustomPathRequest(
        EntityCoordinates start,
        EntityCoordinates end,
        PathFlags flags,
        float distance,
        int layer,
        int mask,
        PathfindingCustomizableComponent comp,
        CancellationToken cancelToken) : base(start, flags, layer, mask, cancelToken)
    {
        Distance = distance;
        End = end;
        Comp = comp;
    }



}
