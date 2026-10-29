// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.NeoScript.Hashing;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P127 hashing behavior the shared fixture doesn't pin.</summary>
    public class P127HashingTests
    {
        [Test]
        public void OrphanValueFoldsFnv1aOverUtf16CodeUnits()
        {
            // FNV-1a("a") = 0xE40C292C and FNV-1a("foobar") = 0xBF9CF968, the reference test vectors.
            Assert.That(NeoEnums.OrphanValue("a"), Is.EqualTo(-1 - (int)(0xE40C292Cu % 0x7FFFFFFFu)));
            Assert.That(NeoEnums.OrphanValue("foobar"), Is.EqualTo(-1 - (int)(0xBF9CF968u % 0x7FFFFFFFu)));
            Assert.That(NeoEnums.OrphanValue(""), Is.EqualTo(-1 - (int)(0x811C9DC5u % 0x7FFFFFFFu)));
        }

        [Test]
        public void EnumOptionsNumberByDeclarationIndexAndPrintOrphanIds()
        {
            var planet = new NeoEnumOptions(new[] { new NeoEnumOption("p1", "Mercury"), new NeoEnumOption("p2", "Venus") });
            Assert.That(planet.NumericValue("p1"), Is.EqualTo(0));
            Assert.That(planet.NumericValue("p2"), Is.EqualTo(1));
            Assert.That(planet.NumericValue("gone"), Is.EqualTo(NeoEnums.OrphanValue("gone")));
            Assert.That(planet.Format("p2", null), Is.EqualTo("Venus"));
            Assert.That(planet.Format("p2", "d"), Is.EqualTo("1"));
            Assert.That(planet.Format("gone", "F"), Is.EqualTo("gone"));
            Assert.That(planet.Format("gone", "D"), Is.EqualTo(NeoEnums.Int32Text(NeoEnums.OrphanValue("gone"))));
        }

        [Test]
        public void Int32TextPrintsInvariantDigits()
        {
            Assert.That(NeoEnums.Int32Text(0), Is.EqualTo("0"));
            Assert.That(NeoEnums.Int32Text(-45), Is.EqualTo("-45"));
            Assert.That(NeoEnums.Int32Text(int.MinValue), Is.EqualTo("-2147483648"));
            Assert.That(NeoEnums.Int32Text(int.MaxValue), Is.EqualTo("2147483647"));
        }

        [Test]
        public void NumberStylesDeclaresNineteenMembersAndNoOptionIdsYet()
        {
            Assert.That(NeoNumberStyles.Members.Count, Is.EqualTo(19));
            Assert.That(NeoNumberStyles.OptionValue("Integer"), Is.Null);
        }

        [Test]
        public void HashTableHashesEachKeyOncePerInsertionAcrossResizes()
        {
            var comparer = new CountingComparer();
            var table = new NeoHashTable<Key, bool>(comparer, NeoHashTableKind.Set);
            for (int key = 0; key < 50; key++)
                table.Add(new Key(key), false, NeoInsertBehavior.None);
            Assert.That(comparer.Hashes, Is.EqualTo(50));
            Assert.That(table.Count, Is.EqualTo(50));
        }

        [Test]
        public void HashTableComparesAnEntryWithItselfAndLookupsLeaveNothingBehind()
        {
            var comparer = new CountingComparer();
            var table = new NeoHashTable<Key, bool>(comparer, NeoHashTableKind.Set);
            var one = new Key(1);
            table.Add(one, false, NeoInsertBehavior.None);
            comparer.Equalities = 0;
            Assert.That(table.Contains(one), Is.True);
            Assert.That(comparer.Equalities, Is.EqualTo(1));
            Assert.That(table.Contains(new Key(3)), Is.False);
            Assert.That(table.Keys(), Is.EqualTo(new[] { one }));
        }

        [Test]
        public void HashTableReusesTheMostRecentlyFreedSlotFirst()
        {
            var table = new NeoHashTable<Key, string>(new CountingComparer(), NeoHashTableKind.Dictionary);
            for (int key = 0; key < 4; key++)
                table.Add(new Key(key), "v" + key, NeoInsertBehavior.Throw);
            table.Remove(new Key(1));
            table.Remove(new Key(2));
            table.Add(new Key(7), "v7", NeoInsertBehavior.Throw);
            table.Add(new Key(8), "v8", NeoInsertBehavior.Throw);
            Assert.That(table.Values(), Is.EqualTo(new[] { "v0", "v8", "v7", "v3" }));
        }

        private sealed class Key
        {
            internal Key(int value)
            {
                Value = value;
            }

            internal int Value
            {
                get;
            }
        }

        private sealed class CountingComparer : IEqualityComparer<Key>
        {
            internal int Hashes
            {
                get;
                private set;
            }

            internal int Equalities
            {
                get;
                set;
            }

            public bool Equals(Key? entry, Key? candidate)
            {
                Equalities++;
                return entry!.Value == candidate!.Value;
            }

            public int GetHashCode(Key key)
            {
                Hashes++;
                return key.Value % 2;
            }
        }
    }
}
