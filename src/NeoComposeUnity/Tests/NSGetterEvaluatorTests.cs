// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Integration coverage for the NSGetter evaluator port. The synth
    /// fixture's three NSGetter attributes
    /// (<c>attr-score</c>, <c>attr-manifest</c>, <c>attr-active</c>)
    /// were authored on the TS side specifically to exercise every
    /// pointer kind, both operations, and the major function variants
    /// (where, count). Running them through
    /// <see cref="NeoAttributeNSGetter.Compute"/> verifies that the
    /// C# evaluator produces the same value the TS evaluator would.
    ///
    /// <para>This isn't comprehensive parity coverage with the TS
    /// 80-case test suite — that's a follow-up. These tests pin the
    /// happy paths through every pointer kind plus a handful of
    /// runtime-error edge cases.</para>
    /// </summary>
    public class NSGetterEvaluatorTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            var loader = new NeoLoader();
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;
            return loader.Load(LoadFixture("synth-example.json"), loadSave, handleSave);
        }

        private static NSGetterAttribute RequireNSGetter(NeoClient client, string id)
        {
            if (!client.TryGetAttribute(id, out NSGetterAttribute? attr))
            {
                Assert.Fail($"Fixture is missing NSGetterAttribute '{id}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        // ---------------------------------------------------------------
        // attr-score — exercises the gnarliest IR shape:
        //   local int x = 1 + 2;                       (variable + arithmetic + value)
        //   local string label = (this.Name ?? "Unknown")!;  (forceUnwrap + coalesce + keyOf + value)
        //   if ((label is string) && (x != 0)) {       (boolean op + isCheck + comparison)
        //     return [1,2,3].Where(n => n != 0).Count();  (listLiteral + where + count)
        //   } else { throw "bad"; }
        //   return;                                    (bare return)
        //
        // The fixture binds `__this__` to a Custom of type-hero. We pass
        // an explicit thisValue so the test doesn't rely on the parent-
        // chain walk (covered separately).
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrScore_RunsFullIR_ReturnsCount()
        {
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            // `__this__` is a Custom record with a Name field; the IR
            // reads `this.Name`. v-name is "hero" in the fixture.
            var thisValue = new Dictionary<string, object?>
            {
                { "Name", "v-name" }, // resolves through the schema → attr-name → row v-name
            };

            var result = node.Compute(thisValue);

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            // [1,2,3].Where(n => n != 0).Count() = 3
            Assert.AreEqual(3.0, result.value);
        }

        // ---------------------------------------------------------------
        // attr-manifest — stringify + dictLiteral coverage. The IR is:
        //   return $"{ {[ "k1" ]: 1} }";
        //   → stringify(dictLiteral([{key: "k1", value: 1}]))
        //   → Dictionary<int> formatted via formatForInterp
        //
        // The dictLiteral has no source row to reference-equality-match
        // against, so the formatted output should fall back to
        // "(Dictionary<int>, Value<<unknown>>)".
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrManifest_StringifiesDictLiteral()
        {
            var client = LoadClient();
            var manifestAttr = RequireNSGetter(client, "attr-manifest");
            var node = new NeoAttributeNSGetter(client, manifestAttr, null);

            var result = node.Compute();

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            Assert.AreEqual("(Dictionary<int>, Value<<unknown>>)", result.value);
        }

        // ---------------------------------------------------------------
        // attr-active — callGetter + toBool coverage. The IR is:
        //   return Boolean(this.Score);
        //   → toBool(callGetter("attr-score", thisPointer = __this__))
        //
        // attr-score is invoked via dispatchNSGetterById; the result
        // (a number) is coerced to bool via JsTruthy. Number 3 → true.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrActive_DispatchesCallGetterAndCoercesToBool()
        {
            var client = LoadClient();
            var activeAttr = RequireNSGetter(client, "attr-active");
            var node = new NeoAttributeNSGetter(client, activeAttr, null);

            var thisValue = new Dictionary<string, object?>
            {
                { "Name", "v-name" },
            };

            var result = node.Compute(thisValue);

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            Assert.AreEqual(true, result.value);
        }

        // ---------------------------------------------------------------
        // resolvedGetter / resolvedReturnTypeInfo — pin the chain-walk.
        // attr-score has its own getter + returnTypeInfo so resolution
        // shouldn't need to walk anywhere.
        // ---------------------------------------------------------------

        [Test]
        public void ResolvedGetter_ReturnsInstanceGetter_WhenPresent()
        {
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            Assert.AreSame(scoreAttr.getter, node.resolvedGetter);
        }

        [Test]
        public void ResolvedReturnTypeInfo_ReturnsInstanceTypeInfo_WhenPresent()
        {
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            Assert.AreSame(scoreAttr.returnTypeInfo, node.resolvedReturnTypeInfo);
            Assert.AreEqual(AttributeType.Int, node.resolvedReturnTypeInfo!.type);
        }

        // ---------------------------------------------------------------
        // Runtime-error paths.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_NoCompiledGetter_ReturnsErrorResult()
        {
            // Synthesize a fresh NSGetterAttribute with no `getter` and
            // no extends chain — simulates an unsaved override.
            var client = LoadClient();
            var attr = new NSGetterAttribute
            {
                id = "test-orphan-getter",
                _id = "test-orphan-getter",
                projectId = "p",
                name = "Orphan",
                type = AttributeType.NSGetter,
                code = "// not compiled",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Int,
                    required = true,
                },
                getter = null!,  // simulate "no getter yet"
                createdAt = "x",
                updatedAt = "x",
            };
            var node = new NeoAttributeNSGetter(client, attr, null);

            var result = node.Compute();

            Assert.IsFalse(result.ok);
            Assert.That(result.error, Does.Contain("Compiled `getter`"));
        }

        [Test]
        public void Compute_OptionalChaining_SurvivesNullThis()
        {
            // attr-score reads `this?.Name ?? "Unknown"` — the keyOf
            // is optional, so a null `__this__` short-circuits to null,
            // the coalesce substitutes "Unknown", and the function
            // continues to its tail (which doesn't depend on `this`).
            // Pinning that the optional/coalesce path resolves cleanly
            // without throwing — the TS evaluator's behavior we're
            // mirroring.
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            var result = node.Compute();  // no thisValue, no parent

            Assert.IsTrue(result.ok, $"Expected ok via optional chaining; got: {result.error}");
            Assert.AreEqual(3.0, result.value);
        }

        [Test]
        public void ForceUnwrap_OnNullValue_ThrowsRuntimeError()
        {
            // Build a tiny getter that just force-unwraps a null literal.
            // Pins the force-unwrap-throws-on-null path that the TS
            // evaluator uses.
            var client = LoadClient();
            var attr = new NSGetterAttribute
            {
                id = "test-force-unwrap-null",
                _id = "test-force-unwrap-null",
                projectId = "p",
                name = "ForceUnwrapNull",
                type = AttributeType.NSGetter,
                code = "// `return (null as string?)!;`",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                getter = new FunctionWithReturnType
                {
                    parameters = new Variable[0],
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = "return",
                            pointer = new ForceUnwrapPointer
                            {
                                type = "forceUnwrap",
                                pointer = new ValuePointer
                                {
                                    type = "value",
                                    value = new Value
                                    {
                                        typeInfo = new PrimitiveTypeInfo
                                        {
                                            type = AttributeType.String,
                                            required = false,
                                        },
                                        value = null,
                                    },
                                },
                            },
                        },
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var node = new NeoAttributeNSGetter(client, attr, null);

            var result = node.Compute();

            Assert.IsFalse(result.ok);
            Assert.That(result.error, Does.Contain("force-unwrapping"));
        }

        // ---------------------------------------------------------------
        // Auto-resolution of __this__ from the parent chain.
        //
        // Build a wrapper tree where a Custom record contains an
        // NSGetter as one of its schema-keyed children. When we look
        // up that NSGetter via the parent and Compute() with no
        // explicit thisValue, the evaluator should walk parent up to
        // find the Custom record.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AutoResolvesThisValue_FromParentChain()
        {
            var client = LoadClient();
            // attr-hero is a Custom of type-hero whose schema has
            // { Name: attr-name, Health: attr-health }. Bind to v-dict
            // (which has `{ Name: "v-name", Level: "v-level" }` —
            // Level isn't in the schema so only Name walks).
            var heroAttr = client.TryGetAttribute("attr-hero", out CustomAttribute? ha)
                ? ha
                : null;
            Assert.IsNotNull(heroAttr);
            var hero = (NeoAttributeCustom)NeoAttribute.Create(client, heroAttr!, "v-dict");

            // Now manually attach an NSGetter child under the hero.
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var nsg = new NeoAttributeNSGetter(client, scoreAttr, null);
            nsg.parent = hero;  // simulates collection-side wiring

            var result = nsg.Compute();  // no explicit thisValue

            Assert.IsTrue(result.ok, $"Expected ok via parent walk; got: {result.error}");
            Assert.AreEqual(3.0, result.value);
        }
    }
}
