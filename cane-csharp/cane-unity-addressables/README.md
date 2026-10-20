# Cane Addressables

Optional on-demand textures for Unity 6000.3 and Addressables 2.9.1.
Core and the base Unity package do not depend on Addressables.

## Setup

1. Select an imported `CaneAsset`, then **Assets > Cane > Create streamed asset**.
2. Select the generated `CaneTextureStream`, then **GameObject > Cane > Streamed skeleton**.
3. Build Addressables content and the Player using Unity's usual build workflow.

The generated asset contains byte-identical animation data and small placeholder
textures. Separate full-resolution texture assets become addressable; they are
not hard dependencies of the streaming asset. Keep the generated texture folder
outside `Resources`. The original Cane document and images are not modified.
This uses a persistent streaming asset instead of temporarily replacing source
references before and after each build.

New `Cane textures` groups pack pages separately with local paths. Existing group
settings are preserved. Configure that group's Addressables build/load paths for
remote hosting as needed.

Selecting the `CaneTextureStream` opens the same setup-pose, animation and skin
preview as an ordinary Cane asset. In edit mode it reads full-resolution pages
from the local AssetDatabase, without downloading bundles or replacing the
saved placeholders. Resident or unavailable pages retain their assigned texture.
Closing the inspector releases the temporary preview and its resources.

## Playback and ownership

`CaneTextureLoader` supplies the ordinary `CanePlayer`. Use the same scene
renderer, Canvas component, animation controls, followers and custom geometry.
For UI Toolkit, bind the element to that player. Texture completion refreshes
bound UI even while playback is paused; it does not sample animation again.

Loaders using the same stream share one resource generation. Only pages used by
final draws are requested. `KeepAliveSeconds` controls idle eviction; zero retains
requested pages until the last owner releases its generation. Disabling a loader
releases its ownership and clears the player it supplied. Pause animation through
the player's time scale or update mode when you want its picture to remain.

For application-managed players, call `Acquire()` on the stream and initialize
the player with the returned `CaneStreamResources.Resources`. Call `Request(page)`
for needed pages and `Release(resources)` when that owner is finished. Returned
Addressables handles are borrowed; do not release them separately. `Loaded` fires
after installation, and `Failed` reports a request or texture preparation error.
`Unload(page)` restores its placeholder and permits a later retry. Failed pages
are not automatically requested again every frame.

An empty page reference denotes a resident texture; place its complete image in
the corresponding `CaneAsset.Textures` entry. Runtime-added textures remain
application-managed. Call these Unity APIs on the main thread. After changing a
stream configuration, call `Reload()` and rebuild its consumers.

Releasing an in-flight request invalidates its result for this generation.
Unity may finish the underlying shared load before freeing it. A late completion
cannot replace a newer request's texture. The asset owns placeholder inputs;
Addressables owns full-resolution inputs; each generation owns only the prepared
working textures and materials it creates.
