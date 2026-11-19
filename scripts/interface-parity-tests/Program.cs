// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Internal;

string repository = args[0];
string managed = args[1];
string output = args[2];
string build = Path.Combine(repository, "build");
string[] directories =
{
    Path.Combine(build, "NeoComposeUnity.Tests/bin/Debug/netstandard2.1"),
    Path.Combine(build, "NeoComposeUnity/bin/Debug/netstandard2.1"),
    Path.Combine(build, "NeoComposeUnityEditor/bin/Debug/netstandard2.1"),
    managed,
    Path.Combine(repository, "samples/HelloWorld/Library/ScriptAssemblies"),
};
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string directory in directories)
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
var tests = Assembly.LoadFrom(Path.Combine(directories[0], "NeoComposeUnity.Tests.dll"));
var fixtureType = tests.GetType("NeoCompose.Tests.P127InterfacesParityTests", throwOnError: true)!;
var provider = fixtureType.GetMethod("HistoricalGeneratedCases")!;
var test = fixtureType.GetMethod("GeneratedHistoricalBridgeMatchesFixture")!;
var cases = ((IEnumerable)provider.Invoke(null, null)!).Cast<TestCaseData>().ToArray();
if (cases.Length == 0)
    throw new InvalidOperationException("No typed interface parity cases were discovered.");
var results = new List<object>();
int passed = 0, failed = 0;
void Run(string name, MethodInfo method, object?[] arguments, object? pin)
{
    // Each assertion gets its own NUnit result; a failure cannot contaminate
    // later cases. Invoke the actual test and its compiled typed delegate.
    using var context = new TestExecutionContext.IsolatedContext();
    string? error = null;
    try
    {
        method.Invoke(Activator.CreateInstance(fixtureType), arguments);
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (TargetInvocationException exception)
    {
        failed++;
        error = exception.InnerException?.ToString() ?? exception.ToString();
        Console.WriteLine("FAIL " + name + ": " + error);
    }
    results.Add(new
    {
        name,
        passed = error is null,
        pin,
        error
    });
}
foreach (var row in cases)
{
    Run(row.TestName!, test, row.Arguments, new
    {
        group = row.Arguments[0],
        slot = row.Arguments[1],
        neo = row.Arguments[2],
        mono = row.Arguments[3],
        selected = Type.GetType("Mono.Runtime") is null ? row.Arguments[2] : row.Arguments[3] ?? row.Arguments[2],
    });
}
foreach (var method in fixtureType.GetMethods().Where(method => method.Name.StartsWith("Generated", StringComparison.Ordinal)
    && method.IsDefined(typeof(TestAttribute), inherit: true)))
{
    Run(method.Name, method, Array.Empty<object?>(), null);
}
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    runtime = RuntimeInformation.FrameworkDescription,
    typedCases = cases.Length,
    total = passed + failed,
    passed,
    failed,
    results,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Typed interface parity: {passed} passed, {failed} failed; {output}");
return failed == 0 ? 0 : 1;
