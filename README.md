# Babylon NPE Exporter

Exports **Unity Shuriken (Particle System)** to **Babylon.js Node Particle Editor** (NodeParticleSystemSet) JSON.  
You can add this tool to your project using either method below.

---

## Installation

### Option 1: Via Package Manager (recommended)

1. In Unity: **Window → Package Manager**.
2. Click **+** → **Add package from git URL...**
3. Paste the repository URL, e.g.:
   ```text
   https://github.com/USER/unity-exporter-tool.git
   ```
   (replace `USER` with the repo owner; you can add `#main` or `#v1.0.0` for branch/tag).
4. Click **Add**. The package will appear under **Packages**, and **Tools → Babylon NPE → Export Shuriken to Node Particle Editor JSON** in the menu.

To update: in Package Manager select the package → **Update** (or change the version/tag in the URL).

### Option 2: Via .unitypackage (no Git)

1. Download the **`.unitypackage`** from [Releases](https://github.com/USER/unity-exporter-tool/releases) (it needs to be built and published once).
2. In Unity: **Assets → Import Package → Custom Package...** → select the downloaded file.
3. Import all items. **Tools → Babylon NPE → ...** will then appear in the menu.

**To build a .unitypackage for distribution:** open the project containing this tool in Unity, select the **Assets/Editor** folder in the Project window, choose **Assets → Export Package...**, include the desired files, and export.

---

## Usage

1. In Unity: **Tools → Babylon NPE → Export Shuriken to Node Particle Editor JSON**.
2. Select GameObjects with a **Particle System** (Shuriken) component in the hierarchy.
3. Click **Refresh from selection** — the list will show the found systems.
4. Set the export folder (relative to `Assets`) and optionally a default texture URL.
5. Click **Export selected to JSON** — you get `SystemName.json` and `SystemName.unity-properties.txt` (dump of Unity particle properties).

Open the JSON in [Node Particle Editor](https://npe.babylonjs.com) or load it in a Babylon.js scene via `BABYLON.NodeParticleSystemSet.ParseFromFileAsync(name, url)`.

---

## Feature checklist (full particle import)

Legend: **done** — implemented; **no** — not implemented.

### Main

| Feature | Status |
|--------|--------|
| Start Lifetime (min/max) | done |
| Start Speed → emit power (min/max) | done |
| Start Size (min/max) | done |
| Start Rotation (min/max) | done |
| Start Color (min/max) — two colors + random | done |
| Gravity Modifier | no |
| Max Particles → capacity | done |
| Simulation Space (Local/World) → isLocal | done |
| Looping / Duration → targetStopDuration | done |
| Start Delay | done |
| Scaling Mode | no |
| Play On Awake / Stop Action | no |

### Emission

| Feature | Status |
|--------|--------|
| Rate over Time (curve sampled for emit rate) | done |
| Bursts (repeating → rate; one-shot at start → manualEmitCount) | done |
| Rate over Distance (heuristic into rate) | done |

### Shape

| Feature | Status |
|--------|--------|
| Point (direction1/direction2) | done |
| Box (minEmitBox/maxEmitBox, direction1/2) | done |
| Sphere (radius, radiusThickness) | done |
| Hemisphere | done |
| Cone / Cone Volume (angle, radius, emit from edge) | done |
| Circle → Cylinder (radius, height) | done |
| Edge | no |
| Mesh / MeshRenderer | no |
| Box (3D) with shape rotation | no |

### Velocity over Lifetime

| Feature | Status |
|--------|--------|
| Linear (X/Y/Z) → velocity / direction | no |
| Orbital, Radial, Speed modifier | no |

### Limit Velocity / Force over Lifetime

| Feature | Status |
|--------|--------|
| Speed / Dampen, External Forces (X/Y/Z) | no |

### Color over Lifetime

| Feature | Status |
|--------|--------|
| Gradient → Lerp(start, end, age gradient) + UpdateColor | done |
| Color at end (color dead) | done (from module) |

### Color by Speed

| Feature | Status |
|--------|--------|
| Color gradient + speed range → Direction scale → normalize → Lerp → UpdateColor | done |

### Size over Lifetime

| Feature | Status |
|--------|--------|
| Curve → Lerp(size start, size end, age gradient) + UpdateSize | done |

### Size by Speed

| Feature | Status |
|--------|--------|
| Size curve + speed range → normalize speed → Lerp → UpdateSize | done |

### Rotation over Lifetime

| Feature | Status |
|--------|--------|
| Angular velocity curve → Lerp(angle at 0, angle at 1, age gradient) + UpdateAngle | done |

### Rotation by Speed

| Feature | Status |
|--------|--------|
| Angle curve + speed range → normalize speed → Lerp → UpdateAngle | done |

### Noise

| Feature | Status |
|--------|--------|
| Strength (X/Y/Z), frequency, scroll, damping | no |

### Collision / Triggers / Sub Emitters

| Feature | Status |
|--------|--------|
| Collision (planes, world, dampen, bounce) | no (no direct NPE equivalent) |
| Triggers (inside/outside, radius, actions) | no |
| Sub Emitters | no (separate systems in NPE) |

### Texture Sheet Animation

| Feature | Status |
|--------|--------|
| Tiles, Frame over Time, Start frame, Cycles | no |
| SetupSpriteSheetBlock / BasicSpriteUpdateBlock | no |

### Renderer

| Feature | Status |
|--------|--------|
| Material / Texture → ParticleTextureSourceBlock (URL or base64 data URL) | done |
| Render Mode (Billboard / Stretched / etc.) | no (billboard by default) |
| Min/Max Particle Size, Sorting | no |

### Tooling & serialization

| Feature | Status |
|--------|--------|
| Editor window (system selection, export folder) | done |
| JSON serialization (no external dependencies) | done |
| EditorData (block locations for NPE) | done |

---

## Files

- `BabylonNodeParticleModels.cs` — DTOs for NPE JSON.
- `ShurikenToNpeConverter.cs` — Shuriken → block graph conversion.
- `NpeJsonWriter.cs` — JSON serialization (no external dependencies).
- `ShurikenToBabylonNpeWindow.cs` — Editor window.

