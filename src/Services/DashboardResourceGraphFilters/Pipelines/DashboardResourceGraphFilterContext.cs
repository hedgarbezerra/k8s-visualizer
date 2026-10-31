using KubernetesVisualizer.Models.UI;

namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;

internal sealed class DashboardResourceGraphFilterContext(
    IReadOnlyList<KubernetesResourceGraphItem> sourceItems,
    DashboardResourceGraphFilterOptions options,
    IReadOnlyCollection<string> relatedLabelKeys)
{
    public DashboardResourceGraphFilterOptions Options { get; } = options;
    public IReadOnlyCollection<string> RelatedLabelKeys { get; } = relatedLabelKeys;
    public IReadOnlyList<KubernetesResourceGraphItem> CandidateItems { get; set; } = sourceItems;
    public IReadOnlyList<string> AvailableKinds { get; set; } = [];
    public HashSet<string> VisibleKinds { get; set; } = new(options.VisibleKinds, StringComparer.OrdinalIgnoreCase);
    public KubernetesResourceGraphRelations? Relations { get; set; }
    public HashSet<string> ActiveIds { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<KubernetesResourceGraphItem> Items { get; set; } = [];
    public IReadOnlyList<KubernetesGraphEdge> Edges { get; set; } = [];
}
