namespace KubernetesVisualizer.Models.UI;

public sealed record ResolvedWikiSection(int Order, string Id, string Title, string GroupTitle, Type ComponentType);
public sealed record WikiHeading(string Id, string Title, string GroupTitle);
public sealed record WikiIndexGroup(string Title, int StartNumber, IReadOnlyList<WikiHeading> Headings);
