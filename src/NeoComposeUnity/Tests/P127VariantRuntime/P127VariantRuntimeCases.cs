// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    // Shared by the filtered EditMode suite and the separately requested player run.
    public abstract class P127VariantRuntimeCases
    {
        public static IEnumerable<TestCaseData> Rows()
        {
            yield return new TestCaseData("Money", "String").SetName("Variant_Money");
            yield return new TestCaseData("Two", "A").SetName("Variant_Two");
            yield return new TestCaseData("TwoRev", "B").SetName("Variant_TwoRev");
            yield return new TestCaseData("Derived", "A").SetName("Variant_Derived");
            yield return new TestCaseData("Inh", "String").SetName("Variant_Inh");
            yield return new TestCaseData("Exact", "Object").SetName("Variant_Exact");
            yield return new TestCaseData("Cons", "Object").SetName("Variant_Cons");
            yield return new TestCaseData("Cons2", "String").SetName("Variant_Cons2");
            yield return new TestCaseData("T2", "default-A").SetName("Variant_T2");
            yield return new TestCaseData("Same", "default-Object").SetName("Variant_Same");
            yield return new TestCaseData("Derived15", "base-explicit-object").SetName("Variant_Derived15");
            yield return new TestCaseData("Derived13", "default-Object").SetName("Variant_Derived13");
            yield return new TestCaseData("Derived8", "base-explicit-A").SetName("Variant_Derived8");
            yield return new TestCaseData("Derived12", "base12-explicit-A").SetName("Variant_Derived12");
            yield return new TestCaseData("Derived18", "base-explicit-A").SetName("Variant_Derived18");
            yield return new TestCaseData("Derived1", "default-Object").SetName("Variant_Derived1");
            yield return new TestCaseData("DerC", "explicit-A").SetName("Variant_DerC");
            yield return new TestCaseData("DerD", "explicit-A").SetName("Variant_DerD");
            yield return new TestCaseData("DerE", "explicit-A").SetName("Variant_DerE");
            yield return new TestCaseData("DerF", "mid-explicit-A").SetName("Variant_DerF");
            yield return new TestCaseData("PD", "pb-explicit-A").SetName("Variant_PD");
            yield return new TestCaseData("ND", "nb-explicit-A").SetName("Variant_ND");
            yield return new TestCaseData("Q2", "q1-explicit-A").SetName("Variant_Q2");
            yield return new TestCaseData("D21", "b19-explicit-A").SetName("Variant_D21");
            yield return new TestCaseData("D19", "b19-explicit-A").SetName("Variant_D19");
            yield return new TestCaseData("G2", "g1-explicit-A").SetName("Variant_G2");
            yield return new TestCaseData("RD2", "rb2-explicit-A").SetName("Variant_RD2");
            yield return new TestCaseData("RD3", "rb2-explicit-A").SetName("Variant_RD3");
        }

        [TestCaseSource(nameof(Rows))]
        public void ClrDispatchMatchesPinnedUnityResult(string row, string expected)
        {
            string actual = Invoke(row);
#if ENABLE_IL2CPP
            // This baseline awaits the first authorized IL2CPP run. Keep the
            // actual result in the test output if that runtime differs from Mono.
            TestContext.WriteLine($"IL2CPP {row}: {actual}");
#else
            TestContext.WriteLine($"Mono {row}: {actual}");
#endif
            Assert.That(actual, Is.EqualTo(expected), row);
        }

        private static string Invoke(string row) => row switch
        {
            "Money" => ((IDescribed<object>)new Money()).Describe(),
            "Two" => ((IDescribed<object>)new Two()).Describe(),
            "TwoRev" => ((IDescribed<object>)new TwoRev()).Describe(),
            "Derived" => ((IDescribed<object>)new Derived()).Describe(),
            "Inh" => ((IDescribed<object>)new Inh()).Describe(),
            "Exact" => ((IDescribed<object>)new Exact()).Describe(),
            "Cons" => ((IConsumer<string>)new Cons()).Eat(),
            "Cons2" => ((IConsumer<string>)new Cons2()).Eat(),
            "T2" => ((ID<object>)new T2()).D(),
            "Same" => ((ID<object>)new Same()).D(),
            "Derived15" => ((ID<object>)new Derived15()).D(),
            "Derived13" => ((ID<object>)new Derived13()).D(),
            "Derived8" => ((ID<object>)new Derived8()).D(),
            "Derived12" => ((ID<object>)new Derived12()).D(),
            "Derived18" => ((ID<object>)new Derived18()).D(),
            "Derived1" => ((ID<object>)new Derived1()).D(),
            "DerC" => ((ID<object>)new DerC()).D(),
            "DerD" => ((ID<object>)new DerD()).D(),
            "DerE" => ((ID<object>)new DerE()).D(),
            "DerF" => ((ID<object>)new DerF()).D(),
            "PD" => ((ID<object>)new PD()).D(),
            "ND" => ((ID<object>)new ND()).D(),
            "Q2" => ((ID<object>)new Q2()).D(),
            "D21" => ((ID<object>)new D21()).D(),
            "D19" => ((ID<object>)new D19()).D(),
            "G2" => ((ID<object>)new G2()).D(),
            "RD2" => ((ID<object>)new RD2()).D(),
            "RD3" => ((ID<object>)new RD3()).D(),
            _ => throw new System.ArgumentOutOfRangeException(nameof(row)),
        };

        private class A
        {
        }
        private class B
        {
        }
        private interface IDescribed<out T>
        {
            string Describe() => typeof(T).Name;
        }
        private interface IConsumer<in T>
        {
            string Eat() => typeof(T).Name;
        }
        private interface ID<out T>
        {
            string D() => "default-" + typeof(T).Name;
        }
        private interface IOver<out T> : ID<T>
        {
            string ID<T>.D() => "over-" + typeof(T).Name;
        }
        private interface IMore<out T> : IDescribed<T>
        {
        }
        private class Money : IDescribed<string>
        {
        }
        private class Two : IDescribed<A>, IDescribed<B>
        {
        }
        private class TwoRev : IDescribed<B>, IDescribed<A>
        {
        }
        private class Base : IDescribed<A>
        {
        }
        private class Derived : Base, IDescribed<B>
        {
        }
        private class Inh : IMore<string>
        {
        }
        private class Exact : IDescribed<object>, IDescribed<string>
        {
        }
        private class Cons : IConsumer<object>
        {
        }
        private class Cons2 : IConsumer<object>, IConsumer<string>
        {
        }
        private class T2 : ID<A>, ID<B>
        {
            string ID<B>.D() => "explicit-B";
        }
        private class Same : ID<object>, ID<string>
        {
            string ID<string>.D() => "explicit-string";
        }
        private class Base15 : ID<object>
        {
            string ID<object>.D() => "base-explicit-object";
        }
        private class Derived15 : Base15, ID<string>
        {
            string ID<string>.D() => "derived-explicit-string";
        }
        private class Base13 : ID<object>
        {
        }
        private class Derived13 : Base13, ID<string>
        {
            string ID<string>.D() => "derived-explicit-string";
        }
        private class Base8 : ID<A>
        {
            string ID<A>.D() => "base-explicit-A";
        }
        private class Derived8 : Base8, ID<B>
        {
        }
        private class Base12 : ID<A>
        {
            string ID<A>.D() => "base12-explicit-A";
        }
        private class Derived12 : Base12, IOver<B>
        {
        }
        private class Base18 : ID<A>, ID<B>
        {
            string ID<A>.D() => "base-explicit-A";
            string ID<B>.D() => "base-explicit-B";
        }
        private class Derived18 : Base18, ID<B>, ID<A>
        {
        }
        private class Base1 : ID<object>
        {
        }
        private class Derived1 : Base1, ID<string>
        {
        }
        private class BaseC : ID<A>
        {
            string ID<A>.D() => "explicit-A";
            public string D() => "base-public";
        }
        private class DerC : BaseC, ID<B>
        {
        }
        private class DerD : BaseC, ID<A>, ID<B>
        {
        }
        private class DerE : BaseC, ID<B>, ID<A>
        {
        }
        private class BaseF
        {
            public string D() => "basef-public";
        }
        private class MidF : BaseF, ID<A>
        {
            string ID<A>.D() => "mid-explicit-A";
        }
        private class DerF : MidF, ID<B>
        {
        }
        private class PB : ID<A>
        {
            string ID<A>.D() => "pb-explicit-A";
            public virtual string D() => "pb-public-virtual";
        }
        private class PD : PB, ID<B>
        {
        }
        private class NB : ID<A>
        {
            string ID<A>.D() => "nb-explicit-A";
            public string D() => "nb-public-nonvirtual";
        }
        private class ND : NB, ID<B>
        {
        }
        private class Q0
        {
            public string D() => "q0-public";
        }
        private class Q1 : Q0, ID<A>
        {
            string ID<A>.D() => "q1-explicit-A";
        }
        private class Q2 : Q1, IOver<B>
        {
        }
        private class B19 : ID<A>, ID<B>
        {
            string ID<A>.D() => "b19-explicit-A";
            public string D() => "b19-public";
        }
        private class D21 : B19, ID<B>
        {
        }
        private class D19 : B19, ID<B>, ID<A>
        {
        }
        private class G0
        {
            public string D() => "g0-public";
        }
        private class G1 : G0, ID<A>, ID<B>
        {
            string ID<A>.D() => "g1-explicit-A";
        }
        private class G2 : G1, ID<B>
        {
        }
        private class RB2 : ID<A>, ID<B>
        {
            string ID<A>.D() => "rb2-explicit-A";
            public virtual string D() => "rb2-public";
        }
        private class RD2 : RB2, ID<B>
        {
            public override string D() => "rd2-override";
        }
        private class RD3 : RB2
        {
            public override string D() => "rd3-override";
        }
    }

#if !UNITY_EDITOR
    public sealed class P127VariantPlayerTests : P127VariantRuntimeCases
    {
    }
#endif
}
