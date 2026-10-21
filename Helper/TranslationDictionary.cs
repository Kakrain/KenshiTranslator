using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using Newtonsoft.Json.Linq;
using NTextCat.Commons;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

public class TranslationDictionary
{
    private ReverseEngineer _source_reverseEngineer;
    private static readonly string lineEnd = "|_END_|\n";
    private static readonly string sep = "|_SEP_|";
    private HashSet<string> unique_keys = new();
    private List<string> _blacklistFields = new();
    private List<int> _whitelistRecordTypesKeys = new();


    public TranslationDictionary(ReverseEngineer source)
    {
        _blacklistFields = CoreUtils.ReadFileAsList("translator_field_blacklist.txt", strict: true);

        _whitelistRecordTypesKeys = CoreUtils.ReadFileAsList("translator_record_type_whitelist.txt", strict: true).Select(s => ModRecord.getRecordTypeInt(s)).ToList();
        _source_reverseEngineer = source;
    }
    // Export all strings to a .dict file
    public void ExportToDictFile(string path)
    {
        using var writer = new StreamWriter(path);
        var typeCodes = ModRecord.ModTypeCodes.Values;
        // Export description is not necesary

        // Export records
        foreach (var record in _source_reverseEngineer.modData.GetRecords())
        {
            if (!_whitelistRecordTypesKeys.Contains(record.getRecordTypeCode()))
                continue;
            if (!record.Name.IsNullOrEmpty() && !typeCodes.Any(s => record.Name.Contains(s)))
                writer.Write($"record{record.StringId}_name{sep}{record.Name}{sep}{lineEnd}");
            if (record.StringFields != null&& record.StringFields.Count > 0)
            {
                foreach (var kvp in record.StringFields) {
                    if (!kvp.Value.IsNullOrEmpty() && !_blacklistFields.Contains(kvp.Key))
                        writer.Write($"record{record.StringId}_{kvp.Key}{sep}{kvp.Value}{sep}{lineEnd}");
                    unique_keys.Add(kvp.Key);
                }
            }
        }
    }
    public void ImportFromDictFile(string path)
    {
        var translations = LoadTranslations(path);
        ApplyTranslationsToSource(translations);
    }
    

    public ReverseEngineer GeneratePatchFromDictFile(string path)
    {
        var translations = LoadTranslations(path);

        var patch = ReverseEngineer.GenerateEmpty(_source_reverseEngineer.modname + "_patch");

        ApplyTranslationsToReverseEngineer(translations, patch);

        patch.SaveModFile(path);

        return patch;
    }
    public void PatchIntoReverseEngineer(string path,ReverseEngineer other_reverseENG)
    {
        var translations = LoadTranslations(path);
        ApplyTranslationsToReverseEngineer(translations, other_reverseENG);
    }

