# Card backs

Original Corp and Runner card backs made for this project; no third-party
artwork. The sources are `tools/card-backs/corp.svg` and `runner.svg`;
`make card-backs-render` rasterizes them here at 300x418 with Godot.

The official Null Signal Games backs are not committed: the NSG visual-assets
license does not cover them (https://nullsignal.games/about/nsg-visual-assets/).
`make card-backs-fetch` (also run by `make card-art-fetch`) downloads them into
the ignored `.tools/card-backs` directory, which overrides these files through
`NETRUNNER_CARD_BACK_DIRECTORY` in `scripts/play.sh` and `scripts/verify.sh`.
