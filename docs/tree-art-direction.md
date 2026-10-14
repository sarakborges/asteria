# Tree families and texture direction

Every tree uses the oak content contract. A family owns natural, stripped,
hollow and stripped-hollow logs, planks, leaves and a sapling. IDs stay
`asteria:{type}_{species}_{variation}`; the base ID omits the variation.
Wood belongs to `blocks_wooden`, foliage and saplings to `foliage`.
Log tool transitions use the existing authored variant family.

## Color identity

| Family | Bark | Exposed wood and planks | Foliage |
| --- | --- | --- | --- |
| oak | Honey brown | Warm honey tan | Biome-tinted green |
| willow | Moss olive | Pale sage olive | Biome-tinted foliage |
| juniper | Red cedar with plum shadows | Salmon cedar | Evergreen |
| windwood | Silver gray | Pale ivory | Yellow green |
| frost_wood | Cold ice blue | Pale icy cyan | Frosted cyan |
| rime_deadwood | Ashen slate violet | Dusty gray lavender | Gray lavender |
| skywood | Sea green | Seafoam mint | Turquoise |
| charwood | Charred brown and ember | Smoke brown | Ember orange |
| thornwood | Dark rust brown | Copper red | Crimson |
| enchanted_tree | Royal and celeste blue | Celeste blue | Gold |
| world_tree | Golden amber | Warm bright gold | Blue |

The four formerly similar temperate woods differ in hue and brightness.
Frostwood and rime deadwood differ in hue across wood, foliage and saplings.
Preview colors are sampled from their actual visible texture pixels.

## Asset contract

Files live in `packs/default/resources/textures/blocks/trees/{species}/`:

- `side.png`: natural bark, flowing vertically.
- `stripped.png`: exposed longitudinal grain, broad calm bands, sparse fibers
  and small knots. Its drawing differs from bark.
- `top.png`: angular end-grain rings with a bark perimeter.
- `top_stripped.png`: exposed end grain reaching the edges without bark.
- `planks.png`: readable horizontal boards and staggered dark joints.
- `leaf.png`: cutout foliage with transparent holes.
- `sapling.png`: transparent planted sprite matching the family.

All tiles are 64×64. Wood is opaque. Leaves and saplings retain cutout alpha.
Oak remains the pixel-scale and material-pattern reference. Export ImageGen
edits with nearest-neighbor resizing and restrained palettes, without dithering.
Review tiled bark, grain, boards and foliage alongside all other families.

Frostwood has a canonical `leaf_frost_wood`. Alpine and arctic foliage remain
explicit frostwood variations; ember foliage is a charwood variation. Localized
foliage names always identify the wood family and use Leaves/Folhas/Hojas.
Frostwood sapling growth uses frostwood foliage, never rime deadwood foliage.

## ImageGen prompt set

The built-in ImageGen tool authored the raster edits; no external model/API was
used. Each call edits one tile, with the existing tile as its edit target.
The following prompts and palette specifications describe the final set:

- **Bark, tops and boards:** change only the family color palette; preserve
  oak-based pixel scale, proportions, vertical bark flow, angular growth rings,
  or horizontal board heights and staggered joints. Full-bleed square tile;
  seamless repeat for bark and boards; no added decoration.
- **Stripped side:** redraw as genuinely exposed wood with broad calm vertical
  growth bands, sparse short fibers and understated elongated knots. Remove
  bark zigzags and deep bark furrows. Use the stripped oak tile as a material
  reference while keeping the family's own colors.
- **Stripped top:** retain angular growth rings; remove the dark bark perimeter;
  exposed wood reaches all four square edges and corners. Reference the same
  family's stripped side for color. No frame, bevel or circular log silhouette.
- **Saplings:** recolor woody stems to the family's bark; preserve the foliage
  colors for willow, juniper and windwood. Frostwood uses icy cyan throughout;
  rime deadwood uses dusty lavender and slate violet. Preserve transparent
  backgrounds, branching and silhouettes.
- **Rime foliage:** recolor existing leaf clusters to dusty gray lavender;
  preserve cluster scale and transparent gaps, with no icy blue hues.

Shared constraints: strict 64×64 texel pixel art, nearest-neighbor enlargement,
restrained solid colors, one flat square tile only, no labels, margins, drawn
checkerboard, anti-aliasing, smooth gradients, photographic detail or 3D cubes.

| Edited palette | Main bark | Main exposed wood | Grain | Highlight |
| --- | --- | --- | --- | --- |
| willow | `596449` | `AFB994` | `82916C` | `D0D5B2` |
| juniper | `844B50` | `CE8B81` | `A46164` | `E9B7A5` |
| windwood | `B7B2A3` | `DEDBCA` | `B5B8AA` | `F0EFE1` |
| frost_wood | `82BACE` | `B6E5EA` | `77B5CE` | `DEF8F4` |
| rime_deadwood | `777080` | `ABA3B6` | `79758E` | `CBC5D1` |
| oak stripped | Existing bark | `C5A666` | `A08451` | `DDC184` |
| charwood stripped | Existing bark | `6A5148` | `453B38` | `9A7360` |
| enchanted_tree stripped | Existing bark | `4499E1` | `2770C2` | `73C4F0` |
| skywood stripped | Existing bark | `A6D5C3` | `70AB9E` | `D2EBD9` |
| thornwood stripped | Existing bark | `A85E3C` | `7C432E` | `CC8958` |
| world_tree stripped | Existing bark | `EDBA45` | `C18A24` | `F9DD76` |

## Regression validation

`TreeFamilyContentTests` discovers every authored texture family and checks
usable log variants, wood categories, planks, canonical leaves, matching
sapling growth, localized family names and all seven 64×64 assets. Existing
content naming tests continue to validate type-first IDs and resource names.
All behavior remains in the existing registry, tools and growth owners;
the change is pack content and presentation assets.
