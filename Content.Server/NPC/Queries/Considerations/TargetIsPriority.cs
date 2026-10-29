namespace Content.Server.NPC.Queries.Considerations;


public sealed partial class TargetIsPriority : UtilityConsideration
{
    [DataField("target")] public string Target = "PriorityTargetEntity";
}
