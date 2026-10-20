using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using UnityEngine;

public static class ProjectionMembers
{
    public static int Main(string[] args)
    {
        var declared = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(args[0]));
        var projections = new Dictionary<string, Type[]>
        {
            ["Handle:Variant"] = new[] { typeof(NeoVariant<>) },
            ["Handle:LookupVariant"] = new[] { typeof(NeoLookupVariant<,>) },
            ["Class"] = new[] { typeof(NeoGeneratedClassValue), typeof(ExtensionFixtures.Person) },
            ["String"] = new[] { typeof(string) },
            ["Bool"] = new[] { typeof(bool) },
            ["Int"] = new[] { typeof(int) },
            ["Float"] = new[] { typeof(float) },
            ["Decimal"] = new[] { typeof(decimal) },
            ["Enum"] = new[] { typeof(System.Enum) },
            ["List"] = new[] { typeof(IReadOnlyList<>), typeof(NeoList<>), typeof(NeoReadOnlyList<>) },
            ["Dictionary"] = new[] { typeof(IReadOnlyDictionary<,>), typeof(NeoDictionary<>), typeof(NeoReadOnlyDictionary<>) },
            ["Set"] = new[] { typeof(INeoReadOnlySet<>), typeof(NeoSet<>), typeof(NeoReadOnlySet<>) },
            ["Lookup"] = new[] { typeof(INeoReadOnlySet<>), typeof(NeoLookupSet<>), typeof(NeoReadOnlyLookupSet<>) },
            ["Tuple"] = new[] { typeof(ValueTuple<,>), typeof(ValueTuple<,,,,,,,>) },
            ["NSDelegate"] = new[] { typeof(NeoDelegateValue) },
            ["Vector2"] = new[] { typeof(Vector2) },
            ["Vector2Int"] = new[] { typeof(Vector2Int) },
            ["Vector3"] = new[] { typeof(Vector3) },
            ["Vector3Int"] = new[] { typeof(Vector3Int) },
            ["Color"] = new[] { typeof(Color) },
            ["Sprite"] = new[] { typeof(Sprite) },
            ["Audio"] = new[] { typeof(AudioClip) },
            ["DialogueLookup"] = new[] { typeof(NeoDialogueReference) },
        };
        var missing = new Dictionary<string, string[]>();
        foreach (var pair in projections)
        {
            var actual = new HashSet<string>();
            foreach (var type in pair.Value)
            {
                Add(type, actual);
                if (type.IsInterface)
                    foreach (var inherited in type.GetInterfaces())
                        Add(inherited, actual);
            }
            var expected = new HashSet<string>(declared.TryGetValue(pair.Key, out var entry) ? entry : Array.Empty<string>());
            expected.UnionWith(new[] { "Equals", "ReferenceEquals", "GetHashCode", "GetType", "ToString" });
            actual.ExceptWith(expected);
            if (actual.Count > 0)
                missing[pair.Key] = actual.OrderBy(name => name).ToArray();
        }
        if (missing.Count == 0)
            return 0;
        Console.Error.WriteLine(JsonConvert.SerializeObject(missing, Formatting.Indented));
        return 1;
    }

    private static void Add(Type type, HashSet<string> names)
    {
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (member is ConstructorInfo)
                continue;
            if (member is MethodInfo method && method.IsSpecialName)
                continue;
            names.Add(member.Name);
        }
    }
}
