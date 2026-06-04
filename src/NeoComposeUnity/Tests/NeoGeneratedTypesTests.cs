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
            string buffer = "";
            string loadSave() => buffer;
            void handleSave(string file) => buffer = file;
            var app = TestProjectNeo.Load(
                LoadFixture("synth-example.json"),
                loadSave,
                handleSave,
                dialogueOptions);
            saveBuffer = buffer;
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
        public void GeneratedLoad_PassesCustomSaveNameBuilderToNeoClient()
        {
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var app = TestProjectNeo.Load(
                LoadFixture("synth-example.json"),
                loadSave,
                handleSave,
                saveOptions: new NeoSaveOptions { BuildSaveName = () => "patient-comet-808" });

            Assert.IsNotNull(app);
            var save = JsonConvert.DeserializeObject<ProjectSaveData>(saveBuffer);
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

            var generatedSaved = new Derived(app.Client, derivedNode);
            generatedSaved.Name = "Ancestor Name";
            generatedSaved.Health = 33;

            var generated = new ReadOnlyDerived(app.Client, derivedNode);
            ReadOnlyBase asBase = generated;

            Assert.AreEqual("Ancestor Name", asBase.Name);
            Assert.AreEqual(33, generated.Health);
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

            var generated = new Derived(app.Client, derivedNode);

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
            var generated = new Derived(app.Client, derivedNode);
            int changes = 0;
            generated.OnChanged(Derived.Fields.Name, _ => changes++);

            generated.Name = "Before Dispose";
            Assert.Greater(changes, 0);

            int beforeDispose = changes;
            generated.Dispose();
            var generatedSaved = new Derived(app.Client, derivedNode);
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
            var generated = new Derived(app.Client, derivedNode);
            string? observed = null;
            int changes = 0;
            using var subscription = generated.OnChanged(Derived.Fields.Name, value =>
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
            var generated = new Derived(app.Client, derivedNode);

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
            var generated = new Derived(app.Client, derivedNode);
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
            var direct = app.Client.save.Get<NeoAttributeNSGetter>("Manifest").Compute();

            Assert.IsTrue(direct.ok, direct.error);
            Assert.AreEqual(direct.value?.ToString(), result);
        }

        [Test]
        public void GeneratedDialogueValueResolver_ReturnsRichGeneratedWrappers()
        {
            var app = LoadGeneratedClient(out _);

            var assetResolved = app.ResolveDialogueValue("v-dict");

            Assert.IsInstanceOf<ReadOnlyHero>(assetResolved);
            Assert.IsNotInstanceOf<Hero>(assetResolved);
            Assert.AreEqual("v-dict", ((ReadOnlyHero)assetResolved!).valueId);

            var savedHero = new Hero(Name: "Saved Hero", Health: 9);
            var savedResolved = app.ResolveDialogueValue(savedHero.valueId!);

            Assert.IsInstanceOf<Hero>(savedResolved);
            Assert.AreEqual("Saved Hero", ((Hero)savedResolved!).Name);
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
            Assert.IsInstanceOf<ReadOnlyHero>(dialogue.Primary);
            Assert.IsTrue(dialogue.LinkedValues.TryGetValue("v-dict", out object? linked));
            Assert.IsInstanceOf<ReadOnlyHero>(linked);

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.IsInstanceOf<ReadOnlyHero>(shown!.Primary);
            Assert.IsTrue(shown.LinkedValues.TryGetValue("v-dict", out object? textLinked));
            Assert.IsInstanceOf<ReadOnlyHero>(textLinked);
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

            app.Commit();

            Assert.AreEqual(transientScore, app.Session.Score);
            Assert.IsFalse(app.SerializeSaveData().Contains(transientScore.ToString()));
        }

        [Test]
        public void GeneratedSession_ReloadStartsFromAuthoredDefaults()
        {
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var first = TestProjectNeo.Load(
                LoadFixture("synth-example.json"),
                loadSave,
                handleSave);
            first.Session.Score = 777777;
            first.Save.Score = 12;
            first.Commit();

            var second = TestProjectNeo.Load(
                LoadFixture("synth-example.json"),
                loadSave,
                handleSave);

            Assert.AreEqual(12, second.Save.Score);
            Assert.AreEqual(10, second.Session.Score);
            Assert.IsFalse(saveBuffer.Contains("777777"));
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
