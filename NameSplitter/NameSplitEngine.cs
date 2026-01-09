using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using ZXing;
using ZXing.Common;

namespace NameSplitter;

public static class NameSplitEngine
{
    public sealed record SplitResult(
        TemplateQrPayload Payload,
        int PagesPerRow,
        int Rows,
        string ExportDirectory
    );

    /// <summary>
    /// QRの情報を読み取り、ネーム画像をページ毎に分割してExportフォルダに保存する。
    /// </summary>
    public static SplitResult SplitIntoPages(string inputImagePath, string exportDirectory, string outputFormat = "png")
    {
        if (!File.Exists(inputImagePath)) throw new FileNotFoundException("Input image not found.", inputImagePath);
        Directory.CreateDirectory(exportDirectory);
        // Exportフォルダの内容をすべて削除
        foreach (var file in Directory.GetFiles(exportDirectory))
        {
            try { File.Delete(file); } catch { }
        }

        using var source = ImageLoader.LoadBitmap(inputImagePath);

        // 1) まずは通常通りペイロードQRを探す
        TemplateQrPayload payload;
        Result payloadResult;
        try
        {
            (payload, payloadResult) = DecodePayloadQr(source);
        }
        catch
        {
            // 2) ペイロードQRが読めない場合でも、四隅マーカー(3点)から傾き補正して再読取を試みる
            using var normalizedByMarkersOnly = TryNormalizeByCornerMarkersOnly(source);
            if (normalizedByMarkersOnly is null)
                throw;

            (payload, payloadResult) = DecodePayloadQr(normalizedByMarkersOnly);

            // 以後の処理は補正後画像で行う
            using var normalized = TryNormalizeByMarkers(normalizedByMarkersOnly, payload, payloadResult) ?? null;
            var working2 = normalized ?? normalizedByMarkersOnly;

            return SplitIntoPagesCore(
                payload: payload,
                payloadResult: payloadResult,
                working: working2,
                exportDirectory: exportDirectory,
                outputFormat: outputFormat);
        }

        // 通常ルート: ペイロードQRが読めた
        using var normalizedFull = TryNormalizeByMarkers(source, payload, payloadResult) ?? null;
        var working = normalizedFull ?? source;

        return SplitIntoPagesCore(
            payload: payload,
            payloadResult: payloadResult,
            working: working,
            exportDirectory: exportDirectory,
            outputFormat: outputFormat);
    }

    private static SplitResult SplitIntoPagesCore(
        TemplateQrPayload payload,
        Result payloadResult,
        Bitmap working,
        string exportDirectory,
        string outputFormat)
    {
        var fmt = string.IsNullOrWhiteSpace(outputFormat) ? "png" : outputFormat.ToLowerInvariant();
        if (fmt is not ("png" or "jpg" or "jpeg"))
            fmt = "png";
        var ext = (fmt == "jpeg") ? "jpg" : fmt;
        var imageFormat = (ext == "jpg") ? ImageFormat.Jpeg : ImageFormat.Png;

        var pagesPerRow = payload.PagesPerRow > 0
            ? payload.PagesPerRow
            : InferPagesPerRow(
                canvasWidth: working.Width,
                pageWidth: payload.PageWidth,
                pageSpacing: payload.PageSpacing,
                startWithLeftPage: payload.StartWithLeftPage,
                paddingX: payload.PaddingX
            );

        var settings = new TemplateSettings(
            TotalPages: payload.TotalPages,
            PagesPerRow: pagesPerRow,
            StartWithLeftPage: payload.StartWithLeftPage,
            PageSpacing: payload.PageSpacing,
            RowSpacing: payload.RowSpacing,
            PaddingX: payload.PaddingX,
            PaddingY: payload.PaddingY
        );

        // 行数推定（高さから逆算）
        var rows = payload.Rows > 0
            ? payload.Rows
            : InferRows(
                canvasHeight: working.Height,
                pageHeight: payload.PageHeight,
                rowSpacing: payload.RowSpacing,
                totalPages: payload.TotalPages,
                pagesPerRow: pagesPerRow,
                startWithLeftPage: payload.StartWithLeftPage
            );

        for (var pageNo = 1; pageNo <= payload.TotalPages; pageNo++)
        {
            var (x, y) = TemplateGenerator.GetPageTopLeft(pageNo, payload.PageWidth, payload.PageHeight, settings);

            var rect = new Rectangle(x, y, payload.PageWidth, payload.PageHeight);
            if (rect.Right > working.Width || rect.Bottom > working.Height)
            {
                throw new InvalidOperationException(
                    $"Split rectangle out of bounds. page={pageNo}, rect={rect}, source={working.Width}x{working.Height}");
            }

            using var page = new Bitmap(payload.PageWidth, payload.PageHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(page))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(working, new Rectangle(0, 0, payload.PageWidth, payload.PageHeight), rect, GraphicsUnit.Pixel);
            }

            var fileName = $"{pageNo:D3}.{ext}";
            var outPath = Path.Combine(exportDirectory, fileName);
            page.Save(outPath, imageFormat);
        }

