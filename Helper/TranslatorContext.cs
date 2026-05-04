using KenshiTranslator.Translator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiTranslator.Helper
{
    public class TranslatorContext
    {
        public TranslatorInterface Active { get; private set; } = GTranslate_Translator.Instance;
        public CustomApiTranslator? Custom { get; private set; }

        public async Task<Dictionary<string, string>> SetProviderAsync(
            string provider,
            Func<string?> getCustomInput,
            Action<string> setStatus)
        {
            Custom?.Dispose();
            Custom = null;

            if (provider == "Custom API")
            {
                var key = getCustomInput();
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Missing API key");

                Custom = new CustomApiTranslator(key);
                Active = Custom;
            }
            else if (provider == "Google Cloud V3")
            {
                var path = getCustomInput();
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("Missing JSON");

                Custom = new CustomApiTranslator(path);
                Active = Custom;
            }
            if (provider == "Dummy")
            {
                Active = new DummyTranslator();
            }
            else
            {
                var gt = GTranslate_Translator.Instance;
                gt.setTranslator(provider);
                Active = gt;
            }

            return await Active.GetSupportedLanguagesAsync();
        }
    }
}
