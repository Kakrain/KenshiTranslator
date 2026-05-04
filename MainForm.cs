using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiTranslator.Helper;
using Microsoft.VisualBasic.FileIO;
using NTextCat;
using System.Diagnostics;
using System.Text;
namespace KenshiTranslator
{

    public class MainForm : ProtoMainForm
    {
        private readonly object reLockRE = new object();
        private ModManager modM = ModManager.Instance;
        private RankedLanguageIdentifier? identifier;
        private ComboBox providerCombo;
        private ComboBox fromLangCombo;
        private ComboBox toLangCombo;
        private string lastSelectedFromLang = "en";
        private string lastSelectedToLang = "en";
        private Dictionary<string, string> languageCache = new();
        private TextBox customApiTextBox;
        private Button testApiButton;
        private Label apiStatusLabel;
        private Button TranslateModButton;
        private TranslatorService _translatorService = new();
        private readonly TranslatorContext _translatorCtx = new();
        private CancellationTokenSource _cancel_token = new();
        private Task? currentTranslationTask = null;

        ReverseEngineer _reverseEngineer = new ReverseEngineer();
        public class ComboItem
        {
            public string Code { get; }
            public string Name { get; }

            public ComboItem(string code, string name)
            {
                Code = code;
                Name = name;
            }

            public override string ToString() => Name;
        }
        protected override void LoadMods()
        {
            var repo = ModRepository.Instance;
            repo.LoadGameDirMods();
            repo.LoadWorkshopMods();
            repo.LoadSelectedMods();
        }
        public MainForm()
        {
            Text = "Kenshi Translator";
            Width = 800;
            Height = 600;

            providerCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            providerCombo.Items.AddRange(new string[] { "Aggregate", "Bing", "Google", "Google2", "Microsoft", "Yandex", "Google Cloud V3", "Custom API", "Dummy" });
            providerCombo.SelectedIndex = 0;
            providerCombo.SelectedIndexChanged += (s, e) => _ = providerCombo_SelectedIndexChanged(s, e);
            buttonPanel.Controls.Add(providerCombo);
            fromLangCombo = new ComboBox();
            toLangCombo = new ComboBox();
            fromLangCombo.Width = 120;
            toLangCombo.Width = 120;

            buttonPanel.Controls.Add(fromLangCombo);
            buttonPanel.Controls.Add(toLangCombo);

            fromLangCombo.SelectedValue = "en";
            toLangCombo.SelectedValue = "en";


            AddButton("Stop All", async (s, e) => await StopAll());
            AddButton("Reset Translation", async (s, e) => await ResetTranslation_Click());
            AddButton("Refresh Status", async (s, e) => await RefreshStatusButton_Click());
            AddButton("Create Dictionary", async (s, e) => await CreateDictionaryButton_Click());
            TranslateModButton = AddButton("Translate Mod", async (s, e) => await TranslateModButton_Click());

            customApiTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Enter DeepL key (ends with :fx) or Google key (starts with AIza)",
                Visible = false
            };

            customApiTextBox.TextChanged += async (s, e) =>
            {
                await UpdateCustomApiAsync();
            };
            buttonPanel.Controls.Add(customApiTextBox);
            testApiButton = new Button
            {
                Text = "Test API",
                AutoSize = true,
                Enabled = false,
                Visible = false
            };
            testApiButton.Click += async (s, e) => await TestApiButton_Click();
            buttonPanel.Controls.Add(testApiButton);
            apiStatusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "",
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            buttonPanel.Controls.Add(apiStatusLabel);

            AddColumn("Language", mod => mod.Language);


            this.Load += async (s, e) =>
            {
                await providerCombo_SelectedIndexChanged(null, null);
            };
            AddColumn("Translation Progress", mod => getTranslationProgress(mod), 200);

            _translatorService.LogForm = getLogForm();
            ThemeManager.Set(
                new AppTheme
                {
                    Background = Color.FromArgb(unchecked((int)0xFFE5E5E5)),
                    Secondary = Color.LightGray,
                });
        }

