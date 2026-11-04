// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using G = NeoCompose.Tests.P127CodegenShapesParity;

namespace NeoCompose.Tests
{
    // The player and EditMode wrappers execute the same SDK and generated paths.
    public abstract class P127GeneratedRuntimeCases
    {
        internal const string ExportPath = "Packages/com.ryanbliss.neocompose/Tests/p127-codegen-shapes-parity-export.json";
        internal const string ResourceName = "P127GeneratedRuntime/export";
        private const string Slot = "RuntimeBox";
        private NeoProjectStore store = null!;
        private NeoClient client = null!;
        private G.TestProjectNeo project = null!;

        [SetUp]
        public void LoadDeliveredProject()
        {
#if UNITY_EDITOR
            string json = File.ReadAllText(ExportPath);
#else
            var asset = Resources.Load<TextAsset>(ResourceName);
            Assert.That(asset, Is.Not.Null, "The delivered shapes export must be packaged by the player prebuild setup.");
            string json = asset.text;
#endif
            var document = JObject.Parse(json);
            Assert.That(document["valuePartitions"], Is.Null, "Update the player loader if the shared export gains named partitions.");
            const string main = "Partitions/main.json";
            var partitions = new Dictionary<string, string>
            {
                [main] = document["values"]!.ToString(Formatting.None),
            };
            document.Remove("values");
            document["partitions"] = new JArray(new JObject { ["key"] = null, ["file"] = main });
            store = new NeoProjectStore(dataSource: new NeoJsonProjectDataSource(document.ToString(Formatting.None), partitions),
                localStore: new NeoInMemoryLocalSaveStore(), loadUserFile: false);
            store.LoadAsync().GetAwaiter().GetResult();
            var schema = store.Schema!;

            // Add one SDK-owned storage slot in memory; the delivered bytes stay intact.
            var save = (ClassMember)schema.members[schema.project.rootSaveFileMemberId];
            Assert.That(save.TryGetDeclaredClassId(out string saveClassId), Is.True);
            schema.classes[saveClassId].schema[Slot] = "runtime-box";
            schema.members["runtime-box"] = new ObjectMember
            {
                id = "runtime-box",
                projectId = schema.project.id,
                name = Slot,
                kind = MemberKind.Object,
            };
            client = new NeoClient(new SchemaLoader(schema));
            project = new G.TestProjectNeo(client);
        }

        [TearDown]
        public void DisposeProject()
        {
            project?.Dispose();
            client?.Dispose();
            store?.Dispose();
        }

        private void Store(object value) => NeoGeneratedTypesSupport.SetValue(client.save, Slot,
            NeoGeneratedTypesSupport.ObjectValue(client, client.save.Get<NeoMemberObject>(Slot), value));

        private static object? Read(NeoClient owner) =>
            NeoGeneratedTypesSupport.ReadObjectValue(owner.save.Get<NeoMemberObject>(Slot));

        private NeoClient Reload() => new(new SchemaLoader(client.Synchronizer.Schema), client.SerializeSaveData());

        [TestCase(true)]
        [TestCase(false)]
        public void StoredValueTypeComparerCrossesThroughTheCachedHostFactory(bool equality)
        {
            Store(equality ? (object)EqualityComparer<int>.Default : Comparer<int>.Default);
            using var reloaded = Reload();
            object comparer = Read(reloaded)!;
            Assert.That(Read(reloaded), Is.SameAs(comparer));
            if (equality)
            {
                var adapter = (IEqualityComparer<int>)comparer;
                Assert.That(adapter.Equals(7, 7), Is.True);
                Assert.That(adapter.Equals(7, 8), Is.False);
                Assert.That(adapter.GetHashCode(7), Is.EqualTo(7));
            }
            else
            {
                var adapter = (IComparer<int>)comparer;
                Assert.That(adapter.Compare(7, 8), Is.LessThan(0));
                Assert.That(adapter.Compare(8, 7), Is.GreaterThan(0));
            }
            // A real adapter came back from storage, not the original CLR singleton.
            Assert.That(comparer, Is.Not.SameAs(equality ? (object)EqualityComparer<int>.Default : Comparer<int>.Default));
        }

        [Test]
        public void StoredTupleBoxReconstructsItsClosedValueTypeAfterReload()
        {
            // This static construction also roots the tuple constructor for AOT.
            var original = new ValueTuple<int, float>(3, 0.5f);
            Store(original);
            using var reloaded = Reload();
            object restored = Read(reloaded)!;
            Assert.That(restored, Is.TypeOf<ValueTuple<int, float>>());
            Assert.That((ValueTuple<int, float>)restored, Is.EqualTo(original));
            Assert.That(Read(reloaded), Is.SameAs(restored));
        }

