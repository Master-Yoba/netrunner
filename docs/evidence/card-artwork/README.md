# Artwork rendering evidence

These captures use the generated geometric card-face fixture from
`Main.CardArtworkSmoke.cs`. No official card-front artwork is included.
The concealed card uses the existing committed generic NSG back.

- `faces-1920.png` and `faces-2560.png`: portrait and sideways card faces,
  selected/actionable and target cues, counters, a concealed card, and text fallback.
- `inspection-1920.png` and `inspection-2560.png`: upright artwork next to
  current rules and state, with the underlying table behind the modal.

Regenerate with Godot arguments `-- --card-art-smoke --card-art-screenshots`.
The smoke additionally checks both hover orientations, retained inspection on
ordinary snapshot updates, immediate clearing on concealment, texture reuse,
and missing/corrupt image fallback. Real-cache captures are written only under
ignored `artifacts/card-artwork` by `--card-art-cache-smoke --card-art-screenshots`.