        private bool _isClosing = false;
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isClosing)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;
            _isClosing = true;

            _cancel_token?.Cancel();

            if (currentTranslationTask != null)
            {
                try
                {
                    await Task.Run(() => currentTranslationTask.Wait(TimeSpan.FromSeconds(10)));
                }
                catch (Exception) { }
                currentTranslationTask = null;
            }
            SaveLanguageCache();
            this.Close();
        }
        private async Task UpdateCustomApiAsync()
        {
            apiStatusLabel.Text = "";

            if (providerCombo.SelectedItem?.ToString() != "Custom API")
            {
                testApiButton.Enabled = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(customApiTextBox.Text))
            {
                testApiButton.Enabled = false;
                return;
            }

            try
            {
                var langs = await _translatorCtx.SetProviderAsync(
                    "Custom API",
                    () => customApiTextBox.Text,
                    msg => apiStatusLabel.Text = msg
                );

                BindLanguages(langs);
                RestoreSelectedLanguages(langs);

                testApiButton.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error setting custom API: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                testApiButton.Enabled = false;
            }
        }
        private void InitializeTranslatorColumns()
        {
            foreach (ListViewItem item in modsListView.Items)
            {
                var mod = (ModItem)item.Tag!;
                while (item.SubItems.Count < 3)
                {
                    item.SubItems.Add("");
                }
                item.SubItems[2].Text = getTranslationProgress(mod);
            }

            modsListView.Refresh();
        }
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await OnShownAsync(e);
        }
        private async Task OnShownAsync(EventArgs e)
        {
            if (InitializationTask != null)
                await InitializationTask;

            InitLanguageDetector();

            LoadLanguageCache();
            InitializeTranslatorColumns();

            await DetectAllLanguagesAsync();
        }
        private void SaveSelectedLanguages()
        {
            if (fromLangCombo.SelectedValue != null)
                lastSelectedFromLang = fromLangCombo.SelectedValue.ToString()!;
            if (toLangCombo.SelectedValue != null)
                lastSelectedToLang = toLangCombo.SelectedValue.ToString()!;
        }
        private void RestoreSelectedLanguages(Dictionary<string, string> langs)
        {
            fromLangCombo.SelectedValue =
                langs.ContainsKey(lastSelectedFromLang) ? lastSelectedFromLang : "en";

            toLangCombo.SelectedValue =
                langs.ContainsKey(lastSelectedToLang) ? lastSelectedToLang : "en";
        }
        private void ToggleCustomApiUI(string provider)
        {
            bool visible = provider == "Custom API" || provider == "Google Cloud V3";

            customApiTextBox.Visible = visible;
            testApiButton.Visible = visible;
            apiStatusLabel.Visible = visible;
        }
        private string? GetProviderInput(string provider)
        {
            if (provider == "Google Cloud V3")
            {
                using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
                return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
            }

            if (provider == "Custom API")
                return customApiTextBox.Text;

            return null;
        }
        private void BindLanguages(Dictionary<string, string> languages)
        {
            var items = languages
                .Select(lang => new ComboItem(lang.Key, lang.Value))
                .ToList();

            // Important: separate lists to avoid shared binding bugs
            fromLangCombo.DataSource = items;
            toLangCombo.DataSource = items.ToList();

            fromLangCombo.DisplayMember = nameof(ComboItem.Name);
            fromLangCombo.ValueMember = nameof(ComboItem.Code);

            toLangCombo.DisplayMember = nameof(ComboItem.Name);
            toLangCombo.ValueMember = nameof(ComboItem.Code);
        }
        private async Task providerCombo_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            if (providerCombo.SelectedItem == null) return;

            string provider = providerCombo.SelectedItem.ToString()!;

            SaveSelectedLanguages();

            ToggleCustomApiUI(provider);

            try
            {
                var langs = await _translatorCtx.SetProviderAsync(
                    provider,
                    () => GetProviderInput(provider),
                    msg => apiStatusLabel.Text = msg
                );

                BindLanguages(langs);
                RestoreSelectedLanguages(langs);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                providerCombo.SelectedIndex = 0;
            }
        }
        private void LoadLanguageCache()
        {
            string path = "languages.txt";
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    string modName = parts[0];
                    string cachedLang = parts[1];
                    languageCache[modName] = cachedLang;
                    if (mergedMods.TryGetValue(modName, out var mod))
                    {
                        mod.Language = cachedLang;
                        var item = modsListView.Items.Cast<ListViewItem>()
                        .FirstOrDefault(i => ((ModItem)i.Tag!).Name == modName);

                        if (item != null)
                        {
                            item.SubItems[1].Text = cachedLang;
                            item.SubItems[1].ForeColor = colorLanguage(cachedLang);
                        }
                    }
                }
            }
            modsListView.Refresh();
        }
        private void SaveLanguageCache()
        {
            try
            {
                using var writer = new StreamWriter("languages.txt", false, Encoding.UTF8);
                foreach (var kvp in languageCache)
                {
                    writer.WriteLine($"{kvp.Key}={kvp.Value}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save language cache: " + ex.Message);
            }
        }
        private void InitLanguageDetector()
        {
            if (identifier == null)
                identifier = new RankedLanguageIdentifierFactory().Load("LanguageModels/Core14.profile.xml");
        }
        private string GetSourceLang()
        {
            return fromLangCombo.SelectedValue?.ToString() ?? "auto";
        }

        private string GetTargetLang()
        {
            return toLangCombo.SelectedValue?.ToString() ?? "en";
        }
        private async Task CreateDictionaryButton_Click()
        {
            var mods = getSelectedMods().ToList();

            getLogForm().Reset();
            _cancel_token = new CancellationTokenSource();
            foreach (var mod in mods)
            {
                if (_cancel_token?.IsCancellationRequested == true)
                    break;
                currentTranslationTask = _translatorService.CreateSingleDictionary(mod, GetSourceLang(), GetTargetLang(), _cancel_token!.Token, getLogForm());//CreateSingleDictionary(mod);
                await currentTranslationTask;
                RefreshRow(mod);
            }
            currentTranslationTask = null;
            StringBuilder sb = new StringBuilder();
            sb.AppendJoin(", ", mods.ConvertAll(m => m.Name));
            if (_cancel_token?.IsCancellationRequested == true)
            {
                UiService.ShowMessage("Dictionary Translation Cancelled");
                return;
            }
            MessageBox.Show($"Dictionary generated for {sb.ToString()}!");
        }
        private Task StopAll()
        {
            var result = MessageBox.Show(
                       $"Are you sure you want to stop the current translation?.",
                       "Stop Translation",
                       MessageBoxButtons.YesNo,
                       MessageBoxIcon.Question);

            if (result == DialogResult.No)
                return Task.CompletedTask;
            _cancel_token?.Cancel();
            return Task.CompletedTask;
        }
        private async Task ResetTranslation_Click()
        {
            var mods = getSelectedMods().ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendJoin(", ", mods.ConvertAll(m => m.Name));
            var result = MessageBox.Show(
                       $"Are you sure you want to reset the translation for {sb.ToString()}.\n\nThis operation cannot be undone.",
                       "Reset Translation",
                       MessageBoxButtons.YesNo,
                       MessageBoxIcon.Question);

            if (result == DialogResult.No)
                return;
            foreach (var mod in mods)
            {
                ResetModTranslation(mod);
                await UpdateModLanguageAsync(mod);
                RefreshRow(mod);
            }
        }
        private void ResetModTranslation(ModItem mod)
        {
            string dictpath = mod.getDictFilePath();
            string backupPath = mod.getBackupFilePath();
            if (File.Exists(dictpath))
                FileSystem.DeleteFile(dictpath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, mod.getModFilePath()!, overwrite: true);
                FileSystem.DeleteFile(backupPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }
        private async Task RefreshStatusButton_Click()
        {
            var mods = getSelectedMods().ToList();
            foreach (var mod in mods)
            {
                await UpdateModLanguageAsync(mod);
                RefreshRow(mod);
            }
        }
        private async Task TestApiButton_Click()
        {
            if (_translatorCtx.Active == null)
            {
                apiStatusLabel.Text = "❌ No API configured";
                apiStatusLabel.ForeColor = Color.Red;
                return;
            }

            testApiButton.Enabled = false;
            apiStatusLabel.Text = "🔄 Testing API...";
            apiStatusLabel.ForeColor = Color.Orange;

            try
            {
                // Test with a simple translation
                string testText = "Hello, world!";
                string testResult = await _translatorCtx.Active.TranslateAsync(testText, "EN", "RU");

                if (!string.IsNullOrEmpty(testResult) && testResult != testText)
                {
                    apiStatusLabel.Text = $"✅ API OK (Test: {testResult})";
                    apiStatusLabel.ForeColor = Color.Green;
                }
                else
                {
                    apiStatusLabel.Text = "⚠️ API returned empty/unchanged result";
                    apiStatusLabel.ForeColor = Color.Orange;
                }
            }
            catch (Exception ex)
            {
                apiStatusLabel.Text = $"❌ API Error: {ex.Message}";
                apiStatusLabel.ForeColor = Color.Red;
            }
            finally
            {
                testApiButton.Enabled = true;
            }
        }
        private async Task TranslateModButton_Click()
        {
            var mods = getSelectedMods().ToList();

            foreach (var mod in mods)
            {
                TranslateSingleMod(mod);
                await UpdateModLanguageAsync(mod);
                RefreshRow(mod);
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendJoin(", ", mods.ConvertAll(m => m.Name));
            MessageBox.Show($"Translation for {sb.ToString()} is finished!");
        }
        private void TranslateSingleMod(ModItem mod)
        {
            string modPath = mod.getModFilePath()!;
            string dictFile = mod.getDictFilePath();
            lock (reLockRE)
            {
                _reverseEngineer.LoadModFile(modPath);
                var td = new TranslationDictionary(_reverseEngineer);
                td.ImportFromDictFile(dictFile);
                int progress = TranslationDictionary.GetTranslationProgress(dictFile);
                if (progress < 95)
                {
                    var result = MessageBox.Show(
                        $"Dictionary of {mod.Name} is only {progress}% complete.\n\nDo you want to apply the partial translation anyway?",
                        "Partial Translation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.No)
                        return;
                }
                string backuppath = mod.getBackupFilePath();
                if (!File.Exists(backuppath))
                    File.Copy(modPath, backuppath);
                _reverseEngineer.SaveModFile(modPath);
            }

        }
        private Color colorLanguage(string lang)
        {
            return (lang == "eng|___") ? Color.Green : Color.Red;
        }
        private string getTranslationProgress(ModItem mod)
        {
            if (mod.getModFilePath() == null)
                return "mod not found";
            if (File.Exists(mod.getBackupFilePath()))
                return "Translated";
            var dictpath = mod.getDictFilePath();
            if (File.Exists(dictpath))
            {
                int progress = TranslationDictionary.GetTranslationProgress(dictpath);
                return $"Dictionary at {progress:F0}%";
            }
            return "Not translated";
        }
        private string detectLanguageFor(string s)
        {
            var candidates = identifier!.Identify(s).OrderBy(c => c.Item2).ToList();
            var best = candidates[0];
            if (best.Item2 > 3950)
                return "___";
            return best.Item1.Iso639_3;

        }
        private async Task<string> DetectModLanguagesAsync(ModItem mod)
        {
            try
            {
                return await Task.Run(() =>
                {
                    _reverseEngineer.LoadModFile(mod.getModFilePath()!);
                    var lang_tuple = _reverseEngineer.getModSummary();
                    var alpha_mostCertain = detectLanguageFor(lang_tuple.Item1);
                    var sign_mostCertain = detectLanguageFor(lang_tuple.Item2);
                    return $"{alpha_mostCertain}|{sign_mostCertain}";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error detecting language for {mod.Name}: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }
        private async Task DetectAllLanguagesAsync()
        {
            var modsToDetect = modsListView.Items
                .Cast<ListViewItem>()
                .Select(item => (ModItem)item.Tag!)
                .Where(mod => !languageCache.ContainsKey(mod.Name))
                .ToList();

            int total = modsToDetect.Count;
            ProgressController progress = ProgressController.Instance;
            progress.Initialize(total);
            int n_progress = 0;
            foreach (var mod in modsToDetect)
            {
                await UpdateModLanguageAsync(mod);

                n_progress++;
                if (n_progress % 10 == 0)
                    progress.Report(n_progress, $"detected {mod.Name}");

                await Task.Delay(10);
            }
            progress.Finish("Language detection complete!");
            SaveLanguageCache();
        }
        private async Task UpdateModLanguageAsync(ModItem mod)
        {
            string detected = await DetectModLanguagesAsync(mod);

            if (modsListView.InvokeRequired)
            {
                modsListView.Invoke(new Action(() =>
                    ApplyDetectedLanguage(mod, detected)));
            }
            else
            {
                ApplyDetectedLanguage(mod, detected);
            }
        }
        private void ApplyDetectedLanguage(ModItem mod, string detected)
        {
            languageCache[mod.Name] = detected;
            mod.Language = detected;

            var item = modsListView.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => ((ModItem)i.Tag!).Name == mod.Name);

            if (item != null)
            {
                item.SubItems[1].Text = detected;
                item.SubItems[1].ForeColor = colorLanguage(detected);
            }
        }
    }
}
