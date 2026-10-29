namespace Content.Server.NPC.Queries.Curves;


public sealed partial class ConstCurve : IUtilityCurve
{
    [DataField("constantSucceed")] public float ConstantOnSucceed = .5f;
    [DataField("constantFail")] public float ConstantOnFail = .1f;
}
