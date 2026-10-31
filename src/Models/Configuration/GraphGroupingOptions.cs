namespace KubernetesVisualizer.Models.Configuration;

public sealed class GraphGroupingOptions
{
    public const string SectionName = "GraphGrouping";

    public string[] DefaultRelatedLabelKeys { get; set; } = [];
}
