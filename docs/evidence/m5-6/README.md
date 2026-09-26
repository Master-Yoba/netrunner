# M5.6 native client screenshots

Captured on 23 September 2026 from the native Godot window at 1920×1080, with
`NETRUNNER_CARD_ART_DIRECTORY` pointing at an empty location. These contain no
fetched card artwork; generic committed card backs and text fallbacks remain.
The save browser is above all board layers and receives only allowlisted metadata.

- [Setup and Standard/Easy selector](setup-1920.png)
- [Existing cog with save/load actions](settings-1920.png)
- [Save browser](browser-1920.png)

Capture used `--m5-client-smoke --phase save --side runner --preset basic`, an
isolated `NETRUNNER_SAVE_DIRECTORY`, and `NETRUNNER_M5_SCREENSHOT_DIRECTORY`.
The process exercised real engine decisions and actual durable local saves.
This is automated visual evidence, not human acceptance of gameplay.

[Native matrix evidence](client-matrix.json) records all five cases and ten
process phases, with only allowlisted setup/outcome information and hashes. The
final save checkpoints are earlier than those in these UI screenshots because
one Corp/basic test game finished before the original checkpoint.
