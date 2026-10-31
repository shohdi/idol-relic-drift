# Source image prompts

Each image was generated separately with the built-in `image_gen.imagegen` tool and `transparent_background: true`, then copied into `Assets/Game/Art/Sources`. These are 2D input images, not externally generated/downloaded 3D models. No image edits or manual 3D modeling were performed after generation.

## Courier — `courier.png`

```text
Use case: stylized-concept. Asset type: a single 2D source image that will be fed into Idol image-to-3D for a small Unity arcade game called Relic Drift. Primary subject: ONE charming compact autonomous courier hovercraft, chunky rounded teal enamel hull with burnished brass panels, a single warm amber headlamp, two short integrated side thruster pods, a dark small cockpit canopy. Strong simple silhouette, thick continuous shapes, no thin antennas or detached pieces. A complete freestanding toy-like vehicle, no pilot. Style: polished stylized game prop / clean product render, hand-painted material feel, restrained teal/copper/amber colors, subtle wear. Composition: one object centered, fills about 75% of square frame, viewed at an elevated three-quarter front angle so front, top and one side are visible; entire vehicle including all edges is visible. Lighting: soft neutral studio light, surfaces readable, baked-in gentle shading, no dramatic rim glow. Background: genuinely transparent alpha; no ground, no floor, no pedestal, no shadow outside the object, no stars, no text, no logos, no watermark. The image must contain only this ONE isolated vehicle, suitable for single-object image-to-3D generation.
```

## Sun crystal — `sun-crystal.png`

```text
Use case: stylized-concept. Asset type: single isolated 2D concept source image for Idol image-to-3D, a collectible for the Unity game Relic Drift. Subject: ONE solid palm-sized sun crystal, a chunky golden amber mineral with a few large clean faceted surfaces, broad hexagonal main shape and blunt point, two tiny attached smaller facets fused at the lower sides, warm bright orange-yellow inner core represented as painted coloration rather than external glow. Bottom has a small integrated bronze band clasp but no stand. Style: polished stylized game prop, readable simple substantial volume, slightly hand-painted materials, brass and amber palette that matches a teal-and-brass courier vehicle. Complete object, upright, isolated and centered in a square, occupies about 70% of frame, viewed in clear three-quarter front view with top and side facets visible. Soft neutral studio lighting. Genuinely transparent alpha background; no floor, ground, cast shadow, halo, pedestal, particles, other objects, text, logo, or watermark. Keep the object opaque with thick continuous surfaces so a single-image-to-3D model can infer a clean volume. Exactly one collectible, no asset sheet.
```

## Void rock — `void-rock.png`

```text
Use case: stylized-concept. Asset type: ONE isolated 2D source image for Idol image-to-3D; a drifting rock obstacle for the Unity arcade game Relic Drift. Subject: a single compact irregular rounded asteroid, chunky continuous rugged stone body, about the shape of a lopsided potato, dark slate gray with muted violet shadows and a few large warm copper mineral patches embedded directly in its surface. Bold readable rock facets and shallow pits, broad medium-size features, no tiny pores or thin protrusions. Style: polished stylized hand-painted game prop with soft neutral studio shading; matches teal-and-brass sci-fi props in a warm/cool restrained palette. Full object centered in square canvas, fills about 75% of frame, elevated three-quarter view showing top and two sides. Genuinely transparent alpha background. No ground, no floor, no contact shadow, no pedestal, no halo, no glow, no stars, no debris, no other objects. No text, watermark or logos. It must be one connected opaque substantial object, suitable for a clean single-image-to-3D reconstruction.
```

The supplied PNGs are the selected outputs of these calls. Prompt wording records intent; it does not assert every generated detail exactly matches the wording. File hashes in the per-asset provenance records identify the actual images used by Idol.
