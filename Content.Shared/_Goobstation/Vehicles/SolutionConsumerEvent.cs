using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;


[ByRefEvent]
public record struct SolutionConsumedEv(Solution SolutionSource, FixedPoint2 AmountToConsume,
    List<ProtoId<ReagentPrototype>> AllowedReagents, bool Handled = false);
