// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ReferenceConstructorArgumentsKeepEffectiveSiblingAndExternalTargetsAfterReload(bool generic)
        {
            ProjectData data = BuildConstructedUnorderedChildrenProjectData();
            ProjectData plain = BuildProjectData();
            data.members["thing-count"] = plain.members["thing-count"];
            data.classes["thing-class"].schema["Count"] = "thing-count";
            data.classes["entry-class"].schema.Remove("Label");
            var argument = data.constructors["entry-ctor"].argumentTypes[0];
            argument.type = MemberKind.Reference;
            argument.targetTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            const string externalId = "external-count";
            const string literal = "12345678-1234-1234-1234-123456789abc";
            data.values[externalId] = new NumberMemberValue { id = externalId, value = 17 };
            data.members["external-count-member"] = new IntMember
            {
                id = "external-count-member",
                projectId = "p75-project",
                name = "ExternalCount",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = externalId,
            };
            data.classes["save-root-class"].schema["ExternalCount"] = "external-count-member";
            ((ObjectMemberValue)data.values["value-save"]).value!["ExternalCount"] = externalId;
            var constructor = data.constructors["entry-ctor"];
            constructor.argumentTypes = new[]
            {
                argument,
                new FunctionArgumentTypeInfo { name = "External", type = MemberKind.Reference, required = true, targetTypeInfo = argument.targetTypeInfo },
                new FunctionArgumentTypeInfo { name = "Literal", type = MemberKind.String, required = true },
            };
            constructor.action.parameters = new[]
            {
                constructor.action.parameters[0], constructor.action.parameters[1],
                ConstructorVariable("__arg_0__", argument),
                ConstructorVariable("__arg_1__", constructor.argumentTypes[1]),
                ConstructorVariable("__arg_2__", constructor.argumentTypes[2]),
            };
            var assign = (AssignInstruction)data.constructors["thing-ctor"].action.instructions[0];
            var list = (ListLiteralPointer)assign.pointer;
            var construction = (DeclaredConstructorFunction)((FunctionPointer)list.entries[0]).function;
            construction.info.args[0].valuePointer = new RowOfPointer
            {
                type = PointerKind.RowOf,
                place = PointerKeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__this__" }, "Count"),
            };
            construction.info.args = new[]
            {
                construction.info.args[0],
                new DeclaredConstructorArgument
                {
                    name = "External", valuePointer = new RowOfPointer
                    {
                        type = PointerKind.RowOf,
                        place = new ReferencePointer { type = PointerKind.Reference, valueId = externalId },
                        typeInfo = argument.targetTypeInfo,
                    },
                },
                new DeclaredConstructorArgument { name = "Literal", valuePointer = StringLiteral(literal) },
            };
            if (generic)
            {
                const string parameter = "entry-param-t";
                data.classes["entry-class"].genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = parameter, name = "T" },
                };
                var closed = new ReferenceTypeInfo
                {
                    type = MemberKind.Reference,
                    required = true,
                    targetTypeInfo = argument.targetTypeInfo!,
                };
                construction.info.schemaClassInfo.typeArguments = new Dictionary<string, TypeInfo>
                {
                    [parameter] = closed,
                };
                // P127 §4.9: the fixture's Entry variant closes Entry the same way.
                data.variants["entry-variant"].typeArguments = new Dictionary<string, TypeBinding>
                {
                    [parameter] = NeoTypeBindings.FromTypeInfo(closed),
                };
                data.members["reference-int-binding"] = new LookupMember
                {
                    id = "reference-int-binding",
                    projectId = "p75-project",
                    name = "ReferenceIntBinding",
                    kind = MemberKind.Lookup,
                    Requirement = NeoMemberRequirementKind.Required,
                    declaredTypeInfo = argument.targetTypeInfo,
                };
                argument.type = MemberKind.Generic;
                argument.ownerClassId = "entry-class";
                argument.genericParamId = parameter;
                argument.targetTypeInfo = null;
            }
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                AssertReferenceArguments(client, externalId, literal);
                var thing = client.save.Get<NeoMemberClassWritable>("Thing");
                var entry = (NeoMemberClassWritable)thing.Get<NeoMemberList>("Children").Single();
                entry.Get<NeoMemberStringWritable>("Tag").Set("saved");
                saved = client.SerializeSaveData();
            }
            using NeoClient reloaded = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: saved);
            AssertReferenceArguments(reloaded, externalId, literal);
        }

        private static void AssertReferenceArguments(NeoClient client, string externalId, string literal)
        {
            NeoMemberClassWritable thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var entry = (NeoMemberClassWritable)thing.Get<NeoMemberList>("Children").Single();
            string target = thing.Get<NeoMemberIntWritable>("Count").value!.id;
            Assert.AreEqual(target, entry.value!.constructorArgs!["__arg_0__"]!.Value<string>(),
                "Reference constructor provenance must name the surviving virtual sibling row");
            Assert.AreEqual(externalId, entry.value.constructorArgs["__arg_1__"]!.Value<string>());
            Assert.AreEqual(literal, entry.value.constructorArgs["__arg_2__"]!.Value<string>());
        }
    }
}