    private void ApplyTranslationsToSource(Dictionary<string, string> loadedTranslations)
    {

        foreach (var record in _source_reverseEngineer.modData.GetRecords())
        {
            // Record name
            string nameKey = $"record{record.StringId}_name";

            if (record.Name != null && loadedTranslations.TryGetValue(nameKey, out var newName) && !string.IsNullOrWhiteSpace(newName))
            {
                record.SetName(newName, _source_reverseEngineer.modData.Header!.FileType);
                //record.Name = newName;
            }

            // String fields
            if (record.StringFields == null)
                continue;

            foreach (var kvp in record.StringFields.ToList())
            {
                string fieldKey = $"record{record.StringId}_{kvp.Key}";

                if (loadedTranslations.TryGetValue(fieldKey, out var newValue) && !string.IsNullOrWhiteSpace(newValue))
                {
                    record.StringFields[kvp.Key] = newValue;
                }
            }
        }
    }
    private void ApplyTranslationsToReverseEngineer(Dictionary<string, string> loadedTranslations, ReverseEngineer other_reverseENG)
    {

        foreach (var record in _source_reverseEngineer.modData.GetRecords())
        {

            ModRecord? otherRecord = null;
            
            
            // Record name
            string nameKey = $"record{record.StringId}_name";

            if (record.Name != null &&loadedTranslations.TryGetValue(nameKey, out var newName) &&!string.IsNullOrWhiteSpace(newName))
            {
                otherRecord ??= other_reverseENG.EnsureRecordExists(record);
                otherRecord!.SetName(newName,other_reverseENG.modData.Header!.FileType);
                //record.Name = newName;
            }

            // String fields
            if (record.StringFields == null)
                continue;

            foreach (var kvp in record.StringFields.ToList())
            {
                string fieldKey = $"record{record.StringId}_{kvp.Key}";

                if (loadedTranslations.TryGetValue(fieldKey, out var newValue) && !string.IsNullOrWhiteSpace(newValue))
                {
                    otherRecord ??= other_reverseENG.EnsureRecordExists(record);
                    otherRecord!.StringFields??= new Dictionary<string, string>();
                    otherRecord.StringFields[kvp.Key] = newValue;
                }
            }
        }
    }
    private Dictionary<string, string> LoadTranslations(string path)
    {
        var result = new Dictionary<string, string>();

        if (!File.Exists(path))
        {
            UiService.ShowMessage( $"Dictionary file not found at path: {path}","Error",MessageBoxIcon.Error);
            return result;
        }

        var all = File.ReadAllText(path);
        var segments = all.Split(lineEnd, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in segments)//all.Split(lineEnd))
        {
            var segment = raw.Trim();
            if (string.IsNullOrWhiteSpace(segment)) continue;
            var parts = segment.Split(sep).Select(p => p.Trim()).ToArray();
            if (parts.Length < 2) continue;
            var key = parts[0].Trim();
            var original = parts[1].Trim();
            var translated = parts.Length >= 3 ? parts[2].Trim() : "";
            if(!string.IsNullOrWhiteSpace(translated))
                result[key] =translated;
        }

        return result;
    }
    public int getTotalToBeTranslated(string dictFilePath)
    {
        return File.ReadAllText(dictFilePath).Split(lineEnd).Length;
    }
    private static async Task<string?> TryTranslateNormalAsync(
    string text, Func<string, Task<string>> translateFunc, List<string> constants)
    {
        string translated = await translateFunc(text);
        if (ContainsAllConstants(translated, constants))
            return translated;
        return null;
    }
    //Translation with markers
    private static async Task<string?> TryTranslateWithSimpleMarkersAsync(
    string text, Func<string, Task<string>> translateFunc, List<string> constants)
    {
        var mapping = constants.Select((c, i) => new { Const = c, Marker = $"¤{i}¤" }).ToList();
        string marked = text;
        foreach (var m in mapping)
            marked = marked.Replace(m.Const, m.Marker);

        string translated = await translateFunc(marked);

        foreach (var m in mapping)
            translated = translated.Replace(m.Marker, m.Const);

        if (ContainsAllConstants(translated, constants))
            return translated;
        return null;
    }

    // 3. Translation with [[MARKER_0]]
    private static async Task<string?> TryTranslateWithVerboseMarkersAsync(
        string text, Func<string, Task<string>> translateFunc, List<string> constants)
    {
        var mapping = constants.Select((c, i) => new { Const = c, Marker = $"[[MARKER_{i}]]" }).ToList();
        string marked = text;
        foreach (var m in mapping)
            marked = marked.Replace(m.Const, m.Marker);

        string translated = await translateFunc(marked);

        foreach (var m in mapping)
            translated = translated.Replace(m.Marker, m.Const);

        if (ContainsAllConstants(translated, constants))
            return translated;
        return null;
    }
    private static async Task<string> TranslateSplitAsync(
    string text, Func<string, Task<string>> translateFunc, List<string> constants)
    {
        var parts = Regex.Split(text, @"(\/[A-Z0-9_]+\/)"); // keep delimiters
        for (int i = 0; i < parts.Length; i++)
        {
            if (!constants.Contains(parts[i]))
            {
                string translated = await translateFunc(parts[i]);
                parts[i] = translated;
            }
        }
        return string.Join("", parts);
    }
    // Utility: check constants
    private static bool ContainsAllConstants(string text, List<string> constants)
    {
        return constants.All(c => text.Contains(c));
    }

