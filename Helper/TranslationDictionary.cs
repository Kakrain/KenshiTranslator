using KenshiCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

public class TranslationDictionary
{
    private ReverseEngineer reverseEngineer;

    // Stores loaded translations from a .dict file
    private Dictionary<string, string> translations = new();
    private static readonly string lineEnd="|_END_|\n";
    private static readonly string sep = "|_SEP_|";


    public TranslationDictionary(ReverseEngineer re)
    {
        reverseEngineer = re;
    }
    // Export all strings to a .dict file
    public void ExportToDictFile(string path)
    {
        using var writer = new StreamWriter(path);

        // Export description
        if (reverseEngineer.modData.Header!.FileType == 16 && reverseEngineer.modData.Header.Description != null)
            writer.Write($"description{sep}{reverseEngineer.modData.Header.Description}{sep}{lineEnd}");

        // Export records
        int recordIndex = 1;
        foreach (var record in reverseEngineer.modData.Records!)
        {
            if (record.Name != null)
                writer.Write($"record{recordIndex}_name{sep}{record.Name}{sep}{lineEnd}");

            if (record.StringFields != null)
            {
                foreach (var kvp in record.StringFields)
                    if (!kvp.Value.Equals(""))
                        writer.Write($"record{recordIndex}_{kvp.Key}{sep}{kvp.Value}{sep}{lineEnd}");
            }
            recordIndex++;
        }
    }
    public void ImportFromDictFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Dictionary file not found.", path);
        translations.Clear();
        var all = File.ReadAllText(path);
        foreach (var segment in all.Split(lineEnd))
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;
            var parts = segment.Split(sep);
            if (parts.Length < 2) continue;
            var key = parts[0].Trim();
            var original = parts[1].Trim();
            var translated = parts.Length >= 3 ? parts[2].Trim() : "";
            translations[key] = !string.IsNullOrWhiteSpace(translated) ? translated : original;
        }


        if (reverseEngineer.modData.Header!.FileType == 16 && reverseEngineer.modData.Header.Description != null)
        {
            if (translations.TryGetValue("description", out var desc) && !string.IsNullOrWhiteSpace(desc))
            {
                reverseEngineer.modData.Header.Description = desc;
            }
        }

        int recordIndex = 1;
        foreach (var record in reverseEngineer.modData.Records!)
        {
            // record name
            string nameKey = $"record{recordIndex}_name";
            if (record.Name != null && translations.TryGetValue(nameKey, out var newName) && !string.IsNullOrWhiteSpace(newName))
            {
                record.Name = newName;
            }

            // string fields
            if (record.StringFields != null)
            {
                foreach (var kvp in record.StringFields.ToList())
                {
                    string fieldKey = $"record{recordIndex}_{kvp.Key}";
                    if (translations.TryGetValue(fieldKey, out var newValue) && !string.IsNullOrWhiteSpace(newValue))
                    {
                        record.StringFields[kvp.Key] = newValue;
                    }
                }
            }
            recordIndex++;
        }
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
    //Translation with ¤0¤ markers
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
    Func<List<string>, Task<List<string>>>? batchTranslateFunc = null)
    {
        var all = File.ReadAllText(dictFilePath).Split(lineEnd, StringSplitOptions.RemoveEmptyEntries);
        int totalSuccess = 0;

        for (int batchStart = 0; batchStart < all.Length; batchStart += batchSize)
        {
            int batchEnd = Math.Min(batchStart + batchSize, all.Length);
            totalSuccess += await ProcessTranslationBatchAsync(
                all, batchStart, batchEnd, translateFunc, batchTranslateFunc, logTranslation, logError);

            // Save after each batch
            File.WriteAllText(dictFilePath, string.Join(lineEnd, all));
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
    Action<string, string>? logError)
    {
        int successCount = 0;

        var batch = all.Skip(batchStart).Take(batchEnd - batchStart).ToList();

        // Select items that need translation
        var itemsToTranslate = batch
            .Select((line, index) => (line, index))
            .Where(t => string.IsNullOrWhiteSpace(t.line.Split(sep)[2]))
            .ToList();

        if (itemsToTranslate.Count == 0) return 0;

        if (batchTranslateFunc != null)
        {
            try
            {
                var originals = itemsToTranslate.Select(t => t.line.Split(sep)[1]).ToList();
                //var translations = await batchTranslateFunc(originals, "en", "ru").ConfigureAwait(false);
                var translations = await batchTranslateFunc(originals).ConfigureAwait(false);

                if (translations != null && translations.Count == originals.Count)
                {
                    for (int i = 0; i < itemsToTranslate.Count; i++)
                    {
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[TranslationDict] Batch translation failed: {ex.Message}");
                logError?.Invoke("Batch translation", ex.Message); 
            }
        }
        // Per-item translation directly
        foreach (var (line, index) in itemsToTranslate)
        {
            var parts = line.Split(sep);
            try
            {
                var translation = await TranslateWithFallbacksAsync(parts[1], translateFunc);
                parts[2] = translation;
                all[batchStart + index] = string.Join(sep, parts);
                successCount++;
                logTranslation?.Invoke(parts[1], translation, true);
            }
            catch (Exception ex)
            {
                parts[2] = parts[1];
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
        if (!File.Exists(dictFilePath)) return 0;
        var parts = File.ReadAllText(dictFilePath).Split(lineEnd).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (parts.Length == 0) return 100;
        int translatedCount = parts.Count(l => l.Split(sep).Length >= 3 && !string.IsNullOrWhiteSpace(l.Split(sep)[2]));


        return (int)Math.Round(Math.Ceiling(((translatedCount / (double)parts.Length) * 100)));
    }
}
