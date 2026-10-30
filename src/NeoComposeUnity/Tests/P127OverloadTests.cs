// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class P127OverloadTests
    {
        private const string Export = "Packages/com.ryanbliss.neocompose/Tests/p127-overloads-parity-export.json";
        private static TypeInfo Scalar(MemberKind kind, bool required = true) => new PrimitiveTypeInfo { type = kind, required = required };
        private static FunctionArgumentTypeInfo Arg(TypeInfo type, NeoParameterModeKind? mode = null)
        {
            var json = JObject.FromObject(type);
            json["name"] = "value";
            var argument = json.ToObject<FunctionArgumentTypeInfo>()!;
            argument.mode = mode;
            return argument;
        }
        private static TypeInfo List(TypeInfo entry) => new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = entry };
        private static TypeInfo MethodParam(string owner, int index) => new FunctionGenericTypeInfo
        {
            type = MemberKind.Generic,
            required = true,
            ownerMemberId = owner,
            index = index
        };
        private static TypeBinding Class(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: id));
        private static TypeBinding Contract => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "b4-interface"));
        private static FunctionGenericParam[] Generics(int count) => Enumerable.Range(0, count)
            .Select(index => new FunctionGenericParam { name = "T" + index }).ToArray();
        private static FunctionMember Function(string id, FunctionArgumentTypeInfo argument, TypeInfo? result = null, int arity = 0) => new()
        {
            id = id,
            name = "M",
            projectId = "project-id",
            kind = MemberKind.Function,
            argumentTypes = new[] { argument },
            returnTypeInfo = result ?? Scalar(MemberKind.Int),
            genericParams = arity == 0 ? null : Generics(arity)
        };
        private static ProjectData Schema(FunctionArgumentTypeInfo parameter, params FunctionMember[] methods)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            schema.interfaces["b4-interface"] = new Interface
            {
                id = "b4-interface",
                name = "IB4",
                projectId = "project-id",
                members = new()
                {
                    ["M#contract"] = new InterfaceMember
                    {
                        id = "contract",
                        kind = NeoInterfaceMemberKind.Function,
                        argumentTypes = new() { parameter },
                        returnTypeInfo = Scalar(MemberKind.Int)
                    }
                }
            };
            schema.classes["b4-class"] = new NeoSchemaClass
            {
                id = "b4-class",
                name = "B4",
                projectId = "project-id",
                implementsInterfaces = new() { new() { interfaceId = "b4-interface" } },
                schema = methods.ToDictionary(method => "M#" + method.id, method => method.id)
            };
            foreach (var method in methods)
                schema.members[method.id] = method;
            return schema;
        }
        private static InterfaceMember Member(ProjectData schema) => schema.interfaces["b4-interface"].members.Values.Single();
        private static NeoInterfaceDispatch.Implementation Resolve(NeoClient client, string owner = "b4-class") =>
            NeoInterfaceDispatch.Of(client).Resolve(Class(owner), Contract, "contract", "call");

        [Test]
        public void NullableReferenceAnnotationsEraseAtEveryDepth()
        {
            var required = Arg(List(Scalar(MemberKind.String)));
            var nullable = Arg(List(Scalar(MemberKind.String, false)));
            var method = Function("implementation", nullable, Scalar(MemberKind.String, false));
            var schema = Schema(required, method);
            Member(schema).returnTypeInfo = Scalar(MemberKind.String);
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            Assert.That(Resolve(client).MemberId, Is.EqualTo(method.id));
        }

        [Test]
        public void NullableValueTypesAndParameterModesSelectDistinctOverloads()
        {
            var nullable = Function("nullable", Arg(Scalar(MemberKind.Int, false)));
            var output = Function("output", Arg(Scalar(MemberKind.Int), NeoParameterModeKind.Out));
            var value = Function("value", Arg(Scalar(MemberKind.Int)));
            var schema = Schema(output.argumentTypes[0], nullable, value, output);
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            Assert.That(Resolve(client).MemberId, Is.EqualTo(output.id));
            Member(schema).argumentTypes![0] = nullable.argumentTypes[0];
            client.InvalidateSchemaResolutionCaches();
            Assert.That(Resolve(client).MemberId, Is.EqualTo(nullable.id));
        }

        [Test]
        public void GenericMethodsMatchByArityAndPositionIncludingNestedTypes()
        {
            var wrongArity = Function("wrong-arity", Arg(List(MethodParam("wrong-arity", 0))), arity: 2);
            var wrongPosition = Function("wrong-position", Arg(List(MethodParam("wrong-position", 1))), arity: 2);
            var method = Function("implementation", Arg(List(MethodParam("implementation", 0))), arity: 1);
            var schema = Schema(Arg(List(MethodParam("contract", 0))), wrongArity, wrongPosition, method);
            Member(schema).genericParams = Generics(1);
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            Assert.That(Resolve(client).MemberId, Is.EqualTo(method.id));
            Member(schema).genericParams = Generics(2);
            Member(schema).argumentTypes![0] = Arg(List(MethodParam("contract", 1)));
            client.InvalidateSchemaResolutionCaches();
            Assert.That(Resolve(client).MemberId, Is.EqualTo(wrongPosition.id));
        }

        [Test]
        public void WrongReturnAndStaticnessDoNotImplementTheContract()
        {
            var wrongReturn = Function("wrong-result", Arg(Scalar(MemberKind.Int)), Scalar(MemberKind.String));
            var wrongStatic = Function("wrong-static", Arg(Scalar(MemberKind.Int)));
            wrongStatic.Modifier = NeoMemberModifierKind.Static;
            var method = Function("implementation", Arg(Scalar(MemberKind.Int)));
            var schema = Schema(method.argumentTypes[0], wrongReturn, wrongStatic, method);
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            Assert.That(Resolve(client).MemberId, Is.EqualTo(method.id));
        }

        [Test]
        public void InheritedGenericSignatureClosesInTheDeclaringBase()
        {
            var argument = Arg(new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "b4-class", genericParamId = "T" });
            var method = Function("implementation", argument);
            var schema = Schema(Arg(Scalar(MemberKind.Int)), method);
            var parent = schema.classes["b4-class"];
            parent.genericParams = new() { new() { id = "T", name = "T" } };
            parent.implementsInterfaces = null;
            schema.classes["derived"] = new NeoSchemaClass
            {
                id = "derived",
                name = "DerivedB4",
                projectId = "project-id",
                extendsClassId = parent.id,
                extendsGenericBindings = new() { ["T"] = NeoTypeBindings.FromTypeInfo(Scalar(MemberKind.Int)) },
                implementsInterfaces = new() { new() { interfaceId = "b4-interface" } },
                schema = new()
            };
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            Assert.That(Resolve(client, "derived").MemberId, Is.EqualTo(method.id));
        }

        [Test]
        public void MappingCacheAndOwnOverloadIndexRebuildOnSchemaInvalidation()
        {
            var first = Function("first", Arg(Scalar(MemberKind.Int)));
            var schema = Schema(first.argumentTypes[0], first);
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            var original = Resolve(client);
            Assert.That(Resolve(client), Is.SameAs(original));
            var second = Function("second", first.argumentTypes[0]);
            schema.members[second.id] = second;
            schema.classes["b4-class"].schema = new()
            {
                ["M"] = second.id
            };
            client.InvalidateSchemaResolutionCaches();
            Assert.That(Resolve(client).MemberId, Is.EqualTo(second.id));
        }

        private static JObject StoredInterface(JObject members) => new()
        {
            ["interfaces"] = new JObject
            {
                ["interface-id"] = new JObject { ["id"] = "interface-id", ["name"] = "IStore", ["members"] = members }
            }
        };
        private static JObject StoredFunction(string id, TypeInfo type, int arity = 0)
        {
            var member = new InterfaceMember
            {
                id = id,
                kind = NeoInterfaceMemberKind.Function,
                argumentTypes = new() { Arg(type) },
                returnTypeInfo = Scalar(MemberKind.Int),
                genericParams = arity == 0 ? null : Generics(arity)
            };
            return JObject.FromObject(member);
        }

        [TestCase("M#wrong")]
        [TestCase("M#")]
        [TestCase("M#a#extra")]
        public void InvalidInterfaceKeySuffixIsRefused(string key)
        {
            var data = StoredInterface(new()
            {
                [key] = StoredFunction("a", Scalar(MemberKind.Int))
            });
            Assert.That(Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateProjectData(data))!.Message,
                Is.EqualTo($"Interface \"IStore\" (interface-id) stores member \"a\" under key \"{key}\"; expected \"M\" or \"M#a\"."));
        }

        [TestCase(NeoInterfaceMemberKind.Property)]
        [TestCase(NeoInterfaceMemberKind.Field)]
        public void NonFunctionOverloadKeyRequiresExplicitImplementation(NeoInterfaceMemberKind kind)
        {
            var member = new JObject
            {
                ["id"] = "a",
                ["kind"] = (int)kind,
                ["typeInfo"] = JObject.FromObject(Scalar(MemberKind.Int))
            };
            if (kind == NeoInterfaceMemberKind.Field)
                member["static"] = true;
            var data = StoredInterface(new()
            {
                ["P#a"] = member
            });
            Assert.That(Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateProjectData(data))!.Message,
                Is.EqualTo("Interface \"IStore\" (interface-id) member key \"P#a\" names no override."));
            if (kind == NeoInterfaceMemberKind.Property)
            {
                member["explicitImplementation"] = new JObject { ["interfaceId"] = "base", ["interfaceMemberId"] = "base-p" };
                Assert.DoesNotThrow(() => RecordShapeContractGuard.ValidateProjectData(data));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateIdentityErasesNullableReferencesAndMethodParameterOwners(bool generic)
        {
            var first = StoredFunction("a", List(generic ? MethodParam("a", 0) : Scalar(MemberKind.String)), generic ? 1 : 0);
            var second = StoredFunction("b", List(generic ? MethodParam("b", 0) : Scalar(MemberKind.String, false)), generic ? 1 : 0);
            var data = StoredInterface(new()
            {
                ["M#a"] = first,
                ["M#b"] = second
            });
            Assert.That(Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateProjectData(data))!.Message,
                Is.EqualTo("Interface \"IStore\" (interface-id) declares functions \"a\" and \"b\" with one name and the same parameters (CS0111)."));
        }

        [Test]
        public void DistinctModesValueNullabilityAndGenericArityAreAccepted()
        {
            var value = StoredFunction("a", Scalar(MemberKind.Int));
            var nullable = StoredFunction("b", Scalar(MemberKind.Int, false));
            var output = StoredFunction("c", Scalar(MemberKind.Int));
            output["argumentTypes"]![0]!["mode"] = (int)NeoParameterModeKind.Out;
            var generic = StoredFunction("d", Scalar(MemberKind.Int), 1);
            Assert.DoesNotThrow(() => RecordShapeContractGuard.ValidateProjectData(StoredInterface(new()
            {
                ["M#a"] = value,
                ["M#b"] = nullable,
                ["M#c"] = output,
                ["M#d"] = generic
            })));
        }

        [Test]
        public void ProjectReaderRejectsAStoredMalformedOverloadKey()
        {
            var export = JObject.Parse(File.ReadAllText(Export));
            var contract = ((JObject)export["interfaces"]!).Properties().Select(property => (JObject)property.Value).First(type => (string?)type["name"] == "IOv");
            var members = (JObject)contract["members"]!;
            var first = members.Properties().First();
            var declaration = first.Value;
            first.Remove();
            members["M#wrong"] = declaration;
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoTestExport.Read(export.ToString()))!.Message,
                Does.Contain("under key \"M#wrong\""));
        }
    }
}
