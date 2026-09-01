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

System Gateway card metadata is a local-only developer artifact and is not
redistributed by this repository. It is generated from the official primary
source `https://github.com/Null-Signal-Games/netrunner-cards-json` at the pinned
commit
`236140cf84491c23995b028bc791281837520b97`, using `pack/sg.json` (77 records,
upstream schema v1) and `COPYRIGHT.md`, for retrieval date `2026-08-28`:

```bash
./scripts/update-card-data.sh \
  --commit 236140cf84491c23995b028bc791281837520b97 \
  --retrieval-date 2026-08-28
```

The manifest records the source license path and the identity
`NOASSERTION`. The copied notice is retained only for local review; the
upstream notice does not establish a redistribution grant, so the generated
`data/vendor/system-gateway/cards.json`, `manifest.json`, and
`SOURCE_COPYRIGHT.md` are ignored by Git and must not be shipped. No card art,
frames, or backs are fetched or included. The checked-in normalized schema and
synthetic fixtures are project implementation artifacts, not official card
data.

The Netrunner Comprehensive Rules are a local reference snapshot committed to
this repository by explicit project decision. Unlike the card metadata above,
the rules source is a live web page with no immutable revision, so provenance
is recorded as `(source URL, rules version, retrieval date, and the sha256 of
the normalized source bytes)` rather than a commit. The current pin is
`https://rules.nullsignal.games/`, rules version `26.03`, retrieval date
`2026-09-01`, source sha256
`ae93dad66bd14cd197c8bb3ddbe0df2a4d5ad14e0461682d602009fbb582f8e2`, rendered
to per-chapter Markdown, `index.json`, and `manifest.json` under
`data/vendor/rules/`. The `source.html` is stored in a normalized form: the
page's single Cloudflare email-obfuscation token (which is re-randomized on
every request) is decoded to its `mailto:` value, leaving all rules content
unchanged, so the committed bytes and their sha256 are stable across fetches.

```bash
./scripts/update-rules.sh --retrieval-date 2026-09-01
```

These files are inert reference material only; they are never parsed into, or
consumed at runtime by, the rules engine or the AI runtime. The license identity
is `NOASSERTION`: the upstream rules are copyrighted by Null Signal Games, and
no redistribution grant is asserted. Although committed here for local
reference use, verify redistribution rights before any public distribution.
