namespace NameSplitter;

public sealed record TemplateTabState(
    int TotalPages,
    int PagesPerRow,
    bool StartWithLeftPage,
    int PageSpacing,
    int RowSpacing,
    string OutputFormat,
    int PaddingX = 0,
    int PaddingY = 0,
    string TemplateSet = ""
);
