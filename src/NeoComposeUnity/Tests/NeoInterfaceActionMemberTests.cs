// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P62 §6 — an interface may declare an NSAction property. The interface
    /// converter's type-info gate mirrors the NSDelegate arm minus the return
    /// slot: void-ness is structural for an action, so only the argument list
    /// is required.
    /// </summary>
    public class NeoInterfaceActionMemberTests
    {
        private const int NSActionKind = (int)MemberKind.NSAction;
        private const int IntKind = (int)MemberKind.Int;

        [Test]
        public void ActionProperty_DeserializesWithoutAReturnTypeInfo()
        {
            InterfaceMember member = Deserialize(
                $@"{{
  ""kind"": ""property"",
  ""accessModifierKind"": ""public"",
  ""settable"": false,
  ""typeInfo"": {{
    ""type"": {NSActionKind},
    ""argumentTypes"": [{{ ""type"": {IntKind} }}]
  }}
}}");

            var typeInfo = member.typeInfo as ActionTypeInfo;
            Assert.IsNotNull(
                typeInfo,
                "An NSAction discriminator must resolve to ActionTypeInfo.");
            Assert.AreEqual(1, typeInfo!.argumentTypes.Length);
            Assert.AreEqual(MemberKind.Int, typeInfo.argumentTypes[0].type);
        }

        [Test]
        public void ActionProperty_WithoutArgumentTypesIsRejected()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                Deserialize(
                    $@"{{
  ""kind"": ""property"",
  ""accessModifierKind"": ""public"",
  ""settable"": false,
  ""typeInfo"": {{ ""type"": {NSActionKind} }}
}}"));

            StringAssert.Contains(
                "action type is missing its signature",
                error!.Message);
        }

        [Test]
        public void ActionArgument_OnAFunctionMemberCarriesItsSignature()
        {
            InterfaceMember member = Deserialize(
                $@"{{
  ""kind"": ""function"",
  ""accessModifierKind"": ""public"",
  ""deferred"": false,
  ""returnTypeInfo"": {{ ""type"": {IntKind} }},
  ""argumentTypes"": [{{
    ""name"": ""onDamaged"",
    ""type"": {NSActionKind},
    ""argumentTypes"": [{{ ""type"": {IntKind} }}]
  }}]
}}");

            FunctionArgumentTypeInfo argument = member.argumentTypes![0];
            Assert.AreEqual(MemberKind.NSAction, argument.type);
            Assert.AreEqual(1, argument.argumentTypes!.Length);
            Assert.IsNull(
                argument.returnTypeInfo,
                "An action argument has no return slot.");
        }

        [Test]
        public void ActionArgument_WithoutArgumentTypesIsRejected()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                Deserialize(
                    $@"{{
  ""kind"": ""function"",
  ""accessModifierKind"": ""public"",
  ""deferred"": false,
  ""returnTypeInfo"": {{ ""type"": {IntKind} }},
  ""argumentTypes"": [{{ ""name"": ""onDamaged"", ""type"": {NSActionKind} }}]
}}"));

            StringAssert.Contains(
                "action type is missing its signature",
                error!.Message);
        }

        private static InterfaceMember Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<InterfaceMember>(json)!;
        }
    }
}
