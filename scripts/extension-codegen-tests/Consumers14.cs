using System.Collections.Generic;
using ExtensionFixtures;

public static class Consumers14
{
    public static string Interface(IFoo value) => value.ToBar();
    public static string Property(IReadOnlyPerson value) => value.FullName;
    public static void Setter(Person value) => value.FullName = "Updated";
    public static IReadOnlyList<T> Generic<T>(IReadOnlyList<T> values) => values.Sort();
    public static int GenericProperty<T>(IReadOnlyList<T> values) => values.Size;
    public static int Static() => int.DefaultValue();
    public static IEnumerable<T> Sequence<T>(IEnumerable<T> values) => values.EchoSequence();
    public static IEnumerator<T> Cursor<T>(IEnumerator<T> values) => values.EchoCursor();
}
