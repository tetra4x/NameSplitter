using System.Text.Json;
using System.Text.Json.Serialization;

namespace NameSplitter;

/// <summary>
/// テンプレート画像に埋め込むQRコードのペイロード。
///
/// ※後方互換のため、FromJson は不足プロパティを 0/false で補完する。
/// </summary>
public sealed record TemplateQrPayload(
    int PageWidth,
    int PageHeight,
    int TotalPages,
    bool StartWithLeftPage,
    int PageSpacing,
    int RowSpacing,
    int PaddingX = 0,
    int PaddingY = 0,
    // v2+ (optional)
    int CanvasWidth = 0,
    int CanvasHeight = 0,
    int PagesPerRow = 0,
    int Rows = 0,
    int PayloadQrSize = 0,
    int PayloadQrMargin = 0,
    int CornerMarkerSize = 0,
    int CornerMarkerMargin = 0
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    // QR用途のためキーを短縮する（読み取り側は旧キーも受理する）
    private static class Keys
    {
        public const string PageWidth = "w";
        public const string PageHeight = "h";
        public const string TotalPages = "n";
        public const string StartWithLeftPage = "l";
        public const string PageSpacing = "ps";
        public const string RowSpacing = "rs";
        public const string PaddingX = "px";
        public const string PaddingY = "py";

        // v2+ (optional)
        public const string CanvasWidth = "cw";
        public const string CanvasHeight = "ch";
        public const string PagesPerRow = "ppr";
        public const string Rows = "r";
        public const string PayloadQrSize = "qs";
        public const string PayloadQrMargin = "qm";
        public const string CornerMarkerSize = "ms";
        public const string CornerMarkerMargin = "mm";
    }

    private static object CompactObject(TemplateQrPayload p)
    {
        var d = new Dictionary<string, object>(16)
        {
            [Keys.PageWidth] = p.PageWidth,
            [Keys.PageHeight] = p.PageHeight,
            [Keys.TotalPages] = p.TotalPages,
            [Keys.StartWithLeftPage] = p.StartWithLeftPage,
            [Keys.PageSpacing] = p.PageSpacing,
            [Keys.RowSpacing] = p.RowSpacing,
        };

        if (p.PaddingX != 0) d[Keys.PaddingX] = p.PaddingX;
        if (p.PaddingY != 0) d[Keys.PaddingY] = p.PaddingY;

        if (p.CanvasWidth != 0) d[Keys.CanvasWidth] = p.CanvasWidth;
        if (p.CanvasHeight != 0) d[Keys.CanvasHeight] = p.CanvasHeight;
        if (p.PagesPerRow != 0) d[Keys.PagesPerRow] = p.PagesPerRow;
        if (p.Rows != 0) d[Keys.Rows] = p.Rows;
        if (p.PayloadQrSize != 0) d[Keys.PayloadQrSize] = p.PayloadQrSize;
        if (p.PayloadQrMargin != 0) d[Keys.PayloadQrMargin] = p.PayloadQrMargin;
        if (p.CornerMarkerSize != 0) d[Keys.CornerMarkerSize] = p.CornerMarkerSize;
        if (p.CornerMarkerMargin != 0) d[Keys.CornerMarkerMargin] = p.CornerMarkerMargin;

        return d;
    }

    public string ToJson() => JsonSerializer.Serialize(CompactObject(this), JsonOptions);

    public static TemplateQrPayload FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            static int GetInt2(JsonElement root, string shortKey, string longKey, int fallback = 0)
            {
                if (root.TryGetProperty(shortKey, out var p) && p.ValueKind == JsonValueKind.Number)
                    return p.GetInt32();
                if (root.TryGetProperty(longKey, out p) && p.ValueKind == JsonValueKind.Number)
                    return p.GetInt32();
                return fallback;
            }

            static bool GetBool2(JsonElement root, string shortKey, string longKey, bool fallback = false)
            {
                if (root.TryGetProperty(shortKey, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
                    return p.GetBoolean();
                if (root.TryGetProperty(longKey, out p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
                    return p.GetBoolean();
                return fallback;
            }

            return new TemplateQrPayload(
                PageWidth: GetInt2(root, Keys.PageWidth, nameof(PageWidth)),
                PageHeight: GetInt2(root, Keys.PageHeight, nameof(PageHeight)),
                TotalPages: GetInt2(root, Keys.TotalPages, nameof(TotalPages)),
                StartWithLeftPage: GetBool2(root, Keys.StartWithLeftPage, nameof(StartWithLeftPage)),
                PageSpacing: GetInt2(root, Keys.PageSpacing, nameof(PageSpacing)),
                RowSpacing: GetInt2(root, Keys.RowSpacing, nameof(RowSpacing)),
                PaddingX: GetInt2(root, Keys.PaddingX, nameof(PaddingX)),
                PaddingY: GetInt2(root, Keys.PaddingY, nameof(PaddingY)),
                CanvasWidth: GetInt2(root, Keys.CanvasWidth, nameof(CanvasWidth)),
                CanvasHeight: GetInt2(root, Keys.CanvasHeight, nameof(CanvasHeight)),
                PagesPerRow: GetInt2(root, Keys.PagesPerRow, nameof(PagesPerRow)),
                Rows: GetInt2(root, Keys.Rows, nameof(Rows)),
                PayloadQrSize: GetInt2(root, Keys.PayloadQrSize, nameof(PayloadQrSize)),
                PayloadQrMargin: GetInt2(root, Keys.PayloadQrMargin, nameof(PayloadQrMargin)),
                CornerMarkerSize: GetInt2(root, Keys.CornerMarkerSize, nameof(CornerMarkerSize)),
                CornerMarkerMargin: GetInt2(root, Keys.CornerMarkerMargin, nameof(CornerMarkerMargin))
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid QR payload.", ex);
        }
    }
}
