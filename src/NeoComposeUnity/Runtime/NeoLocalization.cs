// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public sealed class NeoLocalization
    {
        private readonly ProjectLocalizationExport? export;
        private readonly INeoLocalizationLocaleFileSource? source;
        private readonly INeoLocalizationFormatter formatter;
        private readonly bool useStreamingAssetsForNonMainLocales;
        private readonly string streamingAssetsRelativePath;
        private readonly Dictionary<string, ProjectLocalizationLocale> localeConfigByLocale = new();
        private readonly Dictionary<string, ProjectLocalizationLocaleFile> loadedLocales = new();
        private readonly HashSet<string> warnedMissingTextIds = new();

        public string MainLocale { get; }
        public string CurrentLocale { get; private set; }
        public IReadOnlyList<string> SupportedLocales { get; }
        public IReadOnlyCollection<string> LoadedLocales => loadedLocales.Keys;

        private NeoLocalization(
            ProjectLocalizationExport? export,
            INeoLocalizationLocaleFileSource? source = null,
            NeoLocalizationOptions? options = null,
            INeoLocalizationFormatter? formatter = null)
        {
            this.export = export;
            this.source = source;
            this.formatter = formatter ?? new NeoSmartFormatLocalizationFormatter();
            useStreamingAssetsForNonMainLocales = options?.useStreamingAssetsForNonMainLocales == true;
            streamingAssetsRelativePath =
                options?.streamingAssetsRelativePath ?? NeoComposeDefaults.LocalizationStreamingAssetsRelativePath;
            MainLocale = export?.mainLocale ?? "en-US";
            foreach (var locale in export?.supportedLocales ?? System.Array.Empty<ProjectLocalizationLocale>())
            {
                if (string.IsNullOrEmpty(locale.locale)) continue;
                if (!string.IsNullOrEmpty(locale.archivedAt)) continue;
                localeConfigByLocale[locale.locale] = locale;
            }
            SupportedLocales = export?.supportedLocales
                .Where(locale => string.IsNullOrEmpty(locale.archivedAt))
                .Select(locale => locale.locale)
                .Where(locale => !string.IsNullOrEmpty(locale))
                .ToArray() ?? System.Array.Empty<string>();
            CurrentLocale = ResolveInitialLocale(options);
        }

        public static NeoLocalization CreateEmpty(ProjectLocalizationExport? export)
        {
            return new NeoLocalization(export);
        }

        public static NeoLocalization LoadMain(
            ProjectLocalizationExport? export,
            INeoLocalizationLocaleFileSource source,
            NeoLocalizationOptions? options = null)
        {
            var localization = new NeoLocalization(export, source, options);
            if (export == null || string.IsNullOrEmpty(export.mainLocale))
            {
                return localization;
            }

            if (source.TryLoadResourcesLocale(export, export.mainLocale, out var mainFile) && mainFile != null)
            {
                localization.loadedLocales[mainFile.locale] = mainFile;
            }

            return localization;
        }

        public void SetLocale(string locale)
        {
            CurrentLocale = ResolveSupportedLocale(locale);
        }

        public bool LoadLocale(string locale)
        {
            var resolved = ResolveSupportedLocale(locale);
            return LoadLocaleIfAvailable(resolved) != null;
        }

        public async Task<bool> LoadLocaleAsync(string locale)
        {
            var resolved = ResolveSupportedLocale(locale);
            if (loadedLocales.ContainsKey(resolved)) return true;
            if (export == null || source == null) return false;

            if (!useStreamingAssetsForNonMainLocales ||
                string.Equals(resolved, MainLocale, System.StringComparison.OrdinalIgnoreCase))
            {
                return LoadLocale(resolved);
            }

            var file = await source.LoadStreamingAssetsLocaleAsync(
                export,
                resolved,
                streamingAssetsRelativePath);
            return TryAddLoadedLocale(file);
        }

        public async Task LoadAsync()
        {
            foreach (var locale in BuildLocaleFallbackChain(CurrentLocale))
            {
                await LoadLocaleAsync(locale);
            }
        }

        public string ResolveText(
            string? textId,
            IReadOnlyDictionary<string, object?>? arguments = null)
        {
            if (TryResolveTextTemplate(textId, out var template))
            {
                return FormatResolvedText(template, arguments);
            }
            return textId ?? "";
        }

        public bool TryResolveText(
            string? textId,
            out string value,
            IReadOnlyDictionary<string, object?>? arguments = null)
        {
            if (TryResolveTextTemplate(textId, out var template))
            {
                value = FormatResolvedText(template, arguments);
                return true;
            }
            value = textId ?? "";
            return false;
        }

        public string ResolveTextTemplate(string? textId)
        {
            return TryResolveTextTemplate(textId, out var value)
                ? value
                : textId ?? "";
        }

        public bool TryResolveTextTemplate(string? textId, out string value)
        {
            value = textId ?? "";
            if (string.IsNullOrEmpty(textId)) return false;

            foreach (var locale in BuildLocaleFallbackChain(CurrentLocale))
            {
                var localeFile = LoadLocaleIfAvailable(locale);
                if (localeFile == null) continue;
                if (!localeFile.values.TryGetValue(textId, out var candidate)) continue;
                if (candidate == null) continue;

                value = candidate;
                return true;
            }

            if (warnedMissingTextIds.Add(textId))
            {
                Debug.LogWarning($"Neo Compose localized text id '{textId}' was not found.");
            }
            return false;
        }

        public string FormatResolvedText(
            string template,
            IReadOnlyDictionary<string, object?>? arguments = null)
        {
            return FormatText("<resolved>", CurrentLocale, template, arguments);
        }

        internal bool TryAddLoadedLocale(ProjectLocalizationLocaleFile? file)
        {
            if (file == null || string.IsNullOrEmpty(file.locale)) return false;
            loadedLocales[file.locale] = file;
            return true;
        }

        private string ResolveInitialLocale(NeoLocalizationOptions? options)
        {
            if (!string.IsNullOrEmpty(options?.localeOverride))
            {
                return ResolveSupportedLocale(options.localeOverride!);
            }

            if (options?.preloadSystemLocale != false)
            {
                var systemLocale = CultureInfo.CurrentUICulture.Name;
                if (!string.IsNullOrEmpty(systemLocale))
                {
                    return ResolveSupportedLocale(systemLocale);
                }
            }

            return ResolveSupportedLocale(MainLocale);
        }

        private string ResolveSupportedLocale(string locale)
        {
            if (SupportedLocales.Count == 0) return MainLocale;
            var exact = SupportedLocales.FirstOrDefault(candidate =>
                string.Equals(candidate, locale, System.StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var language = LocaleLanguage(locale);
            if (!string.IsNullOrEmpty(language))
            {
                var languageMatch = SupportedLocales.FirstOrDefault(candidate =>
                    string.Equals(LocaleLanguage(candidate), language, System.StringComparison.OrdinalIgnoreCase));
                if (languageMatch != null) return languageMatch;
            }

            return SupportedLocales.FirstOrDefault(candidate =>
                string.Equals(candidate, MainLocale, System.StringComparison.OrdinalIgnoreCase)) ?? MainLocale;
        }

        private static string LocaleLanguage(string locale)
        {
            var separator = locale.IndexOfAny(new[] { '-', '_' });
            return separator < 0 ? locale : locale.Substring(0, separator);
        }

        private IEnumerable<string> BuildLocaleFallbackChain(string locale)
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var current = ResolveSupportedLocale(locale);
            while (!string.IsNullOrEmpty(current) && seen.Add(current))
            {
                yield return current;
                if (!localeConfigByLocale.TryGetValue(current, out var config)) break;
                current = string.IsNullOrEmpty(config.sourceLocale) ? MainLocale : ResolveSupportedLocale(config.sourceLocale!);
            }

            if (!string.IsNullOrEmpty(MainLocale) && seen.Add(MainLocale))
            {
                yield return MainLocale;
            }
        }

        private ProjectLocalizationLocaleFile? LoadLocaleIfAvailable(string locale)
        {
            if (loadedLocales.TryGetValue(locale, out var cached)) return cached;
            if (export == null || source == null) return null;
            if (useStreamingAssetsForNonMainLocales &&
                !string.Equals(locale, MainLocale, System.StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!source.TryLoadResourcesLocale(export, locale, out var file))
            {
                return null;
            }

            return TryAddLoadedLocale(file) ? file : null;
        }

        private string FormatText(
            string textId,
            string locale,
            string template,
            IReadOnlyDictionary<string, object?>? arguments)
        {
            try
            {
                return formatter.Format(template, arguments);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"Neo Compose localized text id '{textId}' for locale '{locale}' could not be formatted: {ex.Message}");
                return template;
            }
        }
    }
}
