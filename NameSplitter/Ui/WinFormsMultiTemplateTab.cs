using System.Drawing.Imaging;

namespace NameSplitter.Ui;

/// <summary>
/// 複数のページ画像を、テンプレート生成と同じレイアウトで1枚にまとめるタブ。
/// </summary>
internal sealed class WinFormsMultiTemplateTab
{
    private readonly TabPage _tab;

    public event EventHandler? SettingsChanged;

    private readonly TextBox _txtInput = new() { ReadOnly = true };
    private readonly Button _btnSelectFiles = new() { Text = "画像を選択" };
    private readonly ListBox _lstFiles = new() { IntegralHeight = false };
    private readonly Label _lblCount = new() { Text = "0枚", AutoSize = true };

    private readonly ComboBox _cmbPagesPerRow = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkStartLeft = new() { Text = "左ページ始まり" };
    private readonly NumericUpDown _nudPageSpacing = new() { Minimum = 0, Maximum = 500, Value = 20 };
    private readonly NumericUpDown _nudRowSpacing = new() { Minimum = 0, Maximum = 500, Value = 30 };
    private readonly NumericUpDown _nudPaddingX = new() { Minimum = 0, Maximum = 1000, Value = 0 };
    private readonly NumericUpDown _nudPaddingY = new() { Minimum = 0, Maximum = 1000, Value = 0 };
    private readonly NumericUpDown _nudScalePercent = new() { Minimum = 10, Maximum = 100, Value = 100, Increment = 1 };
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

    private readonly List<string> _selectedFiles = new();

    private bool _suppressSettingsEvent;

