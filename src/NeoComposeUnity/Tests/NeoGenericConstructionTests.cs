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
    /// A generic class constructs closed by its own type arguments. The
    /// fixture is a real `neo export` of this source, its main partition
    /// folded in as <c>values</c>, so the IR and the interned type-argument
    /// descriptors are exactly what the CLI ships:
    /// <code>
    /// class Box&lt;T&gt; { public List&lt;T&gt; Values = []; }
    /// class DeclBox&lt;T&gt; { public List&lt;T&gt; Values = []; public DeclBox() {} }
    /// class Bag&lt;T&gt; { public List&lt;T&gt; Values = []; public int N = 2; }
    /// class SubBag&lt;U&gt; : Bag&lt;U&gt; {}
    /// class Holder&lt;T&gt; {
    ///   public Bag&lt;T&gt; B = new Bag&lt;T&gt;();
    ///   public Bag&lt;T&gt; S = new SubBag&lt;T&gt;();
    ///   public Bag&lt;T&gt; C = new Bag&lt;T&gt; { N = 3 };
    ///   public List&lt;Bag&lt;T&gt;&gt; L = [new Bag&lt;T&gt; { N = 3 }];
    ///   public NeoDelegate&lt;Bag&lt;T&gt;&gt; MakeField = () =&gt; new Bag&lt;T&gt;();
    ///   public Bag&lt;T&gt; Make() { return new Bag&lt;T&gt;(); }
    ///   public NeoDelegate&lt;Bag&lt;T&gt;&gt; Maker() { return () =&gt; new Bag&lt;T&gt;(); }
    /// }
    /// class Tag&lt;T&gt; { public readonly int N = 1; }
    /// class TagHolder&lt;T&gt; { @storage(allowed: .Immutable) public Tag&lt;T&gt; B = new Tag&lt;T&gt; { N = 2 }; }
    /// class Probe {
    ///   public static int BoxCount() { var box = new Box&lt;int&gt;(); box.Values.Add(3); var decl = new DeclBox&lt;string&gt;(); decl.Values.Add("x"); return box.Values.Count + decl.Values.Count; }
    ///   public static int HolderCount() { var h = new Holder&lt;string&gt;(); h.B.Values.Add("a"); h.S.Values.Add("b"); var m = h.Make(); m.Values.Add("c"); h.C.Values.Add("d"); return h.B.Values.Count + h.S.Values.Count + m.Values.Count + h.B.N + h.C.Values.Count + h.C.N + h.L[0].N; }
    ///   public static int DelegateCount() { var h = new Holder&lt;string&gt;(); var f = h.MakeField(); f.Values.Add("a"); var maker = h.Maker(); var g = maker(); g.Values.Add("b"); g.Values.Add("c"); return f.Values.Count + g.Values.Count; }
    ///   public static int ImmutableCount() { return new TagHolder&lt;int&gt;().B.N + new TagHolder&lt;string&gt;().B.N; }
    /// }
    /// </code>
    /// </summary>
    public class NeoGenericConstructionTests
    {
        [Test]
        public void ClosedTypeArgumentsConstructImplicitAndDeclaredConstructors()
        {
            using NeoClient client = Client();

            Assert.AreEqual(2, InvokeProbe(client, "BoxCount"));
        }

        [Test]
        public void EnclosingTypeParameterClosesThroughReceiverAndInitializedInstance()
        {
            using NeoClient client = Client();

            Assert.AreEqual(12, InvokeProbe(client, "HolderCount"));
        }

        [Test]
        public void DelegateClosesThroughTheInstanceThatOwnsIt()
        {
            using NeoClient client = Client();

            Assert.AreEqual(3, InvokeProbe(client, "DelegateCount"));
        }

        [Test]
        public void ImmutableInitializerClosesPerInstance()
        {
            using NeoClient client = Client();
            Member tagged = client.members.Values.Single(member =>
                member is ClassMember && member.name == "B" && member.Storage == NeoMemberStorage.Immutable);

            // `new Tag<T> { N = 2 }` differs per `T`, so instances can't share one value.
            Assert.IsFalse(NeoGeneratedTypesSupport.IsConstantMember(client, tagged));
            Assert.AreEqual(4, InvokeProbe(client, "ImmutableCount"));
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/generic-construction-export.json"));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
