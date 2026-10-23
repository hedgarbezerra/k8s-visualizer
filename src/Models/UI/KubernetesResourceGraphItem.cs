namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesResourceOwnerReference(string Kind, string Name, string Uid, bool IsController);
public sealed record KubernetesLabelSelectorRequirement(string Key, string Operator, IReadOnlyList<string> Values);
public sealed record KubernetesResourceGraphReference(
    string Kind,
    string Name,
    string? Namespace,
    string Relationship,
    bool TargetIsParent,
    IReadOnlyDictionary<string, string>? Selector = null,
    IReadOnlyList<KubernetesLabelSelectorRequirement>? SelectorRequirements = null,
    string? ApiGroup = null);
public sealed record KubernetesResourceGraphComposition(string Kind, string Name, string Details, string Status);

public sealed record KubernetesResourceGraphItem(
    string Kind,
    string Name,
    string Namespace,
    string Uid,
    string GroupVersion,
    string ResourceName,
    string Status,
    IReadOnlyList<KubernetesResourceOwnerReference> OwnerReferences,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations,
    IReadOnlyDictionary<string, string> Selector,
    IReadOnlyList<KubernetesResourceGraphReference> References,
    IReadOnlyList<KubernetesResourceGraphComposition> Compositions,
    bool IsKubernetesSystemManaged = false,
    bool IsControllerManaged = false);

public sealed record KubernetesGraphNodeLayout(
    string Id,
    string Kind,
    string Name,
    string Status,
    double X,
    double Y,
    bool IsContextRoot,
    bool IsNamespaceRoot,
    string FunctionalGroup,
    string Details,
    bool HasChildren,
    bool IsCollapsed,
    bool IsComposition,
    KubernetesResourceGraphItem? Resource);

public sealed record KubernetesGraphEdge(string SourceId, string TargetId, string Relationship);
