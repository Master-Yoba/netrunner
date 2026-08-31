using System.Reflection;

namespace Netrunner.Core;

public static class BuildMetadata
{
    public const string RulesVersion = "26.03";
    // M4-4.1: persisted state now carries an explicit behavior-registry
    // version.  This is a deliberate migration; schema-1 state is rejected
    // rather than silently defaulting the new metadata.
    public const int SaveSchemaVersion = 2;
    // Replay/action wire shape did not migrate with the saved state. Keep its
    // contract aligned with EngineCompatibility.ArtifactSchemaVersion while
    // SaveSchemaVersion tracks the separate state/snapshot migration.
    public const int ActionLogSchemaVersion = EngineCompatibility.ArtifactSchemaVersion;

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
