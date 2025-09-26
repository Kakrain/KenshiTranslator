
namespace KenshiTranslator.Translator
{
    public class GTranslate_Translator : TranslatorInterface
    {
        public string Name => "Google Translate";
        private static readonly Lazy<GTranslate_Translator> _instance =
            new(() => new GTranslate_Translator());
        private Dictionary<string, GTranslate.Translators.ITranslator> translators;
        private GTranslate.Translators.ITranslator current_translator;

        public GTranslate_Translator()
        {
            translators = new() {
            { "Aggregate", new GTranslate.Translators.AggregateTranslator()},
            { "Bing", new GTranslate.Translators.BingTranslator()},
            { "Google", new GTranslate.Translators.GoogleTranslator()},
            { "Google2", new GTranslate.Translators.GoogleTranslator2()},
            { "Microsoft", new GTranslate.Translators.MicrosoftTranslator()},
            { "Yandex", new GTranslate.Translators.YandexTranslator()}
        };
            current_translator = translators.GetValueOrDefault("Aggregate")!;
        }
        public void setTranslator(string translator)
        {
            current_translator = translators.GetValueOrDefault(translator)!; 
        }
        public static GTranslate_Translator Instance => _instance.Value;
        public async Task<string> TranslateAsync(string text, string sourceLang  = "auto", string targetLang = "en")
        {
            //try
            //{
                var from = GTranslate.Language.GetLanguage(sourceLang);
                var to = GTranslate.Language.GetLanguage(targetLang);
                var translated = await current_translator.TranslateAsync(text, to, from);
                return translated.Translation;
           // }
            //catch(Exception ex)
           // {
            //    MessageBox.Show($"Translator failed on text:\n\"{text}\"\n\nError: {ex.Message}");
            //    return "";
           // }
        }
        public async Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
        {
            return await Task.Run(() =>
            {
                return GTranslate.Language.LanguageDictionary.Values.OrderBy(l=>(l.ISO6391 ?? l.ISO6393))
                .ToDictionary(
                 lang => lang.ISO6391 ?? lang.ISO6393,
                  lang => $"{lang.Name} ({lang.NativeName})"
                );

            });
        }
    }
}
