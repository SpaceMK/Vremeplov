# ARScene

## The AR Session starts disabled — this is intentional

The `ARSession` component in this scene is saved with **Enabled unticked**
(`m_Enabled: 0`). This is deliberate: it stops ARCore/ARKit from grabbing the
camera (and firing the OS camera-permission prompt) the instant the scene loads.

`ARFloorPlacer` (`Assets/Scripts/ARFloorPlacer.cs`) self-bootstraps on every
`ARScene` load and **enables the session itself, but only after camera permission
has been granted** — so the permission prompt is a single, controlled request
instead of a race between our request and ARCore's auto-request.

### Testing AR in the Editor / on device
- **In normal play** (entering from the map, or pressing Play in this scene),
  you do **not** need to do anything — `ARFloorPlacer` turns the session on.
- **If you strip out / disable `ARFloorPlacer`** and want to test AR directly,
  re-tick **Enabled** on the `AR Session` GameObject's `ARSession` component
  (or call `session.enabled = true` yourself). Otherwise the camera feed and
  plane detection never start and the screen stays on "Searching for a floor…".

If you re-enable it on the component in the Inspector, remember it will then
prompt for the camera as soon as the scene loads, bypassing the gated flow.
