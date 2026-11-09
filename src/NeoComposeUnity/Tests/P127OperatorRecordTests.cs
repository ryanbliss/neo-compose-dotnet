// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace NeoCompose.Tests
{
    public sealed class P127OperatorRecordTests
    {
        private static JObject Type(MemberKind kind, bool required = true) => new() { ["type"] = (int)kind, ["required"] = required };
        private static JObject Own()
        {
            var type = Type(MemberKind.Class);
            type["classId"] = "money";
            return type;
        }
        private static JObject Row(string name = "op_Addition") => new()
        {
            ["id"] = "op",
            ["name"] = name,
            ["kind"] = (int)MemberKind.NSFunction,
            ["operator"] = true,
            ["modifier"] = (int)NeoMemberModifierKind.Static,
            ["argumentTypes"] = new JArray(Own(), Type(MemberKind.Int)),
            ["returnTypeInfo"] = Own(),
        };
        private static JObject Project(params JObject[] rows)
        {
            var schema = new JObject();
            var members = new JObject();
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i]["id"] = "op" + i;
                members["op" + i] = rows[i];
                schema["slot" + i] = "op" + i;
            }
            return new JObject { ["members"] = members, ["classes"] = new JObject { ["money"] = new JObject { ["id"] = "money", ["name"] = "Money", ["schema"] = schema } } };
        }
        [TestCase("operator", "false", "operator must be true when present.")]
        [TestCase("kind", "0", "operator is only valid on a function member.")]
        [TestCase("name", "'op_Bogus'", "'op_Bogus' is not an operator metadata name.")]
        [TestCase("extendsMemberId", "null", "An operator can't override a member.")]
        [TestCase("modifier", "0", "Declare operators as static members.")]
        [TestCase("genericParams", "[{}]", "An operator can't declare type parameters.")]
        [TestCase("argumentTypes", "[]", "'op_Addition' takes 2 parameter(s), not 0.")]
        [TestCase("explicitImplementation", "{}", "explicitImplementation must name an interface, its arguments and an interface member.")]
        public void MemberRefusalsMatchTheContract(string field, string json, string message)
        {
            var row = Row();
            row[field] = JToken.Parse(json);
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoOperatorRecords.ValidateMember(row))!.Message,
                Is.EqualTo("Member \"op\": " + message));
        }
        [Test]
        public void ShapeChecksPrecedeContainingTypeChecks()
        {
            var row = Row();
            row["name"] = "invalid";
            row["modifier"] = 0;
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoOperatorRecords.ValidateMember(row))!.Message,
                Is.EqualTo("Member \"op\": 'invalid' is not an operator metadata name."));
        }
        [Test]
        public void ContainingTypeRefusalIsExact()
        {
            var row = Row();
            row["argumentTypes"] = new JArray(Type(MemberKind.Int), Type(MemberKind.Int));
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoOperatorRecords.ValidateProject(Project(row)))!.Message,
                Is.EqualTo("Class \"Money\" (money) operator 'op_Addition': Give at least one binary operand the declaring class type."));
        }
        [Test]
        public void ValueNullableAnnotationsArePartOfPairIdentity()
        {
            var equal = Row("op_Equality");
            equal["returnTypeInfo"] = Type(MemberKind.Bool);
            var other = (JObject)equal.DeepClone();
            other["name"] = "op_Inequality";
            other["argumentTypes"]![1]!["required"] = false;
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoOperatorRecords.ValidateProject(Project(equal, other)))!.Message,
                Is.EqualTo("Class \"Money\" (money) operator 'op_Equality': Add 'op_Inequality' with the same operand and result types."));
            other["argumentTypes"]![1]!["required"] = true;
            Assert.DoesNotThrow(() => NeoOperatorRecords.ValidateProject(Project(equal, other)));
            equal["argumentTypes"]![1] = Type(MemberKind.String);
            other["argumentTypes"]![1] = Type(MemberKind.String, false);
            Assert.DoesNotThrow(() => NeoOperatorRecords.ValidateProject(Project(equal, other)));
        }
        [Test]
        public void CheckedOperatorNeedsItsRegularPair()
        {
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoOperatorRecords.ValidateProject(Project(Row("op_CheckedAddition"))))!.Message,
                Is.EqualTo("Class \"Money\" (money) operator 'op_CheckedAddition': Add its unchecked operator with the same operand and result types."));
            Assert.DoesNotThrow(() => NeoOperatorRecords.ValidateProject(Project(Row("op_CheckedAddition"), Row())));
        }
    }
}
