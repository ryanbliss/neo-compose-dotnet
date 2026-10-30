// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §12.1's record shapes: declarations, interface members,
    /// parameters, the <c>object</c> member kind, and stored delegate
    /// identity. Each new field reads, writes and round-trips, and each
    /// refused shape fails the read.
    /// </summary>
    public class P127RecordShapeTests
    {
        private static JObject FieldMember(int kind = 2) => new()
        {
            ["id"] = "field",
            ["name"] = "Field",
            ["kind"] = kind,
        };

        private static T RoundTrip<T>(T value) =>
            JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;

        [TestCase(NeoPropertyAccessorsKind.Get, NeoMemberMutabilityKind.ReadOnly)]
        [TestCase(NeoPropertyAccessorsKind.GetSet, NeoMemberMutabilityKind.Mutable)]
        [TestCase(NeoPropertyAccessorsKind.GetInit, NeoMemberMutabilityKind.ReadOnly)]
        public void AutoPropertyFieldAccessorsAgreeWithMutability(
            NeoPropertyAccessorsKind accessors,
            NeoMemberMutabilityKind mutability)
        {
            JObject member = FieldMember();
            member["declarationForm"] = (int)NeoMemberDeclarationFormKind.AutoProperty;
            member["accessors"] = (int)accessors;
            member["mutability"] = (int)mutability;
            member["getterAccess"] = (int)NeoMemberAccessKind.Public;
            member["setterAccess"] = (int)NeoMemberAccessKind.Private;
            Assert.DoesNotThrow(() => RecordShapeContractGuard.ValidateMemberRecord(member));

            Member read = member.ToObject<Member>()!;
            Assert.That(read.DeclaredAccessors, Is.EqualTo(accessors));
            Assert.That(read.DeclarationForm, Is.EqualTo(NeoMemberDeclarationFormKind.AutoProperty));
            Assert.That(read.SetterAccess, Is.EqualTo(NeoMemberAccessKind.Private));
            JObject written = JObject.FromObject(RoundTrip(read));
            Assert.That((int?)written["accessors"], Is.EqualTo((int)accessors));
            Assert.That((int?)written["declarationForm"], Is.EqualTo((int)NeoMemberDeclarationFormKind.AutoProperty));
            Assert.That((int?)written["setterAccess"], Is.EqualTo((int)NeoMemberAccessKind.Private));
        }

        [TestCase(null, NeoMemberMutabilityKind.ReadOnly, "must declare 'accessors'")]
        [TestCase(NeoPropertyAccessorsKind.Set, NeoMemberMutabilityKind.Mutable, "Get, GetSet or GetInit")]
        [TestCase(NeoPropertyAccessorsKind.Init, NeoMemberMutabilityKind.ReadOnly, "Get, GetSet or GetInit")]
        [TestCase(NeoPropertyAccessorsKind.Get, NeoMemberMutabilityKind.Mutable, "needs ReadOnly mutability")]
        [TestCase(NeoPropertyAccessorsKind.GetInit, NeoMemberMutabilityKind.Mutable, "needs ReadOnly mutability")]
        [TestCase(NeoPropertyAccessorsKind.GetSet, NeoMemberMutabilityKind.ReadOnly, "needs Mutable mutability")]
        public void AutoPropertyFieldAccessorsAreRefused(
            NeoPropertyAccessorsKind? accessors,
            NeoMemberMutabilityKind mutability,
            string message)
        {
            JObject member = FieldMember();
            member["declarationForm"] = (int)NeoMemberDeclarationFormKind.AutoProperty;
            if (accessors is not null)
                member["accessors"] = (int)accessors.Value;
            member["mutability"] = (int)mutability;
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(member))!.Message,
                Does.Contain(message));
        }

        [Test]
        public void PlainFieldsCarryNoAccessorsOrAccessorAccess()
        {
            JObject withAccessors = FieldMember();
            withAccessors["accessors"] = (int)NeoPropertyAccessorsKind.Get;
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(withAccessors))!.Message,
                Does.Contain("only NSProperty members and auto-property field members"));

            JObject withAccess = FieldMember();
            withAccess["getterAccess"] = (int)NeoMemberAccessKind.Public;
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(withAccess))!.Message,
                Does.Contain("without an AutoProperty declaration form"));

            JObject property = FieldMember((int)MemberKind.NSProperty);
            property["declarationForm"] = (int)NeoMemberDeclarationFormKind.Field;
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(property))!.Message,
                Does.Contain("only field members carry"));
        }

        [TestCase(NeoPropertyAccessorsKind.Set)]
        [TestCase(NeoPropertyAccessorsKind.GetInit)]
        [TestCase(NeoPropertyAccessorsKind.Init)]
        public void NSPropertyAccessorsRoundTrip(NeoPropertyAccessorsKind accessors)
        {
            JObject member = FieldMember((int)MemberKind.NSProperty);
            member["accessors"] = (int)accessors;
            member["setterCode"] = "field = value;";
            Assert.DoesNotThrow(() => RecordShapeContractGuard.ValidateMemberRecord(member));
            var read = (NSPropertyMember)member.ToObject<Member>()!;
            Assert.That(read.Accessors, Is.EqualTo(accessors));
            Assert.That(((NSPropertyMember)RoundTrip<Member>(read)).Accessors, Is.EqualTo(accessors));
        }

        [Test]
        public void NSPropertyAccessorsDefaultFromTheSetterBody()
        {
            var getOnly = (NSPropertyMember)FieldMember((int)MemberKind.NSProperty).ToObject<Member>()!;
            Assert.That(getOnly.Accessors, Is.EqualTo(NeoPropertyAccessorsKind.Get));
            JObject settable = FieldMember((int)MemberKind.NSProperty);
            settable["setterCode"] = "field = value;";
            Assert.That(((NSPropertyMember)settable.ToObject<Member>()!).Accessors, Is.EqualTo(NeoPropertyAccessorsKind.GetSet));
        }

        [Test]
        public void ObjectMemberRoundTripsItsObjectType()
        {
            JObject member = FieldMember((int)MemberKind.Object);
            member["objectType"] = (int)NeoObjectTypeKind.CultureInfo;
            Assert.DoesNotThrow(() => RecordShapeContractGuard.ValidateMemberRecord(member));
            var read = (ObjectMember)member.ToObject<Member>()!;
            Assert.That(read.objectType, Is.EqualTo(NeoObjectTypeKind.CultureInfo));
            Assert.That(((ObjectMember)RoundTrip<Member>(read)).objectType, Is.EqualTo(NeoObjectTypeKind.CultureInfo));

            var plain = (ObjectMember)FieldMember((int)MemberKind.Object).ToObject<Member>()!;
            Assert.That(plain.objectType, Is.Null);
            Assert.That(JObject.FromObject(plain).Property("objectType"), Is.Null);

            member["objectType"] = 99;
            Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(member));
        }

        [Test]
        public void OperatorFlagIsFunctionOnlyAndTrueOnly()
        {
            JObject function = FieldMember((int)MemberKind.NSFunction);
            function["operator"] = true;
            function["name"] = "op_Addition";
            function["modifier"] = (int)NeoMemberModifierKind.Static;
            function["argumentTypes"] = new JArray(new JObject { ["type"] = (int)MemberKind.Int, ["required"] = true }, new JObject { ["type"] = (int)MemberKind.Int, ["required"] = true });
            Assert.DoesNotThrow(() => RecordShapeContractGuard.ValidateMemberRecord(function));
            function["operator"] = false;
            Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(function));

            JObject field = FieldMember();
            field["operator"] = true;
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateMemberRecord(field))!.Message,
                Is.EqualTo("Member \"field\": operator is only valid on a function member."));
        }

        [Test]
        public void GenericParamDeclarationsReadConstraintListsAndVariance()
        {
            var declaration = JsonConvert.DeserializeObject<GenericParamDeclaration>(@"{
  'id':'t','name':'T','variance':1,
  'constraints':[
    {'kind':0,'classId':'entity','nullable':true},
    {'kind':2,'interfaceId':'number','interfaceArguments':{'self':{'kind':1,'owner':{'type':7,'classId':'c'},'param':'t'}}},
    {'kind':3}
  ]
}")!;
            Assert.That(declaration.variance, Is.EqualTo(NeoGenericParamVarianceKind.In));
            Assert.That(declaration.constraints, Has.Count.EqualTo(3));
            Assert.That(declaration.ClassConstraint!.classId, Is.EqualTo("entity"));
            Assert.That(declaration.ClassConstraint.nullable, Is.True);
            Assert.That(declaration.constraints![1].interfaceArguments!["self"].Param, Is.EqualTo("t"));
            Assert.That(declaration.constraints[2].kind, Is.EqualTo(NeoGenericParamConstraintKind.AnyEnum));

            var reread = RoundTrip(declaration);
            Assert.That(reread.constraints, Has.Count.EqualTo(3));
            Assert.That(reread.variance, Is.EqualTo(NeoGenericParamVarianceKind.In));
            Assert.That(reread.constraints![2].kind, Is.EqualTo(NeoGenericParamConstraintKind.AnyEnum));
        }

        [TestCase("{'id':'t','name':'T','constraints':[]}", "non-empty array")]
        [TestCase("{'id':'t','name':'T','variance':2}", "unknown ordinal '2'")]
        [TestCase("{'id':'t','name':'T','constraint':{'kind':3}}", "removed field 'constraint'")]
        [TestCase("{'id':'t','name':'T','constraints':[{'kind':3,'enumId':'e'}]}", "cannot declare 'enumId'")]
        [TestCase("{'id':'t','name':'T','constraints':[{'kind':1,'enumId':'e','nullable':true}]}", "cannot declare 'nullable'")]
        [TestCase("{'id':'t','name':'T','constraints':[{'kind':2,'interfaceId':'i','nullable':false}]}", "must be true when present")]
        public void GenericParamDeclarationsRefuseOldAndMalformedShapes(string json, string message)
        {
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<GenericParamDeclaration>(json))!.Message,
                Does.Contain(message));
        }

        [Test]
        public void FunctionGenericParamsReadConstraintLists()
        {
            var parameter = JsonConvert.DeserializeObject<FunctionGenericParam>(@"{
  'name':'T',
  'constraints':[
    {'type':7,'classId':'entity','required':false},
    {'type':31,'objectType':1,'required':true}
  ]
}")!;
            Assert.That(parameter.constraints, Has.Length.EqualTo(2));
            Assert.That(parameter.ClassConstraint!.classId, Is.EqualTo("entity"));
            Assert.That(parameter.constraints![1], Is.InstanceOf<ObjectTypeInfo>());

            Assert.That(
                Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<FunctionGenericParam>(
                    "{'name':'T','constraint':{'type':7,'classId':'entity','required':true}}"))!.Message,
                Does.Contain("removed field 'constraint'"));
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<FunctionGenericParam>(
                    "{'name':'T','constraints':[{'type':2,'required':true}]}"))!.Message,
                Does.Contain("must be a Class, Interface or Enum type"));
        }

        [TestCase("{'type':7,'classId':'c','required':false}")]
        [TestCase("{'type':22,'interfaceId':'i','required':false}")]
        [TestCase("{'type':31,'objectType':1,'required':false}")]
        public void FunctionConstraintsAdmitNullableClassInterfaceAndGeneralEnum(string constraint)
        {
            var parameter = JsonConvert.DeserializeObject<FunctionGenericParam>("{'name':'T','constraints':[" + constraint + "]}")!;
            Assert.That(parameter.constraints![0].required, Is.False);
            Assert.That(JsonConvert.DeserializeObject<FunctionGenericParam>(JsonConvert.SerializeObject(parameter))!.constraints![0].required, Is.False);
        }

        [Test]
        public void FunctionConstraintsRefuseNullableSpecificEnum()
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<FunctionGenericParam>(
                "{'name':'T','constraints':[{'type':8,'enumId':'e','required':false}]}"));
        }

        [Test]
        public void ClassAndInterfaceListsUseConstructions()
        {
            var schemaClass = JsonConvert.DeserializeObject<NeoSchemaClass>(@"{
  'id':'c','projectId':'p','name':'C','schema':{},
  'implementsInterfaces':[
    {'interfaceId':'equatable','arguments':{'t':{'kind':0,'type':{'type':2,'required':true}}}},
    {'interfaceId':'equatable','arguments':{'t':{'kind':0,'type':{'type':3,'required':true}}}}
  ]
}")!;
            Assert.That(schemaClass.implementsInterfaces, Has.Count.EqualTo(2));
            Assert.That(schemaClass.implementsInterfaces![1].arguments!["t"].Head!.Type, Is.EqualTo(MemberKind.String));

            var root = JObject.Parse(@"{'classes':{'c':{'id':'c','implementsInterfaceIds':['equatable']}}}");
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateProjectData(root))!.Message,
                Does.Contain("removed field 'implementsInterfaceIds'"));
            root = JObject.Parse(@"{'interfaces':{'i':{'id':'i','extendsInterfaceIds':['equatable']}}}");
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => RecordShapeContractGuard.ValidateProjectData(root))!.Message,
                Does.Contain("removed field 'extendsInterfaceIds'"));
        }

        [Test]
        public void InterfaceMembersReadStaticModifierAndExplicitImplementation()
        {
            var member = JsonConvert.DeserializeObject<InterfaceMember>(@"{
  'kind':1,'static':true,'modifier':2,'returnTypeInfo':{'type':2,'required':true},'argumentTypes':[],
  'implementationMemberId':'impl',
  'explicitImplementation':{'interfaceId':'base','interfaceMemberId':'m','arguments':{'t':{'kind':0,'type':{'type':2,'required':true}}}}
}")!;
            Assert.That(member.IsStatic, Is.True);
            Assert.That(member.DeclaredModifier, Is.EqualTo((NeoMemberModifierKind)2));
            Assert.That(member.implementationMemberId, Is.EqualTo("impl"));
            Assert.That(member.explicitImplementation!.interfaceMemberId, Is.EqualTo("m"));
            Assert.That(member.explicitImplementation.arguments!["t"], Is.EqualTo(TestTypeBindings.Of(MemberKind.Int)));

            var reread = RoundTrip(member);
            Assert.That(reread.IsStatic, Is.True);
            Assert.That(reread.explicitImplementation!.interfaceId, Is.EqualTo("base"));
            Assert.That(JObject.FromObject(RoundTrip(JsonConvert.DeserializeObject<InterfaceMember>(
                "{'kind':0,'typeInfo':{'type':2,'required':true},'accessors':0}")!)).Property("static"), Is.Null);
        }

        [Test]
        public void InterfaceFieldsAreStaticAndTyped()
        {
            var field = JsonConvert.DeserializeObject<InterfaceMember>(
                "{'kind':2,'static':true,'typeInfo':{'type':2,'required':true}}")!;
            Assert.That(field.kind, Is.EqualTo(NeoInterfaceMemberKind.Field));
            Assert.That(RoundTrip(field).kind, Is.EqualTo(NeoInterfaceMemberKind.Field));
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<InterfaceMember>(
                    "{'kind':2,'typeInfo':{'type':2,'required':true}}"))!.Message,
                Does.Contain("requires 'static: true'"));
        }

        [TestCase(NeoPropertyAccessorsKind.Get, false)]
        [TestCase(NeoPropertyAccessorsKind.GetSet, true)]
        [TestCase(NeoPropertyAccessorsKind.Set, true)]
        [TestCase(NeoPropertyAccessorsKind.GetInit, false)]
        [TestCase(NeoPropertyAccessorsKind.Init, false)]
        public void InterfacePropertyAccessorsRoundTrip(NeoPropertyAccessorsKind accessors, bool settable)
        {
            var property = JsonConvert.DeserializeObject<InterfaceMember>(
                $"{{'kind':0,'typeInfo':{{'type':2,'required':true}},'accessors':{(int)accessors},'getterAccess':0,'setterAccess':2}}")!;
            Assert.That(property.Accessors, Is.EqualTo(accessors));
            Assert.That(property.IsSettable, Is.EqualTo(settable));
            var reread = RoundTrip(property);
            Assert.That(reread.Accessors, Is.EqualTo(accessors));
            Assert.That(reread.DeclaredGetterAccess, Is.EqualTo((NeoMemberAccessKind)0));
            Assert.That(reread.DeclaredSetterAccess, Is.EqualTo((NeoMemberAccessKind)2));
        }

        [Test]
        public void ParametersReadModeFlowAndTheDefaultLiteral()
        {
            var parameters = JsonConvert.DeserializeObject<FunctionArgumentTypeInfo[]>(@"[
  {'name':'result','type':2,'required':true,'mode':0,'flow':{'maybeNullWhen':false}},
  {'name':'text','type':3,'required':false,'flow':{'notNullWhen':true}},
  {'name':'count','type':2,'required':true,'defaultValue':{'default':true}},
  {'name':'scale','type':4,'required':true,'defaultValue':{'value':'-Infinity'}}
]")!;
            Assert.That(parameters[0].mode, Is.EqualTo(NeoParameterModeKind.Out));
            Assert.That(parameters[0].flow!.maybeNullWhen, Is.False);
            Assert.That(parameters[1].flow!.notNullWhen, Is.True);
            Assert.That(parameters[2].defaultValue!.isDefault, Is.True);
            JObject written = JObject.FromObject(parameters[2].defaultValue!);
            Assert.That(written.ToString(Formatting.None), Is.EqualTo("{\"default\":true}"));

            Assert.That(
                Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<FunctionArgumentTypeInfo>(
                    "{'name':'r','type':2,'required':true,'mode':0,'defaultValue':{'value':1}}"))!.Message,
                Does.Contain("cannot declare a default value"));
        }

        private static NeoDelegateValue ReadDelegate(string json) =>
            JsonConvert.DeserializeObject<NeoDelegateValue>(json)!;

        private const string Body = "'action':{'parameters':[],'instructions':[],'typeInfo':{'type':2,'required':true}}";

        private const string IntBinding = "{'kind':0,'type':{'type':2,'required':true}}";
        private const string FloatBinding = "{'kind':0,'type':{'type':4,'required':true}}";
        private const string StringBinding = "{'kind':0,'type':{'type':3,'required':true}}";
        private const string ListOfString = "{'kind':0,'type':{'type':6,'required':true},'bindings':{'0':" + StringBinding + "}}";
        private const string BoxOfInt = "{'kind':0,'type':{'type':7,'required':true,'classId':'box'},'bindings':{'t':" + IntBinding + "}}";
        private const string BoxOfFloat = "{'kind':0,'type':{'type':7,'required':true,'classId':'box'},'bindings':{'t':" + FloatBinding + "}}";
        private const string EquatableOfInt = "{'kind':0,'type':{'type':22,'required':true,'interfaceId':'equatable'},'bindings':{'t':" + IntBinding + "}}";
        private const string DelegateOfInt = "{'kind':0,'type':{'type':25,'required':true},'bindings':{'0':" + IntBinding + "}}";
        private const string OpenBinding = "{'kind':1,'owner':{'type':22,'interfaceId':'equatable'},'param':'t'}";

        private static TypeBinding Equatable(TypeBinding argument) => TypeBinding.Closed(
            new TypeHead(MemberKind.Interface, true, interfaceId: "equatable"),
            new Dictionary<string, TypeBinding> { ["t"] = argument });

        [Test]
        public void MemberTargetsRoundTripTheirConstructionFields()
        {
            NeoDelegateValue target = ReadDelegate("{'memberId':'m','valueId':null,'typeArguments':[" + IntBinding + "," + ListOfString + "]"
                + ",'staticConstruction':" + BoxOfInt + ",'interfaceConstruction':" + EquatableOfInt + ",'delegateType':" + DelegateOfInt + "}");
            Assert.That(target.typeArguments, Is.EqualTo(new[]
            {
                TestTypeBindings.Of(MemberKind.Int),
                TestTypeBindings.List(TestTypeBindings.Of(MemberKind.String)),
            }));
            Assert.That(target.staticConstruction, Is.EqualTo(TestTypeBindings.Class("box", true, ("t", TestTypeBindings.Of(MemberKind.Int)))));
            Assert.That(target.interfaceConstruction, Is.EqualTo(Equatable(TestTypeBindings.Of(MemberKind.Int))));
            Assert.That(target.delegateType!.Head!.Type, Is.EqualTo(MemberKind.NSDelegate));
            NeoDelegateValue reread = RoundTrip(target);
            Assert.That(JToken.DeepEquals(JToken.FromObject(reread), JToken.FromObject(target)), Is.True);
            Assert.That(reread.interfaceConstruction, Is.EqualTo(target.interfaceConstruction));
            NeoDelegateValue copy = target.PersistedCopy();
            Assert.That(copy.interfaceConstruction, Is.EqualTo(target.interfaceConstruction));
            Assert.That(copy.typeArguments, Is.Not.SameAs(target.typeArguments));
            Assert.That(copy.typeArguments, Is.EqualTo(target.typeArguments));
        }

        [Test]
        public void ClosuresRoundTripTheirIdentityFields()
        {
            NeoDelegateValue closure = ReadDelegate("{'code':'() => 0'," + Body
                + ",'typeArguments':[" + FloatBinding + "],'declaringConstruction':" + BoxOfFloat + ",'delegateType':" + DelegateOfInt
                + ",'site':'member-1:getter:0','placement':'environment'}");
            Assert.That(closure.site, Is.EqualTo("member-1:getter:0"));
            Assert.That(closure.placement, Is.EqualTo("environment"));
            Assert.That(closure.declaringConstruction, Is.EqualTo(TestTypeBindings.Class("box", true, ("t", TestTypeBindings.Of(MemberKind.Float)))));
            JObject written = JObject.FromObject(RoundTrip(closure));
            foreach (string key in new[] { "typeArguments", "declaringConstruction", "delegateType", "site", "placement" })
                Assert.That(written.Property(key), Is.Not.Null, key);
            Assert.That(written["declaringConstruction"]!.Type, Is.EqualTo(JTokenType.Object));
            Assert.That(written["typeArguments"]![0]!.Type, Is.EqualTo(JTokenType.Object));
            Assert.DoesNotThrow(() => ReadDelegate("{" + Body + ",'site':'scene/node/line:dialogue:2'}"));
        }

        [TestCase("{'memberId':'m','valueId':null,'typeArguments':[]}", "must not be empty")]
        [TestCase("{'memberId':'m','valueId':null,'typeArguments':['int']}", "typeArguments[0] must be a type binding object")]
        [TestCase("{'memberId':'m','valueId':null,'typeArguments':[" + OpenBinding + "]}", "only a Type payload holds an open type")]
        [TestCase("{'memberId':'m','valueId':null,'typeArguments':[{'kind':0,'type':{'type':3,'required':false}}]}", "no nullable reference")]
        [TestCase("{'memberId':'m','valueId':null,'staticConstruction':'c:box<int>'}", "staticConstruction must be a type binding object")]
        [TestCase("{'memberId':'m','valueId':null,'staticConstruction':" + OpenBinding + "}", "only a Type payload holds an open type")]
        [TestCase("{'memberId':'m','valueId':null,'staticConstruction':" + IntBinding + "}", "names a Int; it must be a closed Class or Interface")]
        [TestCase("{'memberId':'m','valueId':null,'interfaceConstruction':" + BoxOfInt + "}", "names a Class; it must be a closed Interface")]
        [TestCase("{'memberId':'m','valueId':null,'delegateType':" + IntBinding + "}", "names a Int; it must be a closed NSDelegate")]
        [TestCase("{'memberId':'m','valueId':null,'delegateTypeKey':'NeoDelegate<int>'}", "exactly 'memberId' and 'valueId'")]
        [TestCase("{'memberId':'m','valueId':null,'site':'m:getter:0'}", "exactly 'memberId' and 'valueId'")]
        [TestCase("{" + Body + ",'declaringConstruction':" + BoxOfInt + "}", "requires a site")]
        [TestCase("{" + Body + ",'declaringConstruction':" + ListOfString + ",'site':'m:getter:0'}", "names a List; it must be a closed Class or Interface")]
        [TestCase("{" + Body + ",'delegateTypeKey':'NeoDelegate<int>'}", "must contain exactly 'action'")]
        [TestCase("{" + Body + ",'site':'m:widget:0'}", "unknown body")]
        [TestCase("{" + Body + ",'site':'m:getter:01'}", "non-negative integer")]
        [TestCase("{" + Body + ",'site':'a/b:getter:0'}", "must be one id")]
        [TestCase("{" + Body + ",'site':'a/b:dialogue:0'}", "three '/'-separated parts")]
        [TestCase("{" + Body + ",'placement':'heap'}", "placement must be")]
        [TestCase("{" + Body + ",'staticConstruction':" + BoxOfInt + "}", "must contain exactly 'action'")]
        public void MalformedDelegateFieldsAreRefused(string json, string message)
        {
            Assert.That(
                Assert.Throws<JsonSerializationException>(() => ReadDelegate(json))!.Message,
                Does.Contain(message));
        }

        [Test]
        public void ListenerIdentityKeepsTheBaseFormAndSeparatesConstructions()
        {
            var plain = new NeoDelegateValue { memberId = "m", valueId = "v" };
            Assert.That(NeoActionValue.ListenerIdentity(plain), Is.EqualTo("m\0v"));
            var ofInt = new NeoDelegateValue { memberId = "m", valueId = "v", interfaceConstruction = Equatable(TestTypeBindings.Of(MemberKind.Int)) };
            var ofString = new NeoDelegateValue { memberId = "m", valueId = "v", interfaceConstruction = Equatable(TestTypeBindings.Of(MemberKind.String)) };
            var typed = new NeoDelegateValue { memberId = "m", valueId = "v", typeArguments = new[] { TestTypeBindings.Of(MemberKind.Int) } };
            var throughStatic = new NeoDelegateValue { memberId = "m", valueId = "v", staticConstruction = TestTypeBindings.Class("box", true, ("t", TestTypeBindings.Of(MemberKind.Int))) };
            var identities = new HashSet<string>
            {
                NeoActionValue.ListenerIdentity(plain),
                NeoActionValue.ListenerIdentity(ofInt),
                NeoActionValue.ListenerIdentity(ofString),
                NeoActionValue.ListenerIdentity(typed),
                NeoActionValue.ListenerIdentity(throughStatic),
            };
            Assert.That(identities, Has.Count.EqualTo(5));
            Assert.That(
                NeoActionValue.ListenerIdentity(ofInt),
                Is.EqualTo(NeoActionValue.ListenerIdentity(RoundTrip(ofInt))));
        }

        [Test]
        public void ListenersDifferingOnlyInAConstructionAreDistinct()
        {
            string Listener(string construction) => "{'memberId':'m','valueId':'v','interfaceConstruction':" + construction + "}";
            string EquatableOf(string argument) => "{'kind':0,'type':{'type':22,'required':true,'interfaceId':'equatable'},'bindings':{'t':" + argument + "}}";
            NeoActionValue action = JsonConvert.DeserializeObject<NeoActionValue>(
                "{'listeners':[" + Listener(EquatableOf(IntBinding)) + "," + Listener(EquatableOf(StringBinding)) + "]}")!;
            Assert.That(action.listeners, Has.Count.EqualTo(2));
            Assert.That(action.listeners[1].interfaceConstruction, Is.EqualTo(Equatable(TestTypeBindings.Of(MemberKind.String))));
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<NeoActionValue>(
                "{'listeners':[" + Listener(EquatableOf(IntBinding)) + "," + Listener(EquatableOf(IntBinding)) + "]}"));
            Assert.That(error!.Message, Does.Contain("duplicates an earlier listener identity"));
            // The identity is an in-memory key; the written set carries only the stored fields.
            string written = JsonConvert.SerializeObject(action);
            Assert.That(written, Does.Not.Contain("\u0000").And.Not.Contain("\\u0000"));
            Assert.That(JToken.DeepEquals(JToken.Parse(written), JToken.FromObject(RoundTrip(action))), Is.True);
        }

        [Test]
        public void ListenersRefuseADelegateType()
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<NeoActionValue>(
                "{'listeners':[{'memberId':'m','valueId':'v','delegateType':" + DelegateOfInt + "}]}"));
            var listener = new NeoDelegateValue { memberId = "m", valueId = "v", delegateType = TestTypeBindings.Of(MemberKind.NSDelegate) };
            Assert.Throws<JsonSerializationException>(() => JsonConvert.SerializeObject(new NeoActionValue { listeners = { listener } }));
            var action = JsonConvert.DeserializeObject<NeoActionValue>(
                "{'listeners':[{'memberId':'m','valueId':'v','staticConstruction':" + BoxOfInt + "}]}")!;
            Assert.That(action.listeners[0].staticConstruction, Is.EqualTo(TestTypeBindings.Class("box", true, ("t", TestTypeBindings.Of(MemberKind.Int)))));
        }

        // §1.3: a held built-in comparer's valueType names the public
        // system class it came from. The ids stand in for the system schema's.
        private const string EqualityComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e01";
        private const string ComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e02";
        private const string ReferenceEqualityComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e03";

        private static readonly string ComparerClassesJson = $@"{{
            '{EqualityComparerId}': {{ 'id': '{EqualityComparerId}', 'name': 'EqualityComparer', 'genericParams': [{{ 'id': 't1', 'name': 'T' }}] }},
            '{ComparerId}': {{ 'id': '{ComparerId}', 'name': 'Comparer', 'genericParams': [{{ 'id': 't2', 'name': 'T' }}] }},
            '{ReferenceEqualityComparerId}': {{ 'id': '{ReferenceEqualityComparerId}', 'name': 'ReferenceEqualityComparer' }},
            'money': {{ 'id': 'money', 'name': 'Money' }},
            'impostor': {{ 'id': 'impostor', 'name': 'EqualityComparer', 'genericParams': [{{ 'id': 't', 'name': 'T' }}] }}
        }}";

        private static ProjectData ComparerSchema() =>
            new()
            {
                classes = JObject.Parse(ComparerClassesJson).ToObject<Dictionary<string, NeoSchemaClass>>()!
            };

        /// <summary>
        /// A held comparer row whose <c>valueType</c> and payload <c>type</c>
        /// are spelled as runtime type keys and stored as the bindings they name.
        /// </summary>
        private static MemberValue ReadHeldComparer(string heldKey, string payload)
        {
            NeoSystemComparerClasses.Register(JObject.Parse(ComparerClassesJson));
            ProjectData schema = ComparerSchema();
            JObject value = JObject.Parse(payload);
            if (value["type"] is JValue { Type: JTokenType.String } type)
                value["type"] = TestTypeBindings.JsonFromKey((string)type!, schema);
            var row = new JObject
            {
                ["id"] = "held",
                ["valueType"] = TestTypeBindings.JsonFromKey(heldKey, schema),
                ["value"] = value,
            };
            return JsonConvert.DeserializeObject<MemberValue>(row.ToString())!;
        }

        private const string Delegate = "{'memberId':'m','valueId':null}";

        [TestCase("c:" + EqualityComparerId + "<int>", "{'comparer':0,'family':0,'type':'int'}")]
        [TestCase("c:" + EqualityComparerId + "<string>", "{'comparer':3,'family':0,'type':'string','delegates':[" + Delegate + "]}")]
        [TestCase("c:" + EqualityComparerId + "<Set<int>>", "{'comparer':2,'family':0,'type':'int'}")]
        [TestCase("c:" + ComparerId + "<c:money>", "{'comparer':0,'family':1,'type':'c:money'}")]
        [TestCase("c:" + ComparerId + "<decimal>", "{'comparer':3,'family':1,'type':'decimal','delegates':[" + Delegate + "]}")]
        [TestCase("c:" + ReferenceEqualityComparerId, "{'comparer':1,'family':0}")]
        public void HeldBuiltInComparersStoreTheirPublicSystemClass(string heldKey, string payload)
        {
            MemberValue row = ReadHeldComparer(heldKey, payload);
            Assert.That(row, Is.InstanceOf<PayloadMemberValue>());
            Assert.That(row.valueType, Is.EqualTo(TestTypeBindings.FromKey(heldKey, ComparerSchema())));
        }

        [TestCase("c:" + ComparerId + "<int>", "{'comparer':0,'family':0,'type':'int'}", "comes from EqualityComparer<T>")]
        [TestCase("c:" + EqualityComparerId + "<int>", "{'comparer':3,'family':1,'type':'int','delegates':[" + Delegate + "]}", "comes from Comparer<T>")]
        [TestCase("c:" + EqualityComparerId + "<object>", "{'comparer':1,'family':0}", "comes from ReferenceEqualityComparer")]
        [TestCase("c:" + ReferenceEqualityComparerId, "{'comparer':0,'family':0,'type':'int'}", "comes from EqualityComparer<T>")]
        [TestCase("c:" + EqualityComparerId + "<int>", "{'comparer':0,'family':0,'type':'string'}", "'valueType' must be EqualityComparer bound to the comparer's 'type'")]
        [TestCase("c:" + EqualityComparerId + "<int>", "{'comparer':2,'family':0,'type':'int'}", "'valueType' must be EqualityComparer bound to Set<T>, with T the comparer's 'type'")]
        [TestCase("c:" + EqualityComparerId + "<Set<string>>", "{'comparer':2,'family':0,'type':'int'}", "'valueType' must be EqualityComparer bound to Set<T>, with T the comparer's 'type'")]
        [TestCase("c:" + EqualityComparerId + "<Set<int>>", "{'comparer':2,'family':0,'type':'Set<int>'}", "'valueType' must be EqualityComparer bound to Set<T>, with T the comparer's 'type'")]
        [TestCase("c:" + ComparerId + "<c:money>", "{'comparer':3,'family':1,'type':'int','delegates':[" + Delegate + "]}", "'valueType' must be Comparer bound to the comparer's 'type'")]
        [TestCase("c:money", "{'comparer':1,'family':0}", "'valueType' class 'money' is no built-in comparer class")]
        [TestCase("c:" + EqualityComparerId + "<int>", "{'invariant':true}", "'comparer' must be a NeoBuiltInComparerKind ordinal")]
        [TestCase("c:" + EqualityComparerId + "<int>", "{'comparer':0,'family':0}", "needs its closed 'type'")]
        public void HeldComparerPayloadMustAgreeWithItsValueType(string heldKey, string payload, string message)
        {
            var error = Assert.Throws<JsonSerializationException>(() => ReadHeldComparer(heldKey, payload));
            Assert.That(error!.Message, Does.Contain(message));
        }

        [Test]
        public void OnlySystemClassesNamedLikeAComparerRegister()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                ReadHeldComparer("c:impostor<int>", "{'comparer':0,'family':0,'type':'int'}"));
            Assert.That(error!.Message, Does.Contain("'valueType' class 'impostor' is no built-in comparer class"));
        }

        [Test]
        public void SemanticEqualityComparesTheNewDelegateFields()
        {
            MemberValue Row(MemberKind argument) => new DelegateMemberValue
            {
                id = "row",
                value = new NeoDelegateValue { memberId = "m", valueId = null, interfaceConstruction = Equatable(TestTypeBindings.Of(argument)) },
            };
            Assert.That(NeoSemanticJson.MemberRowsEqual(Row(MemberKind.Int), Row(MemberKind.Int)), Is.True);
            Assert.That(NeoSemanticJson.MemberRowsEqual(Row(MemberKind.Int), Row(MemberKind.String)), Is.False);
        }

        [Test]
        public void SemanticEqualityComparesTheNewMemberFields()
        {
            Member Read(int accessors) => JObject.Parse(
                $"{{'id':'f','name':'F','kind':2,'declarationForm':1,'accessors':{accessors},'mutability':1}}").ToObject<Member>()!;
            Assert.That(
                NeoSemanticJson.ProjectRecordsEqual(JToken.FromObject(Read(0)), JToken.FromObject(Read(0))),
                Is.True);
            Assert.That(
                NeoSemanticJson.ProjectRecordsEqual(JToken.FromObject(Read(0)), JToken.FromObject(Read(3))),
                Is.False);
        }

        [Test]
        public void IntSlotsStoreNoNegativeZero()
        {
            // Int arithmetic can yield -0 (-1 / 5, 0 * -1); §12.1 keeps "-0"
            // to float positions, so an int row stores plain 0.
            var count = new IntMember { id = "count-field", kind = MemberKind.Int };
            var row = (NumberMemberValue)MemberValueFactory.Create(count, -0d, "count", "x", "x");
            Assert.That(double.IsNegative(row.value!.Value), Is.False);
            JToken written = JObject.Parse(JsonConvert.SerializeObject(row))["value"]!;
            Assert.That(written.Type, Is.Not.EqualTo(JTokenType.String));
            Assert.That(written.Value<double>(), Is.EqualTo(0d));

            var cell = new Vector2IntMember { id = "cell-field", kind = MemberKind.Vector2Int };
            var vector = (Vector2MemberValue)MemberValueFactory.Create(cell, new NeoVector2Value { x = -0d, y = 3 }, "cell", "x", "x");
            Assert.That(double.IsNegative(vector.value!.x), Is.False);
            JToken x = JObject.Parse(JsonConvert.SerializeObject(vector))["value"]!["x"]!;
            Assert.That(x.Type, Is.Not.EqualTo(JTokenType.String));
            Assert.That(vector.value.y, Is.EqualTo(3d));

            var aim = new Vector2Member { id = "aim-field", kind = MemberKind.Vector2 };
            var floats = (Vector2MemberValue)MemberValueFactory.Create(aim, new NeoVector2Value { x = -0d }, "aim", "x", "x");
            Assert.That(double.IsNegative(floats.value!.x), Is.True, "a float component keeps its sign");
        }

        [TestCase("{'id':'held','valueType':{'kind':0,'type':{'type':2,'required':true}},'value':-0.0}")]
        [TestCase("{'id':'held','valueType':{'kind':0,'type':{'type':2,'required':true}},'value':0.0}")]
        public void IntBoxesStoreNoNegativeZero(string json)
        {
            var row = (NumberMemberValue)JsonConvert.DeserializeObject<MemberValue>(json)!;
            Assert.That(row.value, Is.EqualTo(0d));
            Assert.That(double.IsNegative(row.value!.Value), Is.False);
            string text = JsonConvert.SerializeObject(row);
            JObject written = JObject.Parse(text);
            Assert.That(written["value"]!.Type, Is.Not.EqualTo(JTokenType.String));
            StringAssert.DoesNotContain("-0", text);
            Assert.That(JToken.DeepEquals(written["valueType"], TestTypeBindings.JsonFromKey("int")), Is.True);
        }

        [Test]
        public void Vector2IntBoxesStoreNoNegativeZero()
        {
            var row = (Vector2MemberValue)JsonConvert.DeserializeObject<MemberValue>(
                "{'id':'held','valueType':{'kind':0,'type':{'type':15,'required':true}},'value':{'x':-0.0,'y':1}}")!;
            Assert.That(double.IsNegative(row.value!.x), Is.False);
            string text = JsonConvert.SerializeObject(row);
            Assert.That(JObject.Parse(text)["value"]!["x"]!.Type, Is.Not.EqualTo(JTokenType.String));
            StringAssert.DoesNotContain("-0", text);
        }
    }
}
