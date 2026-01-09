using System.Drawing.Imaging;

namespace NameSplitter.Ui;

internal sealed class WinFormsSplitTab
{
    private readonly TabPage _tab;

    public event EventHandler? SettingsChanged;

    private bool _suppressSettingsEvent;

    private readonly TextBox _txtPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button _btnBrowse = new() { Text = "参照...", Dock = DockStyle.Fill };
    private readonly ComboBox _cmbOutputFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly Button _btnSplit = new() { Text = "分割", Height = 36, Dock = DockStyle.Top, Enabled = false };
    private readonly Label _lblInfo = new() { Text = "画像を選択してください。", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 6, 0, 6) };
    private readonly PictureBox _preview = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        SizeMode = PictureBoxSizeMode.Zoom,
        Dock = DockStyle.Fill
    };

    private TemplateQrPayload? _payload;

    public WinFormsSplitTab(TabPage tab)
    {
        _tab = tab;
        Build();
    }

    public void ApplySettings(SplitTabState state)
    {
        _suppressSettingsEvent = true;
        var fmt = string.IsNullOrWhiteSpace(state.OutputFormat) ? "png" : state.OutputFormat.ToLowerInvariant();
        var idx = _cmbOutputFormat.Items.IndexOf(fmt);
        if (idx >= 0) _cmbOutputFormat.SelectedIndex = idx;
        if (!string.IsNullOrWhiteSpace(state.LastInputPath) && File.Exists(state.LastInputPath))
            LoadImage(state.LastInputPath);
        _suppressSettingsEvent = false;
    }

    public SplitTabState CaptureSettings()
    {
        var fmt = _cmbOutputFormat.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(fmt)) fmt = "png";
        return new SplitTabState(_txtPath.Text, fmt);
    }

    private void OnSettingsChanged()
    {
        if (_suppressSettingsEvent) return;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Build()
    {
        _cmbOutputFormat.Items.AddRange(["png", "jpg"]);
        _cmbOutputFormat.SelectedIndex = 0;
        _cmbOutputFormat.SelectedIndexChanged += (_, _) => OnSettingsChanged();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // file row
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // info + button
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // preview

        var fileRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        fileRow.Controls.Add(_txtPath, 0, 0);
        fileRow.Controls.Add(_btnBrowse, 1, 0);

        var header = new Panel { Dock = DockStyle.Top, AutoSize = true };
        header.Controls.Add(_btnSplit);
        header.Controls.Add(_cmbOutputFormat);
        header.Controls.Add(_lblInfo);

        layout.Controls.Add(fileRow, 0, 0);
        layout.Controls.Add(header, 0, 1);
        layout.Controls.Add(_preview, 0, 2);

        _tab.Controls.Add(layout);

        _btnBrowse.Click += (_, _) => Browse();
        _btnSplit.Click += (_, _) => Split();
    }

    private void Browse()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All Files (*.*)|*.*",
            Title = "ネーム画像を選択"
        };

        if (ofd.ShowDialog() != DialogResult.OK) return;
        LoadImage(ofd.FileName);
    }

    private void LoadImage(string path)
    {
        _txtPath.Text = path;
        OnSettingsChanged();

        // preview
        try
        {
            using var tmp = ImageLoader.LoadBitmap(path);
            var bmp = new Bitmap(tmp);
            var old = _preview.Image;
            _preview.Image = bmp;
            old?.Dispose();
        }
        catch (Exception ex)
        {
            _payload = null;
            _btnSplit.Enabled = false;
            _lblInfo.Text = $"画像を読み込めません: {ex.Message}";
            return;
        }

        // decode
        try
        {
            using var src = ImageLoader.LoadBitmap(path);

            // QR読み取りだけを行う
            _payload = TryReadPayload(src);
            if (_payload is null)
            {
                _btnSplit.Enabled = false;
                _lblInfo.Text = "QRコード(ペイロード)を読み取れませんでした。画像右上にQRがあるか確認してください。";
                return;
            }

            _btnSplit.Enabled = true;
            _lblInfo.Text = $"QR読取: 全{_payload.TotalPages}P / 左ページ始まり={_payload.StartWithLeftPage} / Page={_payload.PageWidth}x{_payload.PageHeight} / PageSpacing={_payload.PageSpacing} / RowSpacing={_payload.RowSpacing} / PaddingX={_payload.PaddingX} / PaddingY={_payload.PaddingY}";
        }
        catch (Exception ex)
        {
            _payload = null;
            _btnSplit.Enabled = false;
            _lblInfo.Text = $"QR読み取りエラー: {ex.Message}";
        }
    }

    private static TemplateQrPayload? TryReadPayload(Bitmap source)
    {
        try
        {
            // Split 本番と同等のルートで読ませる（時間がかかっても精度優先）
            return NameSplitEngine.TryDecodePayloadForPreview(source);
        }
        catch
        {
            return null;
        }
    }

    private void Split()
    {
        if (string.IsNullOrWhiteSpace(_txtPath.Text) || !File.Exists(_txtPath.Text))
        {
            MessageBox.Show("入力画像を選択してください。", "NameSplitter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Export");
        Directory.CreateDirectory(exportDir);

        try
        {
            var fmt = (_cmbOutputFormat.SelectedItem as string) ?? "png";
            var result = NameSplitEngine.SplitIntoPages(_txtPath.Text, exportDir, fmt);
            //MessageBox.Show($"分割しました: {result.Payload.TotalPages}ページ\n保存先: {exportDir}", "NameSplitter", MessageBoxButtons.OK, MessageBoxIcon.Information);

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", exportDir);
            }
            catch
            {
                // ignore
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "分割エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
