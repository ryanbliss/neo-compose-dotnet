// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        private sealed class NestedParcel : NeoGeneratedClassValue
        {
            public NestedParcel(NeoClient client, NeoMemberClass node) : base(client, node, node.value!.classId!, false)
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedDespawnPublishesGridRemoval(bool dropFirst)
        {
            var data = BuildClassBackedTileGridProjectData();
            ((ArrayMemberValue)data.values["town-grid-children"]).value = new[] { "objects-link" };
            JObject fixture = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/nested-despawn.json"));
            var classes = (JObject)fixture["classes"]!;
            string toyId = classes.Properties().Single(p => (string?)p.Value["name"] == "Toy").Name;
            string linkId = classes.Properties().Single(p => (string?)p.Value["name"] == "Link").Name;
            string areaId = classes.Properties().Single(p => (string?)p.Value["name"] == "Area").Name;
            string childrenId = (string)classes[areaId]!["schema"]!["Children"]!;
            string objectsId = (string)classes[linkId]!["schema"]!["Objects"]!;
            var replacements = new Dictionary<string, string>
            {
                [toyId] = ObjectClassId,
                [areaId] = GridClassId,
                [childrenId] = "grid-children-member",
                [linkId] = ObjectLayerLinkClassId,
                [objectsId] = "object-layer-link-objects-member",
            };
            foreach (JValue value in fixture.Descendants().OfType<JValue>())
                if (value.Type == JTokenType.String && replacements.TryGetValue((string)value!, out string mapped))
                    value.Value = mapped;
            var authored = new ProjectData
            {
                classes = fixture["classes"]!.ToObject<Dictionary<string, NeoSchemaClass>>()!,
                members = fixture["members"]!.ToObject<Dictionary<string, Member>>()!,
                constructors = fixture["constructors"]!.ToObject<Dictionary<string, ConstructorRecord>>()!,
                values = fixture["values"]!.ToObject<Dictionary<string, MemberValue>>()!,
            };
            foreach (var pair in authored.classes)
                if (data.classes.TryGetValue(pair.Value.id, out var existing))
                {
                    foreach (var field in pair.Value.schema)
                        existing.schema[field.Key] = field.Value;
                    existing.schemaKeyOrder = existing.schema.Keys.ToArray();
                }
                else
                    data.classes[pair.Value.id] = pair.Value;
            foreach (var pair in authored.members)
                if (pair.Value.id == "grid-children-member")
                    continue;
                else if (pair.Value.id != "object-layer-link-objects-member")
                    data.members[pair.Value.id] = pair.Value;
                else
                    ((ListMember)data.members[pair.Value.id]).defaultValue = ((ListMember)pair.Value).defaultValue;
            foreach (var pair in authored.constructors)
                data.constructors[pair.Key] = pair.Value;
            foreach (var pair in authored.values)
                data.values[pair.Key] = pair.Value;
            string MemberId(string name) => authored.members.Values.Single(m => m.name == name).id;
            // An immutable link has a constructor-derived, Save-backed list.
            ((ObjectMemberValue)data.values["objects-link"]).value!.Remove("Objects");
            string tilesId = ((ObjectMemberValue)data.values["shop-object"]).value!["PlacementTiles"];
            ((ListMember)data.members["object-placement-tiles-member"]).defaultValue = new ArrayMemberValueBase { value = ((ArrayMemberValue)data.values[tilesId]).value };
            data.members["object-layer-link-objects-member"].Storage = NeoMemberStorage.Save;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            string gridId = "town-grid";
            string placedLinkId = "objects-link";
            ((ReturnInstruction)((NSFunctionMember)data.members[MemberId("GetArea")]).action!.instructions[0]).pointer = new ReferencePointer { type = PointerKind.Reference, valueId = gridId };
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            string parcelId = authored.classes.Values.Single(c => c.name == "Parcel").id;
            factories[parcelId] = (c, n) => new NestedParcel(c, n);
            writableFactories[parcelId] = (c, n) => new NestedParcel(c, n);
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, gridId, factories, writableFactories);
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            var node = new NeoMemberClassWritable(client,
                new ClassMember { id = "test-objects-link", kind = MemberKind.Class, DeclaredClassId = ObjectLayerLinkClassId }, placedLinkId, NeoValueOwnership.Asset);
            var objects = node.Get<NeoMemberListWritable>("Objects");
            int drops = 0;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [MemberId("Drop")] = (c, receiver, args, _) =>
                {
                    // Match generated native bindings before invoking the no-op host callback.
                    Assert.That(NeoGeneratedTypesSupport.ResolveNativeFunctionClassArgument<NestedParcel>(c, args[0], true, factories, writableFactories, "parcel"), Is.Not.Null);
                    Assert.That(NeoGeneratedTypesSupport.ResolveNativeFunctionClassArgument<TestComposedObject>(c, receiver, true, factories, writableFactories, "this"), Is.Not.Null);
                    drops++;
                    return null;
                },
            });
            var placedNode = NeoGeneratedTypesSupport.CreateWritableClassValue(client, ObjectClassId, Array.Empty<NeoGeneratedConstructorValue>());
            var placed = (NeoGeneratedClassValue)NeoGeneratedTypesSupport.ResolveClassValue(client, placedNode.value!.id, factories, writableFactories)!;
            string placedId = placed.valueId!;
            var cell = new Vector2Int(80, 90);
            var placement = WritableObjectLink(client, placedLinkId).TrySpawn(cell, placed);
            Assert.That(placement.Ok, Is.True, placement.Message);
            var details = (ObjectMemberValue)client.ResolveClassChildRow((ObjectMemberValue)client.ResolveValueRow(placedId)!, "Data")!;
            var quality = (NumberMemberValue)client.ResolveClassChildRow(details, "Quality")!;
            var nextQuality = (NumberMemberValue)client.CloneRowForWrite(quality);
            nextQuality.value = 10;
            client.SetWritableValue(NeoValueOwnership.Save, nextQuality);
            client.StartScriptRuntime();
            Assert.That(objects.ResolveEntryValueIds(), Does.Contain(placedId));
            var changes = new List<NeoTileGridChangedArgs>();
            using var subscription = primitive.OnChanged(changes.Add);
            Assert.That(layer.GetObjects(cell).Any(value => value.valueId == placedId), Is.True);
            using var action = new NeoMemberNSFunction(client, MemberId(dropFirst ? "Hit" : "Despawn"), null, NeoValueOwnership.Save);
            action.Invoke(placedId, Array.Empty<object?>());
            Assert.That(drops, Is.EqualTo(dropFirst ? 1 : 0));
            Assert.That(objects.ResolveEntryValueIds(), Does.Not.Contain(placedId));
            Assert.That(layer.GetObjects(cell).Any(value => value.valueId == placedId), Is.False);
            Assert.That(changes.SelectMany(change => change.ObjectLayers).SelectMany(change => change.RemovedInstances).Count(id => id == placedId), Is.EqualTo(1));
        }
    }
}
