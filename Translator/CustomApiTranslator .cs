using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Google.Cloud.Translate.V3;
using Google.Api.Gax.ResourceNames;

namespace KenshiTranslator.Translator
{
    public enum ApiType
    {
        DeepL,
        GoogleCloud,
        GoogleCloudV3,
        Generic
    }

    public class CustomApiTranslator : TranslatorInterface
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly ApiType _apiType;
        private TranslationServiceClient? _googleV3Client;
        private string? _projectId;

        public string Name => "Custom API";

        // Public property to safely check API type without reflection
        public ApiType CurrentApiType => _apiType;

        public CustomApiTranslator(string apiInput)
        {
            if (string.IsNullOrWhiteSpace(apiInput))
                throw new ArgumentNullException(nameof(apiInput));

            _apiKey = apiInput;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Determine API type based on key format
            _apiType = DetermineApiType(apiInput);

            Debug.WriteLine($"[CustomApiTranslator] Detected API type: {_apiType}");

            if (_apiType == ApiType.DeepL)
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "KenshiTranslator/1.0");
            }
            else if (_apiType == ApiType.GoogleCloudV3)
            {
                InitializeGoogleV3Client();
            }
        }

        private ApiType DetermineApiType(string apiInput)
        {
            // DeepL API keys typically:
            // - Are exactly 39 characters long (ending with :fx for free plan)
            // - Contain alphanumeric characters and hyphens
            // - May end with ":fx" for free plan
            if (apiInput.Length == 39 && (apiInput.EndsWith(":fx") || !apiInput.Contains(".")))
            {
                return ApiType.DeepL;
            }

            // Google Cloud API keys typically:
            // - Start with "AIza"
            // - Are about 39 characters long
            // - Contain alphanumeric characters and some special chars
            if (apiInput.StartsWith("AIza") && apiInput.Length >= 35)
            {
                return ApiType.GoogleCloud;
            }

            // Google Cloud Service Account JSON file path
            // - Contains .json extension
            // - Path to service account credentials
            if (apiInput.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(apiInput))
            {
                return ApiType.GoogleCloudV3;
            }

            // If it starts with http, it's a generic endpoint
            if (apiInput.StartsWith("http"))
            {
                return ApiType.Generic;
            }

            // Default to DeepL if unsure (most common case)
            return ApiType.DeepL;
        }

        private void InitializeGoogleV3Client()
        {
            try
            {
                // Set environment variable for Google Cloud credentials
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _apiKey);

                // Create the client once
                _googleV3Client = TranslationServiceClient.Create();

                // Extract and cache project ID
                _projectId = GetProjectIdFromJson(_apiKey);

                Debug.WriteLine($"[Google V3] Initialized client for project: {_projectId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Google V3] Failed to initialize client: {ex.Message}");
                throw new InvalidOperationException($"Failed to initialize Google Cloud Translation V3: {ex.Message}", ex);
            }
        }

        public async Task<string> TranslateAsync(string text, string sourceLang = "auto", string targetLang = "en")
        {
            try
            {
                Debug.WriteLine($"[CustomApiTranslator] Translating: '{text}' from {sourceLang} to {targetLang} using {_apiType}");

                return _apiType switch
                {
                    ApiType.DeepL => await TranslateWithDeepL(text, sourceLang, targetLang),
                    ApiType.GoogleCloud => await TranslateWithGoogle(text, sourceLang, targetLang),
                    ApiType.GoogleCloudV3 => await TranslateWithGoogleV3(text, sourceLang, targetLang),
                    ApiType.Generic => await TranslateWithGenericApi(text, sourceLang, targetLang),
                    _ => throw new NotSupportedException($"API type {_apiType} not supported")
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CustomApiTranslator] Translation failed: {ex.Message}");
                throw new Exception($"Custom API translation failed: {ex.Message}", ex);
            }
        }

        private async Task<string> TranslateWithDeepL(string text, string sourceLang, string targetLang)
        {
            // Use free API endpoint for keys ending with :fx, otherwise use paid endpoint
            string endpoint = _apiKey.EndsWith(":fx") ?
                "https://api-free.deepl.com/v2/translate" :
                "https://api.deepl.com/v2/translate";

            var requestData = new
            {
                text = new[] { text },
                target_lang = targetLang.ToUpper(),
                source_lang = sourceLang != "auto" && !string.IsNullOrEmpty(sourceLang) ? sourceLang.ToUpper() : (string?)null
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(requestData, options);
            Debug.WriteLine($"[DeepL] Endpoint: {endpoint}");
            Debug.WriteLine($"[DeepL] Request: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);

            var responseContent = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[DeepL] Response: {responseContent}");

            response.EnsureSuccessStatusCode();

            var result = JsonSerializer.Deserialize<DeepLResponse>(responseContent);
            var translation = result?.translations?.FirstOrDefault()?.text ?? text;

            Debug.WriteLine($"[DeepL] Translation result: {translation}");
            return translation;
        }

        private async Task<string> TranslateWithGoogle(string text, string sourceLang, string targetLang)
        {
            // According to Google Cloud Translation v2 API documentation, use POST method with JSON body
            // and API key as query parameter for better reliability
            Debug.WriteLine($"[Google] Using v2 API with POST method for: '{text}' from {sourceLang} to {targetLang}");
            return await TranslateWithGooglePost(text, sourceLang, targetLang);
        }

        private async Task<string> TranslateWithGooglePost(string text, string sourceLang, string targetLang)
        {
            // Try both old and new endpoint formats
            string baseEndpoint = "https://translation.googleapis.com/language/translate/v2";
            var queryParams = new List<string>
            {
                $"key={Uri.EscapeDataString(_apiKey)}"
            };
            string endpoint = $"{baseEndpoint}?{string.Join("&", queryParams)}";

            // Create request body for POST - without API key in body
            var requestData = new
            {
                q = text,
                target = targetLang.ToLower(),
                source = sourceLang != "auto" && !string.IsNullOrEmpty(sourceLang) ? sourceLang.ToLower() : (string?)null,
                format = "text"
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(requestData, options);
            Debug.WriteLine($"[Google] POST Endpoint: {endpoint.Replace(_apiKey, "***API_KEY***")}");
            Debug.WriteLine($"[Google] POST Request: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Debug.WriteLine($"[Google] POST Response Status: {response.StatusCode}");
            Debug.WriteLine($"[Google] POST Response: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                // If old endpoint fails, try the legacy endpoint
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Debug.WriteLine($"[Google] Trying legacy endpoint due to {response.StatusCode}");
                    return await TranslateWithGoogleLegacy(text, sourceLang, targetLang);
                }
                throw new HttpRequestException($"Google Translate API error {response.StatusCode}: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GoogleResponse>(responseContent);
            var translation = result?.data?.translations?.FirstOrDefault()?.translatedText ?? text;

            Debug.WriteLine($"[Google] POST Translation result: {translation}");
            return translation;
        }

        private async Task<string> TranslateWithGoogleLegacy(string text, string sourceLang, string targetLang)
        {
            // Try the old legacy endpoint that might still work with some API keys
            string legacyEndpoint = "https://www.googleapis.com/language/translate/v2";
            var queryParams = new List<string>
            {
                $"key={Uri.EscapeDataString(_apiKey)}",
                $"q={Uri.EscapeDataString(text)}",
                $"target={targetLang.ToLower()}",
                "format=text"
            };

            if (sourceLang != "auto" && !string.IsNullOrEmpty(sourceLang))
            {
                queryParams.Add($"source={sourceLang.ToLower()}");
            }

            string fullUrl = $"{legacyEndpoint}?{string.Join("&", queryParams)}";
            Debug.WriteLine($"[Google] Legacy URL: {fullUrl.Replace(_apiKey, "***API_KEY***")}");

            var response = await _httpClient.GetAsync(fullUrl);
            var responseContent = await response.Content.ReadAsStringAsync();

            Debug.WriteLine($"[Google] Legacy Response Status: {response.StatusCode}");
            Debug.WriteLine($"[Google] Legacy Response: {responseContent}");

            response.EnsureSuccessStatusCode();

            var result = JsonSerializer.Deserialize<GoogleResponse>(responseContent);
            var translation = result?.data?.translations?.FirstOrDefault()?.translatedText ?? text;

            Debug.WriteLine($"[Google] Legacy Translation result: {translation}");
            return translation;
        }

        private async Task<string> TranslateWithGoogleV3(string text, string sourceLang, string targetLang)
        {
            if (_googleV3Client == null || string.IsNullOrEmpty(_projectId))
            {
                throw new InvalidOperationException("Google V3 client not initialized");
            }

            try
            {
                Debug.WriteLine($"[Google V3] Translating: '{text}' from {sourceLang} to {targetLang}");

                // Use LocationName for better performance and global access
                var parent = new LocationName(_projectId, "global").ToString();

                // Build the request with proper location
                var request = new TranslateTextRequest
                {
                    Parent = parent,
                    TargetLanguageCode = targetLang.ToLower(),
                    Contents = { text }
                };

                // Add source language if not auto-detect
                if (sourceLang != "auto" && !string.IsNullOrEmpty(sourceLang))
                {
                    request.SourceLanguageCode = sourceLang.ToLower();
                }

                // Call the API using the cached client
                var response = await _googleV3Client.TranslateTextAsync(request);

                var translation = response.Translations.FirstOrDefault()?.TranslatedText ?? text;
                Debug.WriteLine($"[Google V3] Translation result: {translation}");

                return translation;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Google V3] Translation failed: {ex.Message}");
                throw new Exception($"Google Cloud Translation V3 failed: {ex.Message}", ex);
            }
        }

        // New method for batch translation - much more efficient!
        public async Task<List<string>> TranslateBatchV3Async(List<string> texts, string sourceLang = "auto", string targetLang = "en")
        {
            if (_googleV3Client == null || string.IsNullOrEmpty(_projectId))
            {
                throw new InvalidOperationException("Google V3 client not initialized");
            }

            if (texts == null || texts.Count == 0)
            {
                return new List<string>();
            }

            // Google Cloud Translation V3 limits:
            // - Max 1024 text segments per request
            // - Max 30,000 UTF-8 bytes total per request
            const int maxTextsPerRequest = 100; // Conservative limit to avoid quota issues
            const int maxBytesPerRequest = 25000; // Conservative byte limit

            try
            {
                Debug.WriteLine($"[Google V3] Batch translating {texts.Count} texts from {sourceLang} to {targetLang}");

                var parent = new LocationName(_projectId, "global").ToString();
                var allTranslations = new List<string>();

                // Process in chunks if necessary
                for (int startIndex = 0; startIndex < texts.Count; startIndex += maxTextsPerRequest)
                {
                    var chunk = texts.Skip(startIndex).Take(maxTextsPerRequest).ToList();

                    // Check total byte size of this chunk
                    var totalBytes = chunk.Sum(t => System.Text.Encoding.UTF8.GetByteCount(t ?? ""));
                    if (totalBytes > maxBytesPerRequest)
                    {
                        Debug.WriteLine($"[Google V3] Chunk too large ({totalBytes} bytes), splitting further...");
                        // For now, fall back to smaller chunks
                        chunk = chunk.Take(50).ToList();
                    }

                    Debug.WriteLine($"[Google V3] Processing chunk: {chunk.Count} texts, {totalBytes} bytes");

                    // Build batch request for this chunk
                    var request = new TranslateTextRequest
                    {
                        Parent = parent,
                        TargetLanguageCode = targetLang.ToLower()
                    };

                    // Add all texts in chunk to the request
                    request.Contents.AddRange(chunk);

                    // Add source language if not auto-detect
                    if (sourceLang != "auto" && !string.IsNullOrEmpty(sourceLang))
                    {
                        request.SourceLanguageCode = sourceLang.ToLower();
                    }

                    // Call API for this chunk
                    var response = await _googleV3Client.TranslateTextAsync(request);

                    // CRITICAL: Validate response integrity
                    if (response.Translations.Count != chunk.Count)
                    {
                        Debug.WriteLine($"[Google V3] WARNING: Sent {chunk.Count} texts, received {response.Translations.Count} translations");

                        // Pad missing translations with originals to maintain order
                        var chunkTranslations = new List<string>();
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            if (i < response.Translations.Count)
                            {
                                chunkTranslations.Add(response.Translations[i].TranslatedText ?? chunk[i]);
                            }
                            else
                            {
                                chunkTranslations.Add(chunk[i]); // Use original if translation missing
                                Debug.WriteLine($"[Google V3] Missing translation for: '{chunk[i]}'");
                            }
                        }
                        allTranslations.AddRange(chunkTranslations);
                    }
                    else
                    {
                        // Normal case - same number of results as inputs
                        var chunkTranslations = response.Translations.Select(t => t.TranslatedText ?? "").ToList();
                        allTranslations.AddRange(chunkTranslations);
                    }

                    Debug.WriteLine($"[Google V3] Chunk completed: {allTranslations.Count}/{texts.Count} total translations");

                    // Small delay between chunks to avoid rate limiting
                    if (startIndex + maxTextsPerRequest < texts.Count)
                    {
                        await Task.Delay(100);
                    }
                }

                Debug.WriteLine($"[Google V3] Batch translation completed: {allTranslations.Count} results for {texts.Count} inputs");

                // Final validation
                if (allTranslations.Count != texts.Count)
                {
                    Debug.WriteLine($"[Google V3] CRITICAL ERROR: Result count mismatch. Expected {texts.Count}, got {allTranslations.Count}");
                    throw new InvalidOperationException($"Batch translation result count mismatch: expected {texts.Count}, got {allTranslations.Count}");
                }

                return allTranslations;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Google V3] Batch translation failed: {ex.Message}");
                throw new Exception($"Google Cloud Translation V3 batch failed: {ex.Message}", ex);
            }
        }

        private string GetProjectIdFromJson(string jsonPath)
        {
            try
            {
                var jsonContent = File.ReadAllText(jsonPath);
                var jsonDoc = JsonDocument.Parse(jsonContent);

                if (jsonDoc.RootElement.TryGetProperty("project_id", out var projectIdElement))
                {
                    return projectIdElement.GetString() ?? throw new InvalidOperationException("project_id is null");
                }

                throw new InvalidOperationException("project_id not found in JSON file");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract project_id from JSON: {ex.Message}", ex);
            }
        }

        private async Task<string> TranslateWithGenericApi(string text, string sourceLang, string targetLang)
        {
            var requestData = new
            {
                text = text,
                source_lang = sourceLang,
                target_lang = targetLang
            };

            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_apiKey, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CustomApiResponse>(responseContent);

            return result?.translated_text ?? text;
        }

        public async Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
        {
            return _apiType switch
            {
                ApiType.DeepL => await Task.FromResult(new Dictionary<string, string>
                {
                    {"BG", "Bulgarian"},
                    {"CS", "Czech"},
                    {"DA", "Danish"},
                    {"DE", "German"},
                    {"EL", "Greek"},
                    {"EN", "English"},
                    {"ES", "Spanish"},
                    {"ET", "Estonian"},
                    {"FI", "Finnish"},
                    {"FR", "French"},
                    {"HU", "Hungarian"},
                    {"ID", "Indonesian"},
                    {"IT", "Italian"},
                    {"JA", "Japanese"},
                    {"KO", "Korean"},
                    {"LT", "Lithuanian"},
                    {"LV", "Latvian"},
                    {"NB", "Norwegian"},
                    {"NL", "Dutch"},
                    {"PL", "Polish"},
                    {"PT", "Portuguese"},
                    {"RO", "Romanian"},
                    {"RU", "Russian"},
                    {"SK", "Slovak"},
                    {"SL", "Slovenian"},
                    {"SV", "Swedish"},
                    {"TR", "Turkish"},
                    {"UK", "Ukrainian"},
                    {"ZH", "Chinese"}
                }),
                ApiType.GoogleCloud => await Task.FromResult(new Dictionary<string, string>
                {
                    {"auto", "Auto-detect"},
                    {"en", "English"},
                    {"ru", "Russian"},
                    {"de", "German"},
                    {"fr", "French"},
                    {"es", "Spanish"},
                    {"it", "Italian"},
                    {"ja", "Japanese"},
                    {"ko", "Korean"},
                    {"zh", "Chinese"},
                    {"ar", "Arabic"},
                    {"hi", "Hindi"},
                    {"pt", "Portuguese"},
                    {"pl", "Polish"},
                    {"nl", "Dutch"},
                    {"sv", "Swedish"},
                    {"da", "Danish"},
                    {"no", "Norwegian"},
                    {"fi", "Finnish"}
                }),
                ApiType.GoogleCloudV3 => await Task.FromResult(new Dictionary<string, string>
                {
                    {"auto", "Auto-detect"},
                    {"en", "English"},
                    {"ru", "Russian"},
                    {"de", "German"},
                    {"fr", "French"},
                    {"es", "Spanish"},
                    {"it", "Italian"},
                    {"ja", "Japanese"},
                    {"ko", "Korean"},
                    {"zh", "Chinese"},
                    {"ar", "Arabic"},
                    {"hi", "Hindi"},
                    {"pt", "Portuguese"},
                    {"pl", "Polish"},
                    {"nl", "Dutch"},
                    {"sv", "Swedish"},
                    {"da", "Danish"},
                    {"no", "Norwegian"},
                    {"fi", "Finnish"}
                }),
                _ => await Task.FromResult(new Dictionary<string, string>
                {
                    {"auto", "Auto-detect"},
                    {"en", "English"},
                    {"ru", "Russian"},
                    {"de", "German"},
                    {"fr", "French"},
                    {"es", "Spanish"},
                    {"it", "Italian"},
                    {"ja", "Japanese"},
                    {"ko", "Korean"},
                    {"zh", "Chinese"}
                })
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            // TranslationServiceClient implements IDisposable in newer versions
            if (_googleV3Client is IDisposable disposableClient)
            {
                disposableClient.Dispose();
            }
        }

        private class CustomApiResponse
        {
            public string? translated_text { get; set; }
        }

        private class DeepLResponse
        {
            public DeepLTranslation[]? translations { get; set; }
        }

        private class DeepLTranslation
        {
            public string? text { get; set; }
            public string? detected_source_language { get; set; }
        }

        private class GoogleResponse
        {
            public GoogleData? data { get; set; }
        }

        private class GoogleData
        {
            public GoogleTranslation[]? translations { get; set; }
        }

        private class GoogleTranslation
        {
            public string? translatedText { get; set; }
            public string? detectedSourceLanguage { get; set; }
        }
    }
}