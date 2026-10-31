using KubernetesVisualizer.Services;
using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class BuildResourceRelationsStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context) =>
        context.Relations = ResourceGraphService.BuildRelations(context.CandidateItems, context.RelatedLabelKeys);
}
