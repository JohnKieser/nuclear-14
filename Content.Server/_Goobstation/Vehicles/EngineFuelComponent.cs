using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;


namespace Content.Server.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class EngineFuelComponent : Component
{
    [DataField]
    public int DmgPerInvalidReagent = 10;
    [DataField]
    public ProtoId<DamageTypePrototype> DmgType = "EngineDamage";
}
