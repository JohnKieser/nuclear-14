namespace Content.Server._Misfits.NPC.Components;
/// values that influence NPC pathfinding
[RegisterComponent]
public sealed partial class PathfindingCustomizableComponent : Component
{
    /// <summary>
    /// each node is a tile. So how "far" an NPC will scan to pathfind somewhere.
    /// Might be cool for near sighted NPCs and prolly saves preformance
    /// Not a good idea to have more than 1 NPC with a high limit and
    /// high view range
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public int NodeLimit { get; set; } = 512;
    /// <summary>
    /// h score is cost of a tile based on its distance to NPC
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float hScoreModifier { get; set; } = 1.001f;


    /// Each tile has a path cost depending on what's on it like a wall
    /// higher cost means NPCs are less likely to travel or do some action to go through it
    /// but they still can if they have the flags for it

    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float BaseCost { get; set; } = 1f;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float DoorOpenCost { get; set; } = 0.5f;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float DoorPryCost { get; set; } = 10f;
    /// cost of smashable walls are wall's remaining health / WallSmashCostDivisor
    /// use to be hardcoded as 100f
    /// which made NPCs not consider going around walls
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float WallSmashCostDivisor { get; set; } = 10f;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float ClimbCost { get; set; } = 0.5f;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField, DataField]
    public float NoObstacleCost { get; set; } = 0f;



}
