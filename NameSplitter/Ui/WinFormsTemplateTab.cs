using System.Drawing.Imaging;

namespace NameSplitter.Ui;

internal sealed class WinFormsTemplateTab
{
    private readonly TabPage _tab;

    public event EventHandler? SettingsChanged;

    private readonly NumericUpDown _nudTotalPages = new() { Minimum = 1, Maximum = 999, Value = 8 };
    private readonly ComboBox _cmbPagesPerRow = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkStartLeft = new() { Text = "左ページ始まり" };
    private readonly NumericUpDown _nudPageSpacing = new() { Minimum = 0, Maximum = 500, Value = 20 };
    private readonly NumericUpDown _nudRowSpacing = new() { Minimum = 0, Maximum = 500, Value = 30 };
    private readonly NumericUpDown _nudPaddingX = new() { Minimum = 0, Maximum = 1000, Value = 0 };
    private readonly NumericUpDown _nudPaddingY = new() { Minimum = 0, Maximum = 1000, Value = 0 };
    private readonly ComboBox _cmbOutputFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbTemplateSet = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _btnPreview = new() { Text = "プレビュー" };
    private readonly Button _btnGenerate = new() { Text = "生成" };
    private readonly PictureBox _preview = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        SizeMode = PictureBoxSizeMode.Zoom,
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
    };

    private bool _suppressSettingsEvent;

    public WinFormsTemplateTab(TabPage tab)
    {
        _tab = tab;
        Build();
    }

    private string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    private string TemplateDir => Path.Combine(BaseDir, "Template");

    private string SelectedTemplateSetName
        => _cmbTemplateSet.SelectedItem?.ToString() ?? string.Empty;

    private string SelectedTemplateSetDir
    {
        get
        {
            var name = SelectedTemplateSetName;
            if (string.IsNullOrWhiteSpace(name) || name == "(Template直下)")
                return TemplateDir;
            return Path.Combine(TemplateDir, name);
        }
    }

    private void RefreshTemplateSetList()
    {
        Directory.CreateDirectory(TemplateDir);

        var current = SelectedTemplateSetName;
        var items = new List<string> { "(Template直下)" };
        try
        {
            items.AddRange(Directory.GetDirectories(TemplateDir)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)!);
        }
        catch
        {
            // 読み取りに失敗しても UI を壊さない
        }

        _cmbTemplateSet.BeginUpdate();
        _cmbTemplateSet.Items.Clear();
        _cmbTemplateSet.Items.AddRange(items.ToArray());
        _cmbTemplateSet.EndUpdate();

        // 可能なら選択を維持
        var idx = items.IndexOf(current);
        _cmbTemplateSet.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private bool TryResolveTemplatePaths(out string templateLeftPath, out string templateRightPath, out string templateFallbackPath, out bool hasPair)
    {
        // Template 配下の任意フォルダをテンプレートセットとして扱う。
        // 画像ファイル名は template_left.png / template_right.png で統一する。
        var dir = SelectedTemplateSetDir;
        Directory.CreateDirectory(dir);

        templateLeftPath = Path.Combine(dir, "template_left.png");
        templateRightPath = Path.Combine(dir, "template_right.png");

        // 旧仕様互換: 直下選択時のみ template.png をフォールバックとして許可する
        templateFallbackPath = string.Empty;
        hasPair = File.Exists(templateLeftPath) && File.Exists(templateRightPath);
        if (hasPair) return true;

        if (dir == TemplateDir)
        {
            var fallback = Path.Combine(TemplateDir, "template.png");
            if (File.Exists(fallback))
            {
                templateFallbackPath = fallback;
                return true;
            }
        }

        MessageBox.Show(
            $"テンプレート画像が見つかりません。\n\n" +
            $"次の2つを {dir} に用意してください:\n" +
            $"- template_left.png\n" +
            $"- template_right.png",
            "NameSplitter",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private bool TryGetSettings(out TemplateSettings settings)
    {
        settings = default!;
        if (_cmbPagesPerRow.SelectedItem is not string pagesPerRowStr || !int.TryParse(pagesPerRowStr, out var pagesPerRow))
        {
            MessageBox.Show("1行のページ数を選択してください。", "NameSplitter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        settings = new TemplateSettings(
            TotalPages: (int)_nudTotalPages.Value,
            PagesPerRow: pagesPerRow,
            StartWithLeftPage: _chkStartLeft.Checked,
            PageSpacing: (int)_nudPageSpacing.Value,
            RowSpacing: (int)_nudRowSpacing.Value,
            PaddingX: (int)_nudPaddingX.Value,
            PaddingY: (int)_nudPaddingY.Value
        );
        return true;
    }

    private bool TryGenerateBitmap(out Bitmap bmp, bool previewOnly)
    {
        bmp = null!;

        if (!TryResolveTemplatePaths(out var leftPath, out var rightPath, out var fallbackPath, out var hasPair))
            return false;

        if (!TryGetSettings(out var settings))
            return false;

        try
        {
            bmp = hasPair
                ? TemplateGenerator.GenerateFromSeparateTemplates(leftPath, rightPath, settings)
                : TemplateGenerator.Generate(fallbackPath, settings);
            return true;
        }
        catch (Exception ex)
        {
            var title = previewOnly ? "プレビューエラー" : "生成エラー";
            MessageBox.Show(ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public void ApplySettings(TemplateTabState state)
    {
        _suppressSettingsEvent = true;
        _nudTotalPages.Value = Math.Clamp(state.TotalPages, (int)_nudTotalPages.Minimum, (int)_nudTotalPages.Maximum);
        _chkStartLeft.Checked = state.StartWithLeftPage;
        RefreshTemplateSetList();
        var name = string.IsNullOrWhiteSpace(state.TemplateSet) ? "(Template直下)" : state.TemplateSet;
        var idx = _cmbTemplateSet.Items.IndexOf(name);
        _cmbTemplateSet.SelectedIndex = idx >= 0 ? idx : 0;
        _nudPageSpacing.Value = Math.Clamp(state.PageSpacing, (int)_nudPageSpacing.Minimum, (int)_nudPageSpacing.Maximum);
        _nudRowSpacing.Value = Math.Clamp(state.RowSpacing, (int)_nudRowSpacing.Minimum, (int)_nudRowSpacing.Maximum);
        _nudPaddingX.Value = Math.Clamp(state.PaddingX, (int)_nudPaddingX.Minimum, (int)_nudPaddingX.Maximum);
        _nudPaddingY.Value = Math.Clamp(state.PaddingY, (int)_nudPaddingY.Minimum, (int)_nudPaddingY.Maximum);

        var index = _cmbPagesPerRow.Items.IndexOf(state.PagesPerRow.ToString());
        if (index >= 0) _cmbPagesPerRow.SelectedIndex = index;

        var fmt = string.IsNullOrWhiteSpace(state.OutputFormat) ? "png" : state.OutputFormat.ToLowerInvariant();
        var fmtIndex = _cmbOutputFormat.Items.IndexOf(fmt);
        if (fmtIndex >= 0) _cmbOutputFormat.SelectedIndex = fmtIndex;
        _suppressSettingsEvent = false;
    }

    public TemplateTabState CaptureSettings()
    {
        var pagesPerRow = 6;
        if (_cmbPagesPerRow.SelectedItem is string s && int.TryParse(s, out var v)) pagesPerRow = v;

        var fmt = _cmbOutputFormat.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(fmt)) fmt = "png";

        return new TemplateTabState(
            TotalPages: (int)_nudTotalPages.Value,
            PagesPerRow: pagesPerRow,
            StartWithLeftPage: _chkStartLeft.Checked,
            PageSpacing: (int)_nudPageSpacing.Value,
            RowSpacing: (int)_nudRowSpacing.Value,
            OutputFormat: fmt,
            PaddingX: (int)_nudPaddingX.Value,
            PaddingY: (int)_nudPaddingY.Value,
            TemplateSet: (SelectedTemplateSetName == "(Template直下)") ? "" : SelectedTemplateSetName
        );
    }

    private void OnSettingsChanged()
    {
        if (_suppressSettingsEvent) return;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Build()
    {
        _cmbPagesPerRow.Items.AddRange(Enumerable.Range(2, 11).Select(i => i.ToString()).ToArray());
        _cmbPagesPerRow.SelectedIndex = 4; // デフォルト6

        _cmbOutputFormat.Items.AddRange(["png", "jpg"]);

        RefreshTemplateSetList();
        _cmbTemplateSet.DropDown += (_, _) => RefreshTemplateSetList();
        _cmbTemplateSet.SelectedIndexChanged += (_, _) => OnSettingsChanged();
        _cmbOutputFormat.SelectedIndex = 0;

        _nudTotalPages.ValueChanged += (_, _) => OnSettingsChanged();
        _cmbPagesPerRow.SelectedIndexChanged += (_, _) => OnSettingsChanged();
        _chkStartLeft.CheckedChanged += (_, _) => OnSettingsChanged();
        _nudPageSpacing.ValueChanged += (_, _) => OnSettingsChanged();
        _nudRowSpacing.ValueChanged += (_, _) => OnSettingsChanged();
        _nudPaddingX.ValueChanged += (_, _) => OnSettingsChanged();
        _nudPaddingY.ValueChanged += (_, _) => OnSettingsChanged();
        _cmbOutputFormat.SelectedIndexChanged += (_, _) => OnSettingsChanged();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;
        void AddRow(string label, Control field)
        {
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };
            left.Controls.Add(lbl, 0, row);
            field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            left.Controls.Add(field, 1, row);
            row++;
        }

        AddRow("総ページ数", _nudTotalPages);
        AddRow("1行のページ数", _cmbPagesPerRow);
        AddRow("テンプレート", _cmbTemplateSet);
        left.Controls.Add(_chkStartLeft, 0, row);
        left.SetColumnSpan(_chkStartLeft, 2);
        row++;

        AddRow("ページ間", _nudPageSpacing);
        AddRow("行間", _nudRowSpacing);
        AddRow("左右パディング", _nudPaddingX);
        AddRow("上下パディング", _nudPaddingY);
        AddRow("出力形式", _cmbOutputFormat);

        // プレビュー / 生成 ボタンを上下に並べる
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true
        };
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _btnPreview.Height = 36;
        _btnPreview.Dock = DockStyle.Top;
        _btnGenerate.Height = 36;
        _btnGenerate.Dock = DockStyle.Top;

        buttons.Controls.Add(_btnPreview, 0, 0);
        buttons.Controls.Add(_btnGenerate, 0, 1);

        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(buttons, 0, row);
        left.SetColumnSpan(buttons, 2);

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(_preview, 1, 0);

        _tab.Controls.Add(layout);

        _btnPreview.Click += (_, _) => Preview();
        _btnGenerate.Click += (_, _) => Generate();
    }

    private void Preview()
    {
        if (!TryGenerateBitmap(out var bmp, previewOnly: true)) return;

        var old = _preview.Image;
        _preview.Image = bmp;
        old?.Dispose();
    }

    private void Generate()
    {
        if (!TryGenerateBitmap(out var bmp, previewOnly: false)) return;

        var old = _preview.Image;
        _preview.Image = bmp;
        old?.Dispose();

        var fmt = (_cmbOutputFormat.SelectedItem as string) ?? "png";
        fmt = fmt.ToLowerInvariant();
        var exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Export");
        Directory.CreateDirectory(exportDir);
        var exportPath = Path.Combine(exportDir, $"template_{_nudTotalPages.Value}p.{fmt}");
        var imageFormat = fmt == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png;
        bmp.Save(exportPath, imageFormat);

        // Exportフォルダをエクスプローラーで開く
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", exportDir);
        }
        catch
        {
            // ignore
        }
    }
}
