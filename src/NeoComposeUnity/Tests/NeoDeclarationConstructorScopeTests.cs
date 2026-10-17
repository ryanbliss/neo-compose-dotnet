// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// A nested default reads its declaring constructor's scope through
    /// construction, save reload, and the reload of a save written before
    /// that constructor existed. The fixture is a real <c>neo export</c> of
    /// this source, its main partition folded in as <c>values</c>:
    /// <code>
    /// class Crafting { List&lt;string&gt; Ores = ["copper ore", "iron ore"]; List&lt;string&gt; Bars = ["copper bar", "iron bar"]; List&lt;string&gt; Fuels = ["coal"]; }
    /// class FurnaceIngredientInventory { List&lt;string&gt; Stacks = []; readonly Set&lt;string&gt;? EligibleIngredients; float Capacity = 1; }
    /// abstract class FurnaceStackData(float durationMultiplier = 1) {
    ///   float DurationMultiplier = durationMultiplier;
    ///   FurnaceIngredientInventory Ore = new() { EligibleIngredients = root.Assets.Crafting.Ores.ToSet() };
    ///   FurnaceIngredientInventory Output = new() { EligibleIngredients = root.Assets.Crafting.Bars.ToSet(), Capacity = durationMultiplier * 2 };
    /// }
    /// class CombustibleFurnaceStackData(float durationMultiplier = 1) : FurnaceStackData(durationMultiplier) {
    ///   FurnaceIngredientInventory Fuel = new() { EligibleIngredients = root.Assets.Crafting.Fuels.ToSet() };
    /// }
    /// class EnergyFurnaceStackData(int fuelNeeded, float durationMultiplier = 1) : FurnaceStackData(durationMultiplier) {
    ///   int FuelNeeded = fuelNeeded;
    ///   FurnaceIngredientInventory Battery = new() { EligibleIngredients = root.Assets.Crafting.Fuels.ToSet() };
    /// }
    /// class Furnaces {
    ///   static void Place() {
    ///     root.Save.Combustible = new CombustibleFurnaceStackData(durationMultiplier: 3);
    ///     root.Save.Combustible!.Ore.Stacks.Add("copper ore");
    ///     root.Save.Energy = new EnergyFurnaceStackData(fuelNeeded: 2);
    ///   }
    /// }
    /// </code>
    /// <c>Assets</c> holds <c>Crafting</c>, and <c>Save</c> holds
    /// <c>Combustible</c> and <c>Energy</c>. <c>Output</c> reads a
    /// constructor parameter, so it exports as one initializer; the other
    /// inventories export field-wise, each field initializer still taking the
    /// declaring constructor's parameters.
    /// </summary>
    public class NeoDeclarationConstructorScopeTests
    {
        public enum Load
        {
            Fresh,
            Reloaded,
            // The furnace rows record no constructor, and the field-wise
            // inventory rows omit the fields added since.
            ReloadedFromBeforeConstructor,
        }

        [TestCase(Load.Fresh)]
        [TestCase(Load.Reloaded)]
        [TestCase(Load.ReloadedFromBeforeConstructor)]
        public void NestedDefaultsReadTheirDeclaringConstructorScope(Load load)
        {
            using NeoClient client = Client(load);
            string[] ores = { "copper ore", "iron ore" };
            string[] bars = { "copper bar", "iron bar" };
            string[] fuels = { "coal" };

            Assert.IsTrue(client.save.TryGet("Combustible", out NeoMemberClass? combustible));
            NeoMemberClass ore = AssertInventory(combustible!, "Ore", ores, 1);
            Assert.IsTrue(ore.TryGet("Stacks", out NeoMemberList? stacks));
            Assert.AreEqual(new[] { "copper ore" }, Strings(stacks!));
            AssertInventory(combustible!, "Output", bars, 6);
            AssertInventory(combustible!, "Fuel", fuels, 1);

            Assert.IsTrue(client.save.TryGet("Energy", out NeoMemberClass? energy));
            AssertInventory(energy!, "Ore", ores, 1);
            AssertInventory(energy!, "Output", bars, 2);
            // Energy's fuelNeeded has no default, and Battery never reads it.
            AssertInventory(energy!, "Battery", fuels, 1);
            Assert.IsTrue(energy!.TryGet("DurationMultiplier", out NeoMemberFloat? multiplier));
            Assert.AreEqual(1d, Convert.ToDouble(multiplier!.value?.value));
        }

        private static NeoMemberClass AssertInventory(NeoMemberClass furnace, string key, string[] eligible, double capacity)
        {
            Assert.IsTrue(furnace.TryGet(key, out NeoMemberClass? inventory), key);
            Assert.IsTrue(inventory!.TryGet("EligibleIngredients", out NeoMemberList? set), key);
            Assert.AreEqual(eligible, Strings(set!), key);
            Assert.IsTrue(inventory.TryGet("Capacity", out NeoMemberFloat? actual), key);
            Assert.AreEqual(capacity, Convert.ToDouble(actual!.value?.value), key);
            return inventory;
        }

        private static string?[] Strings(NeoMemberList list) =>
            list.Cast<NeoMemberString>().Select(entry => entry.value?.value).ToArray();

        private static NeoClient Client(Load load)
        {
            NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false);
            Member place = client.members.Values.Single(member => member is NSFunctionMember && member.name == "Place");
            NeoMemberNSFunction.GetOrCreateStatic(client, place.id).InvokeStatic(Array.Empty<object?>());
            if (load == Load.Fresh)
                return client;
            string save;
            using (client)
                save = client.SerializeSaveData();
            if (load == Load.ReloadedFromBeforeConstructor)
                save = BeforeConstructor(save);
            return NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false, loadedSaveContent: save);
        }

        private static string BeforeConstructor(string save)
        {
            JObject content = JObject.Parse(save);
            foreach (JProperty property in ((JObject)content["values"]!).Properties())
            {
                var row = (JObject)property.Value;
                if (row["value"] is not JObject fields || !fields.ContainsKey("Ore"))
                    continue;
                row.Remove("constructorArgs");
                row.Remove("instanceConstructorId");
                foreach (string key in new[] { "Ore", "Fuel", "Battery" })
                {
                    if (fields[key] is not JValue { Value: string inventoryId })
                        continue;
                    var inventory = (JObject)content["values"]![inventoryId]!["value"]!;
                    inventory.Remove("EligibleIngredients");
                    inventory.Remove("Capacity");
                }
            }
            return content.ToString();
        }

        private static ProjectData Schema() => NeoTestExport.Read(
            File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/declaration-constructor-scope-export.json"));
    }
}
