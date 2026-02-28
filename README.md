# Babylon NPE Exporter

Экспорт **Unity Shuriken (Particle System)** в JSON формата **Babylon.js Node Particle Editor** (NodeParticleSystemSet).  
Любой может добавить тулзу в свой проект одним из способов ниже.

---

## Установка

### Вариант 1: через Package Manager (рекомендуется)

1. В Unity: **Window → Package Manager**.
2. Нажмите **+** → **Add package from git URL...**
3. Вставьте URL репозитория, например:
   ```text
   https://github.com/USER/unity-exporter-tool.git
   ```
   (замените `USER` на владельца репо; можно добавить `#main` или `#v1.0.0` для ветки/тега).
4. Нажмите **Add**. Пакет появится в **Packages**, в меню — **Tools → Babylon NPE → Export Shuriken to Node Particle Editor JSON**.

Так можно обновлять тулзу: в Package Manager выберите пакет → **Update** (или смените версию/тег в URL).

### Вариант 2: через .unitypackage (если нет Git)

1. Скачайте файл **`.unitypackage`** из [Releases](https://github.com/USER/unity-exporter-tool/releases) (его нужно один раз собрать и выложить).
2. В Unity: **Assets → Import Package → Custom Package...** → укажите скачанный файл.
3. Импортируйте все пункты. После этого в меню появится **Tools → Babylon NPE → ...**.

**Как собрать .unitypackage для раздачи:** откройте проект с этой тулзой в Unity, в Project выделите папку **Assets/Editor**, выберите **Assets → Export Package...**, отметьте нужные файлы и экспортируйте.

---

## Использование

1. **Tools → Babylon NPE → Export Shuriken to Node Particle Editor JSON**.
2. Выделите объекты с **Particle System** (Shuriken) → **Refresh from selection**.
3. Укажите папку экспорта и при необходимости URL текстуры по умолчанию.
4. **Export selected to JSON**. Файлы можно открыть в [Node Particle Editor](https://npe.babylonjs.com) или загрузить через `BABYLON.NodeParticleSystemSet.ParseFromFileAsync(name, url)`.

Подробный список возможностей и чек-лист фич — в [Assets/Editor/README.md](Assets/Editor/README.md).