    public static async Task<int> ApplyTranslationsAsync(
    string dictFilePath,
    Func<string, Task<string>> translateFunc,
    int batchSize = 100,
    Action<string, string, bool>? logTranslation = null,
    Action<string, string>? logError = null,
    Func<List<string>, Task<List<string>>>? batchTranslateFunc = null,
    CancellationToken token=default)
    {
        var all = File.ReadAllText(dictFilePath).Split(lineEnd, StringSplitOptions.RemoveEmptyEntries);
        int totalSuccess = 0;
        try
        {
            for (int batchStart = 0; batchStart < all.Length; batchStart += batchSize)
            {
                token.ThrowIfCancellationRequested();
                int batchEnd = Math.Min(batchStart + batchSize, all.Length);
                totalSuccess += await ProcessTranslationBatchAsync(
                    all, batchStart, batchEnd, translateFunc, batchTranslateFunc, logTranslation, logError, token).ConfigureAwait(false);

                await Task.Run(() => File.WriteAllText(dictFilePath, string.Join(lineEnd, all)));

            }
        }
        catch (OperationCanceledException)
        {
            await Task.Run(() => File.WriteAllText(dictFilePath, string.Join(lineEnd, all)));
        }

return totalSuccess;
    }

    private static async Task<int> ProcessTranslationBatchAsync(
    string[] all,
    int batchStart,
    int batchEnd,
    Func<string, Task<string>> translateFunc,
    Func<List<string>,Task<List<string>>>? batchTranslateFunc,
    Action<string, string, bool>? logTranslation,
    Action<string, string>? logError,
    CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int successCount = 0;

        var batch = all.Skip(batchStart).Take(batchEnd - batchStart).ToList();

        // Select items that need translation
        var itemsToTranslate = batch.Select((line, index) => (line, index))
            .Where(t => string.IsNullOrWhiteSpace(t.line.Split(sep)[2])).ToList();

        if (itemsToTranslate.Count == 0) return 0;

        if (batchTranslateFunc != null)
        {
            try
            {
                var originals = itemsToTranslate.Select(t => t.line.Split(sep)[1]).ToList();
                var translations = await batchTranslateFunc(originals).ConfigureAwait(false);

                if (translations != null && translations.Count == originals.Count)
                {
                    for (int i = 0; i < itemsToTranslate.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var (line, index) = itemsToTranslate[i];
                        var parts = line.Split(sep);
                        parts[2] = translations[i];
                        all[batchStart + index] = string.Join(sep, parts);
                        successCount++;
                        logTranslation?.Invoke(parts[1], translations[i], true);
                    }

                    return successCount; // Batch succeeded, no need for per-item
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TranslationDict] Batch translation failed: {ex.Message}");
                logError?.Invoke("Batch translation", ex.Message); 
            }
        }
        // Per-item translation directly
        foreach (var (line, index) in itemsToTranslate)
        {
            token.ThrowIfCancellationRequested();
            var parts = line.Split(sep);
            try
            {
                var translation = parts[1];
                translation = await TranslateWithFallbacksAsync(parts[1], translateFunc);
                parts[2] = translation;
                all[batchStart + index] = string.Join(sep, parts);
                successCount++;
                logTranslation?.Invoke(parts[1], translation, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                parts[2] = "";
                all[batchStart + index] = string.Join(sep, parts);
                logError?.Invoke(parts[1], ex.Message);
            }
        }
        return successCount;
    }
    public static async Task<string> TranslateWithFallbacksAsync(
    string text, Func<string, Task<string>> translateFunc)
    {
        var constants = Regex.Matches(text, @"\/[A-Z0-9_]+\/")
                             .Cast<Match>().Select(m => m.Value).ToList();

        // If no constants, just translate normally
        if (constants.Count == 0)
            return await translateFunc(text);

        // Tries to translate while mantaining constants.
        return await TryTranslateNormalAsync(text, translateFunc, constants)
            ?? await TryTranslateWithSimpleMarkersAsync(text, translateFunc, constants)
            ?? await TryTranslateWithVerboseMarkersAsync(text, translateFunc, constants)
            ?? await TranslateSplitAsync(text, translateFunc, constants);
    }
    public static int GetTranslationProgress(string dictFilePath)
    {
        if (!File.Exists(dictFilePath))
            return 0;

        var parts = File.ReadAllText(dictFilePath)
            .Split(lineEnd)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (parts.Length == 0)
            return 100;

        int translatedCount = parts.Count(line =>
        {
            var split = line.Split(sep);
            return split.Length >= 3 &&
                   !string.IsNullOrWhiteSpace(split[2]);
        });

        return (int)Math.Ceiling((translatedCount / (double)parts.Length) * 100);
    }
}
