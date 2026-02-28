# Shuriken → Babylon.js Node Particle Editor

Экспорт **Unity Shuriken (Particle System)** в JSON формата **Babylon.js Node Particle Editor** (NodeParticleSystemSet).

**Установка тулзы в проект:** см. [корневой README](../README.md) (Package Manager по Git URL или .unitypackage).

## Как пользоваться

1. В Unity: **Tools → Babylon NPE → Export Shuriken to Node Particle Editor JSON**.
2. Выделите в иерархии объекты с компонентом **Particle System** (Shuriken).
3. Нажмите **Refresh from selection** — в списке появятся найденные системы.
4. Укажите папку экспорта (относительно `Assets`) и при необходимости URL текстуры по умолчанию.
5. Нажмите **Export selected to JSON** — в папке появятся файлы `ИмяСистемы.json`.

Полученный JSON можно открыть в [Node Particle Editor](https://npe.babylonjs.com) или загрузить в сцену Babylon.js через `BABYLON.NodeParticleSystemSet.ParseFromFileAsync(name, url)`.

---

## Чек-лист фич (полный импорт партикла)

Легенда: **готово** — уже реализовано; **нет** — не сделано.

### Main

| Фича | Готовность |
|------|------------|
| Start Lifetime (min/max) | готово |
| Start Speed — emit power (min/max) | готово |
| Start Size (min/max) | готово |
| Start Rotation (min/max) | готово |
| Start Color (min/max) — два цвета + random | готово |
| Gravity Modifier | нет |
| Max Particles → capacity | готово |
| Simulation Space (Local/World) → isLocal | готово |
| Looping / Duration → targetStopDuration | готово |
| Start Delay | готово |
| Scaling Mode | нет |
| Play On Awake / Stop Action | нет |

### Emission

| Фича | Готовность |
|------|------------|
| Rate over Time (constant) | готово |
| Rate over Time (curve) | нет |
| Bursts | нет |
| Rate over Distance | нет |

### Shape

| Фича | Готовность |
|------|------------|
| Point (direction1/direction2) | готово |
| Box (minEmitBox/maxEmitBox, direction1/2) | готово |
| Sphere (radius, radius Thickness) | нет |
| Hemisphere | нет |
| Cone (angle, radius, emit from edge) | нет |
| Circle (arc, radius) | нет |
| Edge | нет |
| Mesh / MeshRenderer (single edge) | нет |
| Box (3D) с rotation shape | нет |

### Velocity over Lifetime

| Фича | Готовность |
|------|------------|
| Linear (X/Y/Z) → velocity gradient / direction | нет |
| Orbital (X/Y/Z) | нет |
| Radial | нет |
| Speed modifier | нет |

### Limit Velocity over Lifetime

| Фича | Готовность |
|------|------------|
| Speed / Dampen | нет |

### Force over Lifetime (External Forces)

| Фича | Готовность |
|------|------------|
| X/Y/Z (в т.ч. gravity) | нет |

### Color over Lifetime

| Фича | Готовность |
|------|------------|
| Gradient → ParticleGradientBlock + UpdateColor | нет |
| Color at end (color dead) | готово (из модуля) |

### Color by Speed

| Фича | Готовность |
|------|------------|
| Color / Speed range | нет |

### Size over Lifetime

| Фича | Готовность |
|------|------------|
| Curve → gradient size / UpdateSize | нет |

### Size by Speed

| Фича | Готовность |
|------|------------|
| Size / Speed range | нет |

### Rotation over Lifetime

| Фича | Готовность |
|------|------------|
| Angular velocity (constant) → UpdateAngle | нет |
| Angular velocity (curve) | нет |

### Rotation by Speed

| Фича | Готовность |
|------|------------|
| Angular velocity / Speed range | нет |

### Noise

| Фича | Готовность |
|------|------------|
| Strength (X/Y/Z), frequency, scroll, damping | нет |

### Collision

| Фича | Готовность |
|------|------------|
| Planes / World / Dampen / Bounce / Lifetime loss | нет (в NPE нет прямого аналога) |

### Triggers

| Фича | Готовность |
|------|------------|
| Inside/Outside, radius, action (Kill, Callback, etc.) | нет |

### Sub Emitters

| Фича | Готовность |
|------|------------|
| Birth / Death / Collision sub-emitters | нет (отдельные системы в NPE) |

### Texture Sheet Animation

| Фича | Готовность |
|------|------------|
| Tiles, Animation (Single/Grid), Frame over Time, Start frame, Cycles | нет |
| SetupSpriteSheetBlock + BasicSpriteUpdateBlock | нет |

### Renderer

| Фича | Готовность |
|------|------------|
| Material / Texture → ParticleTextureSourceBlock URL или data | частично (только URL по умолчанию) |
| Render Mode (Billboard / Stretched / Horizontal / Vertical) | нет (billboard по умолчанию) |
| Min/Max Particle Size | нет |
| Sorting Fudge / Sorting Layer / Order | нет |

### Прочее

| Фича | Готовность |
|------|------------|
| Editor Window (выбор систем, экспорт в папку) | готово |
| Сериализация в JSON без внешних зависимостей | готово |
| EditorData (locations блоков для NPE) | готово |

---

## Файлы

- `BabylonNodeParticleModels.cs` — DTO под JSON NPE.
- `ShurikenToNpeConverter.cs` — конвертация Shuriken → граф блоков.
- `NpeJsonWriter.cs` — сериализация в JSON без внешних зависимостей.
- `ShurikenToBabylonNpeWindow.cs` — окно редактора.
