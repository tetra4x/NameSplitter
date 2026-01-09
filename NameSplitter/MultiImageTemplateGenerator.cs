using System.Drawing.Drawing2D;
using ZXing;
using ZXing.Common;

namespace NameSplitter;

/// <summary>
/// 複数のページ画像を、テンプレート生成と同じ配置ルールで1枚の大きな画像にまとめる。
/// 生成画像にはQRコードを埋め込み、分割タブで同じ条件で切り出せるようにする。
///
/// - 背景としてテンプレート画像(左/右 または template.png)を敷き、
/// - 各ページ画像を「テンプレート1ページ枠」に対して縮小率をかけて中央配置する。
/// </summary>
public static class MultiImageTemplateGenerator
{
    public static Bitmap Generate(
        IReadOnlyList<string> pageImagePaths,
        TemplateSettings settings,
        string templateLeftPath,
        string templateRightPath,
        string templateFallbackPath,
        bool hasPair,
        int imageScalePercent
    )
    {
        if (pageImagePaths is null) throw new ArgumentNullException(nameof(pageImagePaths));
        if (pageImagePaths.Count <= 0) throw new ArgumentException("ページ画像が選択されていません。", nameof(pageImagePaths));

        if (settings.TotalPages != pageImagePaths.Count)
            throw new InvalidOperationException($"総ページ数({settings.TotalPages})と画像枚数({pageImagePaths.Count})が一致しません。");

        if (settings.PagesPerRow is not (2 or 4 or 6 or 8 or 10 or 12))
            throw new ArgumentOutOfRangeException(nameof(settings.PagesPerRow));
        if (settings.PageSpacing < 0) throw new ArgumentOutOfRangeException(nameof(settings.PageSpacing));
        if (settings.RowSpacing < 0) throw new ArgumentOutOfRangeException(nameof(settings.RowSpacing));
        if (settings.PaddingX < 0) throw new ArgumentOutOfRangeException(nameof(settings.PaddingX));
        if (settings.PaddingY < 0) throw new ArgumentOutOfRangeException(nameof(settings.PaddingY));
        if (imageScalePercent is < 10 or > 100)
            throw new ArgumentOutOfRangeException(nameof(imageScalePercent), "縮小率(%)は 10〜100 の範囲で指定してください。");

        // まずテンプレート側から「1ページの幅/高さ」を決める（分割時もこの値を使う）
        var (pageWidth, pageHeight) = GetTemplatePageSize(templateLeftPath, templateRightPath, templateFallbackPath, hasPair);

        // TemplateGenerator 側でレジストレーション用の外周余白を加算しているため、
        // ページ座標計算も同じ設定を使う。
        var layoutSettings = TemplateGenerator.WithRegistrationBorder(settings);

        // 背景(テンプレート + QR)を生成
        var baseSheet = hasPair
            ? TemplateGenerator.GenerateFromSeparateTemplates(templateLeftPath, templateRightPath, settings)
            : TemplateGenerator.Generate(templateFallbackPath, settings);

        // ページ画像を貼り付け
        using (var g = Graphics.FromImage(baseSheet))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var boxScale = imageScalePercent / 100f;
            var boxW = pageWidth * boxScale;
            var boxH = pageHeight * boxScale;

            for (var pageNo = 1; pageNo <= settings.TotalPages; pageNo++)
            {
                var (x, y) = TemplateGenerator.GetPageTopLeft(pageNo, pageWidth, pageHeight, layoutSettings);
                var pageRect = new Rectangle(x, y, pageWidth, pageHeight);

                var path = pageImagePaths[pageNo - 1];
                if (!File.Exists(path))
                    throw new FileNotFoundException("ページ画像が見つかりません。", path);

                using var img = ImageLoader.LoadBitmap(path);

                // 画像は「縮小率をかけた枠」に収まるように、縦横比維持でフィット
                var fit = Math.Min(boxW / img.Width, boxH / img.Height);
                // 画質劣化を避けるため、拡大はしない
                if (fit > 1f) fit = 1f;

                var drawW = (int)Math.Round(img.Width * fit);
                var drawH = (int)Math.Round(img.Height * fit);

                var drawX = pageRect.X + (pageRect.Width - drawW) / 2;
                var drawY = pageRect.Y + (pageRect.Height - drawH) / 2;

                g.DrawImage(img, new Rectangle(drawX, drawY, drawW, drawH));
            }
        }

        return baseSheet;
    }

    private static (int pageWidth, int pageHeight) GetTemplatePageSize(
        string templateLeftPath,
        string templateRightPath,
        string templateFallbackPath,
        bool hasPair
    )
    {
        if (hasPair)
        {
            if (!File.Exists(templateLeftPath)) throw new FileNotFoundException("左テンプレートが見つかりません。", templateLeftPath);
            if (!File.Exists(templateRightPath)) throw new FileNotFoundException("右テンプレートが見つかりません。", templateRightPath);

            using var left = ImageLoader.LoadBitmap(templateLeftPath);
            using var right = ImageLoader.LoadBitmap(templateRightPath);
            if (left.Width != right.Width || left.Height != right.Height)
                throw new InvalidOperationException("左/右テンプレートの画像サイズが一致していません。");
            return (left.Width, left.Height);
        }

        if (!File.Exists(templateFallbackPath))
            throw new FileNotFoundException("テンプレートが見つかりません。", templateFallbackPath);

        using var template = ImageLoader.LoadBitmap(templateFallbackPath);
        var isTwoPageTemplate = template.Width > template.Height;
        var pageWidth = isTwoPageTemplate ? template.Width / 2 : template.Width;
        var pageHeight = template.Height;
        return (pageWidth, pageHeight);
    }
}
