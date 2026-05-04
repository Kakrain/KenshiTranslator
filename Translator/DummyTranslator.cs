using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiTranslator.Translator
{
    public class DummyTranslator : TranslatorInterface
    {
        public string Name => "Dummy";

        public Task<string> TranslateAsync(string text, string sourceLang = "auto", string targetLang = "en")
        {
            string result = $"[{targetLang}] {text+" to be translated"}";
            return Task.FromResult(result);
        }
        public Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
        {
            // Minimal but valid set
            var langs = new Dictionary<string, string>
            {
                { "auto", "Auto Detect" },
                { "en", "English" },
                { "ru", "Russian" },
                { "de", "German" },
                { "jp", "Japanese" }
            };

            return Task.FromResult(langs);
        }
    }
}
