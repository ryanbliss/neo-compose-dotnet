// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §5.3's runtime type key grammar. The cases mirror the compose twin's
    /// <c>runtime-type-key.test.ts</c>; its project-aware cases need the
    /// version's types, which only the server has.
    /// </summary>
    public class P127RuntimeTypeKeyTests
    {
        private static string RoundTrip(string text)
        {
            Assert.That(NeoRuntimeTypeKeys.TryParse(text, out NeoRuntimeTypeKey key, out string error), Is.True, error);
            return NeoRuntimeTypeKeys.Spell(key);
        }

        [TestCase("int")]
        [TestCase("Vector2Int")]
        [TestCase("NeoDialogueReference")]
        [TestCase("object")]
        [TestCase("NumberFormatInfo")]
        [TestCase("List<int>")]
        [TestCase("Set<string>")]
        [TestCase("Reference<int>")]
        [TestCase("Dictionary<string,List<c:money>>")]
        [TestCase("NeoDelegate<bool,object,object>")]
        [TestCase("NeoDelegate<void,int>")]
        [TestCase("NeoAction")]
        [TestCase("NeoAction<int,string>")]
        [TestCase("c:money")]
        [TestCase("c:box<int>")]
        [TestCase("c:pair<int,c:box<string>>")]
        [TestCase("i:numberBase<int>")]
        [TestCase("e:planet")]
        [TestCase("(int,string)")]
        [TestCase("(int,int,int,int,int,int,int,int,int)")]
        [TestCase("ValueTuple<int>")]
        [TestCase("int?")]
        [TestCase("e:planet?")]
        [TestCase("(int,string)?")]
        [TestCase("Vector2?")]
        [TestCase("List<int?>")]
        public void ClosedKeysRoundTrip(string text)
        {
            Assert.That(RoundTrip(text), Is.EqualTo(text));
            Assert.That(NeoRuntimeTypeKeys.Violation(text, closed: true), Is.Null);
        }

        [TestCase("List<>")]
        [TestCase("Dictionary<,>")]
        [TestCase("NeoAction<,>")]
        [TestCase("c:box<>")]
        [TestCase("c:pair<,>")]
        [TestCase("i:numberBase<>")]
        [TestCase("Nullable<>")]
        [TestCase("ValueTuple<>")]
        [TestCase("ValueTuple<,,,,,,,>")]
        [TestCase("!c:box/0")]
        [TestCase("!i:numberBase/0")]
        [TestCase("!Dictionary/1")]
        [TestCase("c:base<List<!c:box/0>>")]
        public void OpenKeysRoundTripAndFailTheClosedCheck(string text)
        {
            Assert.That(RoundTrip(text), Is.EqualTo(text));
            Assert.That(NeoRuntimeTypeKeys.Violation(text), Is.Null);
            NeoRuntimeTypeKeys.TryParse(text, out NeoRuntimeTypeKey key, out _);
            Assert.That(NeoRuntimeTypeKeys.IsClosed(key), Is.False);
            Assert.That(NeoRuntimeTypeKeys.Violation(text, closed: true), Does.Contain("must be closed"));
        }

        /// <summary>§2's table and §5.3: exactly these opaque types, by .NET full name.</summary>
        [Test]
        public void OpaqueTypesOnlyInTypePayloads()
        {
            string[] expected =
            {
                "System.Reflection.MemberInfo",
                "System.Delegate",
                "System.MulticastDelegate",
                "UnityEngine.Object",
                "NeoCompose.Runtime.NeoActionBase",
            };
            Assert.That(NeoRuntimeTypeKeys.OpaqueTypes, Is.EquivalentTo(expected));
            Assert.That(typeof(NeoActionBase).FullName, Is.EqualTo("NeoCompose.Runtime.NeoActionBase"));
            foreach (string opaque in expected)
            {
                string name = "opaque:" + opaque;
                Assert.That(RoundTrip(name), Is.EqualTo(name));
                Assert.That(NeoRuntimeTypeKeys.Violation(name, allowOpaque: true), Is.Null);
                Assert.That(NeoRuntimeTypeKeys.Violation(name), Does.Contain("outside a Type payload"));
            }
            Assert.That(
                NeoRuntimeTypeKeys.Violation("opaque:System.Object", allowOpaque: true),
                Does.Contain("unknown opaque type"));
            Assert.That(
                NeoRuntimeTypeKeys.Violation("List<opaque:UnityEngine.Object>"),
                Does.Contain("outside a Type payload"));
        }

        [TestCase("", "empty key")]
        [TestCase("List<int", "expected '>'")]
        [TestCase("List<int>>", "unexpected '>'")]
        [TestCase("(int)", "at least two elements")]
        [TestCase("int??", "second '?'")]
        [TestCase("!c:box", "expected '/'")]
        [TestCase("!c:box/", "needs an index")]
        [TestCase("!c:box/01", "non-canonical")]
        [TestCase("!e:planet/0", "enum has no type parameters")]
        [TestCase("e:planet<>", "enum can't be generic")]
        [TestCase("ValueTuple<int,int>", "one type argument")]
        [TestCase("c:", "empty class id")]
        [TestCase("List <int>", "unexpected ' '")]
        public void MalformedKeysAreRejected(string text, string message)
        {
            Assert.That(NeoRuntimeTypeKeys.Violation(text), Does.Contain(message));
        }

        [TestCase("List", "without type arguments")]
        [TestCase("NeoDelegate", "without a result type")]
        [TestCase("List<int,int>", "takes 1")]
        [TestCase("Dictionary<int>", "takes 2")]
        [TestCase("int<int>", "non-generic 'int'")]
        [TestCase("Widget", "unknown built-in type 'Widget'")]
        [TestCase("Partial", "unknown built-in type 'Partial'")]
        [TestCase("Nullable<int>", "write the '?' form")]
        [TestCase("void", "unknown built-in type 'void'")]
        [TestCase("List<void>", "'void' outside a NeoDelegate result")]
        [TestCase("NeoDelegate<int,void>", "'void' outside a NeoDelegate result")]
        [TestCase("string?", "'string', which is not a value type")]
        [TestCase("c:money?", "not a value type")]
        [TestCase("List<int>?", "not a value type")]
        [TestCase("!c:box/0?", "not a value type")]
        [TestCase("List<List<>>", "nests definition 'List<>'")]
        [TestCase("ValueTuple<,,,,,,,,>", "above 8")]
        [TestCase("Nullable<,>", "Nullable with 2 parameters")]
        [TestCase("!NeoAction/0", "no fixed-arity type parameters")]
        [TestCase("!List/1", "parameter 1 of 'List', which has 1")]
        [TestCase("Dictionary<,,>", "takes 2")]
        public void StructurallyInvalidKeysAreRejected(string text, string message)
        {
            Assert.That(NeoRuntimeTypeKeys.Violation(text), Does.Contain(message));
        }
    }
}