        [Test]
        public void StoredTypeReconstructsAClosedGeneratedGenericClass()
        {
            Store(typeof(G.Box<int>));
            using var reloaded = Reload();
            using var loadedProject = ReplaceProject(reloaded);
            Assert.That(Read(reloaded), Is.SameAs(typeof(G.Box<int>)));
            Assert.That(Read(reloaded), Is.SameAs(Read(reloaded)));
        }

        private G.TestProjectNeo ReplaceProject(NeoClient owner)
        {
            project.Dispose();
            return new G.TestProjectNeo(owner);
        }

        [Test]
        public void DefaultComparersDispatchThroughExplicitGeneratedBridges()
        {
            G.IReadOnlyExplicitEq first = new G.ExplicitEq(7);
            G.IReadOnlyExplicitEq same = new G.ExplicitEq(7);
            Assert.That(EqualityComparer<G.IReadOnlyExplicitEq>.Default.Equals(first, same), Is.True);
            Assert.That(EqualityComparer<G.IReadOnlyExplicitEq>.Default.Equals(first, new G.ExplicitEq(8)), Is.False);
            Assert.That(Comparer<G.ExplicitCmp>.Default.Compare(new G.ExplicitCmp(7), new G.ExplicitCmp(4)), Is.EqualTo(3));
            Assert.That(Comparer<G.IReadOnlyExplicitCmp>.Default.Compare(new G.ExplicitCmp(4), new G.ExplicitCmp(7)), Is.EqualTo(-3));
            Assert.That(first.TryWritable<G.ExplicitEq>(out var writable), Is.True);
            Assert.That(writable.K, Is.EqualTo(7));
        }

        [Test]
        public void GameCSharpCallsTheGeneratedDefaultInterfaceBody()
        {
            G.IV<string> value = new G.Kv();
            Assert.That(value.Name(), Is.EqualTo("v"));
        }

        [Test]
        public void GenericStaticsAndActionsAreSeparateForEachClosedConstruction()
        {
            G.Box<int>.Count = 3;
            G.Box<string>.Count = 5;
            Assert.That(G.Box<int>.Count, Is.EqualTo(3));
            Assert.That(G.Box<string>.Count, Is.EqualTo(5));
            Assert.That(G.Box<int>.Changed, Is.SameAs(G.Box<int>.Changed));
            Assert.That(G.Box<int>.Changed, Is.Not.SameAs(G.Box<string>.Changed));
            G.Box<int>.Changed.Invoke();
            G.Box<string>.Changed.Invoke();
            G.Box<int>.Count++;
            Assert.That(G.Box<int>.Count, Is.EqualTo(4));
            Assert.That(G.Box<string>.Count, Is.EqualTo(5));
        }

        private sealed class SchemaLoader : INeoSaveLoader
        {
            internal SchemaLoader(ProjectData schema) => Schema = schema;
            public ProjectData Schema
            {
                get;
            }
            public string CustomId => "generated-player-test";
            public Awaitable<string?> LoadSaveContentAsync() => NeoAwaitable.FromResult<string?>(null);
            public Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot) => NeoAwaitable.Completed();
        }
    }

    [PrebuildSetup(typeof(P127GeneratedRuntimeAssets))]
    [PostBuildCleanup(typeof(P127GeneratedRuntimeAssets))]
    [UnityPlatform(exclude = new[] { RuntimePlatform.OSXEditor, RuntimePlatform.WindowsEditor, RuntimePlatform.LinuxEditor })]
    public sealed class P127GeneratedPlayerTests : P127GeneratedRuntimeCases
    {
    }

    // The player fixture remains discoverable in the Editor for its build hooks.
    public sealed class P127GeneratedRuntimeAssets : IPrebuildSetup, IPostBuildCleanup
    {
        private const string DirectoryPath = "Assets/Resources/P127GeneratedRuntime";
        public void Setup()
        {
#if UNITY_EDITOR
            if (Directory.Exists(DirectoryPath))
                throw new InvalidOperationException("The player test resource directory already exists: " + DirectoryPath);
            Directory.CreateDirectory(DirectoryPath);
            File.Copy(P127GeneratedRuntimeCases.ExportPath, DirectoryPath + "/export.json");
            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.DeleteAsset(DirectoryPath);
#endif
        }
    }
}
