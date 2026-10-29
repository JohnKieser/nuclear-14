using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.Queries;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.HTN.PrimitiveTasks;
/// <summary>
/// OPTIONAL DETIALED INFO ON THIS STUFF LOL IF U DONT GET IT. IT HELPED ME A TON TO WRITE THIS OUT FOR MYSELF
/// --
/// Compound tasks hold primitive tasks and primitive tasks here hold mob actions and their "senses"
/// together a group of primitives oughta define an intended/complex behavior
/// Primtives are decided in sequence on a per branch basis. So one primitive failing/not meeting a precon invalidates the other ones in the same branch(so next branch checked)
///
/// Main way primitives work are through Operators and Services.
/// ---
/// - Operators run WHEN FIRST SELECTED AND WHEN SHUTDOWN. One per primitive. They give results/updates that decide branch traversal(which is what plans kinda are)
/// - Continue result means that primitive will be the NPC's current primitive and so they'll run each update until they give a finish or fail result
/// - Finished means goto/check next primitive or replan if none
/// - Failed means replan(or check previous level branches?)
///
/// - Services run periodically. Multiple can be ran for a primitive
///---
/// What purpose do these serve? Depends but AS A RULE OF THUMB!!!!(need a good reason to go aganist this)
///
/// --- Operators update more LONG TERM/RARELY UPDATED blackboard info/actions and/or info super relavent for TRAVERSING/SEQUENCING off into other tasks
/// -- so you MAY want a sequence of primitives to update specific info and have specific checks that the previous primitive have
/// -- OR!!!!
/// -- They can be the main states or stages for behavior an NPC can have!!!
/// -- Sometimes tho the behavior an operator triggered can last longer than the operator itself
/// -- (MoveToOperator is an exmaple. THe NPC will steer to the set range since the steerer lasts longer than the plan at times)
///
/// --- Services often update and DO short-term/immediate information/actions. Think reactive actions or behavior
/// - ex) this is how NPCs are able to attack, zig-zag/juking and generally be reactive
///
/// Other things:
/// -- "Effects": are what gets updated in the blackboard when a primitive starts(passes precons)
/// -- preconditions: conditions that need to be true before a primitive task is even started. if it fails nothing gets updated and the next branch is looked at
/// -- applyEffectsOnStartup: kinda self explainatory. It just means if we should reuse the old data from when this task was last ran.
///                           useful if you want tasks to be more "static" state or stage and not something that updates info each time it is checked
/// </summary>
public sealed partial class HTNPrimitiveTask : HTNTask
{
    /// <summary>
    /// Should we re-apply our blackboard state as a result of our operator during startup?
    /// This means you can re-use old data, e.g. re-using a pathfinder result, and avoid potentially expensive operations.
    /// </summary>
    [DataField("applyEffectsOnStartup")] public bool ApplyEffectsOnStartup = true;

    /// <summary>
    /// What needs to be true for this task to be able to run.
    /// The operator may also implement its own checks internally as well if every primitive task using it requires it.
    /// </summary>
    [DataField("preconditions")] public List<HTNPrecondition> Preconditions = new();
    /// <summary>
    /// On precons being true, we startup/initiate the operator which runs logic/updates
    /// and also can do that on shutdown if needed(like cleanup)
    ///
    /// </summary>
    [DataField("operator", required:true)] public HTNOperator Operator = default!;

    /// <summary>
    /// Services actively tick and can potentially update keys, such as combat target.
    /// </summary>
    [DataField("services")] public List<UtilityService> Services = new();

    public override string ToString()
    {
        return $"Primitive: {Operator}";
    }
}
