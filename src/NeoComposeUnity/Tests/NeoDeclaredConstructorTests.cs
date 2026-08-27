// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P43 §1 / §6 / §8 — NeoScript initializers and declared class
    /// constructors, exercised over a hand-built schema in the style of
    /// <see cref="NeoStaticBindingTests"/>.
    ///
    /// <para>The schema is deliberately small and every constructor body is
    /// hand-built IR, so each test names exactly one behavior: the four-step
    /// construction order, run-then-overwrite, base chaining, optional-member
    /// initializers, the construction depth cap, and the
    /// metadata-before-arguments ordering invariant.</para>
    /// </summary>
    public class NeoDeclaredConstructorTests
    {
        private const string ProjectId = "ctor-project";

        // -------------------------------------------------------------------
        // Construction order.
        // -------------------------------------------------------------------

        [Test]
        public void DeclaredConstructor_MemberInitializersRunThenTheBodyOverwrites()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(PartConstructorPointer(
                    "ctor-part",
                    StringPointer("hi"))),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            // The body overwrote the member initializer's "base".
            Assert.AreEqual("hi", ReadString(client, root, "Label"));
            // A literal member initializer the body never touched survives.
            Assert.AreEqual(1, ReadNumber(client, root, "Level"));
        }

        [Test]
        public void DeclaredConstructor_OptionalMemberInitializerMaterializes()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            // The implicit `new()` — member initializers only, no body.
            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(PartConstructorPointer(null)),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            Assert.AreEqual("base", ReadString(client, root, "Label"));
            // `Tag` is OPTIONAL. Before P43 an optional member got no row at
            // all; an initializer is an explicit statement that it has a value,
            // so it materializes anyway.
            Assert.AreEqual("init-tag", ReadString(client, root, "Tag"));
        }

        [Test]
        public void DeclaredConstructor_CallSiteFieldWinsOverTheBody()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(PartConstructorPointer(
                    "ctor-part",
                    StringPointer("body"),
                    LabelField(StringPointer("call-site")))),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            Assert.AreEqual("call-site", ReadString(client, root, "Label"));
        }

        [Test]
        public void DeclaredConstructor_OverriddenMemberInitializerStillRuns()
        {
            NeoClient client = BuildClient();

            // `Bad`'s initializer throws. Overriding `Bad` at the call site
            // must NOT suppress it: §1.2 pins run-then-overwrite, and a
            // throwing initializer is exactly what makes the difference
            // observable.
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(BoomConstructorPointer(
                        new FunctionClassConstructorField
                        {
                            schemaKey = "Bad",
                            memberId = "boom-bad",
                            valuePointer = StringPointer("overridden"),
                        })),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("initializer exploded", error.Message);
        }

        [Test]
        public void DeclaredConstructor_BaseChainRunsBeforeTheDerivedBody()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(SubConstructorPointer(StringPointer("S"))),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            // The base constructor received the `: base(Prefix: Suffix)`
            // argument and wrote Label...
            Assert.AreEqual("S", ReadString(client, root, "Label"));
            // ...and the derived body, which copies Label into Extra, saw it —
            // which is only true if the base ran first.
            Assert.AreEqual("S", ReadString(client, root, "Extra"));
        }

        [Test]
        public void DeclaredConstructor_BaseArgumentReadsTheConstructedInstance()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            // `Sub() : base(Prefix: this.Tag)`. Step 1 has already run every
            // member initializer by the time a base argument is evaluated, so
            // `this.Tag` reads the initializer's product — which is what
            // evaluateBaseConstructorArguments binds on the TS side.
            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(DeclaredConstructorPointer(
                    ClassType("sub-class"),
                    "ctor-sub-reads-this",
                    Array.Empty<DeclaredConstructorArgument>(),
                    Array.Empty<FunctionClassConstructorField>())),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            Assert.AreEqual("init-tag", ReadString(client, root, "Label"));
        }

        [Test]
        public void DeclaredConstructor_CallSiteFieldExpressionsRunAfterTheBody()
        {
            NeoClient client = BuildClient();

            // The body throws, and the call-site field expression would throw a
            // DIFFERENT error if it ran first. C# runs an object initializer's
            // expressions only once the constructor has returned, and §6.1
            // step 4 pins the same order, so the body's message is the one that
            // must escape.
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(PartConstructorPointer(
                        "ctor-part-throw",
                        null,
                        LabelField(new ForceUnwrapPointer
                        {
                            type = PointerKind.ForceUnwrap,
                            pointer = NullPointer(),
                        }))),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("body boom", error.Message);
        }

        [Test]
        public void DeclaredConstructor_ThrowingBodyDoesNotLeakThePublishedGraph()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;

            Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(PartConstructorPointer("ctor-part-throw")),
                    new NSGetterEvaluator.Context(client, null, null)));

            // Step 1 publishes the whole instance graph BEFORE the body runs,
            // so a throwing body leaves rows behind unless the root is owned by
            // the allocation tracker from the moment it is published.
            Assert.AreEqual(
                before,
                client.sessionValues.Count,
                "A construction that threw must not leave its published rows in sessionData.");
        }

        [Test]
        public void DeclaredConstructor_ExplicitNullCallSiteFieldClearsAnOptionalMember()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            // `new Part() { Tag = null }`. These fields are the call site's
            // initializer block, not positional parameters, so an explicit null
            // is an assignment that wins over the member initializer — not an
            // "omit this argument" sentinel.
            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(PartConstructorPointer(
                    null,
                    null,
                    TagField(NullPointer()))),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            Assert.IsTrue(
                root.value!.TryGetValue("Tag", out string tagValueId),
                "An explicitly nulled optional member keeps its slot.");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tagValueId,
                out StringMemberValue? tag));
            Assert.IsNull(
                tag!.value,
                "An explicit null at the call site must beat the member initializer.");
        }

        [Test]
        public void DeclaredConstructor_ExplicitNullCallSiteFieldOnARequiredMemberIsRejected()
        {
            NeoClient client = BuildClient();

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(PartConstructorPointer(
                        null,
                        null,
                        LabelField(NullPointer()))),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("is required and cannot be null", error.Message);
        }

        // -------------------------------------------------------------------
        // Ordering invariant, depth, and allocation.
        // -------------------------------------------------------------------

        [Test]
        public void DeclaredConstructor_StaleMetadataFailsBeforeArgumentEvaluation()
        {
            NeoClient client = BuildClient();

            // The field names a schema key that does not exist, and the
            // argument pointer force-unwraps null. Whichever runs first
            // decides the message.
            var pointer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DeclaredConstructorFunction
                {
                    type = FunctionKind.DeclaredConstructor,
                    info = new DeclaredConstructorInfo
                    {
                        schemaClassInfo = PartType(),
                        constructorId = "ctor-part",
                        args = new[]
                        {
                            new DeclaredConstructorArgument
                            {
                                name = "Prefix",
                                valuePointer = new ForceUnwrapPointer
                                {
                                    type = PointerKind.ForceUnwrap,
                                    pointer = NullPointer(),
                                },
                            },
                        },
                        fields = new[]
                        {
                            new FunctionClassConstructorField
                            {
                                schemaKey = "LegacyLabel",
                                memberId = "part-label",
                                valuePointer = StringPointer("x"),
                            },
                        },
                    },
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(pointer),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("contains stale field", error.Message);
            Assert.That(
                error.Message,
                Does.Not.Contain("force-unwrapping"),
                "Metadata must be validated before any argument pointer runs.");
        }

        [Test]
        public void DeclaredConstructor_MissingRecordFailsClosed()
        {
            NeoClient client = BuildClient();

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(PartConstructorPointer("ctor-does-not-exist")),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("ctor-does-not-exist", error.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void DeclaredConstructor_DepthCapNamesTheConstructionChain()
        {
            NeoClient client = BuildClient();

            // Cycle's only member initializer constructs another Cycle.
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(CycleConstructorPointer()),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("construction depth exceeded 64", error.Message);
            // Every construction step gets its own frame, initializers
            // included, exactly as `pushConstructionFrame` /
            // `evaluateInitializerInContext` label them in evaluateNSGetter.ts.
            // Counting the initializer is what keeps the two runtimes tripping
            // at the same nesting on the same graph.
            StringAssert.Contains("Cycle -> Self initializer -> Cycle", error.Message);
        }

        [Test]
        public void DeclaredConstructor_UnescapedConstructionIsCollectedAtTerminal()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;

            object? result = NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        new VariableInstruction
                        {
                            type = InstructionKind.Variable,
                            variable = new Variable
                            {
                                id = "local",
                                typeInfo = PartType(),
                                pointer = PartConstructorPointer(
                                    "ctor-part",
                                    StringPointer("temp")),
                            },
                        },
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = StringPointer("done"),
                        },
                    },
                },
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual("done", result);
            Assert.AreEqual(
                before,
                client.sessionValues.Count,
                "A constructed instance that never escaped must be reclaimed.");
        }

        // -------------------------------------------------------------------
        // The generated-C# seam.
        // -------------------------------------------------------------------

        [Test]
        public void EvaluateDeclaredConstructor_RunsTheBodyWithMarshalledArguments()
        {
            NeoClient client = BuildClient();

            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "part-class",
                    "ctor-part",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Prefix", "from-csharp"),
                    });

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("from-csharp", ReadString(client, root, "Label"));
            Assert.AreEqual("init-tag", ReadString(client, root, "Tag"));
        }

        [Test]
        public void EvaluateDeclaredConstructor_NullIdRunsMemberInitializersOnly()
        {
            NeoClient client = BuildClient();

            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "part-class",
                    null,
                    Array.Empty<NeoDeclaredConstructorArgument>());

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("base", ReadString(client, root, "Label"));
            Assert.AreEqual("init-tag", ReadString(client, root, "Tag"));
        }

        [Test]
        public void EvaluateDeclaredConstructor_UndeclaredArgumentIsRejected()
        {
            NeoClient client = BuildClient();

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                        client,
                        "part-class",
                        "ctor-part",
                        new[]
                        {
                            new NeoDeclaredConstructorArgument("Nope", "x"),
                        }))!;

            StringAssert.Contains("Prefix", error.Message);
        }

        // -------------------------------------------------------------------
        // P49 §1 — required constructors.
        // -------------------------------------------------------------------

        [Test]
        public void RequiredConstructor_EvaluatesThroughTheGeneratedSeam()
        {
            NeoClient client = BuildClient();

            // `Gear` declares its parameter on the class header, so its
            // constructor record is reached through `requiredConstructorId`
            // rather than `constructorIds` — the seam otherwise behaves
            // exactly like a declared constructor.
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    });

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("from-header", ReadString(client, root, "Label"));
            Assert.AreEqual("init-tag", ReadString(client, root, "Tag"));
        }

        [Test]
        public void RequiredConstructor_ImplicitNewIsRejected()
        {
            NeoClient client = BuildClient();

            // §1.3 — the header parameter list IS the statement that `Bar` is
            // not optional, so the implicit parameterless `new` is gone.
            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                        client,
                        "gear-class",
                        null,
                        Array.Empty<NeoDeclaredConstructorArgument>()))!;

            StringAssert.Contains("Gear", error.Message);
            StringAssert.Contains("Bar", error.Message);
            StringAssert.Contains(
                "implicit parameterless new",
                error.Message);
        }

        [Test]
        public void EvaluateDeclaredConstructor_CallSiteValuesWinOverTheBody()
        {
            NeoClient client = BuildClient();

            // §4.4 — the optional members a generated constructor appends after
            // its declared parameters travel as the call-site initializer
            // block, which is applied last (§2.5).
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue(
                            "Label",
                            "gear-label",
                            "from-call-site"),
                        new NeoGeneratedConstructorValue(
                            "Tag",
                            "gear-tag",
                            "call-site-tag"),
                    });

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("from-call-site", ReadString(client, root, "Label"));
            Assert.AreEqual("call-site-tag", ReadString(client, root, "Tag"));
        }

        [Test]
        public void EvaluateDeclaredConstructor_NullCallSiteValueOmitsAnOptionalMember()
        {
            NeoClient client = BuildClient();

            // A generated `= null` default means the caller omitted the
            // parameter, so `Tag` keeps what its initializer produced. That is
            // the opposite of a NeoScript block's explicit `Tag = null`, where
            // the author wrote the null and it clears the member.
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue("Tag", "gear-tag", null),
                    });

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("init-tag", ReadString(client, root, "Tag"));
        }

        [Test]
        public void EvaluateDeclaredConstructor_StaleCallSiteValueIsRejected()
        {
            NeoClient client = BuildClient();

            // The omit rule must not swallow a stale generated field: the whole
            // supplied set is validated against the merged schema before the
            // nulls are dropped.
            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                        client,
                        "gear-class",
                        "ctor-gear",
                        new[]
                        {
                            new NeoDeclaredConstructorArgument("Bar", "from-header"),
                        },
                        new[]
                        {
                            new NeoGeneratedConstructorValue(
                                "LegacyTag",
                                "gear-tag",
                                null),
                        }))!;

            StringAssert.Contains("stale field", error.Message);
        }

        // -------------------------------------------------------------------
        // P49 §4.4 — every member kind the member-wise factory accepts must
        // also travel as the generated call-site initializer block. These are
        // the kinds whose stored payload is not the value the caller passes:
        // a list expands into entry ROWS, an enum into option ids, a dictionary
        // into keyed entry rows, and a class entry has to be adopted.
        // -------------------------------------------------------------------

        [Test]
        public void EvaluateDeclaredConstructor_ListCallSiteValueMaterializesEntryRows()
        {
            NeoClient client = BuildClient();

            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue(
                            "Parts",
                            "gear-parts",
                            new List<string> { "left", "right" }),
                    });

            ObjectMemberValue root = node.value!;
            string[] entryIds = ReadArray(client, root, "Parts")!;
            Assert.AreEqual(2, entryIds.Length);
            Assert.AreEqual("left", ReadStringRow(client, entryIds[0]));
            Assert.AreEqual("right", ReadStringRow(client, entryIds[1]));
        }

        [Test]
        public void EvaluateDeclaredConstructor_EnumCallSiteValueBecomesOptionIds()
        {
            NeoClient client = BuildClient();

            // Generated enum wrappers arrive as INeoEnumOption, not as the
            // option-id array the row stores.
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue(
                            "Mode",
                            "gear-mode",
                            new TestEnumOption("option-fast")),
                    });

            ObjectMemberValue root = node.value!;
            CollectionAssert.AreEqual(
                new[] { "option-fast" },
                ReadArray(client, root, "Mode"));
        }

        [Test]
        public void EvaluateDeclaredConstructor_DictionaryCallSiteValueMaterializesEntryRows()
        {
            NeoClient client = BuildClient();

            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue(
                            "Slots",
                            "gear-slots",
                            new Dictionary<string, string>
                            {
                                ["primary"] = "one",
                            }),
                    });

            ObjectMemberValue root = node.value!;
            Dictionary<string, string> entries =
                ReadObject(client, root, "Slots")!;
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("one", ReadStringRow(client, entries["primary"]));
        }

        [Test]
        public void EvaluateDeclaredConstructor_ListCallSiteValueAdoptsClassEntries()
        {
            NeoClient client = BuildClient();

            NeoMemberClassWritable part =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "part-class",
                    "ctor-part",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Prefix", "linked"),
                    });
            string partValueId = part.value!.id;

            // A Class value inside a supplied collection has no write target of
            // its own, so the seam adopts it: a parentless Session row attaches
            // at its own id rather than being copied.
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "gear-class",
                    "ctor-gear",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Bar", "from-header"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue(
                            "Links",
                            "gear-links",
                            new List<INeoValueReference>
                            {
                                new TestValueReference(partValueId),
                            }),
                    });

            ObjectMemberValue root = node.value!;
            string[] entryIds = ReadArray(client, root, "Links")!;
            CollectionAssert.AreEqual(new[] { partValueId }, entryIds);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                partValueId,
                out ObjectMemberValue? linked));
            Assert.AreEqual("linked", ReadString(client, linked!, "Label"));
        }

        // -------------------------------------------------------------------
        // P49 §1.3/§1.4 — the member-wise paths are closed on an S1 class.
        // -------------------------------------------------------------------

        [Test]
        public void RequiredConstructor_MemberWiseFactoryIsRejected()
        {
            NeoClient client = BuildClient();

            // The generated member-wise factory settles members without
            // invoking a constructor — the one path a required constructor
            // exists to disable.
            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoGeneratedTypesSupport.CreateWritableClassValue(
                        client,
                        "gear-class",
                        new NeoGeneratedConstructorValue(
                            "Label",
                            "gear-label",
                            "member-wise")))!;

            StringAssert.Contains("Gear", error.Message);
            StringAssert.Contains("Bar", error.Message);
            StringAssert.Contains(
                "cannot be constructed without one",
                error.Message);
        }

        [Test]
        public void RequiredConstructor_SchemaDerivedNewIsRejected()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            // §1.4's `Foo c = new { Bar = "bar" }`: the schema-derived
            // `classConstructor` arm, reached when no constructor is named at
            // all. Settling a member is not the same as satisfying the
            // constructor.
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    new FunctionWithReturnType
                    {
                        compilerRevision =
                            FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = Array.Empty<Variable>(),
                        typeInfo = ClassType("gear-class"),
                        instructions = new Instruction[]
                        {
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = new FunctionPointer
                                {
                                    type = PointerKind.Function,
                                    function = new ClassConstructorFunction
                                    {
                                        type = FunctionKind.ClassConstructor,
                                        info = new FunctionClassConstructorInfo
                                        {
                                            schemaClassInfo =
                                                ClassType("gear-class"),
                                            fields = new[]
                                            {
                                                new FunctionClassConstructorField
                                                {
                                                    schemaKey = "Label",
                                                    memberId = "gear-label",
                                                    valuePointer =
                                                        StringPointer("settled"),
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                    ctx))!;

            StringAssert.Contains("Gear", error.Message);
            StringAssert.Contains("Bar", error.Message);
            StringAssert.Contains(
                "cannot be constructed without one",
                error.Message);
        }

        [Test]
        public void RequiredConstructor_BaseClauseSettlesInheritedMembers()
        {
            NeoClient client = BuildClient();

            // `class Cog(string Seed) : Gear(Seed) { Tag = "from-base-clause" }`
            // — the clause's arguments reach the base's required constructor,
            // which is not listed in `constructorIds`, and its block then
            // settles an inherited member.
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "cog-class",
                    "ctor-cog",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Seed", "seeded"),
                    });

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("seeded", ReadString(client, root, "Label"));
            Assert.AreEqual("from-base-clause", ReadString(client, root, "Tag"));
        }

        [Test]
        public void StoredPlacementFollowsNamedBaseOverloadParameterForwarding()
        {
            NeoClient client = BuildClient();
            var classes = (Dictionary<string, NeoSchemaClass>)client.classes;
            var members = (Dictionary<string, JsonMember>)client.members;
            var constructors = (Dictionary<string, ConstructorRecord>)client.constructors;
            var values = (Dictionary<string, MemberValue>)client.values;

            // Exercise a declared base overload, not the required-constructor
            // shortcut: Cog(Seed) -> Gear(Bar: Seed), where Bar settles Parts.
            classes["gear-class"].requiredConstructorId = null;
            classes["gear-class"].constructorIds = new[] { "ctor-gear" };
            var parts = (ListMember)members["gear-parts"];
            parts.defaultValue = new ArrayMemberValueBase
            {
                init = new InitializerBody { code = "Bar" },
            };
            constructors["ctor-gear"].argumentTypes[0].type = MemberKind.List;
            constructors["ctor-gear"].argumentTypes[0].entryTypeInfo =
                new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                };
            constructors["ctor-cog"].argumentTypes[0].type = MemberKind.List;
            constructors["ctor-cog"].argumentTypes[0].entryTypeInfo =
                new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                };

            var frames = new ArrayMemberValue
            {
                id = "forwarded-parts",
                value = Array.Empty<string>(),
            };
            var root = new ObjectMemberValue
            {
                id = "forwarded-cog",
                classId = "cog-class",
                value = new Dictionary<string, string>(),
                instanceConstructorId = "ctor-cog",
                constructorArgs = new Dictionary<string, JToken?>
                {
                    [NeoClient.ConstructorParameterId(
                        constructors["ctor-cog"],
                        0)] = frames.id,
                },
            };
            values[frames.id] = frames;
            values[root.id] = root;

            Assert.AreSame(frames, client.ResolveClassChildRow(root, "Parts"));
            Assert.IsTrue(client.TryInferMemberForValueId(
                frames.id,
                out JsonMember? inferred));
            Assert.AreEqual(parts.id, inferred!.id);
        }

        [Test]
        public void RequiredConstructor_CallSiteBlockBeatsTheBaseClauseBlock()
        {
            NeoClient client = BuildClient();

            // §2.5's two-block collision: the base clause belongs to the
            // subclass's declaration, the call site is the only one the author
            // can see from where they are standing, so the call site wins.
            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "cog-class",
                    "ctor-cog",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Seed", "seeded"),
                    },
                    new[]
                    {
                        new NeoGeneratedConstructorValue(
                            "Tag",
                            "gear-tag",
                            "from-call-site"),
                    });

            ObjectMemberValue root = node.value!;
            Assert.AreEqual("from-call-site", ReadString(client, root, "Tag"));
        }

        [Test]
        public void RequiredConstructor_UnknownBaseInitializerKeyFailsClosed()
        {
            ProjectData data = BuildProjectData();
            data.constructors["ctor-cog"].baseInitializerFields = new[]
            {
                new ConstructorBaseInitializerField
                {
                    name = "Nope",
                    code = "\"x\"",
                },
            };
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                        client,
                        "cog-class",
                        "ctor-cog",
                        new[]
                        {
                            new NeoDeclaredConstructorArgument("Seed", "seeded"),
                        }))!;

            StringAssert.Contains("Nope", error.Message);
            StringAssert.Contains("does not declare", error.Message);
        }

        [Test]
        public void RequiredConstructor_AbsentAuthoredCodeMatchesAnEmptyBody()
        {
            // P49 §1.2 — `class Cog(string Seed) : Gear(Seed) { … }` declares no
            // `init` block, so its record ships no authored code at all. The
            // SDK executes the compiled action and never the source, so a null
            // `code` has to load and construct exactly like the `""` an
            // explicitly-empty block stores.
            (string? label, string? tag) absent = ConstructCog(code: null);
            (string? label, string? tag) empty = ConstructCog(code: "");

            Assert.AreEqual(empty, absent);
            Assert.AreEqual("seeded", absent.label);
            Assert.AreEqual("from-base-clause", absent.tag);
        }

        /// <summary>
        /// Loads a client whose `Cog` required constructor carries
        /// <paramref name="code"/> as its authored body and returns the two
        /// members its base clause settles.
        /// </summary>
        private static (string? label, string? tag) ConstructCog(string? code)
        {
            ProjectData data = BuildProjectData();
            data.constructors["ctor-cog"].code = code;
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            NeoMemberClassWritable node =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "cog-class",
                    "ctor-cog",
                    new[]
                    {
                        new NeoDeclaredConstructorArgument("Seed", "seeded"),
                    });

            ObjectMemberValue root = node.value!;
            return (
                ReadString(client, root, "Label"),
                ReadString(client, root, "Tag"));
        }

        // -------------------------------------------------------------------
        // Load-time record validation.
        // -------------------------------------------------------------------

        [Test]
        public void ConstructorRecords_RequiredConstructorIsOwnedWithoutConstructorIds()
        {
            ProjectData data = BuildProjectData();

            // §1.1/§1.3 — the required constructor record is deliberately absent
            // from its class's `constructorIds`, so the disowned-record guard
            // has to accept a record claimed through `requiredConstructorId`.
            Assert.DoesNotThrow(() => NeoTestSaveStack.ClientFromSchema(data));
        }

        [Test]
        public void ConstructorRecords_RequiredConstructorBesideOverloadsIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.classes["gear-class"].constructorIds = new[] { "ctor-gear-extra" };

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("Gear", error.Message);
            StringAssert.Contains("only way in", error.Message);
        }

        [Test]
        public void ConstructorRecords_MissingRequiredConstructorIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.classes["gear-class"].requiredConstructorId = "ctor-gear-missing";
            data.constructors.Remove("ctor-gear");

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("ctor-gear-missing", error.Message);
            StringAssert.Contains("missing from the export", error.Message);
        }

        [Test]
        public void ConstructorRecords_RequiredConstructorOfAnotherClassIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.classes["gear-class"].requiredConstructorId = "ctor-part";
            data.constructors.Remove("ctor-gear");

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("ctor-part", error.Message);
            StringAssert.Contains("as its owner", error.Message);
        }

        [Test]
        public void ConstructorRecords_BaseInitializerBlockWithoutGettersIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.constructors["ctor-cog"].compiledBaseInitializerFields = null;

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains(
                "compiled base initializer getters",
                error.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void ConstructorRecords_DanglingIdIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.classes["part-class"].constructorIds = new[] { "ctor-missing" };
            data.constructors.Remove("ctor-part");

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("ctor-missing", error.Message);
            StringAssert.Contains("missing from the export", error.Message);
        }

        [Test]
        public void ConstructorRecords_DisownedRecordIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.classes["part-class"].constructorIds = Array.Empty<string>();

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("ctor-part", error.Message);
            StringAssert.Contains("not listed in the constructorIds", error.Message);
        }

        [Test]
        public void ConstructorRecords_NameOnlyDistinctOverloadsAreRejected()
        {
            ProjectData data = BuildProjectData();
            ConstructorRecord twin = ConstructorFor(
                "ctor-part-twin",
                "part-class",
                new[] { StringArgument("Other") },
                LabelAssignment(ArgumentPointer(0)));
            data.constructors[twin.id] = twin;
            data.classes["part-class"].constructorIds =
                new[] { "ctor-part", twin.id };

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("same positional signature", error.Message);
            StringAssert.Contains("parameter name", error.Message);
        }

        [Test]
        public void ConstructorRecords_NonVoidBodyIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.constructors["ctor-part"].action.typeInfo = new PrimitiveTypeInfo
            {
                type = MemberKind.String,
                required = true,
            };

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("required Null return type", error.Message);
        }

        [Test]
        public void ConstructorRecords_StaleArgumentEnvelopeIsRejected()
        {
            ProjectData data = BuildProjectData();
            data.constructors["ctor-part"].action.parameters[2].id = "__arg_7__";

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("__arg_0__", error.Message);
        }

        [Test]
        public void ConstructorRecords_CollectionElementTypeDistinguishesOverloads()
        {
            ProjectData data = BuildProjectData();
            AddOverload(data, "ctor-list-int", "A", ListArgument("A", MemberKind.Int));
            AddOverload(data, "ctor-list-string", "B", ListArgument("B", MemberKind.String));

            // `Foo(List<int> A)` and `Foo(List<string> B)` generate two
            // distinct C# constructors, so the push side accepts them. A
            // signature key that stops at the outer MemberKind collapses both
            // to "List" and rejects the whole project at load.
            Assert.DoesNotThrow(() => NeoTestSaveStack.ClientFromSchema(data));
        }

        [Test]
        public void ConstructorRecords_DictionaryValueTypeDistinguishesOverloads()
        {
            ProjectData data = BuildProjectData();
            AddOverload(
                data,
                "ctor-map-part",
                "A",
                DictionaryArgument("A", ClassType("part-class")));
            AddOverload(
                data,
                "ctor-map-sub",
                "B",
                DictionaryArgument("B", ClassType("sub-class")));

            Assert.DoesNotThrow(() => NeoTestSaveStack.ClientFromSchema(data));
        }

        [Test]
        public void ConstructorRecords_TypeArgumentsDistinguishOverloads()
        {
            ProjectData data = BuildProjectData();
            AddOverload(
                data,
                "ctor-box-int",
                "A",
                ClosedGenericArgument("A", MemberKind.Int));
            AddOverload(
                data,
                "ctor-box-string",
                "B",
                ClosedGenericArgument("B", MemberKind.String));

            Assert.DoesNotThrow(() => NeoTestSaveStack.ClientFromSchema(data));
        }

        [Test]
        public void ConstructorRecords_IdenticalCollectionOverloadsAreStillRejected()
        {
            ProjectData data = BuildProjectData();
            AddOverload(data, "ctor-list-a", "A", ListArgument("A", MemberKind.Int));
            AddOverload(data, "ctor-list-b", "B", ListArgument("B", MemberKind.Int));

            // The recursion must not become a blanket accept: two `List<int>`
            // parameters differing only by name are still one C# signature.
            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("same positional signature", error.Message);
        }

        // -------------------------------------------------------------------
        // Literal materialization of a computed default.
        // -------------------------------------------------------------------

        [Test]
        public void MemberValueFactory_InitBackedDefaultFailsClosed()
        {
            var member = new StringMember
            {
                id = "computed-member",
                projectId = ProjectId,
                name = "Computed",
                kind = MemberKind.String,
                required = true,
                localizable = false,
                isReadOnly = true,
                defaultValue = new StringMemberValueBase
                {
                    init = new InitializerBody { code = "Compute()" },
                },
            };

            // Read-only members are REQUIRED to carry an explicit default, and
            // an initializer satisfies that check — so without this guard a
            // read-only member declared with an initializer would load fine and
            // then read back as null.
            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() =>
                    MemberValueFactory.CreateFromDefault(
                        member,
                        "__neo_default:computed-member",
                        "x",
                        "x"))!;

            StringAssert.Contains("computed default", error.Message);
            StringAssert.Contains("Computed", error.Message);
        }

        // -------------------------------------------------------------------
        // Value-container union.
        // -------------------------------------------------------------------

        [Test]
        public void MemberDefault_CarryingBothValueAndInitIsRejected()
        {
            const string json = @"{
  'id':'member-both','projectId':'ctor-project','name':'Both','kind':3,
  'locked':false,'required':true,'isStatic':false,'accessModifierKind':'public',
  'defaultValue':{'value':'literal','init':{'code':'Compute()'}},
  'createdAt':'x','updatedAt':'x'
}";
            Newtonsoft.Json.JsonSerializationException error =
                Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() =>
                    Newtonsoft.Json.JsonConvert.DeserializeObject<JsonMember>(
                        json.Replace('\'', '"')))!;

            StringAssert.Contains("both 'value' and 'init'", error.Message);
        }

        [Test]
        public void InitBackedDefault_WithoutCompiledBodyFailsClosed()
        {
            ProjectData data = BuildProjectData();
            ((StringMember)data.members["part-tag"]).defaultValue =
                new StringMemberValueBase
                {
                    init = new InitializerBody { code = "InitTag()" },
                };
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(PartConstructorPointer(null)),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("no compiled body", error.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        // -------------------------------------------------------------------
        // Assertion helpers.
        // -------------------------------------------------------------------

        private static ObjectMemberValue RequireConstructedRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            object? result)
        {
            Assert.IsNotNull(result);
            string? valueId = NSGetterEvaluator.FindRowIdByReference(result, ctx);
            Assert.IsNotNull(valueId, "Constructed value has no backing row.");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId!,
                out ObjectMemberValue? row));
            return row!;
        }

        private static string? ReadString(
            NeoClient client,
            ObjectMemberValue root,
            string schemaKey)
        {
            Assert.IsTrue(
                root.value!.TryGetValue(schemaKey, out string childId),
                $"Constructed row has no '{schemaKey}'. Keys: {string.Join(",", root.value.Keys)}");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out StringMemberValue? child));
            return child!.value;
        }

        private static string[]? ReadArray(
            NeoClient client,
            ObjectMemberValue root,
            string schemaKey)
        {
            Assert.IsTrue(
                root.value!.TryGetValue(schemaKey, out string childId),
                $"Constructed row has no '{schemaKey}'. Keys: {string.Join(",", root.value.Keys)}");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out ArrayMemberValue? child));
            return child!.value;
        }

        private static Dictionary<string, string>? ReadObject(
            NeoClient client,
            ObjectMemberValue root,
            string schemaKey)
        {
            Assert.IsTrue(
                root.value!.TryGetValue(schemaKey, out string childId),
                $"Constructed row has no '{schemaKey}'. Keys: {string.Join(",", root.value.Keys)}");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out ObjectMemberValue? child));
            return child!.value;
        }

        private static string? ReadStringRow(NeoClient client, string valueId)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId,
                out StringMemberValue? row),
                $"No string row '{valueId}'.");
            return row!.value;
        }

        /// <summary>
        /// Stands in for a generated enum option wrapper: the seam only ever
        /// sees <see cref="INeoEnumOption"/>, never the option-id array the
        /// value row stores.
        /// </summary>
        private sealed class TestEnumOption : INeoEnumOption
        {
            internal TestEnumOption(string optionId)
            {
                this.optionId = optionId;
            }

            public string optionId { get; }
        }

        /// <summary>
        /// Stands in for a generated Class wrapper inside a supplied
        /// collection: the seam resolves such an entry through
        /// <see cref="INeoValueReference"/>, which is the one thing every
        /// generated wrapper is.
        /// </summary>
        private sealed class TestValueReference : INeoValueReference
        {
            internal TestValueReference(string valueId)
            {
                this.valueId = valueId;
            }

            public string? valueId { get; }
        }

        private static double? ReadNumber(
            NeoClient client,
            ObjectMemberValue root,
            string schemaKey)
        {
            Assert.IsTrue(
                root.value!.TryGetValue(schemaKey, out string childId),
                $"Constructed row has no '{schemaKey}'.");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out NumberMemberValue? child));
            return child!.value;
        }

        // -------------------------------------------------------------------
        // IR builders.
        // -------------------------------------------------------------------

        private static FunctionWithReturnType ReturnFunction(Pointer pointer)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = PartType(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
            };
        }

        private static ClassTypeInfo PartType() => ClassType("part-class");

        private static ClassTypeInfo ClassType(string classId)
        {
            return new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = classId,
            };
        }

        private static FunctionPointer PartConstructorPointer(
            string? constructorId,
            Pointer? prefix = null,
            params FunctionClassConstructorField[] fields)
        {
            var args = new List<DeclaredConstructorArgument>();
            if (prefix is not null)
            {
                args.Add(new DeclaredConstructorArgument
                {
                    name = "Prefix",
                    valuePointer = prefix,
                });
            }
            return DeclaredConstructorPointer(
                PartType(),
                constructorId,
                args.ToArray(),
                fields);
        }

        private static FunctionPointer SubConstructorPointer(Pointer suffix)
        {
            return DeclaredConstructorPointer(
                ClassType("sub-class"),
                "ctor-sub",
                new[]
                {
                    new DeclaredConstructorArgument
                    {
                        name = "Suffix",
                        valuePointer = suffix,
                    },
                },
                Array.Empty<FunctionClassConstructorField>());
        }

        private static FunctionPointer BoomConstructorPointer(
            params FunctionClassConstructorField[] fields)
        {
            return DeclaredConstructorPointer(
                ClassType("boom-class"),
                null,
                Array.Empty<DeclaredConstructorArgument>(),
                fields);
        }

        private static FunctionPointer CycleConstructorPointer()
        {
            return DeclaredConstructorPointer(
                ClassType("cycle-class"),
                null,
                Array.Empty<DeclaredConstructorArgument>(),
                Array.Empty<FunctionClassConstructorField>());
        }

        private static FunctionPointer DeclaredConstructorPointer(
            ClassTypeInfo schemaClassInfo,
            string? constructorId,
            DeclaredConstructorArgument[] args,
            FunctionClassConstructorField[] fields)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DeclaredConstructorFunction
                {
                    type = FunctionKind.DeclaredConstructor,
                    info = new DeclaredConstructorInfo
                    {
                        schemaClassInfo = schemaClassInfo,
                        constructorId = constructorId,
                        args = args,
                        fields = fields,
                    },
                },
            };
        }

        private static FunctionClassConstructorField LabelField(Pointer value)
        {
            return new FunctionClassConstructorField
            {
                schemaKey = "Label",
                memberId = "part-label",
                valuePointer = value,
            };
        }

        private static FunctionClassConstructorField TagField(Pointer value)
        {
            return new FunctionClassConstructorField
            {
                schemaKey = "Tag",
                memberId = "part-tag",
                valuePointer = value,
            };
        }

        // -------------------------------------------------------------------
        // Overload-signature builders (§6.1.1).
        // -------------------------------------------------------------------

        private static FunctionArgumentTypeInfo ListArgument(
            string name,
            MemberKind entryKind)
        {
            return new FunctionArgumentTypeInfo
            {
                name = name,
                type = MemberKind.List,
                required = true,
                entryTypeInfo = new PrimitiveTypeInfo
                {
                    type = entryKind,
                    required = true,
                },
            };
        }

        private static FunctionArgumentTypeInfo DictionaryArgument(
            string name,
            TypeInfo entryTypeInfo)
        {
            return new FunctionArgumentTypeInfo
            {
                name = name,
                type = MemberKind.Dictionary,
                required = true,
                entryTypeInfo = entryTypeInfo,
            };
        }

        private static FunctionArgumentTypeInfo ClosedGenericArgument(
            string name,
            MemberKind typeArgumentKind)
        {
            return new FunctionArgumentTypeInfo
            {
                name = name,
                type = MemberKind.Class,
                required = true,
                classId = "part-class",
                typeArguments = new Dictionary<string, TypeInfo>
                {
                    ["T"] = new PrimitiveTypeInfo
                    {
                        type = typeArgumentKind,
                        required = true,
                    },
                },
            };
        }

        /// <summary>
        /// Registers one more constructor on <c>part-class</c> whose body just
        /// writes a constant, so the test's only variable is the parameter's
        /// declared type.
        /// </summary>
        private static void AddOverload(
            ProjectData data,
            string constructorId,
            string parameterName,
            FunctionArgumentTypeInfo argument)
        {
            var record = new ConstructorRecord
            {
                id = constructorId,
                projectId = ProjectId,
                classId = "part-class",
                argumentTypes = new[] { argument },
                code = "// hand-built",
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        EnvelopeParameter("__this__", PartType()),
                        EnvelopeParameter("__root__", ClassType("root-class")),
                        EnvelopeParameter("__arg_0__", argument),
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        LabelAssignment(StringPointer(parameterName)),
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.constructors[record.id] = record;
            var ids = new List<string>(
                data.classes["part-class"].constructorIds
                    ?? Array.Empty<string>())
            {
                record.id,
            };
            data.classes["part-class"].constructorIds = ids.ToArray();
        }

        private static ValuePointer StringPointer(string value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        private static ValuePointer NullPointer()
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = false,
                    },
                    value = JValue.CreateNull(),
                },
            };
        }

        private static VariablePointer ArgumentPointer(int index)
        {
            return new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = $"__arg_{index}__",
            };
        }

        private static VariablePointer ThisPointer()
        {
            return new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = "__this__",
            };
        }

        private static KeyOfPointer ThisFieldPointer(string schemaKey)
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = ThisPointer(),
                    key = StringPointer(schemaKey),
                },
            };
        }

        private static AssignInstruction ThisFieldAssignment(
            string schemaKey,
            Pointer value)
        {
            return new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = ThisFieldPointer(schemaKey),
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    },
                    writability = WritabilityKind.Session,
                },
                operatorValue = "=",
                pointer = value,
            };
        }

        private static AssignInstruction LabelAssignment(Pointer value) =>
            ThisFieldAssignment("Label", value);

        /// <summary>
        /// A compiled getter with no parameters of its own — the shape an
        /// <c>init</c> body has, since an initializer reads no instance.
        /// </summary>
        private static FunctionWithReturnType InitializerGetter(
            TypeInfo returnType,
            params Instruction[] instructions)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = returnType,
                instructions = instructions,
            };
        }

        private static FunctionWithReturnType ConstructorAction(
            int argumentCount,
            params Instruction[] instructions)
        {
            var parameters = new List<Variable>(argumentCount + 2)
            {
                EnvelopeParameter("__this__", PartType()),
                EnvelopeParameter("__root__", ClassType("root-class")),
            };
            for (int i = 0; i < argumentCount; i++)
            {
                parameters.Add(EnvelopeParameter(
                    $"__arg_{i}__",
                    new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    }));
            }
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = parameters.ToArray(),
                // A constructor body is void; `this` is the product.
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
                instructions = instructions,
            };
        }

        /// <summary>
        /// A compiled base-clause getter — an argument expression or an
        /// initializer-block entry. Both run in the DECLARING constructor's
        /// parameter envelope, so the parameter list mirrors that constructor's
        /// rather than the base's.
        /// </summary>
        private static FunctionWithReturnType BaseClauseGetter(
            string classId,
            int argumentCount,
            Pointer result)
        {
            var parameters = new List<Variable>(argumentCount + 2)
            {
                EnvelopeParameter("__this__", ClassType(classId)),
                EnvelopeParameter("__root__", ClassType("root-class")),
            };
            for (int i = 0; i < argumentCount; i++)
            {
                parameters.Add(EnvelopeParameter(
                    $"__arg_{i}__",
                    new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    }));
            }
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = parameters.ToArray(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = result,
                    },
                },
            };
        }

        private static Variable EnvelopeParameter(string id, TypeInfo typeInfo)
        {
            return new Variable
            {
                id = id,
                typeInfo = typeInfo,
                pointer = new VariablePointer
                {
                    type = PointerKind.Variable,
                    variableId = id,
                },
            };
        }

        private static FunctionArgumentTypeInfo StringArgument(string name)
        {
            return new FunctionArgumentTypeInfo
            {
                name = name,
                type = MemberKind.String,
                required = true,
            };
        }

        private static ConstructorRecord ConstructorFor(
            string id,
            string classId,
            FunctionArgumentTypeInfo[] argumentTypes,
            params Instruction[] instructions)
        {
            return new ConstructorRecord
            {
                id = id,
                projectId = ProjectId,
                classId = classId,
                argumentTypes = argumentTypes,
                code = "// hand-built",
                action = ConstructorAction(argumentTypes.Length, instructions),
                createdAt = "x",
                updatedAt = "x",
            };
        }

        // -------------------------------------------------------------------
        // Schema.
        // -------------------------------------------------------------------

        private static NeoClient BuildClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildProjectData());
        }

        private static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var partClass = new NeoSchemaClass
            {
                id = "part-class",
                projectId = ProjectId,
                name = "Part",
                schema = new Dictionary<string, string>
                {
                    ["Label"] = "part-label",
                    ["Tag"] = "part-tag",
                    ["Level"] = "part-level",
                },
                constructorIds = new[] { "ctor-part", "ctor-part-throw" },
            };
            var subClass = new NeoSchemaClass
            {
                id = "sub-class",
                projectId = ProjectId,
                name = "Sub",
                extendsClassId = partClass.id,
                schema = new Dictionary<string, string>
                {
                    ["Extra"] = "sub-extra",
                },
                constructorIds = new[] { "ctor-sub", "ctor-sub-reads-this" },
            };
            var boomClass = new NeoSchemaClass
            {
                id = "boom-class",
                projectId = ProjectId,
                name = "Boom",
                schema = new Dictionary<string, string>
                {
                    ["Bad"] = "boom-bad",
                },
            };
            var cycleClass = new NeoSchemaClass
            {
                id = "cycle-class",
                projectId = ProjectId,
                name = "Cycle",
                schema = new Dictionary<string, string>
                {
                    ["Self"] = "cycle-self",
                },
            };
            // P49 §1.1 — `class Gear(string Bar)`. The parameter list is on the
            // header, so the constructor record is reached through
            // `requiredConstructorId` and `constructorIds` stays empty: a
            // required constructor is the class's only way in (§1.3).
            var gearClass = new NeoSchemaClass
            {
                id = "gear-class",
                projectId = ProjectId,
                name = "Gear",
                schema = new Dictionary<string, string>
                {
                    ["Label"] = "gear-label",
                    ["Tag"] = "gear-tag",
                    // P49 §4.4 — the optional members a generated constructor
                    // appends after its declared parameters. Every one of them
                    // is a kind whose stored payload is NOT the value the
                    // caller hands over, which is what makes the seam's
                    // marshalling observable.
                    ["Parts"] = "gear-parts",
                    ["Mode"] = "gear-mode",
                    ["Slots"] = "gear-slots",
                    ["Links"] = "gear-links",
                },
                requiredConstructorId = "ctor-gear",
            };
            var cogClass = new NeoSchemaClass
            {
                id = "cog-class",
                projectId = ProjectId,
                name = "Cog",
                extendsClassId = gearClass.id,
                schema = new Dictionary<string, string>(),
                requiredConstructorId = "ctor-cog",
            };

            var rootAssets = RootMember("root-assets", "Assets", "immutable", "value-assets");
            var rootSave = RootMember("root-save", "Save", "save", "value-save");
            var rootSession = RootMember("root-session", "Session", "session", "value-session");

            var partLabel = new StringMember
            {
                id = "part-label",
                projectId = ProjectId,
                name = "Label",
                kind = MemberKind.String,
                required = true,
                localizable = false,
                defaultValue = new StringMemberValueBase { value = "base" },
            };
            // P43 §8's open decision, resolved: an OPTIONAL member with an
            // initializer materializes at construction.
            var partTag = new StringMember
            {
                id = "part-tag",
                projectId = ProjectId,
                name = "Tag",
                kind = MemberKind.String,
                required = false,
                localizable = false,
                defaultValue = new StringMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "InitTag()",
                        compiled = InitializerGetter(
                            new PrimitiveTypeInfo
                            {
                                type = MemberKind.String,
                                required = true,
                            },
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = StringPointer("init-tag"),
                            }),
                    },
                },
            };
            var partLevel = new IntMember
            {
                id = "part-level",
                projectId = ProjectId,
                name = "Level",
                kind = MemberKind.Int,
                required = true,
                defaultValue = new NumberMemberValueBase { value = 1 },
            };
            var subExtra = new StringMember
            {
                id = "sub-extra",
                projectId = ProjectId,
                name = "Extra",
                kind = MemberKind.String,
                required = true,
                localizable = false,
                defaultValue = new StringMemberValueBase { value = "extra" },
            };
            var boomBad = new StringMember
            {
                id = "boom-bad",
                projectId = ProjectId,
                name = "Bad",
                kind = MemberKind.String,
                required = true,
                localizable = false,
                defaultValue = new StringMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "Explode()",
                        compiled = InitializerGetter(
                            new PrimitiveTypeInfo
                            {
                                type = MemberKind.String,
                                required = true,
                            },
                            new ThrowInstruction
                            {
                                type = InstructionKind.Throw,
                                pointer = StringPointer("initializer exploded"),
                            }),
                    },
                },
            };
            var gearLabel = new StringMember
            {
                id = "gear-label",
                projectId = ProjectId,
                name = "Label",
                kind = MemberKind.String,
                required = true,
                localizable = false,
                defaultValue = new StringMemberValueBase { value = "base" },
            };
            var gearTag = new StringMember
            {
                id = "gear-tag",
                projectId = ProjectId,
                name = "Tag",
                kind = MemberKind.String,
                required = false,
                localizable = false,
                defaultValue = new StringMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "InitTag()",
                        compiled = InitializerGetter(
                            new PrimitiveTypeInfo
                            {
                                type = MemberKind.String,
                                required = true,
                            },
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = StringPointer("init-tag"),
                            }),
                    },
                },
            };
            // Optional, default-less collection/selection members: absent
            // unless the call site supplies them, so a test that reads one back
            // is reading exactly what the seam marshalled.
            var gearPartEntry = new StringMember
            {
                id = "gear-part-entry",
                projectId = ProjectId,
                name = "Part",
                kind = MemberKind.String,
                required = true,
                localizable = false,
            };
            var gearParts = new ListMember
            {
                id = "gear-parts",
                projectId = ProjectId,
                name = "Parts",
                kind = MemberKind.List,
                required = false,
                entryMemberId = gearPartEntry.id,
            };
            var gearMode = new EnumMember
            {
                id = "gear-mode",
                projectId = ProjectId,
                name = "Mode",
                kind = MemberKind.Enum,
                required = false,
                enumId = "gear-mode-enum",
                multiselect = false,
            };
            var gearSlotEntry = new StringMember
            {
                id = "gear-slot-entry",
                projectId = ProjectId,
                name = "Slot",
                kind = MemberKind.String,
                required = true,
                localizable = false,
            };
            var gearSlots = new DictionaryMember
            {
                id = "gear-slots",
                projectId = ProjectId,
                name = "Slots",
                kind = MemberKind.Dictionary,
                required = false,
                entryMemberId = gearSlotEntry.id,
            };
            var gearLinkEntry = new ClassMember
            {
                id = "gear-link-entry",
                projectId = ProjectId,
                name = "Link",
                kind = MemberKind.Class,
                required = true,
                classId = partClass.id,
            };
            var gearLinks = new ListMember
            {
                id = "gear-links",
                projectId = ProjectId,
                name = "Links",
                kind = MemberKind.List,
                required = false,
                entryMemberId = gearLinkEntry.id,
            };
            var cycleSelf = new ClassMember
            {
                id = "cycle-self",
                projectId = ProjectId,
                name = "Self",
                kind = MemberKind.Class,
                required = true,
                classId = cycleClass.id,
                defaultValue = new ObjectMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "new()",
                        compiled = InitializerGetter(
                            ClassType("cycle-class"),
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = CycleConstructorPointer(),
                            }),
                    },
                },
            };

            ConstructorRecord partConstructor = ConstructorFor(
                "ctor-part",
                partClass.id,
                new[] { StringArgument("Prefix") },
                LabelAssignment(ArgumentPointer(0)));

            // `: base(Prefix: Suffix)` — the derived body then copies what the
            // base wrote, which is what makes the ordering observable.
            ConstructorRecord subConstructor = ConstructorFor(
                "ctor-sub",
                subClass.id,
                new[] { StringArgument("Suffix") },
                ThisFieldAssignment("Extra", ThisFieldPointer("Label")));
            subConstructor.baseArguments = new[]
            {
                new ConstructorBaseArgument { name = "Prefix", code = "Suffix" },
            };
            subConstructor.compiledBaseArguments = new[]
            {
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        EnvelopeParameter("__this__", ClassType("sub-class")),
                        EnvelopeParameter("__root__", ClassType("root-class")),
                        EnvelopeParameter(
                            "__arg_0__",
                            new PrimitiveTypeInfo
                            {
                                type = MemberKind.String,
                                required = true,
                            }),
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = ArgumentPointer(0),
                        },
                    },
                },
            };

            // A body that throws outright — the observable seam for two
            // separate guarantees: the call site's initializer block has NOT
            // run yet when the body fails, and the rows step 1 already
            // published are reclaimed rather than stranded.
            ConstructorRecord throwingConstructor = ConstructorFor(
                "ctor-part-throw",
                partClass.id,
                Array.Empty<FunctionArgumentTypeInfo>(),
                new ThrowInstruction
                {
                    type = InstructionKind.Throw,
                    pointer = StringPointer("body boom"),
                });

            // `Sub() : base(Prefix: this.Tag)` — a base argument reading the
            // instance the member initializers just built.
            ConstructorRecord subReadsThisConstructor = ConstructorFor(
                "ctor-sub-reads-this",
                subClass.id,
                Array.Empty<FunctionArgumentTypeInfo>(),
                ThisFieldAssignment("Extra", StringPointer("extra")));
            subReadsThisConstructor.baseArguments = new[]
            {
                new ConstructorBaseArgument { name = "Prefix", code = "this.Tag" },
            };
            subReadsThisConstructor.compiledBaseArguments = new[]
            {
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        EnvelopeParameter("__this__", ClassType("sub-class")),
                        EnvelopeParameter("__root__", ClassType("root-class")),
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = ThisFieldPointer("Tag"),
                        },
                    },
                },
            };

            // `class Gear(string Bar) { … }` — the header parameter list is the
            // whole construction contract, and the body assigns from it.
            ConstructorRecord gearConstructor = ConstructorFor(
                "ctor-gear",
                gearClass.id,
                new[] { StringArgument("Bar") },
                LabelAssignment(ArgumentPointer(0)));

            // `class Cog(string Seed) : Gear(Seed) { Tag = "from-base-clause" }`
            // — a base clause carrying both halves of a construction
            // expression. The body is empty on purpose: everything the tests
            // read back then comes from the clause and the call site alone.
            ConstructorRecord cogConstructor = ConstructorFor(
                "ctor-cog",
                cogClass.id,
                new[] { StringArgument("Seed") });
            cogConstructor.baseArguments = new[]
            {
                new ConstructorBaseArgument { name = "Bar", code = "Seed" },
            };
            cogConstructor.compiledBaseArguments = new[]
            {
                BaseClauseGetter(cogClass.id, 1, ArgumentPointer(0)),
            };
            cogConstructor.baseInitializerFields = new[]
            {
                new ConstructorBaseInitializerField
                {
                    name = "Tag",
                    code = "\"from-base-clause\"",
                },
            };
            cogConstructor.compiledBaseInitializerFields = new[]
            {
                BaseClauseGetter(
                    cogClass.id,
                    1,
                    StringPointer("from-base-clause")),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Constructor Tests",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    [partLabel.id] = partLabel,
                    [partTag.id] = partTag,
                    [partLevel.id] = partLevel,
                    [subExtra.id] = subExtra,
                    [boomBad.id] = boomBad,
                    [cycleSelf.id] = cycleSelf,
                    [gearLabel.id] = gearLabel,
                    [gearTag.id] = gearTag,
                    [gearParts.id] = gearParts,
                    [gearPartEntry.id] = gearPartEntry,
                    [gearMode.id] = gearMode,
                    [gearSlots.id] = gearSlots,
                    [gearSlotEntry.id] = gearSlotEntry,
                    [gearLinks.id] = gearLinks,
                    [gearLinkEntry.id] = gearLinkEntry,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", rootClass.id),
                    ["value-save"] = ObjectValue("value-save", rootClass.id),
                    ["value-session"] = ObjectValue("value-session", rootClass.id),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [partClass.id] = partClass,
                    [subClass.id] = subClass,
                    [boomClass.id] = boomClass,
                    [cycleClass.id] = cycleClass,
                    [gearClass.id] = gearClass,
                    [cogClass.id] = cogClass,
                },
                constructors = new Dictionary<string, ConstructorRecord>
                {
                    [partConstructor.id] = partConstructor,
                    [subConstructor.id] = subConstructor,
                    [throwingConstructor.id] = throwingConstructor,
                    [subReadsThisConstructor.id] = subReadsThisConstructor,
                    [gearConstructor.id] = gearConstructor,
                    [cogConstructor.id] = cogConstructor,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>
                {
                    ["gear-mode-enum"] = new NeoCompose.Runtime.Json.Enum
                    {
                        id = "gear-mode-enum",
                        projectId = ProjectId,
                        name = "GearMode",
                        options = new Dictionary<string, EnumOption>
                        {
                            ["option-fast"] = new EnumOption { text = "Fast" },
                            ["option-slow"] = new EnumOption { text = "Slow" },
                        },
                        optionKeyOrder = new List<string>
                        {
                            "option-fast",
                            "option-slow",
                        },
                    },
                },
            };
        }

        private static ClassMember RootMember(
            string id,
            string name,
            string storage,
            string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                required = true,
                classId = "root-class",
                storage = storage,
                valueId = valueId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
