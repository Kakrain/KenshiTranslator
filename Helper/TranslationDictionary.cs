using KenshiCore;
using System;
using System.Collections.Generic;
using System.IO;

public class TranslationDictionary
{
    private ReverseEngineer reverseEngineer;

    // Stores loaded translations from a .dict file
    private Dictionary<string, string> translations = new();
    private static string lineEnd="|_END_|\n";
    private static string sep = "|_SEP_|";

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
    public static async Task ApplyTranslationsAsync(
    string dictFilePath,
    Func<string, Task<string>> translateFunc,
    int batchSize = 100)
    {
        var all = File.ReadAllText(dictFilePath).Split(lineEnd, StringSplitOptions.RemoveEmptyEntries);
        int total = all.Length;
        int completed = 0;
        List<string> failedTranslations = new();

        for (int i = 0; i < total; i++)
        {
            var parts = all[i].Split(sep);
            string original = parts[1];
            string translated = parts[2];

            if (string.IsNullOrWhiteSpace(translated))
            {
                try
                {
                    int retries = 3;
                    for (int attempt = 0; attempt < retries; attempt++)
                    {
                        try
                        {
                            translated = await translateFunc(original);
                            if (!string.IsNullOrWhiteSpace(translated))
                            {
                                parts[2] = translated;
                                break;
                            }
                        }
                        catch (Exception ex) when (ex.Message.Contains("429"))
                        {
                            await Task.Delay((attempt + 1) * 1000);
                        }
                    }
                    await Task.Delay(100);
                }
                catch{}
            }
            all[i] = string.Join(sep, parts);
            completed++;

            // Save every batchSize lines
            if (i % batchSize == 0)
                File.WriteAllText(dictFilePath,string.Join(lineEnd,all));
        }
        File.WriteAllText(dictFilePath, string.Join(lineEnd, all));
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
