# ADR-013: TextMeshPro text renders through the baked-font UI path

Status: accepted. Plan 7.2 ("TextMeshPro: bitmap font fallback provided").

## Decision

A `TextMeshProUGUI` exports as the same text element a uGUI `Text` does
(draw kind 2 in the UI table), with managed kind 3 so the shim instantiates
`TMPro.TextMeshProUGUI` over it. At export the TMP font asset is resolved
back to the TTF it was generated from and that font is rasterised by
Unity's own font engine at the component's font size and style, exactly as
`Text` fonts have been since M12.5. The console draws proportional,
antialiased, baked glyphs; nothing about the runtime's UI pass changes.

The shim offers the TMP surface that works at runtime and nothing that
would have to lie: `text`, `SetText`, `color`, `richText`, `alignment`,
`horizontalAlignment`, `verticalAlignment`, `enabled`, with TMP's own enum
values so a script's constants keep their meaning. Rich text markup is
stripped, at export and again in the runtime setter, because the baked
font has no bold, colour or size variant to switch to and the author's
intent is the words. `fontSize`, `fontStyle`, `enableAutoSizing`,
`characterSpacing` and the SDF material properties do not exist in the
shim: a script that sets them fails to compile against `PS2.UnityShim`
with the property named, which is the honest signal for a bake-time
property (the policy `Text.fontSize` already follows).

World-space `TextMeshPro` (the `MeshRenderer` variant) is not exported.
The validator names it and points at a canvas.

## Why not rasterise the SDF atlas

TMP's atlas is a signed distance field at the font asset's sampling size.
Thresholding it into coverage at the component's size is possible, would
work for font assets whose TTF has left the project, and would reproduce
TMP's glyph shapes rather than the TTF's hinting. It is also a second
rasteriser to get right: atlas channel layout by texture format, dynamic
atlases that only hold the glyphs the Editor happened to render, fallback
font chains for characters the atlas lacks, and the padding-to-spread
arithmetic per glyph. The TTF route reuses a baker that has already
shipped text on target, and every TMP font asset created in the Editor
records its source font: dynamic assets keep the reference at runtime,
static ones keep the asset GUID, which the AssetDatabase resolves. When
neither resolves, Unity's default font bakes in its place and the
exporter says so. The SDF rasteriser is the upgrade path if a project
turns up whose fonts are only atlases, and this ADR is where to record
superseding the decision.

## Why not a 3D text path

A world-space text mesh whose string changes at runtime needs a layout
engine on the EE and a font atlas bound in the 3D pass; a static one
would be a mesh export with a `text` setter that cannot work. Neither is
what "TextMeshPro support" means to a project putting a title and a menu
on screen. The canvas path covers that; 3D labels can be revisited with
the same baked atlases if a game needs them.

## Consequences

- One 48-byte string per element, printable ASCII, up to four
  (font, size, style) atlases per scene: the baked-font path's limits
  apply to TMP text unchanged (deviation 30).
- `fontStyle` Bold and Italic bake as such, for `Text` as well, which used
  to bake every font upright.
- `alignment` is a runtime property for both `Text` (as `TextAnchor`) and
  TMP text, applied per line at draw time, through a new pair of bridge
  calls. TMP's six horizontal and six vertical options fold onto the three
  the draw table has; reading the property back returns the folded value.
- Deviation 45 in docs/supported-api.md records the mapping.
