using System.Reflection;

namespace Netrunner.Core;

public static class BuildMetadata
{
    public const string RulesVersion = "26.03";
    public const int SaveSchemaVersion = 1;
    public const int ActionLogSchemaVersion = 1;

    public static BuildInfo Current { get; } = Create();

    private static BuildInfo Create()
    {
        Assembly assembly = typeof(BuildMetadata).Assembly;
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        return new BuildInfo(
            informationalVersion,
            RulesVersion,
            SaveSchemaVersion,
            ActionLogSchemaVersion);
    }
}

public sealed record BuildInfo(
    string ApplicationVersion,
    string RulesVersion,
    int SaveSchemaVersion,
    int ActionLogSchemaVersion);

