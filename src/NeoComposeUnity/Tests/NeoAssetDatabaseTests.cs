// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NeoCompose.Tests
{
    public class NeoAssetDatabaseTests
    {
        private string directory = null!;
        private string resourcesDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            string name = "NeoAssetDatabaseTests-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            directory = "Assets/" + name;
            AssetDatabase.CreateFolder(directory, "Resources");
            resourcesDirectory = directory + "/Resources";
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(directory);

        [Test]
        public async Task LegacyDatabaseDefersAudioUntilPreloadedAndKeepsItsReference()
        {
            string clipPath = WriteClip("legacy");
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string clipGuid, out long clipId);
            var temporaryDatabase = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var script = MonoScript.FromScriptableObject(temporaryDatabase);
            string scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
            Object.DestroyImmediate(temporaryDatabase);
            string databasePath = directory + "/Legacy.asset";
            // The database before lazy audio references serialized this exact PPtr shape.
            File.WriteAllText(databasePath, $@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}
  m_Name: Legacy
  files:
  - fileId: legacy
    assetPath: {clipPath}
    sprites: []
    audioClip: {{fileID: {clipId}, guid: {clipGuid}, type: 3}}
  tileAssets: []
");
            Resources.UnloadAsset(clip);
            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceSynchronousImport);
            var database = AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(databasePath);
            Assert.That(database.Files.Single().FileId, Is.EqualTo("legacy"));
            Assert.That(IsClipLoaded("legacy"), Is.False, "Reading metadata must not decode audio.");
            var unrelated = AudioClip.Create("unrelated", 32, 1, 8000, false);
            try
            {
                Assert.That(database.TryGetEntryForAudioClip(unrelated), Is.Null);
                Assert.That(IsClipLoaded("legacy"), Is.False, "Reverse lookup must not decode unrelated clips.");
                await database.PreloadAudioAsync();
                var loaded = database.TryGetAudioClip("legacy");
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
                Assert.That(database.TryGetValueForAudioClip(loaded)!.fileId, Is.EqualTo("legacy"));
                Assert.That(database.TryGetAudioClip("missing"), Is.Null);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(loaded, out string loadedGuid, out long loadedId);
                Assert.That(loadedGuid, Is.EqualTo(clipGuid));
                Assert.That(loadedId, Is.EqualTo(clipId));
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
                Resources.UnloadAsset(database);
                var reloaded = AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(databasePath);
                Assert.That(reloaded.TryGetAudioClip("legacy"), Is.SameAs(loaded));
            }
            finally
            {
                Object.DestroyImmediate(unrelated);
            }
        }

        [Test]
        public void RuntimeClipsCanBeReplacedClearedAndOverwritten()
        {
            var first = AudioClip.Create("first", 32, 1, 8000, false);
            var second = AudioClip.Create("second", 32, 1, 8000, false);
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            try
            {
                database.SetFile("audio", "audio", "Assets/runtime.wav", "", "", "", null, "", "", audioClip: first);
                var entry = database.Files.Single();
                Assert.That(database.TryGetAudioClip("audio"), Is.SameAs(first));
                var replacement = new NeoAssetDatabaseEntry { FileId = "audio", AudioClip = second };
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(replacement), entry);
                Assert.That(database.TryGetAudioClip("audio"), Is.SameAs(second));
                Assert.That(database.TryGetEntryForAudioClip(first), Is.Null);
                Assert.That(database.TryGetEntryForAudioClip(second), Is.SameAs(entry));
                entry.AudioClip = null;
                Assert.That(database.TryGetAudioClip("audio"), Is.Null);
                Assert.That(database.TryGetEntryForAudioClip(second), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(database);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public async Task NonPreloadedAudioRemainsUnloaded()
        {
            string path = WriteClip("on-demand", preload: false);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            try
            {
                database.SetFile("audio", "audio", path, "", "", "", null, "", "", audioClip: clip);
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Unloaded));
                await database.PreloadAudioAsync();
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Unloaded));
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void CancelledPreloadDoesNotStartLoading()
        {
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            try
            {
                Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                    await database.PreloadAudioAsync(new CancellationToken(canceled: true)));
                Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () =>
                    await NeoAssetDatabase.LoadDefaultAsync(new CancellationToken(canceled: true)));
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        private bool IsClipLoaded(string name) => Resources.FindObjectsOfTypeAll<AudioClip>().Any(clip => clip.name == name);

        private string WriteClip(string name, bool preload = true)
        {
            string path = resourcesDirectory + "/" + name + ".wav";
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                const int samples = 8000;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(8000);
                writer.Write(16000);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                    writer.Write((short)(Math.Sin(i * 0.1) * 1000));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.loadInBackground = true;
            var settings = importer.defaultSampleSettings;
            settings.preloadAudioData = preload;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return path;
        }
    }
}
