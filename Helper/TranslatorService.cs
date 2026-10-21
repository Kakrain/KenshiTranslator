using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiTranslator.Translator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiTranslator.Helper
{
    public class TranslatorService
    {
        private readonly TranslatorContext _translatorCtx;// = new();

        public TranslatorService(TranslatorContext translatorCtx)
        {
            _translatorCtx = translatorCtx;
            _uiContext = SynchronizationContext.Current;
        }
        private readonly SynchronizationContext? _uiContext;
        public GeneralLogForm? LogForm { get; set; }
        /*public TranslatorService()
        {
            _uiContext = SynchronizationContext.Current;
        }*/
        private string PrepareDictionary(ModItem mod, string modPath, out TranslationDictionary td)
        {
            string dictFile = mod.getDictFilePath();

            //_reverseEngineer.setModName(Path.GetFileName(modPath));
            ReverseEngineer _reverseEngineer = new ReverseEngineer(Path.GetFileName(modPath));
            _reverseEngineer.LoadModFile(modPath);
            td = new TranslationDictionary(_reverseEngineer);

            if (!File.Exists(dictFile))
                td.ExportToDictFile(dictFile);

            return dictFile;
        }
        private bool ValidateModPath(string modPath)
        {
            if (!File.Exists(modPath))
            {
                UiService.ShowMessage($"Mod file not found at path: {modPath}", "Error", MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
        public async Task CreateSingleDictionary(ModItem mod,string sourceLang, string targetLang,CancellationToken token,GeneralLogForm? logform)
        {

            string modPath = mod.getModFilePath()!;
            if (!ValidateModPath(modPath)) return;

            
            if (!ValidateModPath(modPath)) return;
            try
            {
                string dictFile = PrepareDictionary(mod, modPath, out var td);
                int total = td.getTotalToBeTranslated(dictFile);

                ProgressController.Instance.Initialize(total);
                int successCount = await RunTranslation(
                    dictFile,
                    mod.Name,
                    total,
                    sourceLang,
                    targetLang,
                    token,
                    logform
                );

                HandleTranslationResult(mod, successCount, total);
            }
            catch (UnsupportedModFileException ex)
            {
                UiService.ShowMessage($"Unsupported mod file: {ex.Message}", "Error", MessageBoxIcon.Error);
                return;
            }
            catch (Exception ex)
            {
                HandleTranslationError(ex);
            }
            
        }
        private async Task<int> RunTranslation(
    string dictFile,
    string modName,
    int total,
    string sourceLang,
    string targetLang,
    CancellationToken token,
    GeneralLogForm? logform)
        {
            int failureCount = 0;
            int successCount = 0;
            const int failureThreshold = 10;

            var progress = ProgressController.Instance;

            var batchFunc = GetBatchTranslator(sourceLang, targetLang);
            CoreUtils.Print($"Active type: {_translatorCtx.Active.GetType().FullName}");
            CoreUtils.Print($"Active name: {_translatorCtx.Active.Name}");

            CoreUtils.Print("Batch translation enabled: " + (batchFunc != null));
            
            await TranslationDictionary.ApplyTranslationsAsync(
                dictFile,
                async original =>
                {
                    if (failureCount >= failureThreshold)
                        return "";
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var translated = await _translatorCtx.Active.TranslateAsync(original, sourceLang, targetLang);

                        int done = Interlocked.Increment(ref successCount);
                        progress.Report(done, $"Translating {modName}... {done}/{total}");

                        return translated;
                    }
                    catch (OperationCanceledException)
                    {
                        ProgressController.Instance.Finish("Translation stopped by user.");
                        return "";
                    }
                    catch (Exception ex)
                    {
                        failureCount++;

                        MessageBox.Show($"Error on string {successCount}: {ex.Message}");

                        if (failureCount >= failureThreshold)
                            throw new InvalidOperationException(
                                $"Too many consecutive failures. Provider {_translatorCtx.Active.Name} may be broken.\n{ex.Message}");

                        return "";
                    }

                },
                //batchFunc != null ? 100 : 200, 
                batchFunc != null ? 5 : 200,
                (o, t, ok) =>_uiContext?.Post(_ => LogForm?.Log($"{o} : {t} {(ok ? "✓" : "✗")}", null), null),
                (o, err) =>_uiContext?.Post(_ => LogForm?.LogError($"{o}:{err}"), null),
                batchFunc,
                token
            );

            return successCount;
        }

        private Func<List<string>, Task<List<string>>>? GetBatchTranslator(
    string sourceLang, string targetLang)
        {
            if (_translatorCtx.Active is CustomApiTranslator customApi)
            {
                if (customApi.CurrentApiType == ApiType.Ollama)
                    return texts => customApi.TranslateOllamaAsyncGeneral(texts, sourceLang, targetLang);
                //return texts => customApi.TranslateBatchOllamaAsync(texts, sourceLang, targetLang);

                if (customApi.CurrentApiType == ApiType.GoogleCloudV3)
                    return texts => customApi.TranslateBatchV3Async(texts, sourceLang, targetLang);
            }

            return null;
        }
        private void HandleTranslationError(Exception ex)
        {
            ProgressController.Instance.Finish($"Translation aborted. {ex.Message}");
            UiService.ShowMessage($"Dictionary translation failed: {ex.Message}");
        }
        private void HandleTranslationResult(ModItem mod, int successCount, int total)
        {
            var progress = ProgressController.Instance;

            if (successCount == 0)
            {
                UiService.ShowMessage($"No translations were produced. Try a different provider (current: {_translatorCtx.Active.Name}) or delete the .dict file.");
                return;
            }
            progress.Finish($"Dictionary complete {mod.Name}... {total}/{total}");
        }
    }
}