    public WinFormsMultiTemplateTab(TabPage tab)
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
        }

        _cmbTemplateSet.BeginUpdate();
        _cmbTemplateSet.Items.Clear();
        _cmbTemplateSet.Items.AddRange(items.ToArray());
        _cmbTemplateSet.EndUpdate();

        var idx = items.IndexOf(current);
        _cmbTemplateSet.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private bool TryResolveTemplatePaths(out string templateLeftPath, out string templateRightPath, out string templateFallbackPath, out bool hasPair)
    {
        var dir = SelectedTemplateSetDir;
        Directory.CreateDirectory(dir);

        templateLeftPath = Path.Combine(dir, "template_left.png");
        templateRightPath = Path.Combine(dir, "template_right.png");

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

    public void ApplySettings(MultiTemplateTabState state)
    {
        _suppressSettingsEvent = true;

        _chkStartLeft.Checked = state.StartWithLeftPage;
        RefreshTemplateSetList();
        var name = string.IsNullOrWhiteSpace(state.TemplateSet) ? "(Template直下)" : state.TemplateSet;
        var idx = _cmbTemplateSet.Items.IndexOf(name);
        _cmbTemplateSet.SelectedIndex = idx >= 0 ? idx : 0;
        _nudPageSpacing.Value = Math.Clamp(state.PageSpacing, (int)_nudPageSpacing.Minimum, (int)_nudPageSpacing.Maximum);
        _nudRowSpacing.Value = Math.Clamp(state.RowSpacing, (int)_nudRowSpacing.Minimum, (int)_nudRowSpacing.Maximum);
        _nudPaddingX.Value = Math.Clamp(state.PaddingX, (int)_nudPaddingX.Minimum, (int)_nudPaddingX.Maximum);
        _nudPaddingY.Value = Math.Clamp(state.PaddingY, (int)_nudPaddingY.Minimum, (int)_nudPaddingY.Maximum);
        _nudScalePercent.Value = Math.Clamp(state.ImageScalePercent, (int)_nudScalePercent.Minimum, (int)_nudScalePercent.Maximum);

        var index = _cmbPagesPerRow.Items.IndexOf(state.PagesPerRow.ToString());
        if (index >= 0) _cmbPagesPerRow.SelectedIndex = index;

        var fmt = string.IsNullOrWhiteSpace(state.OutputFormat) ? "png" : state.OutputFormat.ToLowerInvariant();
        var fmtIndex = _cmbOutputFormat.Items.IndexOf(fmt);
        if (fmtIndex >= 0) _cmbOutputFormat.SelectedIndex = fmtIndex;

        // 前回ディレクトリは次回のファイル選択の初期フォルダとして使うだけなのでテキスト表示はしない
        _txtInput.Text = state.LastInputDirectory ?? string.Empty;

        _suppressSettingsEvent = false;
    }

    public MultiTemplateTabState CaptureSettings()
    {
        var pagesPerRow = 6;
        if (_cmbPagesPerRow.SelectedItem is string s && int.TryParse(s, out var v)) pagesPerRow = v;

        var fmt = _cmbOutputFormat.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(fmt)) fmt = "png";

        var lastDir = _selectedFiles.Count > 0
            ? Path.GetDirectoryName(_selectedFiles[0])
            : (string.IsNullOrWhiteSpace(_txtInput.Text) ? null : _txtInput.Text);

        return new MultiTemplateTabState(
            LastInputDirectory: lastDir,
            PagesPerRow: pagesPerRow,
            StartWithLeftPage: _chkStartLeft.Checked,
            PageSpacing: (int)_nudPageSpacing.Value,
            RowSpacing: (int)_nudRowSpacing.Value,
            OutputFormat: fmt,
            PaddingX: (int)_nudPaddingX.Value,
            PaddingY: (int)_nudPaddingY.Value,
            TemplateSet: (SelectedTemplateSetName == "(Template直下)") ? "" : SelectedTemplateSetName,
            ImageScalePercent: (int)_nudScalePercent.Value
        );
    }

    private void OnSettingsChanged()
    {
        if (_suppressSettingsEvent) return;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Build()
    {
        _cmbPagesPerRow.Items.AddRange(["2", "4", "6", "8", "10", "12"]);
        _cmbPagesPerRow.SelectedIndex = 2; // 6

        _cmbOutputFormat.Items.AddRange(["png", "jpg"]);

        RefreshTemplateSetList();
        _cmbTemplateSet.DropDown += (_, _) => RefreshTemplateSetList();
        _cmbTemplateSet.SelectedIndexChanged += (_, _) => OnSettingsChanged();
        _cmbOutputFormat.SelectedIndex = 0;

        _chkStartLeft.CheckedChanged += (_, _) => OnSettingsChanged();
        _cmbPagesPerRow.SelectedIndexChanged += (_, _) => OnSettingsChanged();
        _nudPageSpacing.ValueChanged += (_, _) => OnSettingsChanged();
        _nudRowSpacing.ValueChanged += (_, _) => OnSettingsChanged();
        _nudPaddingX.ValueChanged += (_, _) => OnSettingsChanged();
        _nudPaddingY.ValueChanged += (_, _) => OnSettingsChanged();
        _nudScalePercent.ValueChanged += (_, _) => OnSettingsChanged();
        _cmbOutputFormat.SelectedIndexChanged += (_, _) => OnSettingsChanged();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
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

        // 入力ファイル選択
        var inputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true
        };
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _txtInput.Dock = DockStyle.Fill;
        _btnSelectFiles.Dock = DockStyle.Fill;
        inputPanel.Controls.Add(_txtInput, 0, 0);
        inputPanel.Controls.Add(_btnSelectFiles, 1, 0);
        AddRow("入力画像", inputPanel);

        // 選択一覧
        _lstFiles.Height = 110;
        _lstFiles.Dock = DockStyle.Fill;
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        left.Controls.Add(new Label { Text = "選択一覧", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        left.Controls.Add(_lstFiles, 1, row);
        row++;

        // 枚数
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(new Label { Text = "画像枚数", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        left.Controls.Add(_lblCount, 1, row);
        row++;

        AddRow("1行のページ数", _cmbPagesPerRow);
        AddRow("テンプレート", _cmbTemplateSet);
        left.Controls.Add(_chkStartLeft, 0, row);
        left.SetColumnSpan(_chkStartLeft, 2);
        row++;

        AddRow("ページ間", _nudPageSpacing);
        AddRow("行間", _nudRowSpacing);
        AddRow("左右パディング", _nudPaddingX);
        AddRow("上下パディング", _nudPaddingY);
        AddRow("画像縮小率(%)", _nudScalePercent);
        AddRow("出力形式", _cmbOutputFormat);

        // ボタン
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

        _btnSelectFiles.Click += (_, _) => SelectFiles();
        _btnPreview.Click += (_, _) => Preview();
        _btnGenerate.Click += (_, _) => Generate();
    }

    private void SelectFiles()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "ページ画像を選択",
            Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|すべてのファイル (*.*)|*.*",
            Multiselect = true,
        };

        var initial = CaptureSettings().LastInputDirectory;
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            ofd.InitialDirectory = initial;

        if (ofd.ShowDialog() != DialogResult.OK) return;

        _selectedFiles.Clear();
        _selectedFiles.AddRange(ofd.FileNames);
        _selectedFiles.Sort(CompareByNaturalFileName);

        _txtInput.Text = Path.GetDirectoryName(_selectedFiles[0]) ?? string.Empty;

        _lstFiles.Items.Clear();
        foreach (var f in _selectedFiles)
            _lstFiles.Items.Add(Path.GetFileName(f));

        _lblCount.Text = $"{_selectedFiles.Count}枚";

        OnSettingsChanged();
    }

    private static int CompareByNaturalFileName(string a, string b)
    {
        // 先頭の数字(例: 001.png)で並べる。なければ通常の文字列比較。
        static int? LeadingNumber(string s)
        {
            var name = Path.GetFileNameWithoutExtension(s);
            if (string.IsNullOrEmpty(name)) return null;
            var i = 0;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            if (i == 0) return null;
            return int.TryParse(name[..i], out var n) ? n : null;
        }

        var na = LeadingNumber(a);
        var nb = LeadingNumber(b);
        if (na.HasValue && nb.HasValue)
        {
            var c = na.Value.CompareTo(nb.Value);
            if (c != 0) return c;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }

    private bool TryGetSettings(out TemplateSettings settings, out int scalePercent)
    {
        settings = default!;
        scalePercent = 100;

        if (_selectedFiles.Count == 0)
        {
            MessageBox.Show("入力画像を選択してください。", "NameSplitter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (_cmbPagesPerRow.SelectedItem is not string pagesPerRowStr || !int.TryParse(pagesPerRowStr, out var pagesPerRow))
        {
            MessageBox.Show("1行のページ数を選択してください。", "NameSplitter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        settings = new TemplateSettings(
            TotalPages: _selectedFiles.Count,
            PagesPerRow: pagesPerRow,
            StartWithLeftPage: _chkStartLeft.Checked,
            PageSpacing: (int)_nudPageSpacing.Value,
            RowSpacing: (int)_nudRowSpacing.Value,
            PaddingX: (int)_nudPaddingX.Value,
            PaddingY: (int)_nudPaddingY.Value
        );

        scalePercent = (int)_nudScalePercent.Value;
        return true;
    }

    private bool TryGenerateBitmap(out Bitmap bmp, bool previewOnly)
    {
        bmp = null!;
        if (!TryGetSettings(out var settings, out var scalePercent)) return false;

        if (!TryResolveTemplatePaths(out var leftPath, out var rightPath, out var fallbackPath, out var hasPair))
            return false;

        try
        {
            bmp = MultiImageTemplateGenerator.Generate(
                pageImagePaths: _selectedFiles,
                settings: settings,
                templateLeftPath: leftPath,
                templateRightPath: rightPath,
                templateFallbackPath: fallbackPath,
                hasPair: hasPair,
                imageScalePercent: scalePercent
            );
            return true;
        }
        catch (Exception ex)
        {
            var title = previewOnly ? "プレビューエラー" : "生成エラー";
            MessageBox.Show(ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
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
        var exportDir = Path.Combine(BaseDir, "Export");
        Directory.CreateDirectory(exportDir);

        var exportPath = Path.Combine(exportDir, $"template_multi_{_selectedFiles.Count}p.{fmt}");
        var imageFormat = fmt == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png;
        bmp.Save(exportPath, imageFormat);

        try { System.Diagnostics.Process.Start("explorer.exe", exportDir); } catch { }
    }
}
