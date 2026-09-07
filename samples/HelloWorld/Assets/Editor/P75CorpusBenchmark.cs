// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>Opt-in benchmark over exports made by measure-p75-class-identity.ts.</summary>
public static class P75CorpusBenchmark
{
    public static void Run()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-neoP75CorpusPaths");
            if (index < 0 || index + 1 == args.Length)
                throw new ArgumentException("Pass -neoP75CorpusPaths with semicolon-separated corpus directories.");
            foreach (string path in args[index + 1].Split(';')) Measure(Path.GetFullPath(path));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Measure(string directory)
    {
        JObject root = JObject.Parse(File.ReadAllText(Path.Combine(directory, "body-root.json")));
        var results = new JArray();
        foreach (string name in new[] { "body-sparse", "body-position" })
        {
            string json = File.ReadAllText(Path.Combine(directory, name + ".json"));
            UnityEngine.Debug.Log($"P75 benchmark starting {directory}/{name}");
            var watch = Stopwatch.StartNew();
            ProjectData data = JsonConvert.DeserializeObject<ProjectData>(json)!;
            double parse = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(json),
                localStore: new NeoInMemoryLocalSaveStore());
            store.LoadAsync().GetAwaiter().GetResult();
            using NeoClient client = new NeoLoader().Load(
                store.Open("p75-benchmark"),
                localizationFileSource: new LocaleFiles(directory)).GetAwaiter().GetResult();
            double load = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var body = new NeoMemberClass(client, (string)root["memberId"]!, (string)root["rootId"]!);
            NeoMemberVector3 position = body.Get<NeoMemberVector3>("Position");
            var value = position.value!.value;
            if (value.x != (name == "body-position" ? 17 : 0)
                || value.y != (name == "body-position" ? 23 : 0) || value.z != 0)
                throw new InvalidOperationException("Reconstruction changed Position.");
            double first = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            double checksum = 0;
            for (int read = 0; read < 10000; read++) checksum += position.value!.value.x;
            double reads = watch.Elapsed.TotalMilliseconds;
            if (checksum != value.x * 10000) throw new InvalidOperationException("Position changed during reads.");
            var result = new JObject
            {
                ["case"] = name,
                ["parseAndPackedExpansionMs"] = parse,
                ["fullClientLoadIncludingReparseMs"] = load,
                ["firstPositionReadMs"] = first,
                ["tenThousandCachedPositionReadsMs"] = reads,
                ["logicalCorpusValues"] = data.values.Count,
            };
            results.Add(result);
            File.WriteAllText(Path.Combine(directory, "unity-report.json"), results.ToString());
            UnityEngine.Debug.Log($"P75 benchmark {result}");
        }
    }

    private sealed class LocaleFiles : INeoLocalizationLocaleFileSource
    {
        private readonly string directory;
        internal LocaleFiles(string directory) => this.directory = directory;
        public bool TryLoadResourcesLocale(ProjectLocalizationExport localization, string locale,
            out ProjectLocalizationLocaleFile? file)
        {
            file = localization.localeFileNames.TryGetValue(locale, out string name)
                ? JsonConvert.DeserializeObject<ProjectLocalizationLocaleFile>(File.ReadAllText(Path.Combine(directory, name)))
                : null;
            return file is not null;
        }
        public Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(
            ProjectLocalizationExport localization, string locale, string streamingAssetsRelativePath)
        {
            TryLoadResourcesLocale(localization, locale, out ProjectLocalizationLocaleFile? file);
            return Task.FromResult(file);
        }
    }
}
