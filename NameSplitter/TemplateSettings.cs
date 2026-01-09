namespace NameSplitter;

public sealed record TemplateSettings(
    int TotalPages,
    int PagesPerRow,
    bool StartWithLeftPage,
    int PageSpacing,
    int RowSpacing,
    int PaddingX,
    int PaddingY
);