        return new SplitResult(payload, pagesPerRow, rows, exportDirectory);
    }

    private static ZXing.Windows.Compatibility.BarcodeReader CreateQrReader()
        => new()
        {
            AutoRotate = true,
            TryInverted = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                CharacterSet = "UTF-8"
            }
        };

    private static IEnumerable<Result> DecodeAll(ZXing.Windows.Compatibility.BarcodeReader reader, Bitmap source)
    {
        // ZXing の Decode() は「最初に見つかった1つ」しか返さない。
        // テンプレート画像は四隅マーカーQR + ペイロードQR が同居するため、
        // マーカーが先に拾われてペイロード探索が失敗するケースがある。
        // 可能なら DecodeMultiple() を使い、ペイロード(JSON)を優先して採用する。
        try
        {
            var multi = reader.DecodeMultiple(source);
            if (multi is { Length: > 0 })
                return multi;
        }
        catch
        {
            // bindings により DecodeMultiple が内部で失敗することがあるので、単発Decodeへフォールバック
        }

        var single = reader.Decode(source);
        return single is null ? Array.Empty<Result>() : new[] { single };
    }

    private static (TemplateQrPayload payload, Result result) DecodePayloadQr(Bitmap source)
    {
        var reader = CreateQrReader();

        // まず全体をスキャン（複数QRがある前提で、ペイロード(JSON)を探す）
        foreach (var r in DecodeAll(reader, source))
        {
            if (r?.Text is null) continue;
            if (TryParsePayload(r.Text, out var payload1))
                return (payload1, r);
        }

        // 角のマーカーQRが先に拾われる場合があるため、四隅領域も順にスキャンしてペイロード(JSON)を探す
        foreach (var (rect, _) in GetCornerRegions(source.Width, source.Height))
        {
            using var crop = CropBitmap(source, rect);
            foreach (var r in DecodeAll(reader, crop))
            {
                if (r?.Text is null) continue;
                if (!TryParsePayload(r.Text, out var payload)) continue;
                // crop 座標系の ResultPoints を全体座標へ戻す
                var shifted = ShiftResultPoints(r, rect.X, rect.Y);
                return (payload, shifted);
            }
        }

        throw new InvalidOperationException("QRコード(ペイロード)を読み取れませんでした。テンプレート出力画像のQRが写っているか確認してください。");
    }

    private static bool TryParsePayload(string text, out TemplateQrPayload payload)
    {
        try
        {
            payload = TemplateQrPayload.FromJson(text);
            // ざっくり妥当性チェック
            return payload.TotalPages > 0 && payload.PageWidth > 0 && payload.PageHeight > 0;
        }
        catch
        {
            payload = new TemplateQrPayload(0, 0, 0, false, 0, 0);
            return false;
        }
    }

    private static Result ShiftResultPoints(Result r, int dx, int dy)
    {
        if (r is null) return null;

        var srcPts = r.ResultPoints;
        if (srcPts is null || srcPts.Length == 0)
            return r;

        var pts = srcPts
            .Select(p => new ResultPoint(p.X + dx, p.Y + dy))
            .ToArray();

        // 新しい Result を作る（ResultPoints は setter がないため）
        var rr = new Result(r.Text, r.RawBytes, pts, r.BarcodeFormat);

        // ✅ PutAllMetadata は無いので、ResultMetadata を手でコピーする
        // （null の可能性もあるのでガード）
        var md = r.ResultMetadata;
        if (md != null)
        {
            foreach (var kv in md)
            {
                rr.ResultMetadata[kv.Key] = kv.Value;
            }
        }

        // ✅ Timestamp は read-only なので触らない（自動で入る/入らないは実装依存）
        return rr;
    }

    private static Bitmap? TryNormalizeByMarkers(Bitmap source, TemplateQrPayload payload, Result payloadQrResult)
    {
        // v2 の情報がない(=旧テンプレ)場合は補正できないのでスキップ
        var ppr = payload.PagesPerRow > 0
            ? payload.PagesPerRow
            : InferPagesPerRow(
                canvasWidth: source.Width,
                pageWidth: payload.PageWidth,
                pageSpacing: payload.PageSpacing,
                startWithLeftPage: payload.StartWithLeftPage,
                paddingX: payload.PaddingX);

        var rows = payload.Rows > 0
            ? payload.Rows
            : InferRows(
                canvasHeight: source.Height,
                pageHeight: payload.PageHeight,
                rowSpacing: payload.RowSpacing,
                totalPages: payload.TotalPages,
                pagesPerRow: ppr,
                startWithLeftPage: payload.StartWithLeftPage);

        var (expectedW, expectedH) = payload.CanvasWidth > 0 && payload.CanvasHeight > 0
            ? (payload.CanvasWidth, payload.CanvasHeight)
            : ComputeCanvasSize(payload, ppr, rows);

        if (expectedW <= 0 || expectedH <= 0)
            return null;

        var qrSize = payload.PayloadQrSize > 0 ? payload.PayloadQrSize : TemplateGenerator.PayloadQrSize;
        var qrMargin = payload.PayloadQrMargin > 0 ? payload.PayloadQrMargin : TemplateGenerator.PayloadQrMargin;
        var markerSize = payload.CornerMarkerSize > 0 ? payload.CornerMarkerSize : TemplateGenerator.CornerMarkerSize;
        var markerMargin = payload.CornerMarkerMargin > 0 ? payload.CornerMarkerMargin : TemplateGenerator.CornerMarkerMargin;

        var observed = new Dictionary<string, PointF>
        {
            ["PAYLOAD"] = GetResultCenter(payloadQrResult)
        };

        var reader = CreateQrReader();
        foreach (var (rect, _cornerName) in GetCornerRegions(source.Width, source.Height))
        {
            using var crop = CropBitmap(source, rect);
            foreach (var r in DecodeAll(reader, crop))
            {
                if (r?.Text is null) continue;
                var c = GetResultCenter(r);
                observed[r.Text] = new PointF(c.X + rect.X, c.Y + rect.Y);
            }
        }

        // 新テンプレ前提
        var expectedTl = new PointF(markerMargin + (markerSize / 2f), markerMargin + (markerSize / 2f));
        var expectedTr = new PointF(expectedW - markerMargin - (markerSize / 2f), markerMargin + (markerSize / 2f));
        var expectedBl = new PointF(markerMargin + (markerSize / 2f), expectedH - markerMargin - (markerSize / 2f));
        var expectedBr = new PointF(expectedW - markerMargin - (markerSize / 2f), expectedH - markerMargin - (markerSize / 2f));
        var expectedPayload = new PointF(expectedW - qrMargin - (qrSize / 2f), qrMargin + (qrSize / 2f));

        var expected = new Dictionary<string, PointF>
        {
            ["PAYLOAD"] = expectedPayload,
            [TemplateGenerator.MarkerTlText] = expectedTl,
            [TemplateGenerator.MarkerTrText] = expectedTr,
            [TemplateGenerator.MarkerBlText] = expectedBl,
            [TemplateGenerator.MarkerBrText] = expectedBr
        };

        var pairs = new List<(PointF src, PointF dst)>();
        foreach (var (k, dst) in expected)
        {
            if (observed.TryGetValue(k, out var src))
                pairs.Add((src, dst));
        }

        // アフィンは最低3点必要
        if (pairs.Count < 3)
            return null;

        var p = SolveLeastSquaresAffine(pairs);

        var a = (float)p[0];
        var b = (float)p[1];
        var c0 = (float)p[2];
        var d = (float)p[3];
        var e = (float)p[4];
        var f0 = (float)p[5];

        var dest = new Bitmap(expectedW, expectedH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dest))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var m = new Matrix(a, d, b, e, c0, f0);
            g.Transform = m;
            g.DrawImage(source, 0, 0);
        }

        return dest;
    }

    private static Bitmap? TryNormalizeByCornerMarkersOnly(Bitmap source)
    {
        // ペイロードQRが読めない場合の救済:
        // 4隅マーカー(TL/TR/BL/BR)を検出してアフィン変換を推定し、角度/傾きを補正した画像を作る。
        // この段階ではペイロードを持たないため、キャンバスサイズは入力画像と同一とする。

        var expectedW = source.Width;
        var expectedH = source.Height;

        var markerSize = TemplateGenerator.CornerMarkerSize;
        var markerMargin = TemplateGenerator.CornerMarkerMargin;

        var expected = new Dictionary<string, PointF>
        {
            [TemplateGenerator.MarkerTlText] = new PointF(markerMargin + (markerSize / 2f), markerMargin + (markerSize / 2f)),
            [TemplateGenerator.MarkerTrText] = new PointF(expectedW - markerMargin - (markerSize / 2f), markerMargin + (markerSize / 2f)),
            [TemplateGenerator.MarkerBlText] = new PointF(markerMargin + (markerSize / 2f), expectedH - markerMargin - (markerSize / 2f)),
            [TemplateGenerator.MarkerBrText] = new PointF(expectedW - markerMargin - (markerSize / 2f), expectedH - markerMargin - (markerSize / 2f))
        };

        var observed = new Dictionary<string, PointF>();

        var reader = CreateQrReader();
        foreach (var (rect, _) in GetCornerRegions(source.Width, source.Height))
        {
            using var crop = CropBitmap(source, rect);
            foreach (var r in DecodeAll(reader, crop))
            {
                if (r?.Text is null) continue;
                if (!expected.ContainsKey(r.Text)) continue;
                var c = GetResultCenter(r);
                observed[r.Text] = new PointF(c.X + rect.X, c.Y + rect.Y);
            }
        }

        var pairs = new List<(PointF src, PointF dst)>();
        foreach (var (k, dstPt) in expected)
        {
            if (observed.TryGetValue(k, out var srcPt))
                pairs.Add((srcPt, dstPt));
        }

        if (pairs.Count < 3)
            return null;

        var p = SolveLeastSquaresAffine(pairs);

        // x' = a*x + b*y + c, y' = d*x + e*y + f
        var a = (float)p[0];
        var b = (float)p[1];
        var c0 = (float)p[2];
        var d = (float)p[3];
        var e = (float)p[4];
        var f0 = (float)p[5];

        var dest = new Bitmap(expectedW, expectedH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dest))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var m = new Matrix(a, d, b, e, c0, f0);
            g.Transform = m;
            g.DrawImage(source, 0, 0);
        }

        return dest;
    }

    private static (int canvasWidth, int canvasHeight) ComputeCanvasSize(TemplateQrPayload payload, int pagesPerRow, int rows)
    {
        var pairCountPerRow = pagesPerRow / 2;
        var pairGapsPerRow = Math.Max(0, pairCountPerRow - 1);
        var extraFirstSingleGap = payload.StartWithLeftPage ? 1 : 0;

        var contentWidth = (payload.PageWidth * pagesPerRow) + (payload.PageSpacing * (pairGapsPerRow + extraFirstSingleGap));
        var contentHeight = (payload.PageHeight * rows) + (payload.RowSpacing * Math.Max(0, rows - 1));

        var canvasWidth = contentWidth + (payload.PaddingX * 2);
        var canvasHeight = contentHeight + (payload.PaddingY * 2);
        return (canvasWidth, canvasHeight);
    }

    private static IEnumerable<(Rectangle rect, string corner)> GetCornerRegions(int width, int height)
    {
        var baseSize = (int)Math.Round(Math.Min(width, height) * 0.45);
        var size = Math.Clamp(baseSize, 260, 1200);

        yield return (new Rectangle(0, 0, size, size), "TL");
        yield return (new Rectangle(width - size, 0, size, size), "TR");
        yield return (new Rectangle(0, height - size, size, size), "BL");
        yield return (new Rectangle(width - size, height - size, size, size), "BR");
    }

    private static Bitmap CropBitmap(Bitmap source, Rectangle rect)
    {
        rect.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        return source.Clone(rect, PixelFormat.Format32bppArgb);
    }

    private static PointF GetResultCenter(Result r)
    {
        if (r.ResultPoints is null || r.ResultPoints.Length == 0)
            return new PointF(0, 0);

        float sx = 0, sy = 0;
        foreach (var pt in r.ResultPoints)
        {
            sx += pt.X;
            sy += pt.Y;
        }
        return new PointF(sx / r.ResultPoints.Length, sy / r.ResultPoints.Length);
    }

    private static double[] SolveLeastSquaresAffine(IReadOnlyList<(PointF src, PointF dst)> pairs)
    {
        // 正規方程式: (A^T A) p = A^T b
        // p = [a,b,c,d,e,f]
        var ata = new double[6, 6];
        var atb = new double[6];

        foreach (var (src, dst) in pairs)
        {
            var x = (double)src.X;
            var y = (double)src.Y;

            // x' 行: [x y 1 0 0 0]
            AccumulateNormalEq(ata, atb, new[] { x, y, 1d, 0d, 0d, 0d }, (double)dst.X);
            // y' 行: [0 0 0 x y 1]
            AccumulateNormalEq(ata, atb, new[] { 0d, 0d, 0d, x, y, 1d }, (double)dst.Y);
        }

        return SolveLinearSystem6(ata, atb);
    }

    private static void AccumulateNormalEq(double[,] ata, double[] atb, double[] row, double b)
    {
        for (var i = 0; i < 6; i++)
        {
            atb[i] += row[i] * b;
            for (var j = 0; j < 6; j++)
                ata[i, j] += row[i] * row[j];
        }
    }

    private static double[] SolveLinearSystem6(double[,] a, double[] b)
    {
        // ガウス消去(部分ピボット)
        var aug = new double[6, 7];
        for (var i = 0; i < 6; i++)
        {
            for (var j = 0; j < 6; j++) aug[i, j] = a[i, j];
            aug[i, 6] = b[i];
        }

        for (var col = 0; col < 6; col++)
        {
            // pivot
            var pivotRow = col;
            var pivotAbs = Math.Abs(aug[col, col]);
            for (var r = col + 1; r < 6; r++)
            {
                var v = Math.Abs(aug[r, col]);
                if (v > pivotAbs)
                {
                    pivotAbs = v;
                    pivotRow = r;
                }
            }

            if (pivotAbs < 1e-12)
                throw new InvalidOperationException("Affine transform could not be solved (singular matrix).");

            if (pivotRow != col)
            {
                for (var k = col; k < 7; k++)
                    (aug[col, k], aug[pivotRow, k]) = (aug[pivotRow, k], aug[col, k]);
            }

            // normalize pivot row
            var pv = aug[col, col];
            for (var k = col; k < 7; k++) aug[col, k] /= pv;

            // eliminate
            for (var r = 0; r < 6; r++)
            {
                if (r == col) continue;
                var factor = aug[r, col];
                if (Math.Abs(factor) < 1e-12) continue;
                for (var k = col; k < 7; k++)
                    aug[r, k] -= factor * aug[col, k];
            }
        }

        var x = new double[6];
        for (var i = 0; i < 6; i++) x[i] = aug[i, 6];
        return x;
    }

    private static int InferPagesPerRow(int canvasWidth, int pageWidth, int pageSpacing, bool startWithLeftPage, int paddingX)
    {
        // テンプレート外枠のパディング分を除いた「コンテンツ幅」で推定する
        var contentWidth = Math.Max(0, canvasWidth - (paddingX * 2));

        var candidates = new[] { 2, 4, 6, 8, 10, 12 };
        var best = (pagesPerRow: 0, diff: int.MaxValue);

        foreach (var ppr in candidates)
        {
            var pairCountPerRow = ppr / 2;
            var pairGapsPerRow = Math.Max(0, pairCountPerRow - 1);
            var extraFirstSingleGap = startWithLeftPage ? 1 : 0;

            var expected = (pageWidth * ppr) + (pageSpacing * (pairGapsPerRow + extraFirstSingleGap));
            var diff = Math.Abs(contentWidth - expected);
            if (diff < best.diff)
                best = (ppr, diff);
        }

        // 多少の誤差（リサイズ等）もあり得るが、基本は完全一致する想定。
        if (best.pagesPerRow == 0)
            throw new InvalidOperationException("PagesPerRow を推定できませんでした。");

        return best.pagesPerRow;
    }

    private static int InferRows(int canvasHeight, int pageHeight, int rowSpacing, int totalPages, int pagesPerRow, bool startWithLeftPage)
    {
        // 生成側と同じロジックでまず理論行数を出す
        var slots = totalPages + (startWithLeftPage ? 1 : 0);
        var rows = (int)Math.Ceiling(slots / (double)pagesPerRow);

        // 高さが一致しない場合でも、理論値はこの rows なのでそのまま返す。
        // (ここは情報表示用)
        return rows;
    }

    public static TemplateQrPayload? TryDecodePayloadForPreview(Bitmap source)
    {
        try
        {
            // Split と同じ戦略でペイロード検出を試みる
            try
            {
                var (payload, _) = DecodePayloadQr(source);
                return payload;
            }
            catch
            {
                using var normalizedByMarkersOnly = TryNormalizeByCornerMarkersOnly(source);
                if (normalizedByMarkersOnly is null)
                    return null;

                var (payload, _) = DecodePayloadQr(normalizedByMarkersOnly);
                return payload;
            }
        }
        catch
        {
            return null;
        }
    }
}
