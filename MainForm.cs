using KenshiCore;
using KenshiTranslator.Helper;
using KenshiTranslator.Translator;
using NTextCat;
using System.Collections;
using System.Diagnostics;
using System.Security.Policy;
using System.Text;
namespace KenshiTranslator
{
    
    public class MainForm : ProtoMainForm
    {
        private readonly object reLockRE = new object();
        private ModManager modM = new ModManager(new ReverseEngineer());
        private RankedLanguageIdentifier? identifier;
        private Dictionary<string, string>? _supportedLanguages;
        private ComboBox providerCombo;
        private ComboBox fromLangCombo;
        private ComboBox toLangCombo;
        private string lastSelectedFromLang = "en";
        private string lastSelectedToLang = "en";
        private Dictionary<string, string> languageCache = new();
        private TranslatorInterface _activeTranslator = GTranslate_Translator.Instance;
        private TextBox customApiTextBox;
        private Button testApiButton;
        private Label apiStatusLabel;
        private CustomApiTranslator? _customApiTranslator;
        private Button TranslateModButton;
        private TranslationLogForm? logForm;
        private Button ShowLogButton;

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
        public MainForm()
        {
            Text = "Kenshi Translator";
            Width = 800;
            Height = 500;

            providerCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            providerCombo.Items.AddRange(new string[] { "Aggregate", "Bing", "Google", "Google2", "Microsoft", "Yandex", "Google Cloud V3", "Custom API" });
            providerCombo.SelectedIndex = 0;
            _activeTranslator = GTranslate_Translator.Instance;
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

            AddButton("Create Dictionary", async (s, e) => await CreateDictionaryButton_Click());
            TranslateModButton = AddButton("Translate Mod", async (s, e) => await TranslateModButton_Click());
            
            ShowLogButton = AddButton("Show Log",ShowLogButton_Click);

            customApiTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Enter DeepL key (ends with :fx) or Google key (starts with AIza)",
                Visible = false
            };
            customApiTextBox.TextChanged += (s, e) => {
                apiStatusLabel!.Text = "";
                if (providerCombo.SelectedItem?.ToString() == "Custom API" && !string.IsNullOrWhiteSpace(customApiTextBox.Text))
                {
                    try
                    {
                        _customApiTranslator?.Dispose();
                        _customApiTranslator = new CustomApiTranslator(customApiTextBox.Text);
                        _activeTranslator = _customApiTranslator;
                        testApiButton!.Enabled = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error setting custom API: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        testApiButton!.Enabled = false;
                    }
                }
                else
                {
                    testApiButton!.Enabled = false;
                }
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
            AddColumn("Translation Progress", mod => getTranslationProgress(mod),200);
        }
        private void ShowLogButton_Click(object? sender, EventArgs e)
        {
            if (logForm == null || logForm.IsDisposed)
            {
                logForm = new TranslationLogForm();
            }

            if (logForm.Visible)
            {
                logForm.BringToFront();
            }
            else
            {
                logForm.Show(this);
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
            await InitializationTask;

            InitLanguageDetector();

            LoadLanguageCache();
            InitializeTranslatorColumns();

            await DetectAllLanguagesAsync();
        }
        private async Task providerCombo_SelectedIndexChanged(object? sender, EventArgs? e)
        {
            if (providerCombo.SelectedItem == null) return;
            string provider = providerCombo.SelectedItem.ToString()!;

            // Save current language selection
            if (fromLangCombo.SelectedValue != null)
                lastSelectedFromLang = fromLangCombo.SelectedValue.ToString()!;
            if (toLangCombo.SelectedValue != null)
                lastSelectedToLang = toLangCombo.SelectedValue.ToString()!;

            if (provider == "Google Cloud V3")
            {
                customApiTextBox.Visible = true;
                testApiButton.Visible = true;
                apiStatusLabel.Visible = true;
                apiStatusLabel.Text = "";

                // Show file dialog to select Service Account JSON
                using var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Google Cloud Service Account JSON file",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    customApiTextBox.Text = openFileDialog.FileName;
                    customApiTextBox.ReadOnly = true; // Make it read-only since it's selected via dialog

                    try
                    {
                        _customApiTranslator?.Dispose();
                        _customApiTranslator = new CustomApiTranslator(openFileDialog.FileName);
                        _activeTranslator = _customApiTranslator;
                        testApiButton.Enabled = true;
                        apiStatusLabel.Text = "✅ Service Account JSON loaded";
                        apiStatusLabel.ForeColor = Color.Green;
                    }
                    catch (Exception ex)
                    {
                        apiStatusLabel.Text = $"❌ Error: {ex.Message}";
                        apiStatusLabel.ForeColor = Color.Red;
                        testApiButton.Enabled = false;
                    }
                }
                else
                {
                    // User cancelled file selection, revert to previous provider
                    providerCombo.SelectedIndex = 0; // Aggregate
                    return;
                }
            }
            else if (provider == "Custom API")
            {
                customApiTextBox.Visible = true;
                testApiButton.Visible = true;
                apiStatusLabel.Visible = true;
                apiStatusLabel.Text = "";
                customApiTextBox.ReadOnly = false; // Allow manual input for custom API

                // Create custom API translator if API endpoint is provided
                if (!string.IsNullOrWhiteSpace(customApiTextBox.Text))
                {
                    _customApiTranslator?.Dispose();
                    _customApiTranslator = new CustomApiTranslator(customApiTextBox.Text);
                    _activeTranslator = _customApiTranslator;
                    testApiButton.Enabled = true;
                }
                else
                {
                    testApiButton.Enabled = false;
                }
                _supportedLanguages = await _activeTranslator.GetSupportedLanguagesAsync();
            }
            else
            {
                customApiTextBox.Visible = false;
                testApiButton.Visible = false;
                apiStatusLabel.Visible = false;
                _customApiTranslator?.Dispose();
                _customApiTranslator = null;
                _activeTranslator = GTranslate_Translator.Instance;
                ((GTranslate_Translator)_activeTranslator).setTranslator(provider);
                _supportedLanguages = await _activeTranslator.GetSupportedLanguagesAsync();
            }

            fromLangCombo.DataSource = _supportedLanguages!.Select(lang => new ComboItem(lang.Key, lang.Value)).ToList();
            fromLangCombo.DisplayMember = "Name";
            fromLangCombo.ValueMember = "Code";

            toLangCombo.DataSource = _supportedLanguages!.Select(lang => new ComboItem(lang.Key, lang.Value)).ToList();
            toLangCombo.DisplayMember = "Name";
            toLangCombo.ValueMember = "Code";

            // Restore previously selected languages if available
            if (fromLangCombo.Items.Count > 0)
                fromLangCombo.SelectedValue = _supportedLanguages!.ContainsKey(lastSelectedFromLang)?lastSelectedFromLang:"en";
            if (toLangCombo.Items.Count > 0)
                toLangCombo.SelectedValue = _supportedLanguages!.ContainsKey(lastSelectedToLang)?lastSelectedToLang: "en";
            
            //###########################################
            /*_activeTranslator = GTranslate_Translator.Instance;
            ((GTranslate_Translator)_activeTranslator).setTranslator(providerCombo.SelectedItem.ToString()!);

            _supportedLanguages = await _activeTranslator.GetSupportedLanguagesAsync();

            fromLangCombo.DataSource = _supportedLanguages.Select(lang => new ComboItem(lang.Key, lang.Value)).ToList();
            fromLangCombo.DisplayMember = "Name";
            fromLangCombo.ValueMember = "Code";

            toLangCombo.DataSource = _supportedLanguages.Select(lang => new ComboItem(lang.Key, lang.Value)).ToList();
            toLangCombo.DisplayMember = "Name";
            toLangCombo.ValueMember = "Code";

            if (fromLangCombo.Items.Count > 0)
                fromLangCombo.SelectedValue = lastSelectedFromLang;
            if (toLangCombo.Items.Count > 0)
                toLangCombo.SelectedValue = lastSelectedToLang;*/
        }
        private void LoadLanguageCache()
        {
            string path = "languages.txt";
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2) {
                    string modName = parts[0];
                    string cachedLang = parts[1];
                    languageCache[modName] = cachedLang;
                    if (mergedMods.TryGetValue(modName, out var mod)) { 
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
        private async Task CreateDictionaryButton_Click()
        {
            if (modsListView.SelectedItems.Count == 0)
                return;

            var selectedItem = modsListView.SelectedItems[0];
            string modName = selectedItem.Text;

            if (!mergedMods.TryGetValue(modName, out var mod))
                return;

            string modPath = mod.getModFilePath()!;
            if (!File.Exists(modPath))
            {
                MessageBox.Show("Mod file not found!");
                return;
            }

            // Ensure dictionary exists
            string dictFile = mod.getDictFilePath();
            modM.LoadModFile(modPath);
            var td = new TranslationDictionary(modM.GetReverseEngineer());

            if (!File.Exists(dictFile))
                td.ExportToDictFile(dictFile);
            if (logForm == null || logForm.IsDisposed)
            {
                logForm = new TranslationLogForm();
            }
            logForm.Reset();
            ShowLogButton.Enabled = true;
            int total= td.getTotalToBeTranslated(dictFile);
            InitializeProgress(0, total);
            ReportProgress(0, $"Translating {modName}... {0}/{total}");

            string sourceLang = fromLangCombo.SelectedItem?.ToString()?.Split(' ')[0] ?? "auto";
            string targetLang = toLangCombo.SelectedItem?.ToString()?.Split(' ')[0] ?? "en";
            //string sourceLang = fromLangCombo.SelectedItem?.ToString()?.Split(' ')[0] ?? "auto";
            //string targetLang = toLangCombo.SelectedItem?.ToString()?.Split(' ')[0] ?? "en";
            int failureCount = 0;
            int successCount = 0;
            const int failureThreshold = 10;
            // Start async translation with resume support
            try
            {

                Func<List<string>, Task<List<string>>>? batchTranslateFunc = null;
                if (_activeTranslator is CustomApiTranslator customApi &&
                    customApi.CurrentApiType == ApiType.GoogleCloudV3)
                {
                   batchTranslateFunc = (texts) => customApi.TranslateBatchV3Async(texts, sourceLang, targetLang);
                   Debug.WriteLine("[MainForm] Using Google V3 batch translation - much faster!");
                }


                await TranslationDictionary.ApplyTranslationsAsync(dictFile, async (original) =>
                {
                    try
                    {
                        if (failureCount >= failureThreshold)
                            return "";
                        var translated = await _activeTranslator.TranslateAsync(original, sourceLang, targetLang);
                        int done = Interlocked.Increment(ref successCount);
                        ReportProgress(done, $"Translating {modName}... {done}/{total}");
                        return translated;

                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error on string {successCount}: {ex.Message}");
                        failureCount++;
                        if (failureCount >= failureThreshold)
                            throw new InvalidOperationException($"Too many consecutive translation failures. The provider {_activeTranslator.Name} may not be working.{ex.Message}");
                            return "";
                    }
                },batchTranslateFunc != null ? 100 : 200,
                (original, translated, success) => logForm?.LogTranslation(original, translated, success),
                (original, error) => logForm?.LogError(original, error),
                batchTranslateFunc).ConfigureAwait(false);//sourceLang targetLang
            }// limit concurrent requests to prevent API issues
            catch (Exception ex)
            {
                ReportProgress(0, "Translation aborted.");
                MessageBox.Show($"Dictionary translation failed: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (successCount == 0)
            {
                MessageBox.Show($"No translations were produced. Try a different provider (current: {_activeTranslator.Name}).",
                    "Translation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ReportProgress(total, $"Dictionary complete {modName}... {total}/{total}");
            MessageBox.Show($"{modName}: Dictionary generated!");
            updateTranslationProgress(modName);
            TranslateModButton.Enabled = File.Exists(mod.getDictFilePath());
        }
        private async Task TestApiButton_Click()
        {
            if (_customApiTranslator == null)
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
                string testResult = await _customApiTranslator.TranslateAsync(testText, "EN", "RU");

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
            if (modsListView.SelectedItems.Count == 0)
                return;
            var selectedItem = modsListView.SelectedItems[0];
            string modName = selectedItem.Text;

            if (!mergedMods.TryGetValue(modName, out var mod))
                return;
            string modPath = mod.getModFilePath()!;
            string dictFile = mod.getDictFilePath();
            lock (reLockRE)
            {
                modM.LoadModFile(modPath);
                var td = new TranslationDictionary(modM.GetReverseEngineer());
                td.ImportFromDictFile(dictFile);
                int progress = TranslationDictionary.GetTranslationProgress(dictFile);
                if (progress < 95)
                {
                    var result = MessageBox.Show(
                        $"Dictionary of {modName} is only {progress}% complete.\n\nDo you want to apply the partial translation anyway?",
                        "Partial Translation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.No)
                        return;
                }
                /*if (TranslationDictionary.GetTranslationProgress(dictFile) != 100)
                {
                    MessageBox.Show($"Dictionary of {modName} is not complete!");
                    return;
                }*/
                if (!File.Exists(mod.getBackupFilePath()))
                    File.Copy(modPath, mod.getBackupFilePath());
                modM.GetReverseEngineer().SaveModFile(modPath);
                MessageBox.Show($"Translation of {modName} is finished!");
            }
            UpdateDetectedLanguage(modName, await DetectModLanguagesAsync(mod));
            updateTranslationProgress(modName);
            return;
        }
        private void OpenSteamLinkButton_Click(object? sender, EventArgs e)
        {
            string modName = modsListView.SelectedItems[0].Text;
            var mod = mergedMods.ContainsKey(modName) ? mergedMods[modName] : null;
            if (mod != null && mod.WorkshopId != -1)
            {
                string url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={mod.WorkshopId}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("This mod is not from the Steam Workshop.");
            }
        }
        private Color colorLanguage(string lang)
        {
            return (lang == "eng|___") ? Color.Green : Color.Red; 
        }
        private string getTranslationProgress(ModItem mod)
        {
            int progress = File.Exists(mod.getDictFilePath()) ? TranslationDictionary.GetTranslationProgress(mod.getDictFilePath()) : File.Exists(mod.getBackupFilePath()) ? 100 : 0;
            return (progress== 100) ? "Translated" : progress > 0 ? $"{progress:F0}%" :"Not translated";
        }
        private void updateTranslationProgress(string modName)
        {
            if (modsListView.InvokeRequired)
            {
                // Marshal call back onto UI thread
                modsListView.Invoke(new Action(() => updateTranslationProgress(modName)));
                return;
            }
            var item = modsListView.Items.Cast<ListViewItem>().FirstOrDefault(i => ((ModItem)i.Tag!).Name == modName);
            if (item != null)
            {
                var mod = (ModItem)item.Tag!;
                string progressText = getTranslationProgress(mod);
                item.SubItems[2].Text = progressText;
                modsListView.Refresh();
            }
        }
        private void UpdateDetectedLanguage(string modName, string detectedLanguage)
        {
            languageCache[modName] = detectedLanguage;
            var item = modsListView.Items.Cast<ListViewItem>().FirstOrDefault(i => ((ModItem)i.Tag!).Name == modName);
            if (item != null)
            {
                item.SubItems[1].Text = detectedLanguage;
                item.SubItems[1].ForeColor = colorLanguage(detectedLanguage);
                modsListView.Invalidate(item.Bounds);
            }
            SaveLanguageCache();
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
                    modM.LoadModFile(mod.getModFilePath()!);
                    var lang_tuple = modM.GetReverseEngineer().getModSummary();
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

            int total= modsToDetect.Count;
            InitializeProgress(0, total);
            int progress = 0;
            foreach (var mod in modsToDetect)
            {
                
                string detected = await DetectModLanguagesAsync(mod);

                // Update UI on main thread
                this.Invoke((MethodInvoker)delegate {
                    languageCache[mod.Name] = detected;
                    mod.Language = detected;
                    var item = modsListView.Items.Cast<ListViewItem>().FirstOrDefault(i => ((ModItem)i.Tag!).Name == mod.Name);
                    if (item != null)
                    {
                        item.SubItems[1].Text = detected;          // <-- update the text
                        item.SubItems[1].ForeColor = colorLanguage(detected);
                    }
                    progress++;
                    if (progress % 10 == 0)
                        ReportProgress(progress, $"detected {mod.Name}");
                });
                await Task.Delay(10);
            }

            ReportProgress(progress, "Language detection complete!");
            SaveLanguageCache();
        }
    }
}
