namespace Content.Shared._Misfits.Interaction.Components;

/// <summary>
/// Used for barricades as of time of writing.
/// Essentially on Comp init we check for parent in spawned TileRef in the same fixture layer
/// We parent onto that entity and init relayComp to redirect interactions
/// Most other cases such as melee are covered by the Overlapper itself
/// CANNOT exist without a parent that isnt a grid/map
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OverlapperComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid Parent =  EntityUid.Invalid;
}
