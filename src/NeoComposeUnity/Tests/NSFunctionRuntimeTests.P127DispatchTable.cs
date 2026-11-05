// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        // Pinned by the .NET 10 programs in r22-cs: gaps, gaps2, nested and branches.
        // The same table exercises getter, method, generic method, setter and static dispatch.
        private static IEnumerable<TestCaseData> DefaultWalkCases()
        {
            var rows = new (string Shape, string Body, string Frame)[]
            {
                ("ExactMiss", "base-1", ""), ("SameLevelMiss", "receiver-1", ""),
                ("OverrideMiss", "base-1", ""), ("BelowMiss", "base-1", ""),
                ("VariantBeforeExact", "receiver-3", ""), ("ExactBeforeVariant", "receiver-3", ""),
                ("SameLevelExact", "receiver-2", ""),
                ("LaterOverride", "K-body", "D11"), ("EarlierOverride", "K-body", "D11"),
                ("ContraLater", "K-body", "Ba"),
                ("Nested", "L-body", "D11"), ("NestedBelow", "L-body", "D11"),
                ("ContraNested", "L-body", "Ba"), ("UnrelatedFirst", "Q-body", "D1"),
                ("ExactNested", "K-body", "D1"),
                ("DuplicateLevel", "L-body", "D2"),
                ("ReplaceSeveral", "Z-body", "D11"), ("DropAncestor", "Z-body", "D11"),
                ("SameDefinition", "K-body", "D1"), ("SameDefinitionReverse", "K-body", "D11"),
                ("ExactCovariantOwners", "A-body", "D1"),
                ("ExactAmbiguity", "ambiguous", ""), ("Reabstracted", "missing", ""),
                // Round 22 follow-up: generic classes merge the target, while
                // each body's U identifies the winning constructed holder.
                ("HolderExactCast", "A-body", "D1"), ("HolderReversed", "A-body", "D1"),
                ("HolderUnrelated", "ambiguous", ""), ("HolderCross", "LA-body", "D1"),
                ("HolderCrossR", "LA-body", "D1"), ("HolderCrossN", "ambiguous", ""),
                ("HolderThree", "A-body", "D11"), ("HolderInvariant", "ambiguous", ""),
                ("HolderExactCastD1", "A-body", "D1")
            };
            foreach (var row in rows)
                foreach (string operation in new[] { "get", "call", "generic", "set", "static" })
                {
                    string form = operation == "static" && row.Shape.StartsWith("Holder", StringComparison.Ordinal) ? "static-call" : operation;
                    yield return new TestCaseData(row.Shape, form, row.Body, row.Frame)
                        .SetName($"P127Interface_DefaultWalk_{row.Shape}_{form}");
                }
        }

        [TestCaseSource(nameof(DefaultWalkCases))]
        public void P127Interface_DefaultWalk(string shape, string operation, string expectedBody, string expectedFrame)
        {
            bool function = operation is "call" or "generic" or "static-call";
            bool isStatic = operation is "static" or "static-call";
            bool contra = shape.StartsWith("Contra", StringComparison.Ordinal);
            var records = new List<NeoCompose.Runtime.Json.Member>();
            var classes = new Dictionary<string, NeoSchemaClass>();
            var interfaces = new Dictionary<string, Interface>();
            TypeBinding Type(string id) => TestTypeBindings.Class(id);
            TypeBinding Param(string id, string name = "T") => TestTypeBindings.Open(id, id + "-" + name);
            InterfaceConstruction Listing(string id, TypeBinding argument) => new()
            {
                interfaceId = id,
                arguments = new()
                {
                    [id + "-T"] = argument
                }
            };
            NeoCompose.Runtime.Json.Member Body(string id, int value)
            {
                var action = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(value)));
                NeoCompose.Runtime.Json.Member body = function
                    ? ScriptFunction(id, "V", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(), action)
                    : new NSPropertyMember
                    {
                        id = id,
                        name = "V",
                        projectId = ProjectId,
                        kind = MemberKind.NSProperty,
                        Accessors = operation == "set" ? NeoPropertyAccessorsKind.GetSet : NeoPropertyAccessorsKind.Get,
                        returnTypeInfo = IntType(),
                        code = "return " + value + ";",
                        getter = action,
                        setter = operation == "set" ? Action(new PrimitiveTypeInfo { type = MemberKind.Null, required = true }, Array.Empty<FunctionArgumentTypeInfo>()) : null
                    };
                if (operation == "generic")
                    ((NSFunctionMember)body).genericParams = new[] { new FunctionGenericParam { name = "U" } };
                if (isStatic)
                    body.Modifier = NeoMemberModifierKind.Static;
                records.Add(body);
                return body;
            }
            void Interface(string id, int value, params string[] parents)
            {
                var body = Body(id + "-body", value);
                var slot = new InterfaceMember
                {
                    id = id + "-slot",
                    kind = function ? NeoInterfaceMemberKind.Function : NeoInterfaceMemberKind.Property,
                    typeInfo = function ? null : IntType(),
                    returnTypeInfo = function ? IntType() : null,
                    argumentTypes = function ? new() : null,
                    implementationMemberId = body.id,
                    IsStatic = isStatic,
                    Accessors = operation == "set" ? NeoPropertyAccessorsKind.GetSet : NeoPropertyAccessorsKind.Get,
                    genericParams = operation == "generic" ? new[] { new FunctionGenericParam { name = "U" } } : null
                };
                if (id != "I")
                    slot.explicitImplementation = new()
                    {
                        interfaceId = "I",
                        interfaceMemberId = "I-slot",
                        arguments = Listing("I", Param(id)).arguments
                    };
                interfaces[id] = new()
                {
                    id = id,
                    name = id,
                    projectId = ProjectId,
                    genericParams = new() { new() { id = id + "-T", name = "T", variance = contra ? NeoGenericParamVarianceKind.In : NeoGenericParamVarianceKind.Out } },
                    extendsInterfaces = new(),
                    members = new()
                    {
                        ["V"] = slot
                    }
                };
                foreach (string parent in parents)
                    interfaces[id].extendsInterfaces.Add(Listing(parent, Param(id)));
            }
            Interface("I", 4);
            Interface("K", 6, "I");
            Interface("L", 7, shape is "Nested" or "NestedBelow" or "ContraNested" or "ExactNested" ? "K" : "I");
            Interface("Q", 9, "I");
            Interface("Z", 8, "K", "L");
            Interface("A", 6, "I");
            interfaces["A"].genericParams!.Add(new()
            {
                id = "A-U",
                name = "U",
                variance = NeoGenericParamVarianceKind.Out
            });
            Interface("LA", 7, "A");
            interfaces["LA"].genericParams!.Add(new()
            {
                id = "LA-U",
                name = "U",
                variance = NeoGenericParamVarianceKind.Out
            });
            interfaces["LA"].extendsInterfaces![0].arguments!["A-U"] = Param("LA", "U");
            if (shape == "HolderInvariant")
                interfaces["A"].genericParams![1].variance = null;

            NeoSchemaClass Class(string id, string? parent = null, params InterfaceConstruction[] listings)
            {
                var result = ReceiverClass();
                result.id = id;
                result.name = id;
                result.extendsClassId = parent;
                result.implementsInterfaces = new(listings);
                classes[id] = result;
                return result;
            }
            void Explicit(string owner, string argument, int value)
            {
                var body = Body(owner + "-" + value, value);
                body.Access = NeoMemberAccessKind.Private;
                body.Modifier = isStatic ? NeoMemberModifierKind.Static : NeoMemberModifierKind.Sealed;
                body.explicitImplementation = new()
                {
                    interfaceId = "I",
                    interfaceMemberId = "I-slot",
                    arguments = Listing("I", Type(argument)).arguments
                };
                classes[owner].schema[body.id] = body.id;
            }
            Class("Top");
            Class("Ba", "Top");
            Class("D1", "Ba");
            Class("D11", "D1");
            Class("D2", "Ba");
            Class("base");
            var receiver = Class("receiver", "base");
            var b = classes["base"].implementsInterfaces!;
            var r = receiver.implementsInterfaces!;
            void Add(List<InterfaceConstruction> list, string id, string arg) => list.Add(Listing(id, Type(arg)));
            string view = "Ba";
            switch (shape)
            {
                case "ExactMiss":
                case "OverrideMiss":
                case "BelowMiss":
                    Add(b, "I", "D1");
                    Explicit("base", "D1", 1);
                    Add(r, shape == "OverrideMiss" ? "K" : "I", "Ba");
                    if (shape == "BelowMiss")
                        receiver = Class("below", "receiver");
                    break;
                case "SameLevelMiss":
                    Add(r, "I", "Ba");
                    Add(r, "I", "D1");
                    Explicit("receiver", "D1", 1);
                    break;
                case "VariantBeforeExact":
                    Add(b, "I", "Ba");
                    Explicit("base", "Ba", 2);
                    Add(r, "I", "D1");
                    Explicit("receiver", "D1", 3);
                    break;
                case "ExactBeforeVariant":
                    Add(b, "I", "D1");
                    Explicit("base", "D1", 2);
                    Add(r, "I", "Ba");
                    Explicit("receiver", "Ba", 3);
                    break;
                case "SameLevelExact":
                    Add(b, "I", "Ba");
                    Explicit("base", "Ba", 1);
                    Add(r, "I", "Ba");
                    Add(r, "I", "D1");
                    Explicit("receiver", "D1", 2);
                    break;
                case "LaterOverride":
                    Add(r, "I", "D1");
                    Add(r, "K", "D11");
                    break;
                case "EarlierOverride":
                    Add(r, "K", "D11");
                    Add(r, "I", "D1");
                    break;
                case "ContraLater":
                    Add(r, "I", "D1");
                    Add(r, "K", "Ba");
                    view = "D11";
                    break;
                case "Nested":
                case "ExactNested":
                    Add(r, "K", "D1");
                    Add(r, "L", "D11");
                    if (shape == "ExactNested")
                        view = "D1";
                    break;
                case "NestedBelow":
                    Add(b, "K", "D1");
                    Add(r, "L", "D11");
                    break;
                case "ContraNested":
                    Add(r, "K", "D1");
                    Add(r, "L", "Ba");
                    view = "D11";
                    break;
                case "UnrelatedFirst":
                    Add(r, "Q", "D1");
                    Add(r, "L", "D11");
                    break;
                case "DuplicateLevel":
                    Add(b, "K", "D1");
                    Add(r, "K", "D1");
                    Add(r, "L", "D2");
                    break;
                case "ReplaceSeveral":
                    Add(r, "K", "D1");
                    Add(r, "Q", "D2");
                    Add(r, "L", "Ba");
                    Add(r, "Z", "D11");
                    view = "Top";
                    break;
                case "DropAncestor":
                    Add(r, "Z", "D11");
                    Add(r, "K", "D1");
                    break;
                case "SameDefinition":
                    Add(r, "K", "D1");
                    Add(r, "K", "D11");
                    break;
                case "SameDefinitionReverse":
                    Add(r, "K", "D11");
                    Add(r, "K", "D1");
                    break;
                case "ExactCovariantOwners":
                    var first = Listing("A", Type("Ba"));
                    first.arguments!["A-U"] = Type("D1");
                    b.Add(first);
                    var second = Listing("A", Type("Ba"));
                    second.arguments!["A-U"] = Type("Ba");
                    r.Add(second);
                    break;
                case "HolderExactCast":
                case "HolderReversed":
                case "HolderUnrelated":
                case "HolderCross":
                case "HolderCrossR":
                case "HolderCrossN":
                case "HolderThree":
                case "HolderInvariant":
                case "HolderExactCastD1":
                    void Generic(NeoSchemaClass owner, params string[] names)
                    {
                        owner.genericParams = new();
                        foreach (string name in names)
                            owner.genericParams.Add(new()
                            {
                                id = owner.id + "-" + name,
                                name = name
                            });
                        if (owner.extendsClassId is string parent)
                        {
                            owner.extendsGenericBindings = new();
                            foreach (var parameter in classes[parent].genericParams!)
                                owner.extendsGenericBindings[parameter.id] = Param(owner.id, parameter.name);
                        }
                    }
                    void Holder(NeoSchemaClass owner, string id, string parameter, string argument)
                    {
                        var construction = Listing(id, Param(owner.id, parameter));
                        construction.arguments![id + "-U"] = Type(argument);
                        owner.implementsInterfaces!.Add(construction);
                    }
                    Generic(classes["base"], "T");
                    Generic(receiver, "T", "U");
                    string baseHolder = shape == "HolderCrossR" ? "LA" : "A";
                    string baseArgument = shape is "HolderReversed" or "HolderCross" ? "Ba" : "D1";
                    Holder(classes["base"], baseHolder, "T", baseArgument);
                    if (shape == "HolderThree")
                    {
                        var middle = Class("middle", "base");
                        Generic(middle, "T", "U");
                        Holder(middle, "A", "U", "Ba");
                        receiver.extendsClassId = middle.id;
                        Generic(receiver, "T", "U", "W");
                        Holder(receiver, "A", "W", "D11");
                    }
                    else
                    {
                        string ownHolder = shape is "HolderCross" or "HolderCrossN" ? "LA" : "A";
                        string ownArgument = shape is "HolderReversed" or "HolderCross" ? "D1" : shape == "HolderUnrelated" ? "D2" : "Ba";
                        Holder(receiver, ownHolder, "U", ownArgument);
                    }
                    if (shape == "HolderExactCastD1")
                        view = "D1";
                    break;
                case "ExactAmbiguity":
                    Add(r, "K", "Ba");
                    Add(r, "L", "Ba");
                    break;
                case "Reabstracted":
                    Add(r, "K", "Ba");
                    interfaces["K"].members["V"].DeclaredModifier = NeoMemberModifierKind.Abstract;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
            var row = ObjectValue("receiver-value", receiver.id);
            if (receiver.genericParams is not null)
            {
                row.genericBindings = new();
                foreach (var parameter in receiver.genericParams)
                    row.genericBindings[parameter.id] = Type(shape == "HolderExactCastD1" && parameter.name == "T" ? "D1" : "Ba");
            }
            var receiverType = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: receiver.id), row.genericBindings);
            using var client = BuildClient(records.ToArray(), receiver, new List<NeoSchemaClass>(classes.Values).ToArray(), new[] { row }, interfaces);
            var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "I"), Listing("I", Type(view)).arguments);
            string accessor = function ? "call" : operation == "set" ? "set" : "get";
            foreach (var dispatch in new[] { NeoInterfaceDispatch.Of(client), NeoInterfaceDispatch.ForSchemaValidation(client) })
            {
                NeoInterfaceDispatch.Implementation Run() => dispatch.Resolve(receiverType, requested, "I-slot", accessor);
                if (expectedBody is "ambiguous" or "missing")
                {
                    Assert.That(Assert.Throws<NSGetterRuntimeError>(() => Run())!.Message,
                        Does.Contain(expectedBody == "ambiguous" ? "No most specific implementation" : "does not implement interface member"));
                    continue;
                }
                var answer = Run();
                Assert.That(answer.MemberId, Is.EqualTo(expectedBody));
                var frame = answer.InterfaceConstruction ?? answer.StaticConstruction;
                if (expectedFrame.Length != 0)
                    Assert.That(frame!.Bindings![frame.Head!.InterfaceId + (frame.Bindings.ContainsKey(frame.Head.InterfaceId + "-U") ? "-U" : "-T")], Is.EqualTo(Type(expectedFrame)));
                Assert.That(Run(), Is.SameAs(answer));
            }
        }
    }
}
