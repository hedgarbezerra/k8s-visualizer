namespace KubernetesVisualizer.Models.UI;

public abstract record WikiBlock;
public sealed record WikiParagraphBlock(string TextKey) : WikiBlock;
public sealed record WikiHeadingBlock(string TextKey, int Level) : WikiBlock;
public sealed record WikiListItemBlock(string TextKey) : WikiBlock;
public sealed record WikiQuoteBlock(string TextKey) : WikiBlock;
public sealed record WikiCodeItem(string Language, string Code) : WikiBlock;
public sealed record WikiDiagramItem(string Source) : WikiBlock;
public sealed record WikiReferenceBlock(string Href, string Label) : WikiBlock;
public sealed record WikiInternalLinkBlock(string Href, string LabelKey) : WikiBlock;
public sealed record WikiComparisonItem(string LeftTitleKey, string LeftTextKey, string RightTitleKey, string RightTextKey) : WikiBlock;
public sealed record WikiSectionModel(string Id, string Title, string GroupTitle);
public sealed record WikiSectionDefinition(int Order, string Id, string TitleKey, string GroupKey, Type ComponentType);
