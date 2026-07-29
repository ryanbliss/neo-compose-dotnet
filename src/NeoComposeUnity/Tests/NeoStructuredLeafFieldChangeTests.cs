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
    /// <summary>
    /// P42 §1.2 — a structured-leaf <b>field</b> write is a read-modify-write
    /// of the whole leaf, so it must raise the same change notification a
    /// whole-value assignment raises, through the same generated
    /// <c>OnChanged</c> surface an author already subscribes to.
    ///
    /// <para>The per-wrapper suites assert this at the member-node level
    /// (<c>NeoSpriteWrapperTests</c>, <c>NeoColorMemberTests</c>). This suite
    /// closes the loop at the level an author actually uses: a generated class
    /// value's typed field subscription and its batch subscription, driven
    /// through a generated property.</para>
    ///
    /// <para>Vector3 is the vehicle because it is the structured leaf the
    /// generated fixture exposes that needs no synchronized
    /// <see cref="NeoAssetDatabase"/> — a Sprite property cannot be assigned
    /// in EditMode without one.</para>
    /// </summary>
    public class NeoStructuredLeafFieldChangeTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static TestProjectNeo LoadGeneratedClient()
        {
            var stack = NeoTestSaveStack.Create(
                File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json")));
            return TestProjectNeo.Load(stack.Synchronizer).GetAwaiter().GetResult();
        }

        /// <summary>
        /// A standalone writable Hero, mirroring how the Derived-based
        /// subscription tests build theirs. The Position leaf is assigned once
        /// so the field writes below have a row to modify.
        /// </summary>
        private static Hero NewHero(TestProjectNeo app)
        {
            if (!app.Client.TryGetMember("member-hero", out ClassMember? heroMember))
            {
                Assert.Fail("Fixture is missing member 'member-hero'.");
                throw new System.InvalidOperationException("unreachable");
            }
            var node = (NeoMemberClassWritable)NeoMember.CreateWritable(
                app.Client,
                heroMember,
                null);
            var hero = Hero.CreateWritable(app.Client, node);
            hero.Position = new NeoVector3(1f, 2f, 3f);
            return hero;
        }

        [Test]
        public void FieldWrite_RaisesTheSameTypedFieldChangeAsAWholeValueWrite()
        {
            var app = LoadGeneratedClient();
            var hero = NewHero(app);
            int changes = 0;
            NeoVector3? observed = null;
            using var subscription = hero.OnChanged(Hero.Fields.Position, (value, _) =>
            {
                observed = value;
                changes++;
            });

            hero.Position = new NeoVector3(4f, 5f, 6f);

            Assert.AreEqual(1, changes, "whole-value write");
            Assert.AreEqual(new UnityEngine.Vector3(4f, 5f, 6f), observed!.Value);

            hero.Position.y = 9f;

            Assert.AreEqual(2, changes, "field write");
            Assert.AreEqual(
                new UnityEngine.Vector3(4f, 9f, 6f),
                observed!.Value,
                "the subscriber reads the whole leaf, patched");
        }

        [Test]
        public void FieldWrite_RaisesTheBatchChangeNamingTheField()
        {
            var app = LoadGeneratedClient();
            var hero = NewHero(app);
            var observed = new System.Collections.Generic.List<NeoChangedArgs<Hero.Fields>>();
            using var subscription = hero.OnChanged(args => observed.Add(args));

            hero.Position.z = 8f;

            // The fixture's Path list literally holds the Position row
            // (`v-path` = ["v-position"]), so writing that row also raises the
            // pre-existing container-membership change for Path. Find the
            // batch that reports Position rather than assuming it is the only
            // one.
            NeoChangedArgs<Hero.Fields>? positionChange = null;
            foreach (var args in observed)
            {
                if (args.Has(Hero.Fields.Position)) positionChange = args;
                Assert.IsFalse(
                    args.Has(Hero.Fields.Name),
                    "a field write reports the one member it wrote, like a whole-value write");
            }

            Assert.IsNotNull(positionChange, "no batch change reported Position");
            Assert.IsTrue(
                positionChange!.TryGet(Hero.Fields.Position, out NeoVector3 position));
            Assert.AreEqual(new UnityEngine.Vector3(1f, 2f, 8f), position.Value);
            Assert.AreEqual(NeoChangeSource.Local, positionChange.Source);
        }

        [Test]
        public void FieldWrite_StopsNotifyingAfterTheSubscriptionIsDisposed()
        {
            var app = LoadGeneratedClient();
            var hero = NewHero(app);
            int changes = 0;
            var subscription = hero.OnChanged(Hero.Fields.Position, (_, _) => changes++);

            hero.Position.x = 4f;
            Assert.AreEqual(1, changes);

            subscription.Dispose();
            hero.Position.x = 5f;

            Assert.AreEqual(1, changes);
            Assert.AreEqual(5f, hero.Position.x, "the write still happened");
        }

        [Test]
        public void FieldWriteOnADetachedWrapper_NotifiesNobody()
        {
            var app = LoadGeneratedClient();
            var hero = NewHero(app);
            int changes = 0;
            using var subscription = hero.OnChanged(Hero.Fields.Position, (_, _) => changes++);

            var detached = new NeoVector3(7f, 7f, 7f);
            detached.y = 1f;

            Assert.AreEqual(0, changes);
            Assert.AreEqual(new UnityEngine.Vector3(1f, 2f, 3f), hero.Position.Value);
        }
    }
}
