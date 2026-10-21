# UI capture harness

`make ui-capture` plays an official match through the real client and saves
frame bursts, plus a contact sheet per burst, whenever the client renders one
of the chosen event kinds. It records what the player actually sees: bursts
start when card-table playback renders the snapshot, not when the coordinator
publishes it. It is the before/after evidence tool for UI work
([UI/UX plan](history/UI_UX_REDESIGN_PLAN.md) phase U0).

```sh
make ui-capture CAPTURE_ARGS='--capture-seed 42 --capture-side runner --capture-events agenda-scored,run-initiated'
```

The contract agent plays the local seat through the normal human submission
path, and the offline Standard bot plays the other seat. Output goes to a new
directory under `artifacts/ui-capture/` unless `--capture-output <new dir>` is
given. It contains:

- `NN-<event-kind>-MM.png`: the frames of burst NN
- `NN-<event-kind>-sheet.png`: the burst's frames tiled at quarter size
- `capture.json`: options, bursts, local action counts and turn numbers

| Option | Default | Meaning |
|---|---|---|
| `--capture-seed` | 42 | Match seed |
| `--capture-side` | runner | Local seat, `corp` or `runner` |
| `--capture-preset` | basic | Preset for both sides, `basic` or `expanded` |
| `--capture-events` | `run-initiated,agenda-scored,agenda-stolen` | Engine event kinds that start a burst |
| `--capture-bursts` | 4 | Stop after this many bursts |
| `--capture-frames` | 12 | Frames per burst |
| `--capture-interval-ms` | 150 | Time between frames |
| `--capture-max-actions` | 800 | Stop after this many local actions |
| `--capture-pace-ms` | 500 | Wait before each local action |
| `--capture-catalog` | off | Burst on the first play of each U3 catalog beat instead of on engine events, play to the end of the game, and list missing catalog beats in `capture.json` (`catalogMissing`) |

The command prints `UI_CAPTURE PASS ...` and exits 0, or prints
`UI_CAPTURE FAIL ...` and exits nonzero.

Set `NETRUNNER_GODOT_ARGS` to pass engine options, for example
`NETRUNNER_GODOT_ARGS="--resolution 2560x1440"` for a 2560 capture on a display
that size.

## Displays

The harness needs a real renderer; Godot's `--headless` mode draws nothing.
On desktops it opens a normal window. On a headless Linux machine, run it
under Xvfb with software OpenGL (the project uses the GL Compatibility
renderer, which Mesa's llvmpipe supports):

```sh
xvfb-run -s "-screen 0 1920x1080x24" make ui-capture
```

Without root, the `xvfb` package and its dependencies (`libxfont2`,
`libunwind8`, `xserver-common`, `x11-xkb-utils`) can be unpacked locally with
`apt-get download` and `dpkg-deb -x`. That Xvfb binary looks for `xkbcomp` in
`/usr/bin`, so either install `x11-xkb-utils` system-wide or start Xvfb with a
working keymap compiler available at that path.

## Card art

Frames include official card art when a local artwork cache is configured
(`NETRUNNER_CARD_ART_DIRECTORY`, see [card artwork](CARD_ARTWORK.md)). Keep
those frames local and share them privately. Committed UI evidence uses the
synthetic interaction-lab fixtures.
