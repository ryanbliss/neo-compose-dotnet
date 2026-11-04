// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using G = NeoCompose.Tests.P127CodegenShapesParity;

namespace NeoCompose.Tests
{
    public sealed class P127CodegenShapesParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Export = Root + "p127-codegen-shapes-parity-export.json";
        private const string Fixture = Root + "p127-codegen-shapes-parity-fixture.json";

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var test in JObject.Parse(File.ReadAllText(Fixture))["returns"]!.Children<JProperty>())
                foreach (bool generated in new[] { false, true })
                    yield return new TestCaseData(test.Name, test.Value, generated)
                        .SetName("Shapes_" + test.Name + (generated ? "_Generated" : "_Evaluator"));
        }

        [Test]
        public void DeliveryContainsAllCasesAndItsRequiredExport()
        {
            Assert.That(File.Exists(Export), Is.True, "The shared export is required; missing delivery must fail.");
            var fixture = JObject.Parse(File.ReadAllText(Fixture));
            Assert.That(fixture["returns"]!.Children().Count(), Is.EqualTo(47));
            Assert.That(fixture["csharpBridges"]!.Children().Count(), Is.EqualTo(39));
        }

        [TestCaseSource(nameof(Cases))]
        public void SharedCasesMatchInEvaluatorAndGeneratedCSharp(string name, JToken expected, bool generated)
        {
            Assert.That(File.Exists(Export), Is.True, "The shared export is required.");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            object? actual;
            if (generated)
                actual = name switch
                {
                    "OutArgument" => G.Probe.OutArgument(),
                    "ArityPair" => G.Probe.ArityPair(),
                    "InterfaceArityPair" => G.Probe.InterfaceArityPair(),
                    "GenericStaticPerConstruction" => G.Probe.GenericStaticPerConstruction(),
                    "OverrideToString" => G.Probe.OverrideToString(),
                    "OverrideEquals" => G.Probe.OverrideEquals(),
                    "OverrideGetHashCode" => G.Probe.OverrideGetHashCode(),
                    "OperatorForward" => G.Probe.OperatorForward(),
                    "ComparerSlot" => G.Probe.ComparerSlot(),
                    "DefaultInterfaceMember" => G.Probe.DefaultInterfaceMember(),
                    "MaybeNullOutFails" => G.Probe.MaybeNullOutFails(),
                    "MaybeNullOutSucceeds" => G.Probe.MaybeNullOutSucceeds(),
                    "NotNullOutFails" => G.Probe.NotNullOutFails(),
                    "GenericMaybeNullOutFails" => G.Probe.GenericMaybeNullOutFails(),
                    "NonGenericComparerEquals" => G.Probe.NonGenericComparerEquals(),
                    "NonGenericComparerGetHashCode" => G.Probe.NonGenericComparerGetHashCode(),
                    "NonGenericComparerCompare" => G.Probe.NonGenericComparerCompare(),
                    "AbstractOverrideHash" => G.Probe.AbstractOverrideHash(),
                    "InheritedEquatable" => G.Probe.InheritedEquatable(),
                    "ExplicitEquatable" => G.Probe.ExplicitEquatable(),
                    "ExplicitComparable" => G.Probe.ExplicitComparable(),
                    "ProtectedAndPrivateGetters" => G.Probe.ProtectedAndPrivateGetters(),
                    "ReferenceIntWrite" => G.Probe.ReferenceIntWrite(),
                    "ReferenceIntRead" => G.Probe.ReferenceIntRead(),
                    "ReferenceClassWrite" => G.Probe.ReferenceClassWrite(),
                    "ReferenceClassSame" => G.Probe.ReferenceClassSame(),
                    "ReferenceStoredInt" => G.Probe.ReferenceStoredInt(),
                    "ReferenceStoredClass" => G.Probe.ReferenceStoredClass(),
                    "DerivedComparable" => G.Probe.DerivedComparable(),
                    "DerivedBaseComparable" => G.Probe.DerivedBaseComparable(),
                    "DerivedEquatable" => G.Probe.DerivedEquatable(),
                    "DerivedBaseEquatable" => G.Probe.DerivedBaseEquatable(),
                    "DerivedInterface" => G.Probe.DerivedInterface(),
                    "DerivedBaseInterface" => G.Probe.DerivedBaseInterface(),
                    "ExplicitGenericCmp" => G.Probe.ExplicitGenericCmp(),
                    "ExplicitGenericX" => G.Probe.ExplicitGenericX(),
                    "ExplicitGenericBase" => G.Probe.ExplicitGenericBase(),
                    "PublicGenericCmp" => G.Probe.PublicGenericCmp(),
                    "PublicGenericX" => G.Probe.PublicGenericX(),
                    "PublicGenericBase" => G.Probe.PublicGenericBase(),
                    "RelistedComparable" => G.Probe.RelistedComparable(),
                    "RelistedComparer" => G.Probe.RelistedComparer(),
                    "RelistedPublicMember" => G.Probe.RelistedPublicMember(),
                    "RelistedEquatable" => G.Probe.RelistedEquatable(),
                    "RelistedEqualityComparer" => G.Probe.RelistedEqualityComparer(),
                    "RelistedPublic" => G.Probe.RelistedPublic(),
                    "RelistedPublicComparer" => G.Probe.RelistedPublicComparer(),
                    _ => throw new InvalidOperationException("Unconsumed shared case: " + name),
                };
            else
            {
                var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
                actual = NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema[name]).InvokeStatic(Array.Empty<object?>());
            }
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(Convert.ToDouble(actual), Is.EqualTo(expected.Value<double>()), name);
            else
                Assert.That(JToken.DeepEquals(actual is null ? JValue.CreateNull() : JToken.FromObject(actual), expected), Is.True,
                    $"{name}: expected {expected}, got {actual}");
        }

        public static IEnumerable<TestCaseData> Bridges()
        {
            foreach (var entry in JObject.Parse(File.ReadAllText(Fixture))["csharpBridges"]!)
                yield return new TestCaseData(entry["name"]!.Value<string>(),
                    entry["receiver"]!.Value<string>(), entry["interface"]!.Value<string>(),
                    entry["call"]!.Value<string>(), entry["returns"]!)
                    .SetName("CSharpBridge_" + entry["name"]!.Value<string>() + "_" + entry["interface"]!.Value<string>());
        }

        [TestCaseSource(nameof(Bridges))]
        public void EverySharedBridgeCallsTheGeneratedClrInterface(string name, string receiver, string contract,
            string call, JToken expected)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            object actual = (receiver, contract, call) switch
            {
                ("Probe.FoldComparer", "System.Collections.IEqualityComparer", "Equals(\"lower\", \"LOWER\")") =>
                    ((System.Collections.IEqualityComparer)G.Probe.FoldComparer).Equals("lower", "LOWER"),
                ("Probe.FoldComparer", "System.Collections.IEqualityComparer", "GetHashCode(\"abc\")") =>
                    ((System.Collections.IEqualityComparer)G.Probe.FoldComparer).GetHashCode("abc"),
                ("Probe.LengthComparer", "System.Collections.IComparer", "Compare(\"aa\", \"b\")") =>
                    ((System.Collections.IComparer)G.Probe.LengthComparer).Compare("aa", "b"),
                _ => InvokeGenericBridge(receiver, contract, call),
            };
            Assert.That(JToken.DeepEquals(JToken.FromObject(actual), expected), Is.True, name);
        }

        // Each construction remains a closed, statically typed C# interface call.
        // Operand selection is shared by its writable, IReadOnly and base views.
        private static object InvokeGenericBridge(string receiver, string contract, string call)
        {
            (object left, object right) operands = (receiver, call) switch
            {
                ("Probe.EqLeft", "Equals(Probe.EqRight)") => (G.Probe.EqLeft, G.Probe.EqRight),
                ("Probe.CmpLeft", "CompareTo(Probe.CmpRight)") => (G.Probe.CmpLeft, G.Probe.CmpRight),
                ("Probe.DerLeft", "CompareTo(Probe.DerRight)") => (G.Probe.DerLeft, G.Probe.DerRight),
                ("Probe.DerEqLeft", "Equals(Probe.DerEqRight)") => (G.Probe.DerEqLeft, G.Probe.DerEqRight),
                ("Probe.MeasureLeft", "Measure(Probe.MeasureRight)") => (G.Probe.MeasureLeft, G.Probe.MeasureRight),
                ("Probe.GenericExplicitLeft", "CompareTo(Probe.GenericExplicitRight)") => (G.Probe.GenericExplicitLeft, G.Probe.GenericExplicitRight),
                ("Probe.GenericExplicitLeft", "X(Probe.GenericExplicitRight)") => (G.Probe.GenericExplicitLeft, G.Probe.GenericExplicitRight),
                ("Probe.GenericPublicLeft", "CompareTo(Probe.GenericPublicRight)") => (G.Probe.GenericPublicLeft, G.Probe.GenericPublicRight),
                ("Probe.GenericPublicLeft", "X(Probe.GenericPublicRight)") => (G.Probe.GenericPublicLeft, G.Probe.GenericPublicRight),
                ("Probe.RelistedLeft", "CompareTo(Probe.RelistedRight)" or "Compare(Probe.RelistedLeft, Probe.RelistedRight)") => (G.Probe.RelistedLeft, G.Probe.RelistedRight),
                ("Probe.RelistedEqLeft", "Equals(Probe.RelistedEqRight)" or "Equals(Probe.RelistedEqLeft, Probe.RelistedEqRight)") => (G.Probe.RelistedEqLeft, G.Probe.RelistedEqRight),
                ("Probe.RelistedPublicLeft", "CompareTo(Probe.RelistedPublicRight)" or "Compare(Probe.RelistedPublicLeft, Probe.RelistedPublicRight)") => (G.Probe.RelistedPublicLeft, G.Probe.RelistedPublicRight),
                _ => throw new InvalidOperationException("Unconsumed C# bridge operands: " + receiver + "." + call),
            };
            return (contract, call.Substring(0, call.IndexOf('('))) switch
            {
                ("System.IEquatable<ExplicitEq>", "Equals") => Equal<G.ExplicitEq>(operands.left, operands.right),
                ("System.IEquatable<IReadOnlyExplicitEq>", "Equals") => Equal<G.IReadOnlyExplicitEq>(operands.left, operands.right),
                ("System.IComparable<ExplicitCmp>", "CompareTo") => Compare<G.ExplicitCmp>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyExplicitCmp>", "CompareTo") => Compare<G.IReadOnlyExplicitCmp>(operands.left, operands.right),
                ("System.IComparable<DerC>", "CompareTo") => Compare<G.DerC>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyDerC>", "CompareTo") => Compare<G.IReadOnlyDerC>(operands.left, operands.right),
                ("System.IComparable<BaseC>", "CompareTo") => Compare<G.BaseC>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyBaseC>", "CompareTo") => Compare<G.IReadOnlyBaseC>(operands.left, operands.right),
                ("System.IEquatable<DerE>", "Equals") => Equal<G.DerE>(operands.left, operands.right),
                ("System.IEquatable<IReadOnlyDerE>", "Equals") => Equal<G.IReadOnlyDerE>(operands.left, operands.right),
                ("System.IEquatable<BaseE>", "Equals") => Equal<G.BaseE>(operands.left, operands.right),
                ("System.IEquatable<IReadOnlyBaseE>", "Equals") => Equal<G.IReadOnlyBaseE>(operands.left, operands.right),
                ("IMeasure<DerM>", "Measure") => Measure<G.DerM>(operands.left, operands.right),
                ("IMeasure<BaseM>", "Measure") => Measure<G.BaseM>(operands.left, operands.right),
                ("System.IComparable<GDE<int>>", "CompareTo") => Compare<G.GDE<int>>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyGDE<int>>", "CompareTo") => Compare<G.IReadOnlyGDE<int>>(operands.left, operands.right),
                ("IX<GDE<int>>", "X") => X<G.GDE<int>>(operands.left, operands.right),
                ("System.IComparable<GE<int>>", "CompareTo") => Compare<G.GE<int>>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyGE<int>>", "CompareTo") => Compare<G.IReadOnlyGE<int>>(operands.left, operands.right),
                ("System.IComparable<GDP<int>>", "CompareTo") => Compare<G.GDP<int>>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyGDP<int>>", "CompareTo") => Compare<G.IReadOnlyGDP<int>>(operands.left, operands.right),
                ("IX<GDP<int>>", "X") => X<G.GDP<int>>(operands.left, operands.right),
                ("System.IComparable<GP<int>>", "CompareTo") => Compare<G.GP<int>>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyGP<int>>", "CompareTo") => Compare<G.IReadOnlyGP<int>>(operands.left, operands.right),
                ("System.IComparable<ReA>", "CompareTo") => Compare<G.ReA>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyReA>", "CompareTo") => Compare<G.IReadOnlyReA>(operands.left, operands.right),
                ("System.Collections.Generic.Comparer<ReA>.Default", "Compare") => CompareDefault<G.ReA>(operands.left, operands.right),
                ("System.Collections.Generic.Comparer<IReadOnlyReA>.Default", "Compare") => CompareDefault<G.IReadOnlyReA>(operands.left, operands.right),
                ("System.IEquatable<ReAE>", "Equals") => Equal<G.ReAE>(operands.left, operands.right),
                ("System.IEquatable<IReadOnlyReAE>", "Equals") => Equal<G.IReadOnlyReAE>(operands.left, operands.right),
                ("System.Collections.Generic.EqualityComparer<ReAE>.Default", "Equals") => EqualDefault<G.ReAE>(operands.left, operands.right),
                ("System.Collections.Generic.EqualityComparer<IReadOnlyReAE>.Default", "Equals") => EqualDefault<G.IReadOnlyReAE>(operands.left, operands.right),
                ("System.IComparable<ReAX>", "CompareTo") => Compare<G.ReAX>(operands.left, operands.right),
                ("System.IComparable<IReadOnlyReAX>", "CompareTo") => Compare<G.IReadOnlyReAX>(operands.left, operands.right),
                ("System.Collections.Generic.Comparer<ReAX>.Default", "Compare") => CompareDefault<G.ReAX>(operands.left, operands.right),
                ("System.Collections.Generic.Comparer<IReadOnlyReAX>.Default", "Compare") => CompareDefault<G.IReadOnlyReAX>(operands.left, operands.right),
                _ => throw new InvalidOperationException("Unconsumed C# bridge interface: " + contract),
            };
        }

        private static bool Equal<T>(object left, object right) => ((IEquatable<T>)left).Equals((T)right);
        private static int Compare<T>(object left, object right) => ((IComparable<T>)left).CompareTo((T)right);
        private static bool EqualDefault<T>(object left, object right) => EqualityComparer<T>.Default.Equals((T)left, (T)right);
        private static int CompareDefault<T>(object left, object right) => Comparer<T>.Default.Compare((T)left, (T)right);
        private static int Measure<T>(object left, object right) => ((G.IMeasure<T>)left).Measure((T)right);
        private static int X<T>(object left, object right) => ((G.IX<T>)left).X((T)right);

        [Test]
        public void GameCSharpUsesTheGeneratedShapesDirectly()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            Assert.That(G.Probe.TryIt(out int value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(G.Parsed.TryParse("bad", out var absent), Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(G.Parsed.TryParse("ok", out var parsed), Is.True);
            Assert.That(parsed.V, Is.EqualTo(1));
            Assert.That(G.Parsed.TryFind("bad", out var notFound), Is.False);
            Assert.That(notFound, Is.Null);
            Assert.That(G.Parsed.TryFirst(Array.Empty<string>(), out var first), Is.False);
            Assert.That(first, Is.Null);
            Assert.That(G.Parsed.TryFirst(new[] { "found" }, out first), Is.True);
            Assert.That(first, Is.EqualTo("found"));
            Assert.That(typeof(G.Box).Name, Is.EqualTo("Box"));
            Assert.That(typeof(G.Box<string>).Name, Is.EqualTo("Box`1"));
            var square = new G.Sq();
            Assert.That(((G.IShape)square).Sides(), Is.EqualTo(4));
            Assert.That(((G.IShape<int>)square).Size(), Is.EqualTo(2));
            G.Box<int>.Count = 3;
            G.Box<string>.Count = 5;
            Assert.That(G.Box<int>.Count * 10 + G.Box<string>.Count, Is.EqualTo(35));
            Assert.That(G.Box<int>.Changed, Is.SameAs(G.Box<int>.Changed));
            Assert.That(G.Box<int>.Changed, Is.Not.SameAs(G.Box<string>.Changed));
            G.Box<int>.Changed = G.Box<int>.Changed;
            G.Box<int>.Changed.Invoke();
            G.Box<string>.Changed.Invoke();
            var a = new G.Coin(V: 9);
            Assert.That(a.ToString(), Is.EqualTo("coin"));
            Assert.That(a.Equals((object)new G.Coin(V: 9)), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(9));
            Assert.That((new G.Pt(X: 1) + new G.Pt(X: 2)).X, Is.EqualTo(3));
            var holder = new G.Holder();
            var comparer = new G.Ci();
            holder.Cmp = comparer;
            Assert.That(holder.Cmp, Is.InstanceOf<G.Ci>());
            Assert.That(holder.Cmp, Is.SameAs(holder.Cmp));
            Assert.That(holder.Cmp!.Equals("a", "a"), Is.True);
            Assert.That(holder.Cmp.GetHashCode("x"), Is.Zero);
            Assert.That(((G.IV<string>)new G.Kv()).Name(), Is.EqualTo("v"));
        }
    }
}
