namespace NameSplitter;

public sealed record SplitTabState(
    string? LastInputPath,
    string OutputFormat
)
{
    public static SplitTabState Default { get; } = new(LastInputPath: null, OutputFormat: "png");
}
