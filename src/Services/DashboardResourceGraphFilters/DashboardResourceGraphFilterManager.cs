using KubernetesVisualizer.Models.UI;
using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters;

/// <summary>
/// Applies dashboard filters through a fixed template method. Each stage owns one
/// filtering or graph-scope step and the stage order is explicit below.
/// </summary>
public sealed class DashboardResourceGraphFilterManager
{
    private static readonly IDashboardResourceGraphFilterStage[] Stages =
    [
        new ResourceIdentityFilterStage(),
        new HideKubernetesSystemResourcesStage(),
        new HideAutomaticallyGeneratedResourcesStage(),
        new HideControllerManagedResourcesStage(),
        new HideStandaloneResourcesStage(),
        new AvailableResourceKindsStage(),
        new BuildResourceRelationsStage(),
        new FunctionalGroupSeedStage(),
        new VisibleResourceKindsSeedStage(),
        new RelatedResourceExpansionStage(),
        new VisibleResourceKindsOutputStage()
    ];

    /// <summary>The template method that runs every dashboard filter stage in order.</summary>
    public DashboardResourceGraphFilterResult Apply(
        IReadOnlyList<KubernetesResourceGraphItem> sourceItems,
        DashboardResourceGraphFilterOptions options,
        IReadOnlyCollection<string> relatedLabelKeys)
    {
        var context = new DashboardResourceGraphFilterContext(sourceItems, options, relatedLabelKeys);
        foreach (var stage in Stages) stage.Apply(context);

        var relations = context.Relations!;
        return new DashboardResourceGraphFilterResult(
            context.CandidateItems,
            context.Items,
            context.AvailableKinds,
            context.VisibleKinds.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            context.Edges,
            relations.Edges,
            relations.OwnerByUid);
    }

}
