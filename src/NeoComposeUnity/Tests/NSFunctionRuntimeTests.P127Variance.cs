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
        [TestCase("V1", 4)]
        [TestCase("V2", 6)]
        [TestCase("V3", 6)]
        [TestCase("V4", 6)]
        [TestCase("T2", 4)]
        [TestCase("T3", 2)]
        [TestCase("T3k", 2)]
        [TestCase("T5", 6)]
        [TestCase("T6", 7)]
        [TestCase("T7", 6)]
        [TestCase("T8", 6)]
        [TestCase("T9", 6)]
        [TestCase("T10", 7)]
        [TestCase("T10", 7, true)]
        [TestCase("T5", 6, true)]
        [TestCase("StaticCK", 6, true)]
        [TestCase("V1", 4, true)]
        [TestCase("T10", 7, true, true)]
        [TestCase("W1", 1)]
        [TestCase("W2", 2)]
        [TestCase("W3", 2)]
        [TestCase("W4", 1)]
        [TestCase("W5", 2)]
        [TestCase("W6", 4)]
        public void P127Interface_VariantDefaultFollowsClassListingOrder(string family, int expected, bool staticMember = false, bool exactView = false)
        {
            var records = new List<NeoCompose.Runtime.Json.Member>();
            var classes = new Dictionary<string, NeoSchemaClass>();
            var interfaces = new Dictionary<string, Interface>();
            TypeBinding Param(string owner, string name) => TestTypeBindings.Open(owner, owner + "-" + name);
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
                return staticMember
                    ? ScriptFunction(id, "V", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(), action)
                    : new NSPropertyMember
                    {
                        id = id,
                        name = "V",
                        projectId = ProjectId,
                        kind = MemberKind.NSProperty,
                        returnTypeInfo = IntType(),
                        code = "return " + value + ";",
                        getter = action
                    };
            }
            foreach (string id in new[] { "IV", "KV", "LV" })
            {
                var body = Body(id + "-body", id == "IV" ? 4 : id == "KV" ? 6 : 7);
                if (staticMember)
                    body.Modifier = NeoMemberModifierKind.Static;
                records.Add(body);
                var slot = new InterfaceMember
                {
                    id = id + "-slot",
                    kind = staticMember ? NeoInterfaceMemberKind.Function : NeoInterfaceMemberKind.Property,
                    typeInfo = staticMember ? null : IntType(),
                    returnTypeInfo = staticMember ? IntType() : null,
                    argumentTypes = staticMember ? new() : null,
                    implementationMemberId = body.id,
                    IsStatic = staticMember
                };
                if (id != "IV")
                    slot.explicitImplementation = new()
                    {
                        interfaceId = "IV",
                        interfaceMemberId = "IV-slot",
                        arguments = Listing("IV", Param(id, "T")).arguments
                    };
                interfaces[id] = new()
                {
                    id = id,
                    name = id,
                    projectId = ProjectId,
                    genericParams = new() { new() { id = id + "-T", name = "T", variance = NeoGenericParamVarianceKind.Out } },
                    extendsInterfaces = id == "IV" ? null : new() { Listing("IV", Param(id, "T")) },
                    members = new()
                    {
                        ["V"] = slot
                    }
                };
            }
            NeoSchemaClass Class(string id, string? parent = null, bool generic = false, bool merge = false, params InterfaceConstruction[] listings)
            {
                var owner = ReceiverClass();
                owner.id = id;
                owner.name = id;
                owner.extendsClassId = parent;
                owner.implementsInterfaces = new(listings);
                if (generic)
                {
                    owner.genericParams = new() { new() { id = id + "-T", name = "T" }, new() { id = id + "-U", name = "U" } };
                    if (parent is not null)
                        owner.extendsGenericBindings = new()
                        {
                            [parent + "-T"] = Param(id, "T"),
                            [parent + "-U"] = Param(id, merge ? "T" : "U")
                        };
                }
                classes[id] = owner;
                return owner;
            }
            void Explicit(string id, TypeBinding argument, int value)
            {
                var body = Body(id + "-explicit-" + value, value);
                body.Access = NeoMemberAccessKind.Private;
                body.Modifier = staticMember ? NeoMemberModifierKind.Static : NeoMemberModifierKind.Sealed;
                body.explicitImplementation = new()
                {
                    interfaceId = "IV",
                    interfaceMemberId = "IV-slot",
                    arguments = Listing("IV", argument).arguments
                };
                classes[id].schema[body.id] = body.id;
                records.Add(body);
            }
            Class("Ba");
            Class("D1", "Ba");
            Class("D2", "Ba");
            var d1 = TestTypeBindings.Class("D1");
            var d2 = TestTypeBindings.Class("D2");
            Class("CV1", listings: new[] { Listing("IV", d1), Listing("KV", d2) });
            Class("CV2", listings: new[] { Listing("KV", d2), Listing("IV", d1) });
            Class("BV0", generic: true, listings: new[] { Listing("IV", Param("BV0", "T")) });
            Class("MV0", "BV0", true, true, Listing("KV", Param("MV0", "U")));
            Class("BV1", generic: true, listings: new[] { Listing("KV", Param("BV1", "T")) });
            Class("MV1", "BV1", true, true, Listing("IV", Param("MV1", "U")));
            Class("BX", generic: true, listings: new[] { Listing("IV", Param("BX", "T")) });
            Explicit("BX", Param("BX", "T"), 2);
            Class("MX", "BX", true, true, Listing("IV", Param("MX", "U")));
            Class("MXK", "BX", true, true, Listing("KV", Param("MXK", "U")));
            Class("CK", listings: new[] { Listing("KV", d1), Listing("IV", d2) });
            Class("CKL", listings: new[] { Listing("KV", d1), Listing("LV", d2) });
            Class("BK", generic: true, listings: new[] { Listing("KV", Param("BK", "T")) });
            Class("MKL", "BK", true, true, Listing("LV", Param("MKL", "U")));
            Class("NV0", "MV0", true);
            var zv = Class("ZV", "MV1", true, false, Listing("KV", Param("ZV", "W")));
            zv.genericParams!.Add(new()
            {
                id = "ZV-W",
                name = "W"
            });
            Class("CX", listings: new[] { Listing("IV", d1), Listing("IV", d2) });
            Class("CX2", listings: new[] { Listing("IV", d2), Listing("IV", d1) });
            foreach (string id in new[] { "CX", "CX2" })
            {
                Explicit(id, d1, 1);
                Explicit(id, d2, 2);
            }
            Class("BY", listings: new[] { Listing("IV", d1) });
            Explicit("BY", d1, 1);
            Class("MY", "BY", listings: new[] { Listing("IV", d2) });
            Explicit("MY", d2, 2);
            Class("MYK", "BY", listings: new[] { Listing("KV", d2) });
            Class("BZ", listings: new[] { Listing("KV", d1) });
            Class("MZ", "BZ", listings: new[] { Listing("IV", d2) });
            Explicit("MZ", d2, 2);
            Class("MZ0", "BZ", listings: new[] { Listing("IV", d2) });
            string receiverId = family switch
            {
                "StaticCK" => "CK",
                "V1" => "CV1",
                "V2" => "CV2",
                "V3" or "V4" => "MV0",
                "T2" => "MV1",
                "T3" => "MX",
                "T3k" => "MXK",
                "T5" => "CKL",
                "T6" or "T10" => "MKL",
                "T7" => "NV0",
                "T8" or "T9" => "ZV",
                "W1" => "CX",
                "W2" => "CX2",
                "W3" => "MY",
                "W4" => "MYK",
                "W5" => "MZ",
                _ => "MZ0"
            };
            var receiver = classes[receiverId];
            var row = ObjectValue("receiver-value", receiverId);
            if (receiver.genericParams is not null)
            {
                row.genericBindings = new()
                {
                    [receiverId + "-T"] = family == "V4" ? d2 : d1,
                    [receiverId + "-U"] = family is "V4" or "T9" or "T10" ? d1 : d2
                };
                if (receiverId == "ZV")
                    row.genericBindings["ZV-W"] = d2;
            }
            using var client = BuildClient(records.ToArray(), receiver, new List<NeoSchemaClass>(classes.Values).ToArray(), new[] { row }, interfaces);
            if (staticMember)
            {
                var context = new NSGetterEvaluator.Context(client, null, null);
                var receiverType = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: receiverId), row.genericBindings);
                context.BindFunctionTypeArguments(new[] { NeoTypeBindings.ToTypeInfo(receiverType) });
                var pointer = new TypeParameterStaticPointer
                {
                    typeParameter = new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = "caller", index = 0 },
                    interfaceMember = new()
                    {
                        interfaceId = "IV",
                        memberId = "IV-slot",
                        arguments = new()
                        {
                            ["IV-T"] = NeoTypeBindings.ToTypeInfo(TestTypeBindings.Class(exactView ? "D1" : "Ba"))
                        }
                    },
                    operation = new() { kind = "call", args = Array.Empty<Pointer>(), callSiteId = "static-variance" }
                };
                object? Run() => NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), context);
                if (exactView)
                    Assert.That(Assert.Throws<NeoAmbiguousImplementationError>(() => Run())!.Message, Does.Contain("has competing implementations"));
                else
                    Assert.That(Run(), Is.EqualTo(expected));
                return;
            }
            var call = new CallGetterPointer
            {
                receiver = new() { kind = CallReceiverKind.Instance, pointer = new ReferencePointer { valueId = row.id } },
                interfaceMember = new() { interfaceId = "IV", memberId = "IV-slot", arguments = new() { ["IV-T"] = NeoTypeBindings.ToTypeInfo(TestTypeBindings.Class("Ba")) } }
            };
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), new NSGetterEvaluator.Context(client, null, null)), Is.EqualTo(expected));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void P127Interface_DefaultSpecificityKeepsConstructedIdentity(bool property, bool stringView)
        {
            var records = new List<NeoCompose.Runtime.Json.Member>();
            var interfaces = new Dictionary<string, Interface>();
            TypeBinding Param(string id) => TestTypeBindings.Open(id, id + "-T");
            InterfaceConstruction Listing(string id, TypeBinding argument) => new()
            {
                interfaceId = id,
                arguments = new()
                {
                    [id + "-T"] = argument
                }
            };
            foreach (string id in new[] { "I", "K", "L" })
            {
                int value = id == "I" ? 4 : id == "K" ? 6 : 7;
                var action = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(value)));
                NeoCompose.Runtime.Json.Member body = property
                    ? new NSPropertyMember { id = id + "-body", name = "V", projectId = ProjectId, kind = MemberKind.NSProperty, returnTypeInfo = IntType(), code = "return " + value + ";", getter = action }
                    : ScriptFunction(id + "-body", "V", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(), action);
                records.Add(body);
                var member = new InterfaceMember
                {
                    id = id + "-slot",
                    kind = property ? NeoInterfaceMemberKind.Property : NeoInterfaceMemberKind.Function,
                    typeInfo = property ? IntType() : null,
                    returnTypeInfo = property ? null : IntType(),
                    argumentTypes = property ? null : new(),
                    implementationMemberId = body.id
                };
                if (id != "I")
                    member.explicitImplementation = new()
                    {
                        interfaceId = "I",
                        interfaceMemberId = "I-slot",
                        arguments = Listing("I", id == "K" ? Param(id) : TestTypeBindings.Of(MemberKind.Int)).arguments
                    };
                interfaces[id] = new()
                {
                    id = id,
                    name = id,
                    projectId = ProjectId,
                    members = new()
                    {
                        ["V"] = member
                    },
                    genericParams = id == "L" ? null : new() { new() { id = id + "-T", name = "T" } },
                    extendsInterfaces = id == "I" ? null : id == "K" ? new() { Listing("I", Param(id)) }
                        : new() { Listing("K", TestTypeBindings.Of(MemberKind.String)), Listing("I", TestTypeBindings.Of(MemberKind.Int)) }
                };
            }
            var parent = ReceiverClass();
            parent.id = "B";
            parent.genericParams = new() { new() { id = "B-T", name = "T" } };
            parent.implementsInterfaces = new() { Listing("K", Param("B")) };
            var receiver = ReceiverClass();
            receiver.id = "M";
            receiver.genericParams = new() { new() { id = "M-T", name = "T" } };
            receiver.extendsClassId = "B";
            receiver.extendsGenericBindings = new()
            {
                ["B-T"] = Param("M")
            };
            receiver.implementsInterfaces = new() { new() { interfaceId = "L" } };
            var row = ObjectValue("receiver-value", receiver.id);
            row.genericBindings = new()
            {
                ["M-T"] = TestTypeBindings.Of(MemberKind.Int)
            };
            using var client = BuildClient(records.ToArray(), receiver, new[] { parent }, new[] { row }, interfaces);
            var reference = new InterfaceMemberRef { interfaceId = "I", memberId = "I-slot", arguments = new() { ["I-T"] = NeoTypeBindings.ToTypeInfo(TestTypeBindings.Of(stringView ? MemberKind.String : MemberKind.Int)) } };
            var callReceiver = new CallReceiver { kind = CallReceiverKind.Instance, pointer = new ReferencePointer { valueId = row.id } };
            Pointer call = property ? new CallGetterPointer { receiver = callReceiver, interfaceMember = reference }
                : new CallFunctionPointer { receiver = callReceiver, interfaceMember = reference, args = Array.Empty<Pointer>(), callSiteId = "specificity" };
            object? Run() => NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), new NSGetterEvaluator.Context(client, null, null));
            if (stringView)
                Assert.That(Run(), Is.EqualTo(6));
            else
                Assert.That(Assert.Throws<NeoAmbiguousImplementationError>(() => Run())!.Message, Does.Contain("has competing implementations"));
        }
    }
}
