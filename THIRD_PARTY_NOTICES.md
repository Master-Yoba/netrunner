# Third-party notices

This inventory covers M0 development/runtime dependencies. It is not a
substitute for a release-time legal review. Versions are pinned by the project
files and lock/restore artifacts.

| Component | M0 version | License | Use |
|---|---:|---|---|
| Godot Engine / GodotSharp / GodotSharpEditor / Godot.SourceGenerators | 4.7.2 | MIT | Editor, desktop runtime, C# bindings and build generation |
| .NET SDK/runtime | 10.0.400 SDK; 8.0.30 and 10.0.11 runtimes | MIT and bundled third-party notices | Build and managed runtime |
| Microsoft.NET.Test.Sdk and Microsoft Test Platform | 17.14.1 | MIT | Test execution |
| xUnit.net and assertions | 2.9.3 | Apache-2.0 | Tests |
| xUnit Visual Studio runner | 3.1.5 | Apache-2.0 | Test discovery/execution |
| Newtonsoft.Json | 13.0.3 | MIT | Transitive test-platform dependency |

The installed NuGet package metadata is the authority for exact transitive
versions and license expressions. Run:

```bash
dotnet list tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj package --include-transitive --no-restore
dotnet list src/Netrunner.Godot/Netrunner.Godot.csproj package --include-transitive --no-restore
```

System Gateway card metadata is not vendored yet. When added in M1, its source
commit, license/copyright notice, schema, and generated checksum must be recorded
in the data manifest. Official card artwork, frames, and backs are deliberately
absent.

