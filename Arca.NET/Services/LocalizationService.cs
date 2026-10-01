using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Arca.NET.Services;

public static class LocalizationService
{
    private const string SpanishUri = "Localization/Strings.es.xaml";
    private const string EnglishUri = "Localization/Strings.en.xaml";

    public static string CurrentLanguage { get; private set; } = "es";

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string languageCode)
    {
        var lang = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
        CurrentLanguage = lang;

        var culture = new CultureInfo(lang == "en" ? "en-US" : "es-ES");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        var uriString = lang == "en" ? EnglishUri : SpanishUri;
        var dictUri = new Uri(uriString, UriKind.Relative);

        var appResources = System.Windows.Application.Current?.Resources;
        if (appResources == null) return;

        var oldDict = appResources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

        var newDict = new ResourceDictionary { Source = dictUri };

        if (oldDict != null)
        {
            var index = appResources.MergedDictionaries.IndexOf(oldDict);
            appResources.MergedDictionaries[index] = newDict;
        }
        else
        {
            appResources.MergedDictionaries.Add(newDict);
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string GetString(string key, string fallback = "")
    {
        try
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is string text)
            {
                return text;
            }
        }
        catch { }
        return string.IsNullOrEmpty(fallback) ? key : fallback;
    }
}
