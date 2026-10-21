# macOS development setup (macOS 26)

Applies to macOS 26 on Apple Silicon; the Godot 4.7.2 macOS build is a
universal binary, so Intel Macs work too. All commands assume the current
directory is the root of a fresh clone.

`scripts/bootstrap.sh` is Linux-only and exits early on macOS. This setup
replicates the same `.tools/` layout it creates, so `scripts/env.sh`, the
`Makefile` targets, and `scripts/verify.sh` work unchanged. `.tools/` is
gitignored; nothing described here is committed.

## 1. System tools

The Xcode Command Line Tools provide git, curl, unzip, python3, and bash:

```bash
xcode-select --install
```

Homebrew is only required if you want it as the ripgrep installer (section 4):

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

On Apple Silicon, follow the "Add to your profile" step it prints (the prefix
is `/opt/homebrew`).

## 2. .NET into `.tools/` (SDK 10 + runtime 8)

The solution pins .NET 10 (`global.json`: `10.0.100`,
`rollForward: latestFeature`) and the test projects target `net8.0`, so the
.NET 8 runtime is also required. Install both into `.tools/dotnet`, exactly as
the Linux bootstrap does:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --quality GA --install-dir .tools/dotnet
bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir .tools/dotnet
```

Verify:

```bash
source scripts/env.sh
dotnet --version        # 10.0.1xx
dotnet --list-runtimes  # includes Microsoft.NETCore.App 8.0.x
```

`scripts/env.sh` finds `.tools/dotnet`, sets `DOTNET_ROOT`, and puts it on
PATH.

Alternative: install .NET system-wide (the Microsoft installer or
`brew install dotnet-sdk`). Without `.tools/dotnet`, `env.sh` falls back to
the system `dotnet`, but the .NET 8 runtime is still required for the `net8.0`
test hosts (`brew install dotnet-runtime-8.0` or
`dotnet-install.sh --channel 8.0 --runtime dotnet`). The portable install
above keeps behavior identical to Linux and is the recommendation.

The Godot editor builds the C# project with the `dotnet` on PATH, so always
launch it from a shell where `source scripts/env.sh` has been run (see the
editor section below).

## 3. Godot 4.7.2 .NET into `.tools/`

```bash
curl -fL -o /tmp/godot.zip \
  https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_macos.universal.zip
mkdir -p .tools/godot
unzip -q /tmp/godot.zip -d .tools/godot
mv .tools/godot/Godot_mono.app .tools/godot/Godot.app
```

This produces `.tools/godot/Godot.app` (the archive ships it as
`Godot_mono.app`; the rename keeps the paths in this document true).

**Set `GODOT4` yourself.** `scripts/env.sh` locates the single-file Linux
binary (at most two path components below `.tools/godot/`); the macOS
executable sits deeper inside the app bundle, so `env.sh` does not set
`GODOT4`. Export it in each shell:

```bash
export GODOT4="/absolute/path/to/netrunner/.tools/godot/Godot.app/Contents/MacOS/Godot"
```

`env.sh` honors a pre-set `GODOT4`. To make it permanent, add the same line
with your real repo path to `~/.zshrc`.

Verify:

```bash
"$GODOT4" --version   # 4.7.2.stable.mono.official...
```

If Gatekeeper quarantines the app, clear the flag:

```bash
xattr -dr com.apple.quarantine .tools/godot/Godot.app
```

## 4. ripgrep

`scripts/check-secrets.sh` (run by `make verify`) requires `rg` on PATH.

Option A, Homebrew:

```bash
brew install ripgrep
```

Option B, the pinned portable build the Linux bootstrap installs (use the
`x86_64-apple-darwin` archive on an Intel Mac):

```bash
curl -fL -o /tmp/rg.tar.gz \
  https://github.com/BurntSushi/ripgrep/releases/download/14.1.1/ripgrep-14.1.1-aarch64-apple-darwin.tar.gz
mkdir -p .tools/ripgrep
tar -xzf /tmp/rg.tar.gz -C .tools/ripgrep --strip-components 1 --wildcards "*/rg"
```

`env.sh` adds `.tools/ripgrep` to PATH automatically.

## 5. First verification

```bash
source scripts/env.sh
./scripts/verify.sh
```

This is the full gate: restore, build, test, `dotnet format` check, CLI
self-test and doctor, card-data validation (skipped on a clean clone), the
three M1 win-path matches plus a replay round-trip, a headless Godot import
and smoke test, and the secret scan. `make verify` is equivalent.

macOS note: `verify.sh` sets `XDG_DATA_HOME` for the Godot steps, but Godot on
macOS ignores it and uses the standard locations instead
(`~/Library/Application Support/Godot/` for data and export templates,
`~/Library/Preferences/Godot/` for editor settings). Everything still works;
the run is just not isolated under `.tools/godot-data/`.

## 6. Daily workflow

Identical to the Linux workflow in the README. In every new terminal:

```bash
source scripts/env.sh
```

then the usual commands:

```bash
make build
make test
make watch-core
make watch-cards
make cli
make godot-import
make verify
make doctor
```

Play a match:

```bash
dotnet run --project src/Netrunner.Cli -- play --seed 42 --goal corp-score --json
```

Open the editor (from a shell with the env sourced, so the C# build can find
`dotnet`):

```bash
"$GODOT4" --editor --path src/Netrunner.Godot
```

The first open imports the project and builds the C# assembly
(`Godot.NET.Sdk/4.7.2`, restored from NuGet during `dotnet restore`); no
separate Godot .NET SDK install is needed.

VS Code: open the repository root. `.vscode/extensions.json` recommends C# Dev
Kit and Godot Tools, and the default build, verify, and open-godot tasks are
already configured.

## 7. Optional extras

- Optional LLM endpoint: copy `.env.example` to `.env` when LLM work begins.
  Offline play never requires an endpoint.
- Official card data: same as Linux;
  `./scripts/update-card-data.sh --commit <sha> --retrieval-date <date>`
  needs network access and python3 (the Command Line Tools provide it).
- Export templates and packaging: the Linux lane remains available
  (`make templates`, `make package-linux`, export preset "Linux x86_64").
  `scripts/install-export-templates.sh` targets the Linux template and a
  location macOS Godot does not read, so if you ever need templates on macOS,
  install them from the editor (Project > Export > Manage Export Templates) or
  copy the 4.7.2 .NET template contents into
  `~/Library/Application Support/Godot/export_templates/4.7.2.stable.mono/`.
  The current macOS development lane is `make package-macos`; it uses the
  checked-in universal macOS preset and runs both the packaged full-slice smoke
  and the 10-second responsiveness smoke with portable 30-second timeouts. It
  requires the macOS export template (`macos.zip`) to be installed alongside
  the Linux template.
- `make benchmark-m0` is Linux-only (it relies on GNU `date +%s%N`); skip it
  on macOS.

## Troubleshooting

- `dotnet: command not found` after the install steps: run
  `source scripts/env.sh` in that terminal.
- The Godot editor cannot find .NET or fails to build the C# project: launch
  the editor from a shell where `dotnet --version` prints 10.x.
- `make verify` reports Godot unavailable: export `GODOT4` (section 3).
- `rg: command not found`: install ripgrep (section 4).
