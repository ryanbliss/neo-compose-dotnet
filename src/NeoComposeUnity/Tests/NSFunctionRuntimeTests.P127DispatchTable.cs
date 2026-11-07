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
                ("HolderExactCastD1", "A-body", "D1"),
                // Internal fallback coverage. C# rejects a concrete class with
                // this unimplemented exact abstract slot (CS0535).
                ("AbstractExactThenVariant", "K-body", "D1"),
                ("MapOrder", "Q-body", "D1"), ("MapOrderReverse", "K-body", "D2"),
                ("MapOrderClass", "Q-body", "D1"),
                ("RelistIntermediate", "middle-public", ""), ("UnlistedIntermediate", "base-1", ""),
                ("RelistTwoIntermediates", "middle-public", ""), ("RelistTwice", "middle-public", ""),
                ("RelistIntermediateLists", "middle-public", ""), ("RelistBeforeIntermediate", "middle-public", ""),
                ("RelistVirtual", "override-public", ""), ("RelistOverrideBelow", "override-public", ""),
                ("RelistOwnOverride", "receiver-public", ""),
                ("RelistExactBeforeVariant", "middle-public", ""), ("RelistViaSubinterface", "middle-public", ""),
                ("RelistVariant", "middle-public", ""), ("RelistNewExact", "middle-public", ""),
                ("RelistNewVariant", "middle-public", ""), ("RelistPublicBase", "middle-public", ""),
                ("KeepPublicLister", "receiver-2", ""), ("KeepOverrideLister", "receiver-2", ""),
                ("KeepInheritedVirtual", "receiver-2", ""),
                ("RelistAccessorSplit", "middle-public", ""),
                ("RelistFitClosed", "middle-public", ""), ("RelistFitOpen", "base-1", ""),
                ("RelistFitWrong", "base-1", ""),
                ("RelistMergeBetween", "middle-public", ""), ("RelistMergeVirtualBetween", "middle-public", ""),
                ("RelistMergeBetweenDistinct", "middle-public", ""),
                ("RelistMergeOverride", "merge-5", ""), ("RelistMergeDoubleOverride", "merge-5", ""),
                ("RelistMergeOverrideDistinct", "override-public", ""),
                ("RelistMergeAboveLister", "merge-5", ""),
                ("ArgumentInterfaceOut", "receiver-2", ""), ("ArgumentInterfaceIn", "receiver-2", ""),
                ("ArgumentClassToInterfaceOut", "receiver-2", ""), ("ArgumentClassToInterfaceIn", "receiver-2", ""),
                ("ArgumentInheritedClassToInterfaceOut", "receiver-2", ""), ("ArgumentInheritedClassToInterfaceIn", "receiver-2", ""),
                ("ArgumentConstructedOut", "receiver-2", ""), ("ArgumentConstructedReject", "missing", ""),
                ("RelistAccessorOverrideGet", "receiver-2", ""), ("RelistAccessorOverrideGetWireDefault", "receiver-2", ""), ("RelistAccessorOverrideSet", "middle-public", "")
            };
            foreach (var row in rows)
                foreach (string operation in new[] { "get", "call", "generic", "set", "static" })
                {
                    if (row.Shape.StartsWith("RelistAccessorOverride", StringComparison.Ordinal) && operation is not ("get" or "set"))
                        continue;
                    string form = operation == "static" && (row.Shape.StartsWith("Holder", StringComparison.Ordinal) || row.Shape.Contains("Relist") || row.Shape.StartsWith("Keep", StringComparison.Ordinal) || row.Shape.StartsWith("MapOrder", StringComparison.Ordinal) || row.Shape == "UnlistedIntermediate") ? "static-call" : operation;
                    string body = operation == "set" && row.Shape is "RelistAccessorOverrideGet" or "RelistAccessorOverrideGetWireDefault" ? "middle-public"
                        : operation == "set" && row.Shape == "RelistAccessorOverrideSet" ? "receiver-2"
                        : operation == "static" && row.Shape == "KeepOverrideLister" ? "override-public"
                        : operation == "static" && row.Shape == "KeepInheritedVirtual" ? "middle-public"
                        : operation == "static" && row.Shape == "RelistMergeOverrideDistinct" ? "base-public"
                        : operation == "set" && row.Shape == "RelistAccessorSplit" ? "base-1"
                        : operation == "static" && row.Shape is "RelistMergeBetween" or "RelistMergeVirtualBetween" ? "merge-7" : row.Body;
                    yield return new TestCaseData(row.Shape, form, body, row.Frame)
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
            TypeBinding Type(string id) => id == "TagConstructed"
                ? TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "TagGeneric"),
                    new Dictionary<string, TypeBinding> { ["TagGeneric-T"] = TestTypeBindings.Class(shape == "ArgumentConstructedReject" ? "D2" : "Ba") })
                : id.StartsWith("Tag", StringComparison.Ordinal)
                    ? TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id)) : TestTypeBindings.Class(id);
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
            Interface("Q", 8, "I");
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
            void Public(string owner, bool virtualMember = false, string? overridden = null)
            {
                var body = Body(owner + "-public", owner == "override" ? 7 : 5);
                body.Access = NeoMemberAccessKind.Public;
                body.Modifier = isStatic ? NeoMemberModifierKind.Static : virtualMember ? NeoMemberModifierKind.Virtual : NeoMemberModifierKind.Sealed;
                body.extendsMemberId = isStatic ? null : overridden;
                classes[owner].schema["V"] = body.id;
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
                case "ArgumentConstructedOut":
                case "ArgumentConstructedReject":
                    interfaces["TagGeneric"] = new Interface
                    {
                        id = "TagGeneric",
                        name = "TagGeneric",
                        projectId = ProjectId,
                        members = new(),
                        genericParams = new() { new() { id = "TagGeneric-T", name = "T", variance = NeoGenericParamVarianceKind.Out } }
                    };
                    var genericParent = Class("GenericImplementing");
                    genericParent.genericParams = new() { new() { id = "GenericImplementing-T", name = "T" } };
                    genericParent.implementsInterfaces = new() { Listing("TagGeneric", Param("GenericImplementing")) };
                    var closedChild = Class("Implementing", "GenericImplementing");
                    closedChild.extendsGenericBindings = new()
                    {
                        ["GenericImplementing-T"] = Type("D1")
                    };
                    view = "TagConstructed";
                    Add(r, "I", "Implementing");
                    Explicit("receiver", "Implementing", 2);
                    break;
                case "ArgumentInheritedClassToInterfaceOut":
                case "ArgumentInheritedClassToInterfaceIn":
                case "ArgumentInterfaceOut":
                case "ArgumentInterfaceIn":
                case "ArgumentClassToInterfaceOut":
                case "ArgumentClassToInterfaceIn":
                    interfaces["TagBase"] = new Interface { id = "TagBase", name = "TagBase", projectId = ProjectId, members = new() };
                    interfaces["TagDerived"] = new Interface { id = "TagDerived", name = "TagDerived", projectId = ProjectId, members = new(), extendsInterfaces = new() { new() { interfaceId = "TagBase" } } };
                    var implementing = Class("Implementing");
                    implementing.implementsInterfaces = new() { new() { interfaceId = "TagDerived" } };
                    if (shape.StartsWith("ArgumentInherited", StringComparison.Ordinal))
                    {
                        var parentImplementing = Class("ParentImplementing");
                        parentImplementing.implementsInterfaces = implementing.implementsInterfaces;
                        implementing.implementsInterfaces = new();
                        implementing.extendsClassId = parentImplementing.id;
                    }
                    bool argumentIn = shape.EndsWith("In", StringComparison.Ordinal);
                    interfaces["I"].genericParams![0].variance = argumentIn ? NeoGenericParamVarianceKind.In : NeoGenericParamVarianceKind.Out;
                    string descendant = shape.Contains("ClassToInterface") ? "Implementing" : "TagDerived";
                    view = argumentIn ? descendant : "TagBase";
                    string listedArgument = argumentIn ? "TagBase" : descendant;
                    Add(r, "I", listedArgument);
                    Explicit("receiver", listedArgument, 2);
                    break;
                case "MapOrder":
                case "MapOrderReverse":
                case "MapOrderClass":
                    Class("Other");
                    var ordered = new List<InterfaceConstruction> { Listing("K", Type("Other")) };
                    ordered.Add(Listing(shape == "MapOrderReverse" ? "K" : "Q", Type(shape == "MapOrderReverse" ? "D2" : "D1")));
                    ordered.Add(Listing(shape == "MapOrderReverse" ? "Q" : "K", Type(shape == "MapOrderReverse" ? "D1" : "D2")));
                    if (shape == "MapOrderClass")
                        r.AddRange(ordered);
                    else
                    {
                        interfaces["Order"] = new Interface { id = "Order", name = "Order", projectId = ProjectId, members = new(), extendsInterfaces = ordered };
                        r.Add(new InterfaceConstruction { interfaceId = "Order" });
                    }
                    break;
                case "RelistFitClosed":
                case "RelistFitOpen":
                case "RelistFitWrong":
                    var typeContract = interfaces["I"].members["V"];
                    var genericReturn = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerInterfaceId = "I", genericParamId = "I-T" };
                    if (function)
                        typeContract.returnTypeInfo = genericReturn;
                    else
                        typeContract.typeInfo = genericReturn;
                    Add(b, "I", "Ba");
                    Explicit("base", "Ba", 1);
                    if (function)
                        ((NSFunctionMember)records.Find(member => member.id == "base-1")!).returnTypeInfo = NeoTypeBindings.ToTypeInfo(Type("Ba"));
                    else
                        ((NSPropertyMember)records.Find(member => member.id == "base-1")!).returnTypeInfo = NeoTypeBindings.ToTypeInfo(Type("Ba"));
                    var genericMiddle = Class("middle", "base");
                    genericMiddle.genericParams = new() { new() { id = "middle-T", name = "T" } };
                    Public("middle");
                    var middleReturn = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "middle", genericParamId = "middle-T" };
                    if (function)
                        ((NSFunctionMember)records.Find(member => member.id == "middle-public")!).returnTypeInfo = middleReturn;
                    else
                        ((NSPropertyMember)records.Find(member => member.id == "middle-public")!).returnTypeInfo = middleReturn;
                    receiver.extendsClassId = "middle";
                    if (shape == "RelistFitOpen")
                        receiver.genericParams = new() { new() { id = "receiver-T", name = "T" } };
                    receiver.extendsGenericBindings = new()
                    {
                        ["middle-T"] = shape == "RelistFitOpen" ? Param("receiver") : Type(shape == "RelistFitWrong" ? "D1" : "Ba")
                    };
                    Add(r, "I", "Ba");
                    break;
                case "RelistMergeBetween":
                case "RelistMergeVirtualBetween":
                case "RelistMergeBetweenDistinct":
                case "RelistMergeOverride":
                case "RelistMergeDoubleOverride":
                case "RelistMergeOverrideDistinct":
                case "RelistMergeAboveLister":
                    interfaces["I"].genericParams![0].variance = null;
                    void MakeGeneric(NeoSchemaClass owner, bool mergeParent = false)
                    {
                        owner.genericParams = new() { new() { id = owner.id + "-T", name = "T" }, new() { id = owner.id + "-U", name = "U" } };
                        if (owner.extendsClassId is string parent)
                            owner.extendsGenericBindings = new()
                            {
                                [parent + "-T"] = Param(owner.id),
                                [parent + "-U"] = Param(owner.id, mergeParent ? "T" : "U")
                            };
                    }
                    void GenericExplicit(string owner, string param, int value)
                    {
                        Explicit(owner, "Ba", value);
                        records.Find(member => member.id == owner + "-" + value)!.explicitImplementation!.arguments = Listing("I", Param(owner, param)).arguments;
                    }
                    MakeGeneric(classes["base"]);
                    bool between = shape.Contains("Between");
                    bool above = shape == "RelistMergeAboveLister";
                    if (!above)
                        b.Add(Listing("I", Param("base")));
                    if (between)
                        GenericExplicit("base", "T", 1);
                    else
                        Public("base", !isStatic);
                    string mergeParent = "base";
                    if (between || above)
                    {
                        var publicMiddle = Class("middle", "base");
                        MakeGeneric(publicMiddle);
                        if (between)
                            Public("middle", shape == "RelistMergeVirtualBetween");
                        var lister = Class("lister", "middle");
                        MakeGeneric(lister);
                        lister.implementsInterfaces!.Add(Listing("I", Param("lister")));
                        mergeParent = "lister";
                    }
                    var merged = Class("merge", mergeParent);
                    MakeGeneric(merged, !between);
                    merged.implementsInterfaces!.Add(Listing("I", Param("merge", "U")));
                    GenericExplicit("merge", "U", between ? 7 : 5);
                    receiver.extendsClassId = "merge";
                    if (!between && !above)
                    {
                        var overridden = Class("override", "merge");
                        MakeGeneric(overridden);
                        Public("override", overridden: "base-public");
                        receiver.extendsClassId = "override";
                        // Static members have no override chain; the static pin
                        // is XS, with no intermediate declaration.
                        if (isStatic)
                            overridden.schema.Clear();
                        if (shape == "RelistMergeDoubleOverride")
                        {
                            var secondOverride = Class("second", "override");
                            MakeGeneric(secondOverride);
                            if (!isStatic)
                                Public("second", overridden: "override-public");
                            receiver.extendsClassId = "second";
                        }
                    }
                    MakeGeneric(receiver);
                    r.Add(Listing("I", Param("receiver")));
                    break;
                case "RelistAccessorOverrideGetWireDefault":
                case "RelistAccessorOverrideGet":
                case "RelistAccessorOverrideSet":
                    Add(b, "I", "Ba");
                    Explicit("base", "Ba", 1);
                    Class("middle", "base");
                    Public("middle", true);
                    var bothAccessors = (NSPropertyMember)records.Find(member => member.id == "middle-public")!;
                    bothAccessors.Accessors = NeoPropertyAccessorsKind.GetSet;
                    bothAccessors.setterCode = "compiled setter";
                    bothAccessors.setter = Action(new PrimitiveTypeInfo { type = MemberKind.Null, required = true }, Array.Empty<FunctionArgumentTypeInfo>());
                    Class("override", "middle", Listing("I", Type("Ba")));
                    Public("override", overridden: "middle-public");
                    var splitOverride = (NSPropertyMember)records.Find(member => member.id == "override-public")!;
                    splitOverride.Accessors = shape != "RelistAccessorOverrideSet" ? NeoPropertyAccessorsKind.Get : NeoPropertyAccessorsKind.Set;
                    if (shape == "RelistAccessorOverrideSet")
                        splitOverride.getter = null!;
                    else
                        splitOverride.setter = null;
                    if (shape == "RelistAccessorOverrideGetWireDefault")
                        splitOverride.DeclaredAccessors = null; // Wire-default Get; materialized setterCode belongs to the base.
                    receiver.extendsClassId = "override";
                    Add(r, "I", "Ba");
                    Add(r, "I", "D1");
                    Explicit("receiver", "D1", 2);
                    break;
                case "RelistAccessorSplit":
                case "RelistIntermediate":
                case "UnlistedIntermediate":
                case "RelistTwoIntermediates":
                case "RelistTwice":
                case "RelistIntermediateLists":
                case "RelistBeforeIntermediate":
                case "RelistVirtual":
                case "RelistOverrideBelow":
                case "RelistOwnOverride":
                case "RelistExactBeforeVariant":
                case "RelistViaSubinterface":
                case "RelistVariant":
                case "RelistNewExact":
                case "RelistNewVariant":
                case "RelistPublicBase":
                case "KeepPublicLister":
                case "KeepOverrideLister":
                case "KeepInheritedVirtual":
                    string relistedBaseArgument = shape is "RelistVariant" or "RelistNewExact" or "RelistNewVariant" ? "D1" : "Ba";
                    Add(b, "I", relistedBaseArgument);
                    Explicit("base", relistedBaseArgument, 1);
                    var mid = Class("middle", "base");
                    receiver.extendsClassId = "middle";
                    bool virtualPublic = shape is "RelistVirtual" or "RelistOverrideBelow" or "RelistOwnOverride" or "KeepOverrideLister" or "KeepInheritedVirtual";
                    Public("middle", virtualPublic);
                    if (shape == "RelistAccessorSplit" && operation == "set")
                    {
                        var getterOnly = (NSPropertyMember)records.Find(member => member.id == "middle-public")!;
                        getterOnly.Accessors = NeoPropertyAccessorsKind.Get;
                        getterOnly.setter = null;
                    }
                    if (shape == "RelistPublicBase")
                    {
                        classes["base"].schema.Clear();
                        Public("base");
                    }
                    if (shape == "KeepInheritedVirtual")
                    {
                        classes["base"].schema.Clear();
                        Public("base", true);
                        if (!isStatic)
                            records.Find(member => member.id == "middle-public")!.extendsMemberId = "base-public";
                    }
                    if (shape is "RelistIntermediateLists" or "KeepPublicLister" or "KeepOverrideLister")
                        Add(mid.implementsInterfaces!, "I", "Ba");
                    if (shape == "RelistBeforeIntermediate")
                    {
                        Class("before", "base", Listing("I", Type("Ba")));
                        mid.extendsClassId = "before";
                    }
                    if (shape is "RelistTwoIntermediates" or "RelistTwice")
                    {
                        var interveningClass = Class("between", "middle");
                        receiver.extendsClassId = "between";
                        if (shape == "RelistTwice")
                            Add(interveningClass.implementsInterfaces!, "I", "Ba");
                    }
                    if (shape is "RelistVirtual" or "RelistOverrideBelow" or "KeepOverrideLister")
                    {
                        Class("override", "middle");
                        receiver.extendsClassId = "override";
                        Public("override", overridden: "middle-public");
                        if (shape == "RelistOverrideBelow")
                        {
                            Add(mid.implementsInterfaces!, "I", "Ba");
                        }
                    }
                    if (shape == "RelistOwnOverride")
                        Public("receiver", overridden: "middle-public");
                    if (shape != "UnlistedIntermediate")
                        Add(r, shape == "RelistViaSubinterface" ? "K" : "I", shape is "RelistVariant" or "RelistNewVariant" ? "D2" : "Ba");
                    if (shape == "RelistVariant")
                        r[0] = Listing("I", Type("D1"));
                    if (shape is "RelistExactBeforeVariant" or "KeepPublicLister" or "KeepOverrideLister" or "KeepInheritedVirtual")
                    {
                        Add(r, "I", "D1");
                        Explicit("receiver", "D1", 2);
                    }
                    break;
                case "AbstractExactThenVariant":
                    Add(r, "I", "Ba");
                    Add(r, "K", "D1");
                    interfaces["I"].members["V"].implementationMemberId = null;
                    interfaces["I"].members["V"].DeclaredModifier = NeoMemberModifierKind.Abstract;
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
            foreach (var record in records)
                if (record is NSFunctionMember declaredFunction && declaredFunction.returnTypeInfo is not null)
                    declaredFunction.action!.typeInfo = declaredFunction.returnTypeInfo;
            foreach (var record in records)
                if (record.extendsMemberId is not null)
                {
                    if (record is NSFunctionMember inheritedFunction)
                    {
                        inheritedFunction.returnTypeInfo = null!;
                        inheritedFunction.argumentTypes = null!;
                        inheritedFunction.DeclaredDispatch = null;
                        inheritedFunction.genericParams = null;
                    }
                    else if (record is NSPropertyMember inheritedProperty)
                        inheritedProperty.returnTypeInfo = null!;
                }
            var row = ObjectValue("receiver-value", receiver.id);
            if (receiver.genericParams is not null)
            {
                row.genericBindings = new();
                foreach (var parameter in receiver.genericParams)
                    row.genericBindings[parameter.id] = Type((shape == "HolderExactCastD1" && parameter.name == "T") || (shape.EndsWith("Distinct", StringComparison.Ordinal) && parameter.name == "U") ? "D1" : "Ba");
            }
            var receiverType = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: receiver.id), row.genericBindings);
            using var client = BuildClient(records.ToArray(), receiver, new List<NeoSchemaClass>(classes.Values).ToArray(), new[] { row }, interfaces);
            if (shape == "RelistAccessorOverrideGetWireDefault")
            {
                var loaded = (NSPropertyMember)client.ProjectDataForRuntime.members["override-public"];
                Assert.That(loaded.DeclaredAccessors, Is.Null);
                Assert.That(loaded.setterCode, Is.Not.Null, "The loader materializes the base's setter body.");
                Assert.That(loaded.DeclaresWireField("setterCode"), Is.False);
            }
            var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "I"), Listing("I", Type(view)).arguments);
            string accessor = function ? "call" : operation == "set" ? "set" : "get";
            foreach (var dispatch in new[] { NeoInterfaceDispatch.Of(client), NeoInterfaceDispatch.ForSchemaValidation(client) })
            {
                NeoInterfaceDispatch.Implementation Run() => dispatch.Resolve(receiverType, requested, "I-slot", accessor);
                if (expectedBody is "ambiguous" or "missing")
                {
                    Assert.That(Assert.Catch<NSGetterRuntimeError>(() => Run())!.Message,
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
