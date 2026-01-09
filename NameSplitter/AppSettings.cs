namespace NameSplitter;

public sealed record AppSettings(
    TemplateTabState Template,
    SplitTabState Split,
    MultiTemplateTabState? MultiTemplate = null
)
{
    public static AppSettings Default { get; } = new(
        Template: new TemplateTabState(
            TotalPages: 8,
            PagesPerRow: 6,
            StartWithLeftPage: false,
            PageSpacing: 20,
            RowSpacing: 30,
            OutputFormat: "png",
            PaddingX: 0,
            PaddingY: 0,
            TemplateSet: ""
        ),
        Split: new SplitTabState(LastInputPath: null, OutputFormat: "png"),
        MultiTemplate: MultiTemplateTabState.Default
    );

    public MultiTemplateTabState MultiTemplateResolved => MultiTemplate ?? MultiTemplateTabState.Default;
}
