# Artwork rendering evidence

These captures use the generated geometric card-face fixture from
`Main.CardArtworkSmoke.cs`. No official card-front artwork is included.
The concealed card uses the existing committed generic NSG back.

- `faces-1920.png` and `faces-2560.png`: portrait and sideways card faces,
  selected/actionable and target cues, counters, a concealed card, and text fallback.
- `inspection-1920.png` and `inspection-2560.png`: upright artwork next to
  current rules and state, with the underlying table behind the modal.
- `lab-runner-1920.png` and `lab-corp-1920.png`: interaction-lab boards using
  the cache, including newly drawn cards.
- `hover-details-1920.png` and `hover-details-2560.png`: a single artwork preview
  with subtypes, current stats, advancement/typed counters, use state, hosting,
  rules, and interaction hints.

Regenerate with Godot arguments `-- --card-art-smoke --card-art-screenshots`.
The smoke additionally checks both hover orientations, retained inspection on
ordinary snapshot updates, immediate clearing on concealment, texture reuse,
all 19 lab scenarios, lab draws for both seats, and missing/corrupt image
fallback. It checks suppression of native card/pile tooltips and clearing of
hover details, including concealed hosts. Real-cache captures are written only under
ignored `artifacts/card-artwork` by `--card-art-cache-smoke --card-art-screenshots`.
