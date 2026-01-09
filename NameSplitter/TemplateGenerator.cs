using System.Drawing.Drawing2D;
using ZXing;
using ZXing.Common;

namespace NameSplitter;

public static class TemplateGenerator
{
    // 画像のずれ/回転/拡大縮小に対応するためのレジストレーション(位置合わせ)用マーカー
    // 既存レイアウトを壊さないよう、ユーザー指定の Padding に加算する形で外周余白を確保する。
    public const int RegistrationBorder = 160;
    public const int CornerMarkerSize = 150;
    public const int CornerMarkerMargin = 32;
    public const int PayloadQrSize = 300;
    public const int PayloadQrMargin = 32;

    public const string MarkerTlText = "NS_MARKER_TL";
    public const string MarkerTrText = "NS_MARKER_TR";
    public const string MarkerBlText = "NS_MARKER_BL";
    public const string MarkerBrText = "NS_MARKER_BR";

    public static TemplateSettings WithRegistrationBorder(TemplateSettings settings)
        => settings with
        {
            PaddingX = settings.PaddingX + RegistrationBorder,
            PaddingY = settings.PaddingY + RegistrationBorder
        };

    public static Bitmap GenerateFromSeparateTemplates(string templateLeftPath, string templateRightPath, TemplateSettings settings)
    {
        if (settings.TotalPages <= 0) throw new ArgumentOutOfRangeException(nameof(settings.TotalPages));
        if (settings.PagesPerRow is not (2 or 4 or 6 or 8 or 10 or 12))
            throw new ArgumentOutOfRangeException(nameof(settings.PagesPerRow));
        if (settings.PageSpacing < 0) throw new ArgumentOutOfRangeException(nameof(settings.PageSpacing));
        if (settings.RowSpacing < 0) throw new ArgumentOutOfRangeException(nameof(settings.RowSpacing));
        if (settings.PaddingX < 0) throw new ArgumentOutOfRangeException(nameof(settings.PaddingX));
        if (settings.PaddingY < 0) throw new ArgumentOutOfRangeException(nameof(settings.PaddingY));

        // 位置合わせ用の外周余白を追加
        settings = WithRegistrationBorder(settings);

        using var leftTemplate = ImageLoader.LoadBitmap(templateLeftPath);
        using var rightTemplate = ImageLoader.LoadBitmap(templateRightPath);

        if (leftTemplate.Width != rightTemplate.Width || leftTemplate.Height != rightTemplate.Height)
            throw new InvalidOperationException("左/右テンプレートの画像サイズが一致していません。");

        var pageWidth = leftTemplate.Width;
        var pageHeight = leftTemplate.Height;

        var slots = settings.TotalPages + (settings.StartWithLeftPage ? 1 : 0);
        var rows = (int)Math.Ceiling(slots / (double)settings.PagesPerRow);

        var pairCountPerRow = settings.PagesPerRow / 2;
        var pairGapsPerRow = Math.Max(0, pairCountPerRow - 1);
        var extraFirstSingleGap = settings.StartWithLeftPage ? 1 : 0;

        var contentWidth = (pageWidth * settings.PagesPerRow) + (settings.PageSpacing * (pairGapsPerRow + extraFirstSingleGap));
        var contentHeight = (pageHeight * rows) + (settings.RowSpacing * Math.Max(0, rows - 1));

        var canvasWidth = contentWidth + (settings.PaddingX * 2);
        var canvasHeight = contentHeight + (settings.PaddingY * 2);

        var bmp = new Bitmap(canvasWidth, canvasHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        for (var pageNo = 1; pageNo <= settings.TotalPages; pageNo++)
        {
            var (x, y) = GetPageTopLeft(pageNo, pageWidth, pageHeight, settings);

            // 右開き想定:
            // - 通常は 奇数=右 / 偶数=左
            // - 左ページ始まりの場合は先頭に空きが入るため、実ページの左右が1ページ分シフトする
            var isRightPage = settings.StartWithLeftPage ? (pageNo % 2 == 0) : (pageNo % 2 == 1);
            var src = isRightPage ? rightTemplate : leftTemplate;
            g.DrawImage(src, new Rectangle(x, y, pageWidth, pageHeight));
        }

        var payload = new TemplateQrPayload(
            PageWidth: pageWidth,
            PageHeight: pageHeight,
            TotalPages: settings.TotalPages,
            StartWithLeftPage: settings.StartWithLeftPage,
            PageSpacing: settings.PageSpacing,
            RowSpacing: settings.RowSpacing,
            PaddingX: settings.PaddingX,
            PaddingY: settings.PaddingY,
            CanvasWidth: bmp.Width,
            CanvasHeight: bmp.Height,
            PagesPerRow: settings.PagesPerRow,
            Rows: rows,
            PayloadQrSize: PayloadQrSize,
            PayloadQrMargin: PayloadQrMargin,
            CornerMarkerSize: CornerMarkerSize,
            CornerMarkerMargin: CornerMarkerMargin
        );

        // 位置合わせ用マーカー(3隅)を描画
        DrawCornerMarkers(g, bmp.Width, bmp.Height);

        // ペイロードQR(右上)
        using var qr = GenerateQrBitmap(payload.ToJson(), PayloadQrSize, PayloadQrSize);
        var qrX = bmp.Width - qr.Width - PayloadQrMargin;
        var qrY = PayloadQrMargin;
        g.DrawImage(qr, qrX, qrY, qr.Width, qr.Height);

        return bmp;
    }

    /// <summary>
    /// ネームテンプレートを生成する。
    /// template.png は「1ページ(縦長)」または「見開き2ページ(横長)」のどちらでも扱える。
    /// </summary>
    public static Bitmap Generate(string templatePngPath, TemplateSettings settings)
    {
        if (settings.TotalPages <= 0) throw new ArgumentOutOfRangeException(nameof(settings.TotalPages));
        if (settings.PagesPerRow is not (2 or 4 or 6 or 8 or 10 or 12))
            throw new ArgumentOutOfRangeException(nameof(settings.PagesPerRow));
        if (settings.PageSpacing < 0) throw new ArgumentOutOfRangeException(nameof(settings.PageSpacing));
        if (settings.RowSpacing < 0) throw new ArgumentOutOfRangeException(nameof(settings.RowSpacing));
        if (settings.PaddingX < 0) throw new ArgumentOutOfRangeException(nameof(settings.PaddingX));
        if (settings.PaddingY < 0) throw new ArgumentOutOfRangeException(nameof(settings.PaddingY));

        // 位置合わせ用の外周余白を追加
        settings = WithRegistrationBorder(settings);

        using var template = ImageLoader.LoadBitmap(templatePngPath);

        // 旧実装は「template.png が2ページ分(見開き)」である前提で常に幅を半分にしていた。
        // しかし実際の template.png が1ページ画像の場合、生成結果の横幅が想定より小さくなる。
        // ここでは、画像が横長(幅 > 高さ)なら見開き、縦長なら1ページとして自動判定する。
        var isTwoPageTemplate = template.Width > template.Height;

        var pageWidth = isTwoPageTemplate ? template.Width / 2 : template.Width;
        var pageHeight = template.Height;

        // 左ページ始まりの場合は先頭に1ページ分の空き(=スロットが1つ増える)
        var slots = settings.TotalPages + (settings.StartWithLeftPage ? 1 : 0);
        var rows = (int)Math.Ceiling(slots / (double)settings.PagesPerRow);

        // 2ページ(見開き相当のペア)ごとにスペースを入れる。
        // 例: PagesPerRow=6 ならペアは3つ、ペア間スペースは2つ。
        var pairCountPerRow = settings.PagesPerRow / 2;
        var pairGapsPerRow = Math.Max(0, pairCountPerRow - 1);

        // 左ページ始まりの場合は「先頭1ページの直後」にもスペースが入る。
        // キャンバス幅としては、追加スペースは常に「1ページ目と2ページ目の間」なので 1 を加算する。
        var extraFirstSingleGap = settings.StartWithLeftPage ? 1 : 0;

        var contentWidth = (pageWidth * settings.PagesPerRow) + (settings.PageSpacing * (pairGapsPerRow + extraFirstSingleGap));
        var contentHeight = (pageHeight * rows) + (settings.RowSpacing * Math.Max(0, rows - 1));

        var canvasWidth = contentWidth + (settings.PaddingX * 2);
        var canvasHeight = contentHeight + (settings.PaddingY * 2);

        var bmp = new Bitmap(canvasWidth, canvasHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        for (var pageNo = 1; pageNo <= settings.TotalPages; pageNo++)
        {
            var (x, y) = GetPageTopLeft(pageNo, pageWidth, pageHeight, settings);

            if (!isTwoPageTemplate)
            {
                // 1ページテンプレ: そのまま貼る
                g.DrawImage(template, new Rectangle(x, y, pageWidth, pageHeight));
                continue;
            }

            // 見開きテンプレ: 奇数ページが右、偶数ページが左
            var isRightPage = (pageNo % 2 == 1);
            var srcRect = isRightPage
                ? new Rectangle(pageWidth, 0, pageWidth, pageHeight)
                : new Rectangle(0, 0, pageWidth, pageHeight);

            g.DrawImage(template, new Rectangle(x, y, pageWidth, pageHeight), srcRect, GraphicsUnit.Pixel);
        }

        // QRに埋め込むページサイズは「1ページ分」。
        // (見開きテンプレの場合も 1ページ = 幅/2、高さ=そのまま)
        var payload = new TemplateQrPayload(
            PageWidth: pageWidth,
            PageHeight: pageHeight,
            TotalPages: settings.TotalPages,
            StartWithLeftPage: settings.StartWithLeftPage,
            PageSpacing: settings.PageSpacing,
            RowSpacing: settings.RowSpacing,
            PaddingX: settings.PaddingX,
            PaddingY: settings.PaddingY,
            CanvasWidth: bmp.Width,
            CanvasHeight: bmp.Height,
            PagesPerRow: settings.PagesPerRow,
            Rows: rows,
            PayloadQrSize: PayloadQrSize,
            PayloadQrMargin: PayloadQrMargin,
            CornerMarkerSize: CornerMarkerSize,
            CornerMarkerMargin: CornerMarkerMargin
        );

        // 位置合わせ用マーカー(3隅)を描画
        DrawCornerMarkers(g, bmp.Width, bmp.Height);

        // ペイロードQR(右上)
        using var qr = GenerateQrBitmap(payload.ToJson(), PayloadQrSize, PayloadQrSize);
        var qrX = bmp.Width - qr.Width - PayloadQrMargin;
        var qrY = PayloadQrMargin;
        g.DrawImage(qr, qrX, qrY, qr.Width, qr.Height);

        return bmp;
    }

    internal static (int x, int y) GetPageTopLeft(int pageNo, int pageWidth, int pageHeight, TemplateSettings settings)
    {
        var logicalIndex = settings.StartWithLeftPage ? pageNo + 1 : pageNo;
        var zeroBased = logicalIndex - 1;

        var row = zeroBased / settings.PagesPerRow;
        var colFromRight = zeroBased % settings.PagesPerRow;
        var col = (settings.PagesPerRow - 1) - colFromRight;

        var x = settings.PaddingX + (col * pageWidth) + (GetPairGapsBeforeCol(col, settings) * settings.PageSpacing);
        var y = settings.PaddingY + (row * pageHeight) + (row * settings.RowSpacing);
        return (x, y);
    }

    internal static int GetPairGapsBeforeCol(int col, TemplateSettings settings)
    {
        // col は左→右の0始まり。
        // 「2ページで1ペア」なので、ペア境界は (1,3,5,...) の列の次。
        var gaps = 0;
        for (var leftCol = 0; leftCol < col; leftCol++)
        {
            if (leftCol % 2 == 1) gaps++;
        }

        // 左ページ始まりの場合、先頭の「空き1ページ」の直後にもスペースが入れる。
        if (settings.StartWithLeftPage && col >= 1)
            gaps++;

        return gaps;
    }

    private static void DrawCornerMarkers(Graphics g, int canvasWidth, int canvasHeight)
    {
        // ペイロードQRは右上に配置するため、同じコーナー(TR)にはマーカーQRを置かない。
        // （同一領域に複数QRがあると、読み取り側がマーカーを先に検出してしまい
        //  ペイロードQR探索が安定しないケースがある）
        using var tl = GenerateQrBitmap(MarkerTlText, CornerMarkerSize, CornerMarkerSize);
        using var bl = GenerateQrBitmap(MarkerBlText, CornerMarkerSize, CornerMarkerSize);
        using var br = GenerateQrBitmap(MarkerBrText, CornerMarkerSize, CornerMarkerSize);

        var xL = CornerMarkerMargin;
        var xR = canvasWidth - CornerMarkerMargin - CornerMarkerSize;
        var yT = CornerMarkerMargin;
        var yB = canvasHeight - CornerMarkerMargin - CornerMarkerSize;

        g.DrawImage(tl, xL, yT, tl.Width, tl.Height);
        g.DrawImage(bl, xL, yB, bl.Width, bl.Height);
        g.DrawImage(br, xR, yB, br.Width, br.Height);
    }

    private static Bitmap GenerateQrBitmap(string text, int width, int height)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 0
            }
        };

        var pixelData = writer.Write(text);

        var bmp = new Bitmap(pixelData.Width, pixelData.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bmpData.Scan0, pixelData.Pixels.Length);
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }

        return bmp;
    }
}
