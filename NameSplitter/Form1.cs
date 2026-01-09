namespace NameSplitter
{
    public partial class Form1 : Form
    {
        private readonly Ui.WinFormsTemplateTab _templateTab;
        private readonly Ui.WinFormsSplitTab _splitTab;
        private readonly Ui.WinFormsMultiTemplateTab _multiTemplateTab;
        private AppSettings _settings;

        public Form1()
        {
            InitializeComponent();
            _settings = AppSettingsStore.Load();

            _templateTab = new Ui.WinFormsTemplateTab(tabTemplate);
            _splitTab = new Ui.WinFormsSplitTab(tabSplit);
            _multiTemplateTab = new Ui.WinFormsMultiTemplateTab(tabMultiTemplate);

            _templateTab.SettingsChanged += (_, _) => SaveSettings();
            _splitTab.SettingsChanged += (_, _) => SaveSettings();
            _multiTemplateTab.SettingsChanged += (_, _) => SaveSettings();

            _templateTab.ApplySettings(_settings.Template);
            // Split タブは状態を復元しない
            _splitTab.ApplySettings(SplitTabState.Default);
            _multiTemplateTab.ApplySettings(_settings.MultiTemplateResolved);

            FormClosing += (_, _) => SaveSettings();
        }

        private void SaveSettings()
        {
            _settings = _settings with
            {
                Template = _templateTab.CaptureSettings(),
                // Split タブは状態を永続化しない
                Split = SplitTabState.Default,
                MultiTemplate = _multiTemplateTab.CaptureSettings()
            };

            AppSettingsStore.Save(_settings);
        }
    }
}
