# Cane for UI Toolkit

Native Unity 6.3 control over Cane's final geometry, validated on Windows x64
Mono/D3D11 with Gamma and Linear panels.

Install Core and Cane for Unity, then this package. `CaneVisualElement` consumes
Core's final geometry directly. It does not create a character RenderTexture.
The control is declared through UXML or created in code; set its `Asset`,
`InitialAnimation` and layout size. `ViewBox` is a rectangle in Cane coordinates;
an empty rectangle fits the initial pose and keeps that framing during animation.

The control owns a manual `CanePlayer` while attached to a panel. Its `Player`
provides animation tracks, skins, constraints and events. Detaching releases the
owned player and materials; reattaching starts the configured animation again.
Alternatively `Bind(existingPlayer)` displays an application-owned player without
changing its update mode or resource ownership. `Refresh()` uploads manual edits.

Core supplies mesh deformation, clipping, draw order and float tints. The adapter
merges adjacent draws with identical texture, blend and float tints, preserves
those tints in material uniforms, and splits oversized geometry for UI Toolkit's
16-bit indices. It supports the four Core blend modes, UI opacity,
rectangular/rounded overflow clipping, Gamma/Linear projects and forced Gamma
panel output. `Frozen` stops the owned player's clock; an externally bound player
remains under its owner's control.

Untinted controls using the same resources share UI materials. A control that
needs independent tint keeps its own reusable material; color animation preserves
full floating-point tint and cannot alter another control's material.

The base Unity package has no UI Toolkit dependency. This package requires Unity
6.3's custom UI material and mesh APIs. UI Builder editing, world-space panels and
graphics APIs other than D3D11 are not yet validated.
