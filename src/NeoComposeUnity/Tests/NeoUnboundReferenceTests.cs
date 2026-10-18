// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P107 §3.3. An unbound <c>Reference&lt;Hero&gt;</c> names any stored Hero
    /// row. Each selected row decides its own store: a Save row resolves
    /// writable, an asset row read-only.
    /// </summary>
    public class NeoUnboundReferenceTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        /// <summary>An authored Hero row with no Save shadow.</summary>
        private const string AssetHeroId = "v-dict";

        private static TestProjectNeo LoadApp()
        {
            var stack = NeoTestSaveStack.Create(
                File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json")));
            return new TestProjectNeo(NeoTestSaveStack.LoadSynchronously(stack.Synchronizer));
        }

        private static Hero SavedHero(TestProjectNeo app, string name)
        {
            var hero = new Hero(Name: name, Health: 3);
            app.Save.Heroes.Add(hero);
            return hero;
        }

        private static Hero AssetHero(TestProjectNeo app) =>
            (Hero)app.ResolveValue(AssetHeroId)!;

        [Test]
        public void SingleReference_TakesItsRowsStore()
        {
            using var app = LoadApp();
            var saved = SavedHero(app, "Saved");

            app.Save.FavoriteHero = saved;
            Assert.IsTrue(app.Save.FavoriteHero!.TryWritable(out Hero writable));
            writable.Health = 8;
            Assert.AreEqual(8, saved.Health);

            app.Save.FavoriteHero = AssetHero(app);
            Assert.AreEqual(AssetHeroId, app.Save.FavoriteHero!.valueId);
            Assert.IsFalse(app.Save.FavoriteHero.TryWritable(out Hero _));
        }

        [Test]
        public void MultiReference_ResolvesEachRowOnItsOwn()
        {
            using var app = LoadApp();
            var saved = SavedHero(app, "Saved");

            app.Save.Party.Add(saved);
            app.Save.Party.Add(AssetHero(app));

            CollectionAssert.AreEqual(
                new[] { true, false },
                app.Save.Party.Select(hero => hero.TryWritable(out Hero _)).ToArray());
            var node = app.Client.save.Get<NeoMemberLookup>("Party");
            CollectionAssert.AreEqual(
                new[] { NeoValueOwnership.Save, NeoValueOwnership.Asset },
                node.GetSelected().Select(selected => selected.ownership).ToArray());
            Assert.AreSame(node.GetSelected()[0], node.GetFirstSelected());
        }

        [Test]
        public void MultiReference_AddsOnlyStoredRowsOfItsType()
        {
            using var app = LoadApp();
            var party = app.Client.save.Get<NeoMemberLookupWritable>("Party");

            Assert.IsTrue(party.Add(AssetHeroId));
            var wrongClass = Assert.Throws<System.InvalidOperationException>(
                () => party.Add("v-storage-a"));
            Assert.AreEqual(
                "Lookup selection id 'v-storage-a' is not a stored row of the reference's type.",
                wrongClass!.Message);
            Assert.Throws<System.InvalidOperationException>(() => party.Add("v-missing"));
            CollectionAssert.AreEqual(new[] { AssetHeroId }, party.Selected());
        }

        [Test]
        public void Evaluator_ReadsThroughAnUnboundReference()
        {
            using var app = LoadApp();
            app.Save.FavoriteHero = SavedHero(app, "Saved");

            object? name = NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new Variable[0],
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = FavoriteHeroKey(app, "Name"),
                        },
                    },
                },
                new NSGetterEvaluator.Context(app.Client, thisValue: null, rootValue: null));

            Assert.AreEqual("Saved", name);
        }

        [Test]
        public void Evaluator_WritesThroughAnUnboundReferenceToASaveRow()
        {
            using var app = LoadApp();
            var saved = SavedHero(app, "Saved");
            app.Save.FavoriteHero = saved;

            RunAssignHealth(app, 11);

            Assert.AreEqual(11, saved.Health);
        }

        [Test]
        public void Evaluator_RejectsAWriteThroughAnUnboundReferenceToAnAssetRow()
        {
            using var app = LoadApp();
            app.Save.FavoriteHero = AssetHero(app);

            var error = Assert.Catch<NSGetterRuntimeError>(() => RunAssignHealth(app, 11));

            Assert.AreEqual(
                "Cannot write runtime-owned target because its value is Asset-owned.",
                error!.Message);
            Assert.AreEqual(100, AssetHero(app).Health);
        }

        /// <summary><c>Save.FavoriteHero.Health = health</c>, as the compiler lowers it.</summary>
        private static void RunAssignHealth(TestProjectNeo app, int health)
        {
            var body = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new Variable[0],
                typeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        operatorValue = "=",
                        target = new WriteTarget
                        {
                            pointer = FavoriteHeroKey(app, "Health"),
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                            // An unbound reference has no static store (P107 §3.2).
                            writability = WritabilityKind.Runtime,
                        },
                        pointer = Literal(MemberKind.Int, health),
                    },
                },
            };
            var ctx = new NSGetterEvaluator.Context(app.Client, null, null);
            NeoScriptExecutor.Execute(app.Client, body, new Dictionary<string, object?>(), ctx);
        }

        private static KeyOfPointer FavoriteHeroKey(TestProjectNeo app, string key) =>
            KeyOf(
                KeyOf(
                    new ReferencePointer { type = PointerKind.Reference, valueId = app.Save.valueId! },
                    "FavoriteHero"),
                key);

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = Literal(MemberKind.String, key),
            },
        };

        private static ValuePointer Literal(MemberKind kind, object value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = kind, required = true },
                value = JToken.FromObject(value),
            },
        };
    }
}
