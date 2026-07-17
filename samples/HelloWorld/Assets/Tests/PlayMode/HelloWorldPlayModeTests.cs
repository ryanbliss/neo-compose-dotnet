// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HelloWorld.Assets.Tests.PlayMode
{
    /// <summary>
    /// Player-compatible smoke coverage for the generated Hello World client.
    /// This assembly deliberately has no UnityEditor reference, so these tests
    /// exercise the same schema and generated Class/Member runtime used by a game.
    /// </summary>
    public sealed class HelloWorldPlayModeTests
    {
        private const string ProjectResourcePath = "Neo/project";

        [UnityTest]
        public IEnumerator Schema8Export_LoadsClassAndMemberContractInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");
            yield return null;

            using var store = CreateLoadedStore();
            using var synchronizer = store.CreateNew("playmode-schema-9");
            var schema = synchronizer.Schema;

            Assert.IsNotNull(schema.metadata);
            Assert.AreEqual(8, schema.metadata!.schemaVersion);
            Assert.IsNotEmpty(schema.classes);
            Assert.IsNotEmpty(schema.members);

            var rootSaveMember = schema.members[schema.project.rootSaveFileMemberId];
            Assert.AreEqual(MemberKind.Class, rootSaveMember.kind);
            Assert.IsInstanceOf<ClassMember>(rootSaveMember);

            var rootSaveClass = schema.classes[((ClassMember)rootSaveMember).classId];
            Assert.AreEqual("Save", rootSaveClass.name);
            Assert.IsTrue(rootSaveClass.schema.TryGetValue("Bits", out var bitsMemberId));

            var bitsMember = schema.members[bitsMemberId];
            Assert.AreEqual("Bits", bitsMember.name);
            Assert.AreEqual(MemberKind.Int, bitsMember.kind);

            using var client = HelloWorldNeo.Load(
                    synchronizer,
                    localizationOptions: EnglishLocalizationOptions())
                .GetAwaiter()
                .GetResult();
            Assert.IsInstanceOf<NeoMemberClass>(client.AssetsRoot);
            Assert.IsInstanceOf<NeoMemberClassWritable>(client.SaveRoot);
        }

        [UnityTest]
        public IEnumerator GeneratedClassMemberApi_ReadsWritesAndNotifiesInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");

            using var store = CreateLoadedStore();
            using var synchronizer = store.CreateNew("playmode-generated-api");
            using var client = HelloWorldNeo.Load(
                    synchronizer,
                    localizationOptions: EnglishLocalizationOptions())
                .GetAwaiter()
                .GetResult();

            Assert.AreSame(Planet.earth, client.Save.World);
            Assert.AreEqual("Hello Earth!", client.Assets.Computed.fullText);

            var startingBits = client.Save.Bits;
            var observedBits = int.MinValue;
            var observedSource = NeoChangeSource.External;
            using var subscription = client.Save.OnChanged(
                Save.Fields.Bits,
                (bits, source) =>
                {
                    observedBits = bits;
                    observedSource = source;
                });

            client.Save.Bits = startingBits + 7;
            client.Save.World = Planet.mars;
            yield return null;

            Assert.AreEqual(startingBits + 7, client.Save.Bits);
            Assert.AreEqual(startingBits + 7, observedBits);
            Assert.AreEqual(NeoChangeSource.Local, observedSource);
            Assert.AreSame(Planet.mars, client.Save.World);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds(), client.SerializeSaveData());
        }

        private static NeoProjectStore CreateLoadedStore()
        {
            var export = Resources.Load<TextAsset>(ProjectResourcePath);
            Assert.IsNotNull(export, $"Missing Resources/{ProjectResourcePath}.json.");

            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(export!.text),
                localStore: new NeoInMemoryLocalSaveStore());
            try
            {
                store.LoadAsync().GetAwaiter().GetResult();
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        private static NeoLocalizationOptions EnglishLocalizationOptions()
        {
            return new NeoLocalizationOptions
            {
                localeOverride = "en-US",
                preloadSystemLocale = false,
            };
        }
    }
}
