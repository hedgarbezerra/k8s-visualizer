namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesResourceOwnerReference(string Kind, string Name, string Uid, bool IsController,
    bool BlockOwnerDeletion = false);
public sealed record KubernetesLabelSelectorRequirement(string Key, string Operator, IReadOnlyList<string> Values);
public sealed record KubernetesResourceGraphReference(
    string Kind,
    string Name,
    string? Namespace,
    string Relationship,
    bool TargetIsParent,
    IReadOnlyDictionary<string, string>? Selector = null,
    IReadOnlyList<KubernetesLabelSelectorRequirement>? SelectorRequirements = null,
    string? ApiGroup = null,
    bool TargetNameIsPrefix = false);
public sealed record KubernetesResourceGraphComposition(string Kind, string Name, string Details, string Status,
    KubernetesResourceGraphReference? RelatedResource = null);

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
    bool IsControllerManaged = false,
    IReadOnlyList<string>? Finalizers = null);

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

public sealed record KubernetesGraphEdge(
    string SourceId,
    string TargetId,
    string Relationship,
    string Axis = "",
    string FieldPath = "",
    bool IsInferred = false,
    string? LabelKey = null,
    string? LabelValue = null);

public sealed record KubernetesResourceGraphData(
    IReadOnlyList<KubernetesResourceGraphItem> Items,
    IReadOnlyList<string> Failures);

public sealed record KubernetesResourceGraphRelations(
    IReadOnlyList<KubernetesGraphEdge> Edges,
    IReadOnlyDictionary<string, string> OwnerByUid);

public sealed record GraphLayoutEntry(string Id, string Kind, string Name, string Status, string Details,
    string Group, KubernetesResourceGraphItem? Resource, bool IsComposition, int Depth);

public sealed record GraphColumnHeader(double X, string Level, string Kinds);
