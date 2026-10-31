using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class RelatedResourceExpansionStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        var relations = context.Relations!;
        foreach (var edge in relations.Edges.Where(edge => context.ActiveIds.Contains(edge.SourceId)
                                                            || context.ActiveIds.Contains(edge.TargetId)).ToArray())
        {
            context.ActiveIds.Add(edge.SourceId);
            context.ActiveIds.Add(edge.TargetId);
        }

        foreach (var uid in context.ActiveIds.ToArray())
        {
            var current = uid;
            var visitedOwnerChain = new HashSet<string>(StringComparer.Ordinal);
            while (relations.OwnerByUid.TryGetValue(current, out var ownerUid) && visitedOwnerChain.Add(ownerUid))
            {
                context.ActiveIds.Add(ownerUid);
                current = ownerUid;
            }
        }

        var dependentByOwner = relations.OwnerByUid
            .GroupBy(pair => pair.Value, pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var descendants = new Queue<string>(context.ActiveIds);
        while (descendants.TryDequeue(out var ownerId))
            foreach (var dependentId in dependentByOwner.GetValueOrDefault(ownerId, []))
                if (context.ActiveIds.Add(dependentId)) descendants.Enqueue(dependentId);
    }
}
