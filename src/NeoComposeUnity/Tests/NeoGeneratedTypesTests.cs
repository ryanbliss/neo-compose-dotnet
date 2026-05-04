// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
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

        private static TestProjectNeo LoadGeneratedClient(out string saveBuffer)
        {
            string buffer = "";
            string loadSave() => buffer;
            void handleSave(string file) => buffer = file;
            var app = TestProjectNeo.Load(LoadFixture("synth-example.json"), loadSave, handleSave);
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
        public void GeneratedRootClient_WrapsRuntimeAndEnumHelpersSupportUnknownIds()
        {
            var app = LoadGeneratedClient(out _);

            Assert.IsNotNull(app.Runtime);
            Assert.IsNotNull(app.Assets);
            Assert.IsNotNull(app.Save);
            Assert.AreEqual(Element.fire, "fire");
            Assert.IsTrue(Element.IsKnown("fire"));
            Assert.IsFalse(Element.IsKnown("modded-element"));
        }

        [Test]
        public void GeneratedInheritance_ReadsInheritedAndOwnedMembers()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Runtime, "attr-derived");
            var derivedNode = (NeoAttributeCustomSaved)NeoAttribute.CreateSaved(
                app.Runtime,
                derivedAttr,
                null);

            var generatedSaved = new Derived(app.Runtime, derivedNode);
            generatedSaved.Name = "Ancestor Name";
            generatedSaved.Health = 33;

            var generated = new ReadOnlyDerived(app.Runtime, derivedNode);
            ReadOnlyBase asBase = generated;

            Assert.AreEqual("Ancestor Name", asBase.Name);
            Assert.AreEqual(33, generated.Health);
        }

        [Test]
        public void GeneratedSavedInheritance_SettersUpdateRuntimeValues()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Runtime, "attr-derived");
            var derivedNode = (NeoAttributeCustomSaved)NeoAttribute.CreateSaved(
                app.Runtime,
                derivedAttr,
                null);

            var generated = new Derived(app.Runtime, derivedNode);

            generated.Name = "Saved Name";
            generated.Health = 44;

            Assert.AreEqual("Saved Name", generated.Name);
            Assert.AreEqual(44, generated.Health);
            Assert.IsTrue(app.Runtime.SerializeSaveData().Contains("Saved Name"));
        }

        [Test]
        public void GeneratedWrapper_DisposeUnsubscribesFromAttributeChanges()
        {
            var app = LoadGeneratedClient(out _);
            var derivedAttr = RequireAttribute<CustomAttribute>(app.Runtime, "attr-derived");
            var derivedNode = (NeoAttributeCustomSaved)NeoAttribute.CreateSaved(
                app.Runtime,
                derivedAttr,
                null);
            var generated = new ReadOnlyDerived(app.Runtime, derivedNode);
            int changes = 0;
            generated.OnChanged += () => changes++;

            var generatedSaved = new Derived(app.Runtime, derivedNode);
            generatedSaved.Name = "Before Dispose";
            Assert.Greater(changes, 0);

            int beforeDispose = changes;
            generated.Dispose();
            generatedSaved.Name = "After Dispose";
            Assert.AreEqual(beforeDispose, changes);
        }

        [Test]
        public void GeneratedFactory_AddsCustomListEntryReadableThroughGeneratedRoot()
        {
            var app = LoadGeneratedClient(out _);

            app.Save.Heroes.Add(Hero.factory(app.Runtime, Name: "Ada", Health: 7));

            Assert.AreEqual(1, app.Save.Heroes.Count);
            var hero = app.Save.Heroes[0];
            Assert.AreEqual("Ada", hero.Name);
            Assert.AreEqual(7, hero.Health);

            var heroesNode = app.Runtime.save.Get<NeoAttributeListSaved>("Heroes");
            var childNode = (NeoAttributeCustom)heroesNode[0];
            Assert.IsNotNull(childNode.overrideValueId);
            Assert.IsTrue(app.Runtime.TryGetValue<ObjectAttributeValue>(
                childNode.overrideValueId!,
                out ObjectAttributeValue? row));
            Assert.AreEqual("type-hero", row!.typeId);
            Assert.IsTrue(app.Runtime.SerializeSaveData().Contains("Ada"));
        }

        [Test]
        public void GeneratedNSGetterProperty_ComputesThroughRuntimeNode()
        {
            var app = LoadGeneratedClient(out _);

            NeoGeneratedTypesSupport.SetValue(
                app.Runtime.save,
                "Manifest",
                NeoGeneratedTypesSupport.Value<object?>(null));

            var result = app.Save.Manifest;
            var direct = app.Runtime.save.Get<NeoAttributeNSGetter>("Manifest").Compute();

            Assert.IsTrue(direct.ok, direct.error);
            Assert.AreEqual(direct.value?.ToString(), result);
        }

        [Test]
        public void GeneratedFactory_CreatesCollectableUnlinkedSavedValue()
        {
            var app = LoadGeneratedClient(out _);

            var orphan = Hero.factory(app.Runtime, Name: "Orphan", Health: 1);
            Assert.IsNotNull(orphan.valueId);
            CollectionAssert.Contains(
                new System.Collections.Generic.List<string>(
                    app.Runtime.FindUnlinkedSaveValueIds()),
                orphan.valueId);

            Assert.GreaterOrEqual(app.Runtime.RunGarbageCollector(), 1);
            Assert.IsFalse(app.Runtime.TryGetValue<ObjectAttributeValue>(
                orphan.valueId!,
                out _));
        }
    }
}
