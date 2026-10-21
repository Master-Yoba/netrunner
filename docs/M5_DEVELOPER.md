# Developer engine sessions

Run `make play-dev` (or `make play PLAY_ARGS=--developer`) with the installed
official metadata. The cog contains **Developer tools** only when both the
development build and explicit launch flag are present. Release builds ignore
the flag. Saved games and presentation settings cannot enable the capability.
The metadata gallery follows the same production-shell visibility rule.

Opening the panel pauses at a committed boundary. Close/Escape returns to the
paused board; **Continue match** resumes normal play. The panel offers:

- **Step one action**: accept exactly one engine-issued action, then pause.
  The dropdown can choose an issued human action. Automatic selection uses the
  actual offline opponent and the existing contract policy for the human seat.
- **To human decision**, **To turn**, and **To outcome or cap**: advance with an
  explicit limit of 1–10,000 accepted actions. The default UI cap is 100. Pause
  and closing the panel interrupt advancement at a committed boundary. Outcome,
  cancellation, failures and standalone scenarios with no further decision all
  produce explicit stop reasons. A target already reached accepts zero actions.
- **Import snapshot**: validate the file, matching catalogs, invariants and
  replay before replacing the current session. Imports always start paused and
  preserve run/nested resolution frames, RNG, allocated IDs and issued actions.
  A failed or canceled import retains the current session.
- **Export session**: write a private developer document containing the current
  Core snapshot, replay prefix and committed opponent policy. Export pauses;
  atomic file replacement leaves an existing destination intact on failure.
- **Export raw snapshot**: write just the Core `GameSnapshot`. Importing a raw
  snapshot starts an explicitly marked `imported-scenario-root` at its existing
  action sequence, with fresh deterministic policy knowledge derived solely
  from the opponent's normal observation. It does not claim prior actions or
  private knowledge that the raw snapshot does not contain.
- **Reveal private card inspector**: view hidden card identities and locations
  in this separate developer panel. Player observations, board publications,
  history, AI inputs, random streams and checkpoints remain unchanged.

Developer documents retain their origin marker across subsequent exports.
Imported scenarios use developer export; they cannot enter the ordinary save
browser or rolling auto-save. Resource grants and state-edit widgets are not
included. To examine an externally edited Core snapshot, import it as a new raw
scenario root and pass normal validation.

Private files default to `NetrunnerSolo/developer` under the operating system's
local application-data directory. Keep exports, authoritative traces and
official artwork outside source control. Import accepts the installed catalog
versions; it does not fetch missing content or migrate incompatible snapshots.

## CLI

After `source scripts/env.sh` and an official-content CLI build:

```sh
dotnet src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll snapshot /path/scenario.snapshot.json --side corp --json
dotnet src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll snapshot /path/scenario.snapshot.json --side corp --step --output /path/session.developer.json
dotnet src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll snapshot /path/session.developer.json --until turn:5 --max-actions 300 --output /path/advanced.developer.json
dotnet src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll snapshot /path/session.developer.json --raw --output /path/export.snapshot.json
```

Use `--step --action <issued-id>` to select the human action. `--until human`
and `--until outcome` are the other advancement targets. `--side` selects the
human seat for a raw import; a developer document retains its own saved seat.
`--synthetic` explicitly selects the reduced developer catalog. The existing
`scenario` command still consumes its original `ScenarioArtifact` format.

Without advancement or output options, `snapshot` validates only. JSON stdout
contains hashes, counts, turn/outcome and stop reason, with no private cards or
policy memory. Exit 0 means the requested operation succeeded (including an
explicit cap/no-decision stop), 1 means advancement failed, and 2 means invalid
arguments or an unreadable/incompatible document. Every export contains a
self-contained replay that can be checked on a later import.

## Validation

`DeveloperSessionTests` cover exact one-action boundaries, policy/replay
continuation, both seats, overrides, all stop targets, capability rejection,
late evaluation cancellation, reveal isolation and all 80 frozen official
tactical boundaries. Those fixtures include ongoing runs, payments, prevention,
replacement and compulsory choices. Legal standalone fixture endpoints with no
further decision remain exportable and stop without hanging.

`SnapshotCommandTests` exercise file import/step/export/reopen, raw source
preservation, corrupt-source destination preservation and invalid arguments.
`make m5-developer-smoke` exercises the native panel in both seats at
1080p/1440p, checks raw versus session imports, failures, action caps and
Escape/focus, and proves a normal development launch creates no developer UI.
The same `--m5-developer-smoke --developer --expect-disabled` probe is used on
the exported release to verify the build guard, including a loaded developer
setting. Smoke artifacts are private and remain under ignored `artifacts/m5`.
