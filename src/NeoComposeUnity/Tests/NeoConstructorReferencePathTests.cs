// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P43 §1.1a — the supplied-ownership map an initializer writes has to be
    /// keyed by the SAME root-anchored dotted path the constructed-graph
    /// preflight looks up.
    ///
    /// <para>When the key misses, the preflight silently falls back to asking
    /// the client which store owns the id — and the client answers "Session"
    /// for any id that happens to be shadowed into the session store. The
    /// initializer's Save-owned result is then <b>re-parented</b> into the new
    /// instance instead of cloned, which is exactly the outcome the ownership
    /// map exists to prevent. A list entry makes the miss visible with the
    /// least schema: its path is <c>{root}.Items[0]</c>, and keying it by the
    /// member's name alone misses even at depth one.</para>
    /// </summary>
    public class NeoConstructorReferencePathTests
    {
        private const string ProjectId = "refpath-project";
        private const string SavedWidgetValueId = "widget-save";

        [Test]
        public void InitBackedListEntry_ClonesASaveOwnedResultInsteadOfReparenting()
        {
            NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            // The authored Save-owned widget is ALSO shadowed into the session
            // store — an everyday state once anything has written through it.
            // It is what makes an ownership lookup that ignores the recorded
            // ownership answer "Session".
            client.SetWritableValue(
                NeoValueOwnership.Session,
                new ObjectMemberValue
                {
                    id = SavedWidgetValueId,
                    classId = "widget-class",
                    value = new Dictionary<string, string>
                    {
                        ["Name"] = "widget-name-row",
                    },
                });

            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(ClassConstructorPointer("holder-class")),
                ctx);

            string? rootId = NSGetterEvaluator.FindRowIdByReference(result, ctx);
            Assert.IsNotNull(rootId, "Constructed value has no backing row.");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                rootId!,
                out ObjectMemberValue? root));
            Assert.IsTrue(
                root!.value!.TryGetValue("Items", out string itemsValueId),
                $"Constructed row has no 'Items'. Keys: {string.Join(",", root.value.Keys)}");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                itemsValueId,
                out ArrayMemberValue? items));
            Assert.AreEqual(1, items!.value!.Length);

            Assert.AreNotEqual(
                SavedWidgetValueId,
                items.value[0],
                "A Save-owned initializer result must be cloned for its new parent, not re-parented into the constructed instance.");
            Assert.IsTrue(
                client.TryGetValue(
                    NeoValueOwnership.Session,
                    items.value[0],
                    out ObjectMemberValue? entry),
                "The cloned entry must exist in the session store.");
            Assert.IsNotNull(entry!.value);
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
                typeInfo = ClassType("holder-class"),
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

        private static ClassTypeInfo ClassType(string classId)
        {
            return new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = classId,
            };
        }

        private static FunctionPointer ClassConstructorPointer(string classId)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ClassConstructorFunction
                {
                    type = FunctionKind.ClassConstructor,
                    info = new FunctionClassConstructorInfo
                    {
                        schemaClassInfo = ClassType(classId),
                        fields = Array.Empty<FunctionClassConstructorField>(),
                    },
                },
            };
        }

        // -------------------------------------------------------------------
        // Schema.
        // -------------------------------------------------------------------

        private static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var widgetClass = new NeoSchemaClass
            {
                id = "widget-class",
                projectId = ProjectId,
                name = "Widget",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "widget-name",
                },
            };
            var holderClass = new NeoSchemaClass
            {
                id = "holder-class",
                projectId = ProjectId,
                name = "Holder",
                schema = new Dictionary<string, string>
                {
                    ["Items"] = "holder-items",
                },
            };

            var widgetName = new StringMember
            {
                id = "widget-name",
                projectId = ProjectId,
                name = "Name",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
                Format = NeoStringFormatKind.Plain,
                defaultValue = new StringMemberValueBase { value = "unnamed" },
            };
            var holderItem = new ClassMember
            {
                id = "holder-item",
                projectId = ProjectId,
                name = "Item",
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                classId = widgetClass.id,
            };
            var holderItems = new ListMember
            {
                id = "holder-items",
                projectId = ProjectId,
                name = "Items",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = holderItem.id,
                defaultValue = new ArrayMemberValueBase
                {
                    value = new[] { "holder-items-entry" },
                },
            };
            // The static binding is how the initializer gets hold of a
            // Save-owned row: reading it records Save as the reference's
            // ownership, which is precisely the fact the path key has to carry
            // to the preflight.
            var savedWidget = new ClassMember
            {
                id = "static-saved-widget",
                projectId = ProjectId,
                name = "SavedWidget",
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
                classId = widgetClass.id,
                valueId = SavedWidgetValueId,
                Modifier = NeoMemberModifierKind.Static,
            };

            ClassMember rootAssets = RootMember("root-assets", "Assets", NeoMemberStorage.Immutable, "value-assets");
            ClassMember rootSave = RootMember("root-save", "Save", NeoMemberStorage.Save, "value-save");
            ClassMember rootSession = RootMember("root-session", "Session", NeoMemberStorage.Session, "value-session");

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Constructor reference path tests",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    [widgetName.id] = widgetName,
                    [holderItem.id] = holderItem,
                    [holderItems.id] = holderItems,
                    [savedWidget.id] = savedWidget,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", rootClass.id),
                    ["value-save"] = ObjectValue("value-save", rootClass.id),
                    ["value-session"] = ObjectValue("value-session", rootClass.id),
                    [SavedWidgetValueId] = new ObjectMemberValue
                    {
                        id = SavedWidgetValueId,
                        classId = widgetClass.id,
                        value = new Dictionary<string, string>
                        {
                            ["Name"] = "widget-name-row",
                        },
                    },
                    ["widget-name-row"] = new StringMemberValue
                    {
                        id = "widget-name-row",
                        value = "saved",
                    },
                    // P43 §1.1a — a computed ENTRY row: the list stays a stored
                    // literal while this entry evaluates.
                    ["holder-items-entry"] = new NullMemberValue
                    {
                        id = "holder-items-entry",
                        init = new InitializerBody
                        {
                            code = "SavedWidget",
                            compiled = new FunctionWithReturnType
                            {
                                compilerRevision =
                                    FunctionWithReturnType.CurrentCompilerRevision,
                                parameters = Array.Empty<Variable>(),
                                typeInfo = ClassType(widgetClass.id),
                                instructions = new Instruction[]
                                {
                                    new ReturnInstruction
                                    {
                                        type = InstructionKind.Return,
                                        pointer = new StaticMemberPointer
                                        {
                                            type = PointerKind.StaticMember,
                                            memberId = savedWidget.id,
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [widgetClass.id] = widgetClass,
                    [holderClass.id] = holderClass,
                },
                constructors = new Dictionary<string, ConstructorRecord>(),
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember RootMember(
            string id,
            string name,
            NeoMemberStorage storage,
            string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                classId = "root-class",
                Storage = storage,
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
