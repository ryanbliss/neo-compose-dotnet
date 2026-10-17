// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// A collection a closed class declares by default closes its generic
    /// entries through that class, though the default's rows carry no
    /// <c>genericBindings</c> stamp. The fixture is a real <c>neo export</c>
    /// of this source, its main partition folded in as <c>values</c>, with
    /// <c>Three</c>'s default entries rewritten into the field-wise rows
    /// Neowyn's stored class defaults carry (a <c>Value</c> row initialized
    /// from the constructor argument beside literal fields):
    /// <code>
    /// class Frame&lt;T&gt;(T value) { public T Value = value; public int Index = 0; }
    /// abstract class Segment&lt;TV&gt; {
    ///   public virtual List&lt;Frame&lt;TV&gt;&gt; Frames = [];
    ///   public virtual Dictionary&lt;string, Frame&lt;TV&gt;&gt; Named = {};
    /// }
    /// abstract class IntSegment : Segment&lt;int&gt; {}
    /// class Three(int f0, int f1) : IntSegment {
    ///   public override List&lt;Frame&lt;int&gt;&gt; Frames = [new Frame&lt;int&gt;(value: f0) { Index = 0 }, new Frame&lt;int&gt;(value: f1) { Index = 1 }];
    ///   public override Dictionary&lt;string, Frame&lt;int&gt;&gt; Named = { "a": new Frame&lt;int&gt;(value: f0) };
    /// }
    /// class Holder { public IntSegment? Explicit; public IntSegment? Defaulted; public IntSegment? Other; }
    /// class Store {
    ///   @storage(allowed: .Immutable) public static Holder S = new() {
    ///     Explicit = new Three(f0: 1, f1: 2) {
    ///       Frames = [new Frame&lt;int&gt;(value: 1) { Index = 0 }, new Frame&lt;int&gt;(value: 2) { Index = 1 }],
    ///       Named = { "a": new Frame&lt;int&gt;(value: 1) }
    ///     },
    ///     Defaulted = new Three(f0: 1, f1: 2),
    ///     Other = new Three(f0: 3, f1: 4)
    ///   };
    /// }
    /// </code>
    /// The client builds the Assets tree before sparse replay stamps
    /// anything, so each test places <c>S</c>'s row under Assets the way a
    /// project's asset folders hold Neowyn's items.
    /// </summary>
    public class NeoGenericDeclarationDefaultTests
    {
        [TestCase("Explicit", 1, 2)]
        [TestCase("Defaulted", 1, 2)]
        [TestCase("Other", 3, 4)]
        public void DefaultCollectionEntriesCloseThroughTheDeclaringClass(string field, int f0, int f1)
        {
            using NeoClient client = Client();
            Assert.IsTrue(client.assets.TryGet("Store", out NeoMemberClass? store));
            Assert.IsTrue(store!.TryGet(field, out NeoMemberClass? segment));
            Assert.IsTrue(segment!.TryGet("Frames", out NeoMemberList? frames));
            Assert.IsTrue(segment.TryGet("Named", out NeoMemberDictionary? named));
            Assert.IsTrue(named!.TryGet("a", out NeoMemberClass? named0));

            Assert.AreEqual(new[] { f0, f1 }, frames!.Cast<NeoMemberClass>().Select(frame => ReadInt(frame, "Value")).ToArray());
            Assert.AreEqual(new[] { 0, 1 }, frames.Cast<NeoMemberClass>().Select(frame => ReadInt(frame, "Index")).ToArray());
            Assert.AreEqual(f0, ReadInt(named0!, "Value"));
        }

        private static int ReadInt(NeoMemberClass owner, string key)
        {
            Assert.IsTrue(owner.TryGet(key, out NeoMemberInt? member), key);
            return Convert.ToInt32(member!.value?.value);
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(
                File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/generic-declaration-default-export.json"));
            var s = (ClassMember)data.members.Values.Single(member => member.name == "S");
            var store = new ClassMember
            {
                id = "store",
                projectId = s.projectId,
                name = "Store",
                kind = MemberKind.Class,
                DeclaredClassId = s.DeclaredClassId,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members[store.id] = store;
            var assets = (ClassMember)data.members[data.project.rootAssetsMemberId];
            data.classes[assets.DeclaredClassId!].schema["Store"] = store.id;
            ((ObjectMemberValue)data.values[assets.valueId!]).value!["Store"] = s.valueId!;
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }
    }
}
