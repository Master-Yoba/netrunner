using System.Xml.Linq;

using Xunit;

namespace Netrunner.Core.Tests;

public sealed class ArchitectureDependencyTests
{
    [Theory]
    [InlineData("src/Netrunner.Core/Netrunner.Core.csproj", "../Netrunner.Cards/Netrunner.Cards.csproj")]
    [InlineData("src/Netrunner.Core/Netrunner.Core.csproj", "../Netrunner.Match/Netrunner.Match.csproj")]
    [InlineData("src/Netrunner.AI/Netrunner.AI.csproj", "../Netrunner.Cards/Netrunner.Cards.csproj")]
    public void ForbiddenProjectReferencesRemainAbsent(string ownerProject, string forbiddenProject)
    {
        string repositoryRoot = FindRepositoryRoot();
        string ownerPath = Path.GetFullPath(Path.Combine(repositoryRoot, ownerProject));
        string forbiddenPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ownerPath)!, forbiddenProject));

        XDocument project = XDocument.Load(ownerPath, LoadOptions.PreserveWhitespace);
        string[] references = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ownerPath)!, include!)))
            .ToArray();

        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference, forbiddenPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
