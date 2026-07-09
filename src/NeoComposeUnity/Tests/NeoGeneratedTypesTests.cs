// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Collections.Generic;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoGeneratedTypesTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static TestProjectNeo LoadGeneratedClient(
            out string saveBuffer,
            NeoDialogueRuntimeOptions? dialogueOptions = null)
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var app = TestProjectNeo.Load(stack.Synchronizer, dialogueOptions)
                .GetAwaiter()
                .GetResult();
            saveBuffer = app.SerializeSaveData();
            return app;
        }

        private static T RequireAttribute<T>(NeoClient client, string id)
            where T : Attribute
        {
            if (!client.TryGetAttribute(id, out T? attr))
            {
                Assert.Fail($"Fixture is missing attribute '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        /// <summary>
        /// Inbound live save sessions (specs/live-save-sessions.md): a merged
        /// content blob applies into the running value graph in place, raising
        /// the same typed change events local writes raise, and rows the blob
        /// no longer carries fall back to authored defaults.
        /// </summary>
        [Test]
        public void ApplyExternalSaveContent_AppliesInboundLiveEditsInPlace()
        {
            var app = LoadGeneratedClient(out _);

            // Capture content with Score=2 as the "inbound co-editor edit".
            app.Save.Score = 2;
            var inbound = app.SerializeSaveData();

            // The running game has since moved on locally.
            app.Save.Score = 1;
            Assert.AreEqual(1, app.Save.Score);

            var changed = new System.Collections.Generic.List<int>();
            var sources = new System.Collections.Generic.List<NeoChangeSource>();
            using var subscription = app.Save.OnChanged(
                Root.Fields.Score, (score, source) =>
                {
                    changed.Add(score);
                    sources.Add(source);
                });

            app.Client.ApplyExternalSaveContent(inbound);

            Assert.AreEqual(2, app.Save.Score, "the inbound row re-shadowed the value");
            Assert.That(changed, Has.Count.EqualTo(1), "typed subscription fired once");
            Assert.AreEqual(
                NeoChangeSource.External,
                sources[0],
                "the inbound apply reports an external change source");

            // Re-applying identical content disturbs nothing.
            app.Client.ApplyExternalSaveContent(inbound);
            Assert.That(changed, Has.Count.EqualTo(1));

            // A plain local write reports Local.
            app.Save.Score = 3;
            Assert.AreEqual(NeoChangeSource.Local, sources[^1]);
        }

        /// <summary>
        /// Shadows the inbound content no longer carries (the web's "Reset to
        /// default") fall back to the authored value graph.
        /// </summary>
        [Test]
        public void ApplyExternalSaveContent_RestoresDroppedShadowsToAuthored()
        {
            var app = LoadGeneratedClient(out _);
            var authoredScore = app.Save.Score;
            var pristine = app.SerializeSaveData();

            app.Save.Score = authoredScore + 5;
            Assert.AreEqual(authoredScore + 5, app.Save.Score);

            app.Client.ApplyExternalSaveContent(pristine);

            Assert.AreEqual(
                authoredScore,
                app.Save.Score,
                "the dropped shadow falls back to the authored default");
        }

        /// <summary>
        /// The client subscribes to <see cref="INeoLiveContentSource"/> on
        /// construction and applies inbound live content itself — games never
        /// wire the synchronizer to the client by hand. Disposal detaches it.
        /// </summary>
        [Test]
        public void LiveContentSource_AppliesInboundContentWithoutManualWiring()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var loader = new LiveContentLoader(stack.Synchronizer);
            var app = TestProjectNeo.Load(loader).GetAwaiter().GetResult();

            app.Save.Score = 7;
            var inbound = app.SerializeSaveData();
            app.Save.Score = 1;

            var sources = new List<NeoChangeSource>();
            using var subscription = app.Save.OnChanged(
                Root.Fields.Score, (_, source) => sources.Add(source));

            loader.RaiseLiveContent(inbound);

            Assert.AreEqual(7, app.Save.Score, "the client applied the inbound content itself");
            Assert.AreEqual(NeoChangeSource.External, sources[0]);

            app.Dispose();
            loader.RaiseLiveContent(inbound);
            Assert.That(
                sources,
                Has.Count.EqualTo(1),
                "a disposed client detaches from the live content source");
        }

        /// <summary>
        /// Wraps the test stack's synchronizer so the test controls when live
        /// content arrives (the real synchronizer only raises it from a live
        /// session's websocket push).
        /// </summary>
        private sealed class LiveContentLoader : INeoSaveLoader, INeoLiveContentSource
        {
            private readonly INeoSaveLoader inner;

            public LiveContentLoader(INeoSaveLoader inner)
            {
                this.inner = inner;
            }

            public ProjectData Schema => inner.Schema;
            public string CustomId => inner.CustomId;

            public UnityEngine.Awaitable<string?> LoadSaveContentAsync() =>
                inner.LoadSaveContentAsync();

            public UnityEngine.Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot) =>
                inner.CommitSaveContentAsync(content, replaceSnapshot);

            public event System.Action<string>? OnLiveContentChanged;

            public void RaiseLiveContent(string content) =>
                OnLiveContentChanged?.Invoke(content);
        }

        private sealed class VectorFunctionHandler : IHeroFunctionHandler
        {
            public Vector3 MoveTo(Vector3 destination, Vector2Int? cell)
            {
                return cell.HasValue
                    ? destination + new Vector3(cell.Value.x, cell.Value.y, 0)
                    : destination;
            }
        }

        [Test]
        public void GeneratedRootClient_WrapsClientAndEnumHelpersSupportUnknownIds()
        {
            var app = LoadGeneratedClient(out _);
            INeoClient host = app;

            Assert.IsNotNull(app.Client);
            Assert.IsNotNull(app.Assets);
            Assert.IsNotNull(app.Save);
            Assert.IsNotNull(app.Session);
            Assert.AreSame(app.Client.SessionRoot, host.SessionRoot);
            Assert.IsNotNull(host.FindUnlinkedSaveValueIds());
            Assert.AreEqual("fire", Element.fire.optionId);
            Assert.IsTrue(Element.IsKnown("fire"));
            Assert.IsFalse(Element.IsKnown("modded-element"));
        }

        [Test]
        public void GeneratedPropertySetter_WritesThroughReadOnlyInterfaceReceiver()
        {
            var app = LoadGeneratedClient(out _);
            IReadOnlyRoot readOnlyRoot = app.Save;

            readOnlyRoot.Active = true;

            Assert.AreEqual(true, app.Session.Flag);
        }

        [Test]
        public void GeneratedLoad_PassesCustomSaveNameBuilderToNeoClient()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var app = TestProjectNeo.Load(
                stack.Synchronizer,
                saveOptions: new NeoSaveOptions { BuildSaveName = () => "patient-comet-808" })
                .GetAwaiter()
                .GetResult();

            Assert.IsNotNull(app);
            var save = JsonConvert.DeserializeObject<ProjectSaveData>(app.SerializeSaveData());
            Assert.AreEqual("patient-comet-808", save!.name);
        }

        [Test]
        public void GeneratedEnum_TextResolvesThroughLocalization()
        {
            var app = LoadGeneratedClient(out _);
            app.Client.Localization.TryAddLoadedLocale(new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = "test-project",
                versionId = "version-1",
                locale = "en-US",
                formattingSyntax = "smart-format",
                values = new Dictionary<string, string?>
                {
                    ["Fire"] = "Localized Fire",
                },
            });

            Assert.AreEqual("Fire", Element.fire.TextId);
            Assert.AreEqual("Localized Fire", Element.fire.Text);
            Assert.AreEqual("Localized Fire", Element.TextForOptionId("fire", app.Client));
            Assert.AreEqual("modded-element", Element.TextIdForOptionId("modded-element"));
        }

        [Test]
        public void GeneratedRootClient_DisposeForwardsToRuntimeClient()
        {
            var app = LoadGeneratedClient(out _);
            var client = app.Client;
            System.IDisposable disposable = app;

            disposable.Dispose();

            Assert.IsTrue(client.IsDisposed);
            Assert.Throws<System.ObjectDisposedException>(() =>
            {
                app.Dialogues.Standard.TryTrigger(out NeoDialogue _);
            });
            Assert.DoesNotThrow(() => disposable.Dispose());
        }

        [Test]
        public void GeneratedInheritance_ReadsInheritedAndOwnedMembers()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Client, "attr-derived");
            var derivedNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                app.Client,
                derivedAttr,
                null,
                NeoValueOwnership.Save);

            var generatedSaved = Derived.CreateWritable(app.Client, derivedNode);
            generatedSaved.Name = "Ancestor Name";
            generatedSaved.Health = 33;

            IReadOnlyBase asBase = generatedSaved;

            Assert.AreEqual("Ancestor Name", asBase.Name);
            Assert.AreEqual(33, generatedSaved.Health);
            Assert.IsTrue(asBase.TryWritable(out Derived writableDerived));
            Assert.AreSame(generatedSaved, writableDerived);
        }

        [Test]
        public void GeneratedSavedInheritance_SettersUpdateRuntimeValues()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Client, "attr-derived");
            var derivedNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                app.Client,
                derivedAttr,
                null,
                NeoValueOwnership.Save);

            var generated = Derived.CreateWritable(app.Client, derivedNode);

            generated.Name = "Saved Name";
            generated.Health = 44;

            Assert.AreEqual("Saved Name", generated.Name);
            Assert.AreEqual(44, generated.Health);
            Assert.IsTrue(app.SerializeSaveData().Contains("Saved Name"));
        }

        [Test]
        public void GeneratedWrapper_DisposeUnsubscribesFromAttributeChanges()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Client, "attr-derived");
            var derivedNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                app.Client,
                derivedAttr,
                null);
            var generated = Derived.CreateWritable(app.Client, derivedNode);
            int changes = 0;
            generated.OnChanged(Derived.Fields.Name, (_, _) => changes++);

            generated.Name = "Before Dispose";
            Assert.Greater(changes, 0);

            int beforeDispose = changes;
            generated.Dispose();
            var generatedSaved = Derived.CreateWritable(app.Client, derivedNode);
            generatedSaved.Name = "After Dispose";
            Assert.AreEqual(beforeDispose, changes);
        }

        [Test]
        public void GeneratedWrapper_FieldOnChanged_ReceivesTypedValue()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Client, "attr-derived");
            var derivedNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                app.Client,
                derivedAttr,
                null);
            var generated = Derived.CreateWritable(app.Client, derivedNode);
            string? observed = null;
            int changes = 0;
            using var subscription = generated.OnChanged(Derived.Fields.Name, (value, _) =>
            {
                observed = value;
                changes++;
            });

            generated.Name = "Typed Name";

            Assert.AreEqual("Typed Name", observed);
            Assert.AreEqual(1, changes);

            generated.Name = "Typed Name Again";

            Assert.AreEqual("Typed Name Again", observed);
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void GeneratedWrapper_GetLocalizedTextId_ReturnsTextIdForFieldTokens()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Client, "attr-derived");
            var derivedNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                app.Client,
                derivedAttr,
                null,
                NeoValueOwnership.Save);
            var generated = Derived.CreateWritable(app.Client, derivedNode);

            generated.Name = "text-hero-name";
            var nameNode = derivedNode.Get<NeoAttributeStringWritable>("Name");
            nameNode.value!.neoLocalizationMode = NeoStringLocalizationMode.TextId;
            app.Client.SetWritableValue(NeoValueOwnership.Save, nameNode.value);

            Assert.AreEqual(
                "text-hero-name",
                generated.GetLocalizedTextId(Derived.Fields.Name));
            Assert.IsNull(generated.GetLocalizedTextId(Derived.Fields.Health));

            generated.Name = "Literal Name";

            Assert.IsNull(generated.GetLocalizedTextId(Derived.Fields.Name));
            Assert.Throws<System.ArgumentException>(
                () => generated.GetLocalizedTextId(Hero.Fields.Name));
        }

        [Test]
        public void GeneratedWrapper_BatchOnChanged_ReportsChangedField()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Client, "attr-derived");
            var derivedNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                app.Client,
                derivedAttr,
                null);
            var generated = Derived.CreateWritable(app.Client, derivedNode);
            NeoChangedArgs<Derived.Fields>? observed = null;
            using var subscription = generated.OnChanged(args => observed = args);

            generated.Health = 42;

            Assert.IsNotNull(observed);
            Assert.IsTrue(observed!.TryGet(Derived.Fields.Health, out int? health));
            Assert.AreEqual(42, health);
            Assert.IsFalse(observed.Has(Derived.Fields.Name));
        }

        [Test]
        public void GeneratedConstructor_AddsCustomListEntryReadableThroughGeneratedRoot()
        {
            var app = LoadGeneratedClient(out _);

            app.Save.Heroes.Add(new Hero(Name: "Ada", Health: 7));

            Assert.AreEqual(1, app.Save.Heroes.Count);
            var hero = app.Save.Heroes[0];
            Assert.IsNotNull(hero);
            Assert.AreEqual("Ada", hero!.Name);
            Assert.AreEqual(7, hero.Health);

            var heroesNode = app.Client.save.Get<NeoAttributeListWritable>("Heroes");
            var childNode = (NeoAttributeCustom)heroesNode[0];
            Assert.IsNotNull(childNode.overrideValueId);
            Assert.IsTrue(app.Client.TryGetValue<ObjectAttributeValue>(
                childNode.overrideValueId!,
                out ObjectAttributeValue? row));
            Assert.AreEqual("type-hero", row!.typeId);
            Assert.IsTrue(app.SerializeSaveData().Contains("Ada"));
        }

        [Test]
        public void GeneratedConstructor_UsesAttributeDefaultsForOmittedArguments()
        {
            var app = LoadGeneratedClient(out _);
            RequireAttribute<StringAttribute>(app.Client, "attr-name").required = true;
            RequireAttribute<IntAttribute>(app.Client, "attr-health").required = true;

            var hero = new Hero();

            Assert.AreEqual("Hero", hero.Name);
            Assert.AreEqual(100, hero.Health);
        }

        [Test]
        public void GeneratedVectorProperties_ReadAndMutateComponents()
        {
            var app = LoadGeneratedClient(out _);

            var assetHero = (Hero)app.ResolveDialogueValue("v-dict")!;
            Vector3 authoredPosition = assetHero.Position;
            Vector3 pathEntry = assetHero.Path[0];

            Assert.AreEqual(new Vector3(1, 2, 3), authoredPosition);
            Assert.AreEqual(1f, assetHero.Position.x);
            Assert.AreEqual(4, assetHero.GridCell!.x);
            Assert.AreEqual(new Vector3(1, 2, 3), pathEntry);

            var hero = new Hero(
                Position: new NeoVector3(7, 8, 9),
                GridCell: new NeoVector3Int(1, 2, 3),
                Path: new[] { new NeoVector3(3, 4, 5) });

            // Assignment convention (specs/color-attribute.md §6): wrappers
            // expose no component setters; a per-component update is a
            // whole-value reassignment through the generated setter.
            hero.Position = new Vector3(10, hero.Position.y, hero.Position.z);
            Assert.AreEqual(10f, hero.Position.x);

            hero.Position = new Vector3(11, 12, 13);
            Vector3 replaced = hero.Position;
            Assert.AreEqual(new Vector3(11, 12, 13), replaced);

            hero.GridCell = new Vector3Int(hero.GridCell!.x, hero.GridCell.y, 99);
            Assert.AreEqual(99, hero.GridCell!.z);

            hero.GridCell = null;
            Assert.IsNull(hero.GridCell);

            hero.Path.Add(new Vector3(21, 22, 23));
            Vector3 appended = hero.Path[1];
            Assert.AreEqual(new Vector3(21, 22, 23), appended);
        }

        [Test]
        public void GeneratedVectorFunction_UsesUnityNativeSignature()
        {
            var app = LoadGeneratedClient(out _);
            var hero = (Hero)app.ResolveDialogueValue("v-dict")!;
            hero.FunctionHandler = new VectorFunctionHandler();

            var moved = hero.MoveTo(new Vector3(1, 2, 3), new Vector2Int(4, 5));

            Assert.AreEqual(new Vector3(5, 7, 3), moved);
        }

        [Test]
        public void GeneratedConstructor_RecursivelyCreatesNestedCustomDefaults()
        {
            var app = LoadGeneratedClient(out _);
            RequireAttribute<StringAttribute>(app.Client, "attr-name").required = true;
            RequireAttribute<IntAttribute>(app.Client, "attr-health").required = true;
            var heroAttribute = RequireAttribute<CustomAttribute>(app.Client, "attr-hero");
            heroAttribute.required = true;
            heroAttribute.defaultValue!.value = new Dictionary<string, string>();
            var types = (Dictionary<string, CustomType>)app.Client.types;
            types["type-default-holder"] = new CustomType
            {
                id = "type-default-holder",
                name = "DefaultHolder",
                schema = new Dictionary<string, string> { ["Hero"] = "attr-hero" },
                createdAt = "1970-01-01T00:00:00.000Z",
                updatedAt = "1970-01-01T00:00:00.000Z",
            };

            var holder = NeoGeneratedTypesSupport.CreateWritableCustomValue(
                app.Client,
                "type-default-holder",
                new Dictionary<string, string>(),
                System.Array.Empty<AttributeValue>());
            var hero = holder.Get<NeoAttributeCustomWritable>("Hero");

            Assert.AreEqual(
                "Hero",
                hero.Get<NeoAttributeString>("Name").value?.value);
            Assert.AreEqual(
                100,
                NeoGeneratedTypesSupport.ReadInt(
                    hero.Get<NeoAttributeInt>("Health")));
        }

        [Test]
        public void GeneratedNSGetterProperty_ComputesThroughRuntimeNode()
        {
            var app = LoadGeneratedClient(out _);

            NeoGeneratedTypesSupport.SetValue(
                app.Client.save,
                "Manifest",
                NeoGeneratedTypesSupport.Value<object?>(null));

            var result = app.Save.Manifest;
            var direct = app.Client.save.Get<NeoAttributeNSProperty>("Manifest").Compute();

            Assert.IsTrue(direct.ok, direct.error);
            Assert.AreEqual(direct.value?.ToString(), result);
        }

        [Test]
        public void GeneratedDialogueValueResolver_ReturnsRichGeneratedWrappers()
        {
            var app = LoadGeneratedClient(out _);

            var assetResolved = app.ResolveDialogueValue("v-dict");

            Assert.IsInstanceOf<Hero>(assetResolved);
            var assetHero = (Hero)assetResolved!;
            Assert.IsTrue(assetHero.IsReadOnly);
            Assert.IsFalse(assetHero.TryWritable(out Hero assetWritable));
            Assert.IsNull(assetWritable);
            Assert.AreEqual("v-dict", assetHero.valueId);

            var savedHero = new Hero(Name: "Saved Hero", Health: 9);
            var savedResolved = app.ResolveDialogueValue(savedHero.valueId!);

            Assert.IsInstanceOf<Hero>(savedResolved);
            var writableHero = (Hero)savedResolved!;
            Assert.IsFalse(writableHero.IsReadOnly);
            Assert.IsTrue(writableHero.TryWritable(out Hero resolvedWritable));
            Assert.AreSame(writableHero, resolvedWritable);
            Assert.AreEqual("Saved Hero", writableHero.Name);
        }

        [Test]
        public void GeneratedCustomValues_ExposeReadOnlyStateAndGuardWrites()
        {
            var app = LoadGeneratedClient(out _);

            Assert.IsTrue(app.Assets.IsReadOnly);
            Assert.IsInstanceOf<Root>(app.Assets);
            var assetRoot = (Root)app.Assets;
            Assert.IsTrue(assetRoot.IsReadOnly);
            Assert.IsFalse(assetRoot.TryWritable(out Root assetWritable));
            Assert.IsNull(assetWritable);
            Assert.IsFalse(app.Assets.TryWritable(out Root assetWritableFromInterface));
            Assert.IsNull(assetWritableFromInterface);

            var scoreBefore = assetRoot.Score;
            var setterError = Assert.Throws<System.InvalidOperationException>(() =>
                assetRoot.Score = scoreBefore + 1);
            StringAssert.Contains("Root.Score", setterError!.Message);

            Assert.DoesNotThrow(() => Assert.GreaterOrEqual(assetRoot.Heroes.Count, 0));
            var collectionError = Assert.Throws<System.InvalidOperationException>(() =>
                assetRoot.Heroes.Clear());
            StringAssert.Contains("Root.Heroes", collectionError!.Message);

            Assert.IsFalse(app.Save.IsReadOnly);
            Assert.IsTrue(app.Save.TryWritable(out Root writableRoot));
            Assert.AreSame(app.Save, writableRoot);

            app.Save.Score = scoreBefore + 1;
            Assert.AreEqual(scoreBefore + 1, app.Save.Score);
            Assert.DoesNotThrow(() => app.Save.Heroes.Clear());
        }

        [Test]
        public void GeneratedInheritedStorage_SaveBackedConcreteDescendantMutatesInheritedListFromAssetPath()
        {
            var app = LoadGeneratedClient(out _);
            var assetRoot = (Root)app.Assets;

            Assert.IsInstanceOf<SampleBlockedPath>(assetRoot.SampleLayerGroup);
            var blocked = (SampleBlockedPath)assetRoot.SampleLayerGroup;

            Assert.IsFalse(blocked.IsReadOnly);
            Assert.AreEqual(1, blocked.Tiles.Count);

            blocked.Tiles.Clear();

            Assert.AreEqual(0, blocked.Tiles.Count);
            StringAssert.Contains(
                "v-sample-tiles",
                app.SerializeSaveData(),
                "clearing the asset-authored inherited list should shadow the list row into the save store");
        }

        [Test]
        public void GeneratedInheritedStorage_AttachingConstructedListEntriesRetargetsHeldReferencesToSave()
        {
            var app = LoadGeneratedClient(out _);
            var assetRoot = (Root)app.Assets;
            var blocked = (SampleBlockedPath)assetRoot.SampleLayerGroup!;

            var tile = new SampleTileInstance(Value: 121212);
            blocked.Tiles.Add(tile);
            tile.Value = 343434;

            Assert.AreSame(tile, blocked.Tiles[blocked.Tiles.Count - 1]);
            Assert.AreEqual(343434, tile.Value);
            Assert.AreEqual(343434, blocked.Tiles[blocked.Tiles.Count - 1].Value);

            var saveData = app.SerializeSaveData();
            StringAssert.Contains(
                "343434",
                saveData,
                "the constructed list entry should write through the save-owned list path after attachment");
            StringAssert.DoesNotContain(
                "121212",
                saveData,
                "the initial session-only list entry value should not remain as a stale save row after the held reference is mutated");
        }

        [Test]
        public void GeneratedInheritedStorage_PropagatesThroughExplicitSaveSessionAndStaticChildren()
        {
            var app = LoadGeneratedClient(out _);
            var assetRoot = (Root)app.Assets;
            var storageA = assetRoot.StorageInherit;

            Assert.IsTrue(storageA.IsReadOnly);
            var assetError = Assert.Throws<System.InvalidOperationException>(() =>
                storageA.Value = 111111);
            StringAssert.Contains("StorageA.Value", assetError!.Message);

            var storageC = storageA.SaveChild.InheritChild;
            Assert.IsFalse(storageC.IsReadOnly);
            storageC.Value = 333333;
            Assert.AreEqual(333333, storageC.Value);
            StringAssert.Contains(
                "333333",
                app.SerializeSaveData(),
                "the inherit child under the explicit Save segment should write to the save overlay");

            var storageE = storageC.SessionChild.InheritChild;
            Assert.IsFalse(storageE.IsReadOnly);
            storageE.Value = 555555;
            Assert.AreEqual(555555, storageE.Value);
            StringAssert.DoesNotContain(
                "555555",
                app.SerializeSaveData(),
                "the inherit child under the explicit Session segment should stay out of serialized save data");

            Assert.IsInstanceOf<StorageF>(storageE.StaticChild);
            var storageF = (StorageF)storageE.StaticChild;
            Assert.IsInstanceOf<StorageG>(storageF.InheritChild);
            var storageG = (StorageG)storageF.InheritChild;

            Assert.IsTrue(storageG.IsReadOnly);
            Assert.IsFalse(storageG.TryWritable(out StorageG writableG));
            Assert.IsNull(writableG);
            var valueProperty = typeof(StorageG).GetProperty(nameof(StorageG.Value));
            Assert.IsNotNull(valueProperty);
            Assert.IsNull(
                valueProperty!.SetMethod,
                "the inherit child under the explicit Static segment should expose no public setter");
            Assert.Throws<System.ArgumentException>(() =>
                valueProperty.SetValue(storageG, 777777));
        }

        [Test]
        public void GeneratedInheritedStorage_AttachingConstructedValuesRetargetsHeldReferencesToSave()
        {
            var app = LoadGeneratedClient(out _);
            var saveRoot = (Root)app.Save;

            var storageA = new StorageA();
            saveRoot.StorageInherit = storageA;
            storageA.Value = 818181;

            var storageB = new StorageB();
            storageA.SaveChild = storageB;

            var storageC = new StorageC(Value: 101010);
            storageB.InheritChild = storageC;
            storageC.Value = 202020;

            Assert.AreSame(storageA, saveRoot.StorageInherit);
            Assert.AreSame(storageB, storageA.SaveChild);
            Assert.AreSame(storageC, storageB.InheritChild);
            var attachedA = saveRoot.StorageInherit!;
            Assert.AreEqual(818181, attachedA.Value);
            Assert.AreEqual(202020, attachedA.SaveChild!.InheritChild!.Value);

            var saveData = app.SerializeSaveData();
            StringAssert.Contains(
                "818181",
                saveData,
                "the constructed parent should write through the save-owned reference it was attached to");
            StringAssert.Contains(
                "202020",
                saveData,
                "the constructed inherit child should move from session to the save path after attachment");
            StringAssert.DoesNotContain(
                "101010",
                saveData,
                "the initial session-only value should not remain as a stale save row after the held reference is mutated");
        }

        [Test]
        public void GeneratedDialogueMemoryStore_FirstWritePersistsThroughFind()
        {
            var app = LoadGeneratedClient(out _);

            var created = app.Save.NeoMemory.GetOrCreateDialogueMemory("direct-dialogue");
            created.VisitCount += 1;

            var found = app.Save.NeoMemory.FindDialogueMemory("direct-dialogue");
            Assert.IsNotNull(found);
            Assert.AreEqual(1, found!.VisitCount);
            StringAssert.Contains("direct-dialogue", app.SerializeSaveData());
        }

        [Test]
        public void GeneratedDialogueGroup_UsesGeneratedValueResolverAndMemoryStore()
        {
            var now = new System.DateTime(
                2026,
                5,
                7,
                12,
                0,
                0,
                System.DateTimeKind.Utc);
            var app = LoadGeneratedClient(
                out _,
                new NeoDialogueRuntimeOptions
                {
                    UtcNow = () => now,
                    RandomDouble = () => 0,
                });

            Assert.IsTrue(app.Dialogues.Standard.TryTrigger(out NeoDialogue dialogue));

            Assert.AreEqual("dialogue-linked-hero", dialogue.Id);
            Assert.IsInstanceOf<Hero>(dialogue.Primary);
            Assert.IsTrue(dialogue.LinkedValues.TryGetValue("v-dict", out object? linked));
            Assert.IsInstanceOf<Hero>(linked);

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.IsInstanceOf<Hero>(shown!.Primary);
            Assert.IsTrue(shown.LinkedValues.TryGetValue("v-dict", out object? textLinked));
            Assert.IsInstanceOf<Hero>(textLinked);
            Assert.AreEqual(1, shown.Options.Count);

            shown.Options[0].Select();

            var memory = (NeoDialogueMemory)app.Save.NeoMemory
                .FindDialogueMemory("dialogue-linked-hero")!;
            Assert.AreEqual(1, memory.VisitCount);
            Assert.AreEqual(now.ToString("o"), memory.LastVisitedAt);

            var textMemory = (NeoTextNodeMemory)memory
                .FindTextNodeMemory("dialogue-linked-hero-text")!;
            Assert.AreEqual(1, textMemory.VisitCount);
            Assert.AreEqual(now.ToString("o"), textMemory.LastVisitedAt);
            Assert.AreEqual(
                "dialogue-linked-hero-option",
                textMemory.MostRecentChoiceId);
            Assert.IsTrue(textMemory.HasChoice("dialogue-linked-hero-option"));
            Assert.AreEqual(1, textMemory.ChoiceHistory.Count);
            Assert.AreEqual(
                "dialogue-linked-hero-option",
                textMemory.ChoiceHistory[0].ChoiceId);
        }

        [Test]
        public void GeneratedSession_MutationsDoNotSerializeOrResetOnCommit()
        {
            var app = LoadGeneratedClient(out _);
            const int transientScore = 424242;

            app.Session.Score = transientScore;
            Assert.AreEqual(transientScore, app.Session.Score);

            string serializedBeforeCommit = app.SerializeSaveData();
            Assert.IsFalse(serializedBeforeCommit.Contains(transientScore.ToString()));

            app.CommitAsync().GetAwaiter().GetResult();

            Assert.AreEqual(transientScore, app.Session.Score);
            Assert.IsFalse(app.SerializeSaveData().Contains(transientScore.ToString()));
        }

        [Test]
        public void GeneratedSession_ReloadStartsFromAuthoredDefaults()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));

            var first = TestProjectNeo.Load(stack.Synchronizer).GetAwaiter().GetResult();
            first.Session.Score = 777777;
            first.Save.Score = 12;
            first.CommitAsync().GetAwaiter().GetResult();

            var second = TestProjectNeo.Load(stack.Reopen()).GetAwaiter().GetResult();

            Assert.AreEqual(12, second.Save.Score);
            Assert.AreEqual(10, second.Session.Score);
            Assert.IsFalse(stack.PersistedContent()!.Contains("777777"));
        }

        [Test]
        public void GeneratedConstructor_CreatesTransientSessionValueThenPromotesToSave()
        {
            var app = LoadGeneratedClient(out _);

            var transient = new Hero(Name: "Transient Hero", Health: 1);
            Assert.IsNotNull(transient.valueId);
            Assert.IsTrue(app.Client.TryGetValueOwnership(
                transient.valueId!,
                out NeoValueOwnership initialOwnership));
            Assert.AreEqual(NeoValueOwnership.Session, initialOwnership);
            Assert.IsFalse(app.SerializeSaveData().Contains("Transient Hero"));
            Assert.AreEqual(0, app.RunGarbageCollector());
            Assert.IsTrue(app.Client.TryGetValue<ObjectAttributeValue>(
                transient.valueId!,
                out _));

            app.Save.Heroes.Add(transient);

            Assert.IsTrue(app.Client.TryGetValueOwnership(
                transient.valueId!,
                out NeoValueOwnership promotedOwnership));
            Assert.AreEqual(NeoValueOwnership.Save, promotedOwnership);
            Assert.IsTrue(app.SerializeSaveData().Contains("Transient Hero"));
        }
    }
}
