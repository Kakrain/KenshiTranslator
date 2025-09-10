﻿using System.Reflection;
namespace KenshiTranslator
{
    public static class ResourceLoader
    {
        public static Image LoadImage(string resourceName)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                return Image.FromStream(stream);
            }
        }
    }
}
