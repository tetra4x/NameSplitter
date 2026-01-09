namespace NameSplitter;

/// <summary>
/// 複数画像テンプレート化タブの設定状態。
/// </summary>
public sealed record MultiTemplateTabState(
    string? LastInputDirectory,
    int PagesPerRow,
    bool StartWithLeftPage,
    int PageSpacing,
    int RowSpacing,
    string OutputFormat,
    int PaddingX = 0,
    int PaddingY = 0,
    string TemplateSet = "",
    int ImageScalePercent = 100
)
{
    public static MultiTemplateTabState Default { get; } = new(
        LastInputDirectory: null,
        PagesPerRow: 6,
        StartWithLeftPage: false,
        PageSpacing: 20,
        RowSpacing: 30,
        OutputFormat: "png",
        PaddingX: 0,
        PaddingY: 0,
        TemplateSet: "",
        ImageScalePercent: 100
    );
}
