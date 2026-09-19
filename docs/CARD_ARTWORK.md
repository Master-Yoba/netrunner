# Official card artwork caches

The client displays full English NSG System Gateway card faces for known cards.
Installed ICE is sideways; hover and inspection artwork is upright. Selection,
legal-action cues, counters, and state badges remain visible. Inspection retains
the game's current rules text alongside the printed face. Hidden cards never
look up artwork. Missing or invalid images use the generated card layout.

Artwork is optional and local-only. Launching, playing, importing, exporting,
and validating a cache never downloads anything. Only `card-art fetch` contacts
the image source. Standard builds and executable packages contain no card faces.

## Download and validate

After installing the repository toolchain:

```bash
make card-art-fetch
make card-art-validate
make play
```

The downloader uses the [NetrunnerDB V3 API](https://api.netrunnerdb.com/api/docs/)
to select all 77 Gateway printing IDs, requiring Null Signal Games as publisher.
It prefers the advertised high-resolution WebP face, or the large JPEG when no
high-resolution URL is provided. The current
[Gateway edition](https://nullsignal.games/products/system-gateway/) is remastered.
Source URLs, artist credits, retrieval times, sizes, and SHA-256 checksums are
preserved in the cache manifest. Existing printed credits remain on the images.

Rerunning fetch reuses a complete verified cache without network requests. An
interrupted first download resumes verified image receipts. Use
`make card-art-fetch ART_ARGS=--refresh` to retrieve the current upstream listing
and images again. Requests identify this application, are limited to two per
second, and retry transient failures up to three attempts with a 30-second
request timeout. Rate-limit waits longer than one minute stop with a diagnostic.
An unsuccessful fetch does not publish an incomplete replacement cache.

## Cache location

The CLI and Godot use the same default:
`Environment.SpecialFolder.LocalApplicationData/NetrunnerSolo/card-art`.
The commands print the resolved absolute directory. Set
`NETRUNNER_CARD_ART_DIRECTORY` to use another location for both:

```bash
export NETRUNNER_CARD_ART_DIRECTORY="$PWD/.tools/card-art"
make card-art-fetch
make play
```

CLI `--cache-dir <directory>` takes precedence over that variable. If used,
point the game at the same directory using the variable. Restart the game after
changing cache contents; textures and missing-image results are shared for the
process lifetime. The legacy Godot `user://card-art/<code>.png` location is still
read as an illustration fallback, not as a full-face cache.

## Pack and unpack without internet

```bash
make card-art-pack ART_ARGS='--output artifacts/gateway-art.zip'
# Copy the ZIP to another machine, then:
make card-art-unpack ART_ARGS='--input /path/to/gateway-art.zip'
make card-art-validate
```

ZIP bundles contain a versioned inventory, the cache manifest, and only referenced
images. Packing checks every image. Unpacking checks the complete inventory,
checksums, versions, safe relative paths, and expanded sizes before publishing
the cache. Identical imports are harmless; replacing different content requires
`--replace`. Existing archive output filenames are never overwritten.

To also provision the official metadata needed for a clean offline build:

```bash
make card-art-pack ART_ARGS='--output artifacts/gateway-offline.zip --include-metadata data/vendor/system-gateway'
make card-art-unpack ART_ARGS='--input /path/to/gateway-offline.zip --metadata-output data/vendor/system-gateway'
make play
```

Metadata must already be provisioned through `scripts/update-card-data.sh` on
the connected machine. Bundles carry its original `cards.json`, `manifest.json`,
and `SOURCE_COPYRIGHT.md`, validated using the existing official-content checks.
Unpacking restores metadata only when `--metadata-output` is supplied; otherwise
it imports artwork alone. Metadata and artwork destinations must be separate.
Offline builds still need the SDK, Godot, and restored NuGet dependencies.

For exported games, unpack the cache on the destination machine into its default
location, or launch with `NETRUNNER_CARD_ART_DIRECTORY` pointing to the unpacked
cache. Ship the artwork ZIP as a separate local deployment artifact. Artwork
does not change an executable's embedded metadata; build the official-content
executable before deploying it. Source caches and archives belong outside the
repository or under ignored `.tools/` and `artifacts/` directories.

## Automation and verification

`scripts/card-art.sh` exposes `fetch`, `validate`, `pack`, and `unpack`; all
accept `--cache-dir` and `--json`. Progress goes to stderr; JSON results go to
stdout. Exit codes are 0 for success, 1 for operational/validation failure, and
2 for invalid arguments. The wrapper isolates its CLI build outputs from game
assemblies, including generated card data.

`make verify` exercises synthetic artwork and offline archive/downloader tests.
No official images or live network requests are required in CI. With a real
cache, run Godot with `-- --card-art-cache-smoke` to check all 77 images. Use
`-- --card-art-smoke --card-art-screenshots` for synthetic evidence safe to commit;
live-cache screenshots go under ignored `artifacts/card-artwork` instead.

The cache schema is version 1: English `system_gateway`, with 77 definition-ID
entries pointing to `images/<sha256>.<png|jpg|webp>`. Import rejects other schemas,
unknown files, duplicate paths, and links. Individual files are limited to
10 MiB; archive contents are limited to 512 MiB and 100 entries. Images are
validated by signature, byte size, and checksum; Godot additionally decodes them
before showing a face and falls back if decoding fails. Images are not engine
inputs and cannot affect deterministic game state or replay hashes.
