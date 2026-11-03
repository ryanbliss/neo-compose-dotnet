// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    public sealed class P127GlobalizationEvaluatorTests
    {
        private NeoClient client = null!;
        private NSGetterEvaluator.Context ctx = null!;
        private NeoScriptScope scope = null!;

        [SetUp]
        public void SetUp()
        {
            client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-types-parity-export.json")), assumeCurrentSchema: false);
            ctx = new NSGetterEvaluator.Context(client, null, null);
            scope = new NeoScriptScope();
        }

        [TearDown]
        public void TearDown() => client.Dispose();

        private static Pointer Value(object? value) => new ValuePointer { type = PointerKind.Value, primitiveResolved = true, primitive = value };
        private object? Eval(Pointer pointer) => NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);
        private object? Primitive(TypeInfo type, string member, object? receiver, params object?[] args) => Eval(new PrimitiveMemberPointer
        {
            type = PointerKind.PrimitiveMember,
            primitive = type,
            member = member,
            receiver = receiver is null ? null : Value(receiver),
            args = args.Select(Value).ToArray(),
        });
        private object? ObjectMember(string member, object receiver, params object?[] args) => Eval(new ObjectMemberPointer
        {
            type = PointerKind.ObjectMember,
            member = member,
            receiver = Value(receiver),
            args = args.Select(Value).ToArray(),
        });
        private static TypeInfo Type(MemberKind kind) => new PrimitiveTypeInfo { type = kind, required = true };

        private GlobalizationMemberPointer GlobalPointer(string owner, string member, object? receiver = null, params object?[] args) => new()
        {
            type = PointerKind.GlobalizationMember,
            owner = owner,
            member = member,
            receiver = receiver is null ? null : Value(receiver),
            args = args.Select(Value).ToArray(),
        };
        private object? Global(string owner, string member, object? receiver = null, params object?[] args) => Eval(GlobalPointer(owner, member, receiver, args));
        private void Set(string owner, string member, object receiver, object? value) => NeoScriptExecutor.Execute(client, new FunctionWithReturnType
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            typeInfo = Type(MemberKind.Null),
            parameters = Array.Empty<Variable>(),
            instructions = new Instruction[] { new AssignInstruction
            {
                type = InstructionKind.Assign, operatorValue = "=", pointer = Value(value),
                target = new WriteTarget { pointer = GlobalPointer(owner, member, receiver), typeInfo = Type(MemberKind.Object), writability = WritabilityKind.Setter },
            } },
        }, scope, ctx);

        [Test]
        public void GlobalizationPointerConstructsReadsWritesAndClones()
        {
            var culture = (CultureInfo)Global("CultureInfo", ".ctor", null, "")!;
            var info = (NumberFormatInfo)Global("NumberFormatInfo", ".ctor")!;
            Set("CultureInfo", "NumberFormat", culture, info);
            Assert.That(Global("CultureInfo", "NumberFormat", culture), Is.SameAs(info));
            Set("NumberFormatInfo", "NegativeSign", info, "~");
            Set("NumberFormatInfo", "NumberGroupSizes", info, new object?[] { 2d, 3d, 0d });
            Assert.That(Global("NumberFormatInfo", "NegativeSign", info), Is.EqualTo("~"));
            Assert.That(Global("NumberFormatInfo", "NumberGroupSizes", info), Is.EqualTo(new object?[] { 2d, 3d, 0d }));
            var digits = new object?[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            Set("NumberFormatInfo", "NativeDigits", info, digits);
            var clone = (NumberFormatInfo)Global("NumberFormatInfo", "Clone", info)!;
            digits[0] = "x";
            Assert.That(((string?[])Global("NumberFormatInfo", "NativeDigits", clone)!)[0], Is.EqualTo("x"));
            var shape = (object?[])Global("NumberFormatInfo", "DigitSubstitution", info)!;
            Set("NumberFormatInfo", "DigitSubstitution", clone, shape);
            Assert.That(clone.DigitSubstitution, Is.EqualTo(info.DigitSubstitution));
            Assert.That(Global("CultureInfo", "Parent", culture), Is.SameAs(CultureInfo.InvariantCulture));
            Assert.That(Global("CultureInfo", "GetCultureInfo", null, ""), Is.SameAs(Global("CultureInfo", "GetCultureInfo", null, "")));
            Assert.That(Global("NumberFormatInfo", "GetInstance", null, culture), Is.SameAs(info));
            Assert.That(Global("CultureInfo", "Clone", culture), Is.Not.SameAs(culture));
        }

        [TestCase("NumberDecimalDigits", -1, NeoErrorContract.NeoArgumentOutOfRangeError)]
        [TestCase("NumberGroupSizes", null, NeoErrorContract.NeoArgumentNullError)]
        [TestCase("NumberDecimalSeparator", null, NeoErrorContract.NeoInvalidOperationError)]
        [TestCase("NumberGroupSeparator", null, NeoErrorContract.NeoInvalidOperationError)]
        [TestCase("NegativeSign", null, NeoErrorContract.NeoArgumentNullError)]
        [TestCase("NativeDigits", null, NeoErrorContract.NeoInvalidOperationError)]
        public void GlobalizationSetterChecksBeforeAssigning(string member, object? value, string expected)
        {
            var error = Assert.Catch<NSGetterRuntimeError>(() => Set("NumberFormatInfo", member, NumberFormatInfo.InvariantInfo, value));
            Assert.That(error!.Payload.ClassId, Is.EqualTo(expected));
        }

        [Test]
        public void GlobalizationPointerOptionalNullSkipsArguments()
        {
            var pointer = GlobalPointer("CultureInfo", "GetFormat");
            pointer.receiver = Value(null);
            pointer.optional = true;
            pointer.args = new Pointer[] { new ReferencePointer { type = PointerKind.Reference, valueId = "missing" } };
            Assert.That(Eval(pointer), Is.Null);
            pointer.optional = null;
            Assert.That(Assert.Catch<NSGetterRuntimeError>(() => Eval(pointer))!.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
        }

        [Test]
        public void NativeDigitsRetainsTheListAcrossBackingArrayChanges()
        {
            var info = new NumberFormatInfo();
            var digits = new object?[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            Set("NumberFormatInfo", "NativeDigits", info, digits);
            var clone = (NumberFormatInfo)Global("NumberFormatInfo", "Clone", info)!;
            var changed = (object?[])digits.Clone();
            changed[0] = "x";
            NSGetterEvaluator.LinkRuntimeListSnapshot(digits, changed);
            Assert.That(((string?[])Global("NumberFormatInfo", "NativeDigits", info)!)[0], Is.EqualTo("x"));
            Assert.That(((string?[])Global("NumberFormatInfo", "NativeDigits", clone)!)[0], Is.EqualTo("x"));
            Assert.That(NeoStorablePayloads.Encode(info)["nativeDigits"]![0]!.Value<string>(), Is.EqualTo("x"));
        }

        [Test]
        public void LegacyNumericTextUsesGlobalizationHelpers()
        {
            object? Text(string number, string op, params object?[] args) => Eval(new FunctionPointer
            {
                type = PointerKind.Function,
                function = new TextOpFunction { type = FunctionKind.TextOp, info = new FunctionTextOpInfo { number = number, op = op, argPointers = args.Select(Value).ToArray() } },
            });
            Assert.That(Text("int", TextOpKind.Format, 255d, "X"), Is.EqualTo("FF"));
            Assert.That(Text("float", TextOpKind.Parse, "1e1000"), Is.EqualTo(double.PositiveInfinity));
            Assert.That(Text("float", TextOpKind.TryParse, "NaN"), Is.NaN);
            Assert.That(Text("decimal", TextOpKind.Parse, "1,234.5"), Is.EqualTo("1234.5"));
            Assert.That(Text("int", TextOpKind.TryParse, "garbage"), Is.Null);
            Assert.That(Primitive(Type(MemberKind.String), "ToString", "text", new Provider()), Is.EqualTo("text"));
            Assert.That(Primitive(Type(MemberKind.Bool), "ToString", true, new Provider()), Is.EqualTo("True"));
        }

        private sealed class Provider : IFormatProvider
        {
            internal int Calls;
            internal System.Type? LastType;
            internal readonly NumberFormatInfo Info = new NumberFormatInfo { NumberDecimalSeparator = ":", NegativeSign = "~" };
            public object GetFormat(System.Type? type)
            {
                LastType = type;
                Calls++;
                return type == typeof(NumberFormatInfo) ? Info : null!;
            }
        }

        [TestCase(MemberKind.Vector2, "(~1:2, 2:5)", 2)]
        [TestCase(MemberKind.Vector3, "(~1:2, 2:5, 3:8)", 3)]
        [TestCase(MemberKind.Vector2Int, "(~1, 2)", 1)]
        [TestCase(MemberKind.Vector3Int, "(~1, 2, 3)", 1)]
        [TestCase(MemberKind.Color, "RGBA(0:1, 0:2, 0:4, 1:0)", 4)]
        public void FormatsEveryUnityComponentThroughTheProvider(MemberKind kind, string expected, int calls)
        {
            object value = kind switch
            {
                MemberKind.Vector2 => new NeoVector2Value { x = -1.25, y = 2.5 },
                MemberKind.Vector3 => new NeoVector3Value { x = -1.25, y = 2.5, z = 3.75 },
                MemberKind.Vector2Int => new NeoVector2Value { x = -1, y = 2 },
                MemberKind.Vector3Int => new NeoVector3Value { x = -1, y = 2, z = 3 },
                _ => new NeoColorValue { r = .125f, g = .25f, b = .375f, a = 1 },
            };
            var provider = new Provider();
            string format = kind is MemberKind.Vector2Int or MemberKind.Vector3Int ? "D" : "F1";
            Assert.That(Primitive(Type(kind), "ToString", value, format, provider), Is.EqualTo(expected));
            Assert.That(provider.Calls, Is.EqualTo(calls));
        }

        [Test]
        public void CustomClrProviderKeepsIdentityAndMapsGetFormatTypes()
        {
            var source = new Provider();
            string interfaceId = NeoPrimitiveConformance.InterfaceId("IFormatProvider");
            var type = new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = interfaceId };
            object value = NeoScriptValueMarshaller.NormalizeResolved(client, NeoValueOwnership.Session, source, type, ctx, "provider")!;
            Assert.That(NeoScriptValueMarshaller.NormalizeResolved(client, NeoValueOwnership.Session, source, type, ctx, "provider"), Is.SameAs(value));
            Assert.That(NSGetterEvaluator.ResolveHostCollection(value, ctx), Is.SameAs(source));
            Assert.That(Primitive(Type(MemberKind.Float), "ToString", 1.5, "F1", value), Is.EqualTo("1:5"));
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.That(Primitive(Type(MemberKind.Int), "ToString", 5, "D", value), Is.EqualTo("5"));
            Assert.That(source.Calls, Is.EqualTo(1));
            Assert.Throws<NeoRuntimeError>(() => Primitive(Type(MemberKind.Int), "ToString", 5, "Q", value));
            Assert.That(source.Calls, Is.EqualTo(2));
            object? GetFormat(object? requested) => Eval(new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                receiver = CallReceiver.Instance(Value(value)),
                interfaceMember = new InterfaceMemberRef { interfaceId = interfaceId, memberId = client.ProjectDataForRuntime.interfaces[interfaceId].members["GetFormat"].id! },
                args = new[] { Value(requested) },
            });
            var numberFormatType = NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo)));
            Assert.That(GetFormat(numberFormatType), Is.SameAs(source.Info));
            Assert.That(source.LastType, Is.EqualTo(typeof(NumberFormatInfo)));
            Assert.That(GetFormat(NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.String, true)))), Is.Null);
            Assert.That(source.LastType, Is.EqualTo(typeof(string)));
            Assert.That(GetFormat(null), Is.Null);
            Assert.That(source.LastType, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(5));
            source.Info.NumberDecimalSeparator = "!";
            Assert.That(Primitive(Type(MemberKind.Float), "ToString", 1.5, "F1", value), Is.EqualTo("1!5"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClrCultureProvidersCrossInterfaceArgumentsWithoutCopying(bool culture)
        {
            object original = culture ? (object)new CultureInfo("fr-FR") : new NumberFormatInfo();
            var type = new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = NeoPrimitiveConformance.InterfaceId("IFormatProvider") };
            object? normalized = NeoScriptValueMarshaller.NormalizeResolved(client, NeoValueOwnership.Session, original, type, ctx, "provider");
            Assert.That(normalized, Is.SameAs(original));
            NeoScriptValueMarshaller.ValidateResolvedRuntimeValue(client, normalized, type, ctx, "provider return");
            Assert.That(NSGetterEvaluator.ResolveHostCollection(normalized, ctx), Is.SameAs(original));
            var formatType = NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo)));
            var runtimeType = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = culture ? NeoObjectTypeKind.CultureInfo : NeoObjectTypeKind.NumberFormatInfo };
            Assert.That(Primitive(runtimeType, "GetFormat", original, formatType), Is.SameAs(culture ? ((CultureInfo)original).NumberFormat : original));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void HostProvidersRoundTripThroughTypedFunctionArgumentsAndReturns(int kind, bool throughInterface)
        {
            object source = kind == 0 ? (object)new CultureInfo("fr-FR") : kind == 1 ? new NumberFormatInfo() : new Provider();
            var argument = new FunctionArgumentTypeInfo
            {
                name = "provider",
                required = true,
                type = throughInterface ? MemberKind.Interface : MemberKind.Object,
                interfaceId = throughInterface ? NeoPrimitiveConformance.InterfaceId("IFormatProvider") : null,
                objectType = throughInterface ? null : kind == 0 ? NeoObjectTypeKind.CultureInfo : NeoObjectTypeKind.NumberFormatInfo,
            };
            TypeInfo returns = throughInterface
                ? new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = argument.interfaceId! }
                : new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = argument.objectType };
            var variable = new VariablePointer { type = PointerKind.Variable, variableId = "__arg_0__" };
            var function = new NSFunctionMember
            {
                id = "b7-provider-identity",
                name = "ProviderIdentity",
                projectId = client.ProjectDataForRuntime.project.id,
                kind = MemberKind.NSFunction,
                Modifier = NeoMemberModifierKind.Static,
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                code = "compiled test function",
                argumentTypes = new[] { argument },
                returnTypeInfo = returns,
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    typeInfo = returns,
                    parameters = new[] { new Variable { id = "__arg_0__", typeInfo = argument, pointer = variable } },
                    instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = variable } },
                },
            };
            var data = NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-types-parity-export.json"));
            data.members[function.id] = function;
            var declaringClass = data.classes.Values.Single(type => type.name == "Probe");
            var receiverParameters = declaringClass.schema.Values.Select(id => data.members[id])
                .OfType<NSFunctionMember>().First(member => member.action is not null).action!.parameters.Take(2);
            function.action.parameters = receiverParameters.Concat(function.action.parameters).ToArray();
            declaringClass.schema[function.name] = function.id;
            declaringClass.schemaKeyOrder = declaringClass.schema.Keys.ToArray();
            using var host = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            var callable = NeoMemberNSFunction.GetOrCreateStatic(host, function.id);
            Assert.That(callable.InvokeStatic(new[] { source }), Is.SameAs(source));
            Assert.That(callable.InvokeStatic(new[] { source }), Is.SameAs(source));
        }

        [Test]
        public void CultureObjectMembersUseTheClrInstanceAndDotNetEqualityBody()
        {
            var a = new CultureInfo("");
            var b = new CultureInfo("");
            b.NumberFormat.NegativeSign = "~";
            Assert.That(ObjectMember(ObjectMemberKind.Equals, a, b), Is.True);
            Assert.That(ObjectMember(ObjectMemberKind.GetHashCode, a), Is.EqualTo(a.GetHashCode()));
            Assert.That(ObjectMember(ObjectMemberKind.ToString, a), Is.EqualTo(""));
            var french = new CultureInfo("fr-FR");
            Assert.That(ObjectMember(ObjectMemberKind.Equals, french, a), Is.False);
            Assert.That(ObjectMember(ObjectMemberKind.Equals, french, new CultureInfo("fr-FR")), Is.True);
            Assert.That(ObjectMember(ObjectMemberKind.ToString, french), Is.EqualTo("fr-FR"));
            Assert.That(ObjectMember(ObjectMemberKind.Equals, a.NumberFormat, b.NumberFormat), Is.False);
            Assert.That(ObjectMember(ObjectMemberKind.Equals, a.NumberFormat, a.NumberFormat), Is.True);
            Assert.That(ObjectMember(ObjectMemberKind.ToString, a.NumberFormat), Is.EqualTo("System.Globalization.NumberFormatInfo"));
        }
    }
}
