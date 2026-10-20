using System.Threading.Tasks;
using ExtensionFixtures;

public static class Consumers9
{
    public static int DefaultGeneric(int value) => value.Identity(value);
    public static string ExplicitGeneric(int value) => value.Identity<string>("text");
    public static int Scalar(int value) => value.Twice();
    public static bool Enum(WeatherKind value) => value.IsCurrent();
    public static Task<int> Deferred(int value) => value.Later();
}
