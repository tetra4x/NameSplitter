using System;
using System.IO;
using System.Drawing;
using SkiaSharp;

namespace NameSplitter;

/// <summary>
/// 画像読み込みユーティリティ。
/// System.Drawing(GDI+) は WebP を直接扱えないため、WebP の場合は SkiaSharp でデコードして Bitmap に変換する。
/// </summary>
internal static class ImageLoader
{
    public static Bitmap LoadBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is null or empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("画像ファイルが見つかりません。", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();

        // WebP は GDI+ 非対応のため SkiaSharp 経由で読み込む
        if (ext == ".webp")
            return LoadWebpAsBitmap(path);

        // それ以外は System.Drawing で OK
        // Image.FromFile はファイルロックを保持しやすいので、Stream -> Clone で読み込む
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var img = Image.FromStream(fs);
        return new Bitmap(img);
    }

    private static Bitmap LoadWebpAsBitmap(string path)
    {
        using var skBitmap = SKBitmap.Decode(path);
        if (skBitmap is null)
            throw new InvalidOperationException("WebP画像のデコードに失敗しました。");

        // Bitmap(Stream) は Stream の寿命に依存しやすいので、いったん作ってクローンして返す
        using var skImage = SKImage.FromBitmap(skBitmap);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp);
    }
}
