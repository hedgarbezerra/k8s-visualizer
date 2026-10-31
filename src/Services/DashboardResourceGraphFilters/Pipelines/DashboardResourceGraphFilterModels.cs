using KubernetesVisualizer.Models.UI;

namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;

public sealed record DashboardResourceGraphFilterOptions(
    string FunctionalGroup,
    IReadOnlyCollection<string> VisibleKinds,
    bool HideKubernetesSystemResources,
    bool HideControllerManagedResources,
    bool HideStandaloneResources,
    bool HideAutomaticallyGeneratedResources);

public sealed record DashboardResourceGraphFilterResult(
    IReadOnlyList<KubernetesResourceGraphItem> CandidateItems,
    IReadOnlyList<KubernetesResourceGraphItem> Items,
    IReadOnlyList<string> AvailableKinds,
    IReadOnlyList<string> VisibleKinds,
    IReadOnlyList<KubernetesGraphEdge> Edges,
    IReadOnlyList<KubernetesGraphEdge> AllEdges,
    IReadOnlyDictionary<string, string> OwnerByUid);
