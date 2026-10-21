using Google.Cloud.Translate.V3;
using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiTranslator.Helper;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.FileIO;
using NTextCat;
using System.Diagnostics;
using System.Text;
namespace KenshiTranslator
{

    public class MainForm : ProtoMainForm
    {
        private readonly object reLockRE = new object();
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
        private const string _columnName_TranslationProgress = "Translation Progress";
        private const string _columnName_Language = "Language";
        private readonly TranslatorContext _translatorCtx = new();
        private readonly TranslatorService _translatorService;
        private CancellationTokenSource _cancel_token = new();
        private Task? currentTranslationTask = null;

        ReverseEngineer _reverseEngineer = new ReverseEngineer("translated.mod");
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
            Height = 700;

            _translatorService= new TranslatorService(_translatorCtx);

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
            AddButton("Generate Translation", async (s, e) => await GenTranslationButton_Click());

            customApiTextBox = new TextBox
            {
                /*Dock = DockStyle.Top,
                PlaceholderText = "Enter DeepL key (ends with :fx) or Google key (starts with AIza)",
                Visible = false,*/

                Dock = DockStyle.None,
                Width = 300,
                Height = 25,
                PlaceholderText = "Enter API key or URL",
                Visible = false
            };

            buttonPanel.Controls.Add(customApiTextBox);
            testApiButton = new Button
            {
                Text = "Test API",
                AutoSize = true,
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

            AddColumn(_columnName_Language, mod => mod.Language);


            this.Load += async (s, e) =>
            {
                await providerCombo_SelectedIndexChanged(null, null);
            };
            AddColumn(_columnName_TranslationProgress, mod => getTranslationProgress(mod), 200);

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

            SetColumnValues(_columnName_TranslationProgress,mod => getTranslationProgress(mod));
            /*int column= GetColumnIndex(_columnName_TranslationProgress);

            foreach (ListViewItem item in modsListView.Items)
            {
                var mod = (ModItem)item.Tag!;
                while (item.SubItems.Count < 3)
                {
                    item.SubItems.Add("");
                }
                item.SubItems[2].Text = getTranslationProgress(mod);
            }

            modsListView.Refresh();*/
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

            DetectAllLanguagesAsync();
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
            if (provider == "Custom API")
            {
                customApiTextBox.Focus();
                return;
            }

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
                UiService.ShowMessage(ex.Message);
                providerCombo.SelectedIndex = 0;
            }
        }
        private void LoadLanguageCache()
        {
            int columnIndex = GetColumnIndex(_columnName_Language);
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
                    if (ModRepository.Instance.Mods.TryGetValue(modName, out var mod))
                    {
                        mod.Language = cachedLang;
                    }
                    int rowIndex = GetRowIndex(modName);
                    if (rowIndex == -1)
                        continue;
                    
                    SetValue(columnIndex,rowIndex, cachedLang);
                    SetColor(columnIndex,rowIndex, colorLanguage(cachedLang));
                }
            }
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
                UiService.ShowMessage("Failed to save language cache: " + ex.Message);
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
            UiService.ShowMessage($"Dictionary generated for {sb.ToString()}!");
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
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(dictpath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, mod.getModFilePath()!, overwrite: true);
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(backupPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
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
            if (providerCombo.SelectedItem?.ToString() == "Custom API")
            {
                string apiInput = customApiTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(apiInput))
                {
                    apiStatusLabel.Text = "Please enter an API key or endpoint.";
                    apiStatusLabel.ForeColor = Color.Red;
                    return;
                }

                try
                {
                    var langs = await _translatorCtx.SetProviderAsync(
                        "Custom API",
                        () => customApiTextBox.Text.Trim(),
                        msg => apiStatusLabel.Text = msg
                    );

                    BindLanguages(langs);
                    RestoreSelectedLanguages(langs);
                }
                catch (Exception ex)
                {
                    apiStatusLabel.Text = $"API configuration error: {ex.Message}";
                    apiStatusLabel.ForeColor = Color.Red;
                    return;
                }
            }
            if (_translatorCtx.Active == null)
            {
                apiStatusLabel.Text = "❌ No API configured";
                apiStatusLabel.ForeColor = Color.Red;
                return;
            }

            testApiButton.Enabled = false;
            apiStatusLabel.Text = $"🔄 Testing {providerCombo.SelectedItem}...";
            apiStatusLabel.ForeColor = Color.Orange;

            try
            {
                // Test with a simple translation
                string testText = "Hello, world!";
                string testResult = await _translatorCtx.Active.TranslateAsync(testText, "EN", "RU");

                string testResultado = await _translatorCtx.Active.TranslateAsync(testText, "EN", "ES");

                if (!string.IsNullOrEmpty(testResult) && testResult != testText)
                {
                    apiStatusLabel.Text = $"✅ API OK (Test: RU:{testResult}, ES:{testResultado})";
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
        private string getTranslatedInventoryPath(string modpath)
        {
            return Path.ChangeExtension(modpath, "_translated_inventory.txt");
        }
        private async Task GenTranslationButton_Click()
        {
            string? modName = UiService.PromptForString("Enter the name for the new or existing mod:",caption: "Translation Mod");
            if (modName == null)
                return; // User cancelled.
            if (string.IsNullOrWhiteSpace(modName))
                return; // Empty input is disallowed by default.
            modName = Path.GetFileNameWithoutExtension(modName);
            string? modPath = findMod(modName);
            ReverseEngineer transMod;
            bool hastoUpdate = false;
            if(modPath == null) {
                string modDirectory = Path.Combine(ModManager.gamedirModsPath!,modName);
                Directory.CreateDirectory(modDirectory);
                modPath = Path.Combine(modDirectory, $"{modName}.mod");
                transMod = ReverseEngineer.GenerateEmpty($"{modName}.mod");
                hastoUpdate = true;
            }
            else
            {
                transMod = new ReverseEngineer($"{modName}.mod");
                transMod.LoadModFile(modPath);
            }

            string translated_inventory_path = getTranslatedInventoryPath(modPath);
            List<string> translatedmods = CoreUtils.ReadFileAsList(translated_inventory_path,false);
            
            modName = Path.GetFileNameWithoutExtension(modName) + ".mod";

            var mods = getSelectedMods().Where(mod => File.Exists(mod.getDictFilePath())).ToList();
            List<string> modnames = mods.Select(mod => mod.Name).ToList();

            foreach (var mod in mods)
            {
                ReverseEngineer re= new ReverseEngineer(mod.Name);
                re.LoadModFile(mod.getModFilePath()!);

                TranslationDictionary td = new TranslationDictionary(re);
                td.PatchIntoReverseEngineer(mod.getDictFilePath(), transMod);
                RefreshRow(mod);
            }
            transMod.SaveModFile(modPath);
            _translated_via_patch = null;
            CoreUtils.WriteListToFile(translated_inventory_path, translatedmods.Concat(modnames).Distinct().ToList(), $"Translated mods:");
            if(hastoUpdate)
                reloadListView();
            
            foreach (var mod in mods)
            {
                RefreshRow(mod);
                await UpdateModLanguageAsync(mod);
            }

            CoreUtils.Prompt("Translation generated successfully");
        }
        private string? findMod(string modname)
        {
            
            string modPath = Path.Combine(ModManager.gamedirModsPath!, modname, $"{modname}.mod");
            if (File.Exists(modPath))
                return modPath;
            string workshopPath = ModManager.workshopModsPath!;
            if (Directory.Exists(workshopPath))
            {
                string? workshopMod = Directory.GetFiles(workshopPath, $"{modname}.mod", System.IO.SearchOption.AllDirectories).FirstOrDefault();

                if (workshopMod != null)
                    return workshopMod;
            }
            return null;
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
            UiService.ShowMessage($"Translation for {sb.ToString()} is finished!");
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

        private List<string>? _translated_via_patch = null;
        private string getTranslationProgress(ModItem mod)
        {
            if(_translated_via_patch == null)
            {
                _translated_via_patch = new List<string>();
                foreach (var m in ModRepository.Instance.GetMergedMods().Values)
                {
                    List<string> translated_list = m.getModFilePath() != null && File.Exists(m.getModFilePath()!) ? CoreUtils.ReadFileAsList(getTranslatedInventoryPath(m.getModFilePath()!), false) : new List<string>();
                    _translated_via_patch.AddRange(translated_list);
                }
            }
            if (mod.getModFilePath() == null)
                return "mod not found";
            if (File.Exists(mod.getBackupFilePath()))
                return "Translated";
            if (_translated_via_patch.Contains(mod.Name))
                return "Translated via patch";
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
                    if(!File.Exists(mod.getModFilePath()))
                    {
                        return "mod file not found";
                    }
                    try
                    {
                        _reverseEngineer.LoadModFile(mod.getModFilePath()!);
                    }
                    catch (UnsupportedModFileException ex)
                    {
                        Console.WriteLine(ex.Message);
                        return $"unsupported filetype {ex.FileType}";
                    }
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
        private void DetectAllLanguagesAsync()
        {
            var modsToDetect = ModRepository.Instance.Mods.Values.Where(mod => !languageCache.ContainsKey(mod.Name)).ToList();

            int total = modsToDetect.Count;
            ProgressController progress = ProgressController.Instance;
            progress.Initialize(total);

            int n_progress = 0;

            ProgressController.ForEach(modsToDetect, async mod =>
            {
                await UpdateModLanguageAsync(mod);
                n_progress++;
                if (n_progress % 10 == 0)
                    progress.Report(n_progress, $"detected {mod.Name}");
                await Task.Delay(10);
            }, "Detecting languages...");

            progress.Finish("Language detection complete!");
            SaveLanguageCache();
        }
        private async Task UpdateModLanguageAsync(ModItem mod)
        {
            string detected = await DetectModLanguagesAsync(mod);
            InvokeOnUiThread(() => ApplyDetectedLanguage(mod, detected));
        }

        public void reloadListView()
        {
            LoadMods();
            PopulateModsListView();
            LoadLanguageCache();
        }
        public void refreshListView()
        {
            foreach (var mod in ModRepository.Instance.Mods.Values)
            {
                int row = GetRowIndex(mod.Name);
                if (row != -1)
                {
                    SetValue(GetColumnIndex(_columnName_Language), row, mod.Language);
                    SetColor(GetColumnIndex(_columnName_Language), row, colorLanguage(mod.Language));
                    SetValue(GetColumnIndex(_columnName_TranslationProgress), row, getTranslationProgress(mod));
                }
            }
        }
        private void ApplyDetectedLanguage(ModItem mod, string detected)
        {
            languageCache[mod.Name] = detected;
            mod.Language = detected;
            int row = GetRowIndex(mod.Name);

            SetValue(GetColumnIndex(_columnName_Language),row, detected);
            SetColor(GetColumnIndex(_columnName_Language), row, colorLanguage(detected));
            SetValue(GetColumnIndex(_columnName_TranslationProgress), row, getTranslationProgress(mod));

        } /*var item = modsListView.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(i => ((ModItem)i.Tag!).Name == mod.Name);

            if (item != null)
            {
                item.SubItems[1].Text = detected;
                item.SubItems[1].ForeColor = colorLanguage(detected);
            }*/
    }
}
