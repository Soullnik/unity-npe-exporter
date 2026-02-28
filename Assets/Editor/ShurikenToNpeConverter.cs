// Converts Unity Shuriken (ParticleSystem) to Babylon NodeParticleSystemSet JSON.
// Graph structure mirrors nodeParticleSystemSet.helper.ts (CreateParticle -> Shape -> UpdatePosition -> System).

using System;
using System.Collections.Generic;
using UnityEngine;
using BabylonNodeParticle;

namespace ShurikenToBabylonNpe
{
    /// <summary>Builds NodeParticleSystemSet JSON from a Unity ParticleSystem (Shuriken).</summary>
    public static class ShurikenToNpeConverter
    {
        // Contextual values from NPE CONTEXTUAL_VALUE_BY_OUTPUT_TYPE (blockSerializationInterfaces.ts)
        private const int ContextPosition = 1;
        private const int ContextScaledDirection = 6;
        private const int ContextAgeGradient = 8;  // 0..1 over lifetime, for Lerp in Color/Size over lifetime
        private const int ContextAge = 3;
        private const int TypeFloat = 0x0002;
        private const int TypeVector2 = 0x0004;
        private const int TypeVector3 = 0x0008;
        private const int TypeColor4 = 0x0080;
        private const int TypeParticle = 0x0020;
        private const int TypeTexture = 0x0040;
        private const int TypeInt = 0x0001;
        private const int LockPerParticle = 1;
        private const int LockOncePerParticle = 3;
        private const int MathAdd = 0;

        // Layout by connection flow: left = sources, right = consumers. Each "frame" = inputs | block.
        private const float LayoutStepX = 260f;
        private const float LayoutStepY = 80f;
        private const float FrameHeight = 240f; // vertical space per frame (min+max+random or similar)
        private const int LayoutColInputs = 0;
        private const int LayoutColRandomLerp = 1;
        private const int LayoutColCreateParticle = 2;
        private const int LayoutColShape = 3;
        private const int LayoutColPositionDir = 4;
        private const int LayoutColAdd = 5;
        private const int LayoutColUpdatePosition = 6;
        private const int LayoutColTexture = 7;
        private const int LayoutColSystem = 8;
        private const float SystemLayoutWidth = 2400f; // 9 columns

        /// <summary>Convert single ParticleSystem to NodeParticleSystemSet (one SystemBlock).</summary>
        public static NodeParticleSystemSetJson Convert(UnityEngine.ParticleSystem ps, string setName = null, string defaultTextureUrl = null)
        {
            if (ps == null) return null;
            return ConvertMultiple(new List<UnityEngine.ParticleSystem> { ps }, setName, defaultTextureUrl);
        }

        /// <summary>Convert multiple ParticleSystems to one NodeParticleSystemSet (multiple SystemBlocks).</summary>
        public static NodeParticleSystemSetJson ConvertMultiple(System.Collections.Generic.List<UnityEngine.ParticleSystem> systems, string setName = null, string defaultTextureUrl = null)
        {
            if (systems == null || systems.Count == 0) return null;
            var first = systems[0];
            var set = new NodeParticleSystemSetJson
            {
                name = setName ?? first.name ?? "ParticleSystemSet",
                comment = "",
                editorData = new EditorDataJson()
            };

            int nextId = 1;

            for (int i = 0; i < systems.Count; i++)
            {
                var ps = systems[i];
                if (ps == null) continue;
                float baseX = i * SystemLayoutWidth;
                float baseY = 0f;
                AddSystemToSet(set, ps, defaultTextureUrl, ref nextId, baseX, baseY);
            }

            return set;
        }

        static int NextId(ref int nextId) { return nextId++; }

        /// <summary>Get particles-per-second rate from Unity Emission.rateOverTime (MinMaxCurve). NPE emitRate = same semantic.</summary>
        static float GetEmitRateFromUnity(ParticleSystem.MinMaxCurve rateOverTime)
        {
            return Mathf.Max(0f, rateOverTime.Evaluate(0.5f, 0.5f));
        }

        /// <summary>Get start/end color from Unity Color over Lifetime gradient (time 0 and 1).</summary>
        static void GetColorOverLifetimeGradientEndpoints(ParticleSystem.ColorOverLifetimeModule colorOverLifetime, out Color start, out Color end)
        {
            var gradient = colorOverLifetime.color;
            start = gradient.Evaluate(0f, 0f);
            end = gradient.Evaluate(1f, 0f);
        }

        /// <summary>Encode Unity texture to data URL (data:image/png;base64,...). Returns null if not a readable Texture2D or readable RenderTexture.</summary>
        static string GetTextureDataUrl(Texture tex)
        {
            if (tex == null)
            {
                UnityEngine.Debug.Log("[ShurikenToNpe] GetTextureDataUrl: texture is null.");
                return null;
            }
            UnityEngine.Debug.Log($"[ShurikenToNpe] GetTextureDataUrl: '{tex.name}' type={tex.GetType().Name}");
            Texture2D t2d = tex as Texture2D;
            bool tempCreated = false;
            if (t2d == null)
            {
                if (tex is RenderTexture rt)
                {
                    UnityEngine.Debug.Log($"[ShurikenToNpe] Reading from RenderTexture {rt.width}x{rt.height}");
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    t2d = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    t2d.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    t2d.Apply();
                    RenderTexture.active = prev;
                    tempCreated = true;
                }
                else
                {
                    UnityEngine.Debug.Log($"[ShurikenToNpe] Texture is not Texture2D or RenderTexture, cannot encode.");
                    return null;
                }
            }
            else if (!t2d.isReadable)
            {
                UnityEngine.Debug.Log($"[ShurikenToNpe] Texture2D '{t2d.name}' is not readable (enable Read/Write in Import Settings).");
                return null;
            }
            try
            {
                byte[] png = t2d.EncodeToPNG();
                if (tempCreated && t2d != null)
                    UnityEngine.Object.DestroyImmediate(t2d);
                if (png == null || png.Length == 0)
                {
                    UnityEngine.Debug.Log("[ShurikenToNpe] EncodeToPNG returned null or empty.");
                    return null;
                }
                UnityEngine.Debug.Log($"[ShurikenToNpe] Texture encoded to PNG, {png.Length} bytes, base64 length {System.Convert.ToBase64String(png).Length}");
                return "data:image/png;base64," + System.Convert.ToBase64String(png);
            }
            catch (System.Exception e)
            {
                if (tempCreated && t2d != null)
                    UnityEngine.Object.DestroyImmediate(t2d);
                UnityEngine.Debug.LogException(e);
                return null;
            }
        }

        /// <summary>Get min/max from Unity MinMaxCurve. Uses mode: Constant = one value for both, TwoConstants = constantMin/Max, else Evaluate.</summary>
        static void GetMinMaxFromCurve(ParticleSystem.MinMaxCurve curve, out float minVal, out float maxVal)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    minVal = maxVal = curve.constant;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    minVal = curve.constantMin;
                    maxVal = curve.constantMax;
                    break;
                default:
                    minVal = curve.Evaluate(0f, 0f);
                    maxVal = curve.Evaluate(1f, 0f);
                    break;
            }
        }

        static void AddSystemToSet(NodeParticleSystemSetJson set, UnityEngine.ParticleSystem ps, string defaultTextureUrl, ref int nextId, float baseX, float baseY)
        {
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var colorOverLifetime = ps.colorOverLifetime;
            var sizeOverLifetime = ps.sizeOverLifetime;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            UnityEngine.Debug.Log($"[ShurikenToNpe] ParticleSystem '{ps.name}': renderer={(renderer != null ? renderer.name : "null")}");
            UnityEngine.Material mat = renderer != null ? renderer.sharedMaterial : null;
            if (mat == null)
                UnityEngine.Debug.Log("[ShurikenToNpe] No material on renderer (sharedMaterial is null).");
            else
                UnityEngine.Debug.Log($"[ShurikenToNpe] Material '{mat.name}', shader '{mat.shader?.name}'");
            // Only the albedo (main) texture from the material is needed; material itself is not used in NPE
            Texture tex = null;
            if (mat != null)
            {
                tex = mat.GetTexture("_MainTex");
                UnityEngine.Debug.Log($"[ShurikenToNpe] _MainTex: {(tex != null ? tex.name + " (" + tex.GetType().Name + ")" : "null")}");
                if (tex == null)
                {
                    tex = mat.mainTexture;
                    UnityEngine.Debug.Log($"[ShurikenToNpe] mainTexture: {(tex != null ? tex.name + " (" + tex.GetType().Name + ")" : "null")}");
                }
                if (tex == null)
                {
                    var names = new List<string>();
                    mat.GetTexturePropertyNames(names);
                    UnityEngine.Debug.Log($"[ShurikenToNpe] Material texture property names: [{string.Join(", ", names)}]");
                    foreach (string prop in names)
                    {
                        var t = mat.GetTexture(prop);
                        if (t != null)
                            UnityEngine.Debug.Log($"[ShurikenToNpe]   {prop} => {t.name} ({t.GetType().Name})");
                    }
                }
            }
            string textureUrl = defaultTextureUrl ?? (tex != null ? "data or url" : "https://assets.babylonjs.com/core/textures/flare.png");

            // --- Blocks (order: create block tree, then system, then inputs/random/lerp/texture) ---
            int idSystem = NextId(ref nextId);
            int idUpdatePosition = NextId(ref nextId);
            int idAdd = NextId(ref nextId);
            int idPosition = NextId(ref nextId);
            int idScaledDir = NextId(ref nextId);
            int idCreateParticle = NextId(ref nextId);
            int idShape = NextId(ref nextId);
            int idLifetimeMin = NextId(ref nextId);
            int idLifetimeMax = NextId(ref nextId);
            int idRandomLifetime = NextId(ref nextId);
            int idEmitPowerMin = NextId(ref nextId);
            int idEmitPowerMax = NextId(ref nextId);
            int idRandomEmitPower = NextId(ref nextId);
            int idSizeMin = NextId(ref nextId);
            int idSizeMax = NextId(ref nextId);
            int idRandomSize = NextId(ref nextId);
            int idScaleMin = NextId(ref nextId);
            int idScaleMax = NextId(ref nextId);
            int idRandomScale = NextId(ref nextId);
            int idAngleMin = NextId(ref nextId);
            int idAngleMax = NextId(ref nextId);
            int idRandomAngle = NextId(ref nextId);
            int idColor1 = NextId(ref nextId);
            int idColor2 = NextId(ref nextId);
            int idColorStep = NextId(ref nextId);
            int idLerpColor = NextId(ref nextId);
            int idColorDead = NextId(ref nextId);
            int idTexture = NextId(ref nextId);
            int idUpdateColor = 0, idAge = 0, idColorOverLifetimeStart = 0, idColorOverLifetimeEnd = 0, idLerpColorOverLifetime = 0;
            int idUpdateSize = 0, idSizeOverLifetimeStart = 0, idSizeOverLifetimeEnd = 0, idLerpSizeOverLifetime = 0, idAgeForSize = 0;
            if (colorOverLifetime.enabled)
            {
                idUpdateColor = NextId(ref nextId);
                idAge = NextId(ref nextId);
                idColorOverLifetimeStart = NextId(ref nextId);
                idColorOverLifetimeEnd = NextId(ref nextId);
                idLerpColorOverLifetime = NextId(ref nextId);
            }
            if (sizeOverLifetime.enabled)
            {
                idUpdateSize = NextId(ref nextId);
                idAgeForSize = NextId(ref nextId);
                idSizeOverLifetimeStart = NextId(ref nextId);
                idSizeOverLifetimeEnd = NextId(ref nextId);
                idLerpSizeOverLifetime = NextId(ref nextId);
            }

            float fy(int frame) => frame * FrameHeight;

            GetMinMaxFromCurve(main.startLifetime, out float minLife, out float maxLife);
            GetMinMaxFromCurve(main.startSpeed, out float minSpeed, out float maxSpeed);
            GetMinMaxFromCurve(main.startSize, out float minSize, out float maxSize);
            GetMinMaxFromCurve(main.startRotation, out float minRot, out float maxRot);
            Color c1 = main.startColor.colorMin;
            Color c2 = main.startColor.colorMax;
            Color cDead = colorOverLifetime.enabled && colorOverLifetime.color.mode == ParticleSystemGradientMode.Color ? colorOverLifetime.color.color : new Color(0, 0, 0, 0);

            // SystemBlock
            var systemBlock = new SystemBlockJson
            {
                customType = "BABYLON.SystemBlock",
                id = idSystem,
                name = main.simulationSpace == ParticleSystemSimulationSpace.World ? "Particle system" : "Particle system (local)",
                capacity = main.maxParticles,
                manualEmitCount = -1,
                blendMode = 0,
                updateSpeed = 0.0167f,
                isBillboardBased = true,
                billBoardMode = 0,
                isLocal = main.simulationSpace == ParticleSystemSimulationSpace.Local,
                startDelay = main.startDelay.constant,
                emitRate = GetEmitRateFromUnity(emission.rateOverTime),
                targetStopDuration = main.duration * (main.loop ? 0 : 1),
                emitter = new float[] { 0, 0, 0 }
            };
            // Particle chain: UpdatePosition -> [UpdateColor] -> [UpdateSize] -> System
            int idParticleSourceForSystem = idUpdatePosition;
            if (colorOverLifetime.enabled) idParticleSourceForSystem = idUpdateColor;
            if (sizeOverLifetime.enabled) idParticleSourceForSystem = idUpdateSize;
            systemBlock.inputs.Add(Connection("particle", idParticleSourceForSystem, "output"));
            systemBlock.inputs.Add(ValueInput("emitRate", systemBlock.emitRate));
            systemBlock.inputs.Add(Connection("texture", idTexture, "texture"));
            systemBlock.inputs.Add(EmptyInput("translationPivot"));
            systemBlock.inputs.Add(EmptyInput("textureMask"));
            systemBlock.inputs.Add(ValueInput("targetStopDuration", systemBlock.targetStopDuration));
            systemBlock.inputs.Add(EmptyInput("onStart"));
            systemBlock.inputs.Add(EmptyInput("onEnd"));
            systemBlock.outputs.Add(Out("system"));
            AddLoc(set, idSystem, baseX + LayoutColSystem * LayoutStepX, baseY + 720f);
            set.blocks.Add(systemBlock);

            // UpdatePositionBlock: position = Add(Position, ScaledDirection)
            var updatePos = new UpdatePositionBlockJson
            {
                customType = "BABYLON.UpdatePositionBlock",
                id = idUpdatePosition,
                name = "Update position"
            };
            updatePos.inputs.Add(Connection("particle", idShape, "output"));
            updatePos.inputs.Add(Connection("position", idAdd, "output"));
            updatePos.outputs.Add(Out("output"));
            AddLoc(set, idUpdatePosition, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + 720f);
            set.blocks.Add(updatePos);

            // Color over Lifetime: UpdateColorBlock (particle from UpdatePosition, color from Lerp(start, end, age gradient))
            if (colorOverLifetime.enabled)
            {
                GetColorOverLifetimeGradientEndpoints(colorOverLifetime, out Color colStart, out Color colEnd);
                var ageGradientInput = new ParticleInputBlockJson
                {
                    customType = "BABYLON.ParticleInputBlock",
                    id = idAge,
                    name = "Age gradient",
                    type = TypeFloat,
                    contextualValue = ContextAgeGradient,
                    systemSource = 0,
                    min = 0,
                    max = 0,
                    groupInInspector = "",
                    displayInInspector = true
                };
                ageGradientInput.outputs.Add(Out("output"));
                set.blocks.Add(ageGradientInput);
                AddLoc(set, idAge, baseX + LayoutColInputs * LayoutStepX, baseY + fy(6));

                AddInputBlockColor4(set, idColorOverLifetimeStart, "Color over lifetime (start)", colStart.r, colStart.g, colStart.b, colStart.a, baseX, baseY, LayoutColInputs, fy(6) + LayoutStepY);
                AddInputBlockColor4(set, idColorOverLifetimeEnd, "Color over lifetime (end)", colEnd.r, colEnd.g, colEnd.b, colEnd.a, baseX, baseY, LayoutColInputs, fy(6) + 2 * LayoutStepY);
                AddLerpBlock(set, idLerpColorOverLifetime, "Lerp color over lifetime", idColorOverLifetimeStart, idColorOverLifetimeEnd, idAge);
                AddLoc(set, idLerpColorOverLifetime, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(6) + LayoutStepY);

                var updateColor = new UpdateColorBlockJson
                {
                    customType = "BABYLON.UpdateColorBlock",
                    id = idUpdateColor,
                    name = "Update color"
                };
                updateColor.inputs.Add(Connection("particle", idUpdatePosition, "output"));
                updateColor.inputs.Add(Connection("color", idLerpColorOverLifetime, "output"));
                updateColor.outputs.Add(Out("output"));
                set.blocks.Add(updateColor);
                AddLoc(set, idUpdateColor, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + 900f);
            }

            // Size over Lifetime: UpdateSizeBlock (particle from chain, size from Lerp(sizeStart, sizeEnd, age))
            if (sizeOverLifetime.enabled)
            {
                float sizeStart = sizeOverLifetime.size.Evaluate(0f, 0f);
                float sizeEnd = sizeOverLifetime.size.Evaluate(1f, 0f);
                int ageBlockId = colorOverLifetime.enabled ? idAge : idAgeForSize;
                if (!colorOverLifetime.enabled)
                {
                    var ageGradientInput = new ParticleInputBlockJson
                    {
                        customType = "BABYLON.ParticleInputBlock",
                        id = idAgeForSize,
                        name = "Age gradient",
                        type = TypeFloat,
                        contextualValue = ContextAgeGradient,
                        systemSource = 0,
                        min = 0,
                        max = 0,
                        groupInInspector = "",
                        displayInInspector = true
                    };
                    ageGradientInput.outputs.Add(Out("output"));
                    set.blocks.Add(ageGradientInput);
                    AddLoc(set, idAgeForSize, baseX + LayoutColInputs * LayoutStepX, baseY + fy(7));
                }
                AddInputBlock(set, idSizeOverLifetimeStart, "Size over lifetime (start)", sizeStart, baseX, baseY, LayoutColInputs, colorOverLifetime.enabled ? fy(7) : fy(7) + LayoutStepY);
                AddInputBlock(set, idSizeOverLifetimeEnd, "Size over lifetime (end)", sizeEnd, baseX, baseY, LayoutColInputs, colorOverLifetime.enabled ? fy(7) + LayoutStepY : fy(7) + 2 * LayoutStepY);
                AddLerpBlock(set, idLerpSizeOverLifetime, "Lerp size over lifetime", idSizeOverLifetimeStart, idSizeOverLifetimeEnd, ageBlockId);
                AddLoc(set, idLerpSizeOverLifetime, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(7) + LayoutStepY);

                int chainHeadForSize = colorOverLifetime.enabled ? idUpdateColor : idUpdatePosition;
                var updateSize = new UpdateSizeBlockJson
                {
                    customType = "BABYLON.UpdateSizeBlock",
                    id = idUpdateSize,
                    name = "Update size"
                };
                updateSize.inputs.Add(Connection("particle", chainHeadForSize, "output"));
                updateSize.inputs.Add(Connection("size", idLerpSizeOverLifetime, "output"));
                updateSize.outputs.Add(Out("output"));
                set.blocks.Add(updateSize);
                AddLoc(set, idUpdateSize, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + (colorOverLifetime.enabled ? 1080f : 900f));
            }

            // Add (Position + ScaledDirection)
            var addBlock = new ParticleMathBlockJson
            {
                customType = "BABYLON.ParticleMathBlock",
                id = idAdd,
                name = "Add",
                operation = MathAdd
            };
            addBlock.inputs.Add(Connection("left", idPosition, "output"));
            addBlock.inputs.Add(Connection("right", idScaledDir, "output"));
            addBlock.outputs.Add(Out("output"));
            AddLoc(set, idAdd, baseX + LayoutColAdd * LayoutStepX, baseY + 1560f);
            set.blocks.Add(addBlock);

            var posInput = new ParticleInputBlockJson
            {
                customType = "BABYLON.ParticleInputBlock",
                id = idPosition,
                name = "Position",
                type = TypeVector3,
                contextualValue = ContextPosition,
                systemSource = 0,
                min = 0,
                max = 0,
                groupInInspector = "",
                displayInInspector = true
            };
            posInput.outputs.Add(Out("output"));
            AddLoc(set, idPosition, baseX + LayoutColPositionDir * LayoutStepX, baseY + 1520f);
            set.blocks.Add(posInput);

            var dirInput = new ParticleInputBlockJson
            {
                customType = "BABYLON.ParticleInputBlock",
                id = idScaledDir,
                name = "Scaled direction",
                type = TypeVector3,
                contextualValue = ContextScaledDirection,
                systemSource = 0,
                min = 0,
                max = 0,
                groupInInspector = "",
                displayInInspector = true
            };
            dirInput.outputs.Add(Out("output"));
            AddLoc(set, idScaledDir, baseX + LayoutColPositionDir * LayoutStepX, baseY + 1600f);
            set.blocks.Add(dirInput);

            // CreateParticle
            var createBlock = new CreateParticleBlockJson
            {
                customType = "BABYLON.CreateParticleBlock",
                id = idCreateParticle,
                name = "Create particle"
            };
            bool lifetimeConst = main.startLifetime.mode == ParticleSystemCurveMode.Constant;
            bool emitPowerConst = main.startSpeed.mode == ParticleSystemCurveMode.Constant;
            bool sizeConst = main.startSize.mode == ParticleSystemCurveMode.Constant;
            bool scaleConst = true;
            bool angleConst = main.startRotation.mode == ParticleSystemCurveMode.Constant;
            bool colorConst = main.startColor.mode == ParticleSystemGradientMode.Color;
            int idLifetimeForCreate = lifetimeConst ? idLifetimeMin : idRandomLifetime;
            int idEmitPowerForCreate = emitPowerConst ? idEmitPowerMin : idRandomEmitPower;
            int idSizeForCreate = sizeConst ? idSizeMin : idRandomSize;
            int idScaleForCreate = scaleConst ? idScaleMin : idRandomScale;
            int idAngleForCreate = angleConst ? idAngleMin : idRandomAngle;
            int idColorForCreate = colorConst ? idColor1 : idLerpColor;
            createBlock.inputs.Add(Connection("lifeTime", idLifetimeForCreate, "output"));
            createBlock.inputs.Add(Connection("emitPower", idEmitPowerForCreate, "output"));
            createBlock.inputs.Add(Connection("size", idSizeForCreate, "output"));
            createBlock.inputs.Add(Connection("scale", idScaleForCreate, "output"));
            createBlock.inputs.Add(Connection("angle", idAngleForCreate, "output"));
            createBlock.inputs.Add(Connection("color", idColorForCreate, "output"));
            createBlock.inputs.Add(Connection("colorDead", idColorDead, "output"));
            createBlock.outputs.Add(Out("particle"));
            AddLoc(set, idCreateParticle, baseX + LayoutColCreateParticle * LayoutStepX, baseY + 720f);
            set.blocks.Add(createBlock);

            // Shape (Box or Point)
            if (shape.shapeType == ParticleSystemShapeType.Box)
            {
                var box = new BoxShapeBlockJson
                {
                    customType = "BABYLON.BoxShapeBlock",
                    id = idShape,
                    name = "Box shape"
                };
                box.inputs.Add(Connection("particle", idCreateParticle, "particle"));
                Vector3 size = shape.scale;
                box.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                box.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                box.inputs.Add(ValueVector3("minEmitBox", -size.x * 0.5f, -size.y * 0.5f, -size.z * 0.5f));
                box.inputs.Add(ValueVector3("maxEmitBox", size.x * 0.5f, size.y * 0.5f, size.z * 0.5f));
                box.outputs.Add(Out("output"));
                AddLoc(set, idShape, baseX + LayoutColShape * LayoutStepX, baseY + 720f);
                set.blocks.Add(box);
            }
            else
            {
                var point = new PointShapeBlockJson
                {
                    customType = "BABYLON.PointShapeBlock",
                    id = idShape,
                    name = "Point shape"
                };
                point.inputs.Add(Connection("particle", idCreateParticle, "particle"));
                point.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                point.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                point.outputs.Add(Out("output"));
                AddLoc(set, idShape, baseX + LayoutColShape * LayoutStepX, baseY + 720f);
                set.blocks.Add(point);
            }

            // Frame 0: Lifetime — Constant: one block; TwoConstants/Curve: Min, Max, Random
            AddInputBlock(set, idLifetimeMin, lifetimeConst ? "Lifetime" : "Min Lifetime", minLife, baseX, baseY, LayoutColInputs, fy(0));
            if (!lifetimeConst)
            {
                AddInputBlock(set, idLifetimeMax, "Max Lifetime", maxLife, baseX, baseY, LayoutColInputs, fy(0) + LayoutStepY);
                AddRandomBlock(set, idRandomLifetime, "Random Lifetime", idLifetimeMin, idLifetimeMax, LockPerParticle);
                AddLoc(set, idRandomLifetime, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(0) + LayoutStepY);
            }

            // Frame 1: Emit Power
            AddInputBlock(set, idEmitPowerMin, emitPowerConst ? "Emit Power" : "Min Emit Power", minSpeed, baseX, baseY, LayoutColInputs, fy(1));
            if (!emitPowerConst)
            {
                AddInputBlock(set, idEmitPowerMax, "Max Emit Power", maxSpeed, baseX, baseY, LayoutColInputs, fy(1) + LayoutStepY);
                AddRandomBlock(set, idRandomEmitPower, "Random Emit Power", idEmitPowerMin, idEmitPowerMax, LockPerParticle);
                AddLoc(set, idRandomEmitPower, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(1) + LayoutStepY);
            }

            // Frame 2: Size
            AddInputBlock(set, idSizeMin, sizeConst ? "Size" : "Min size", minSize, baseX, baseY, LayoutColInputs, fy(2));
            if (!sizeConst)
            {
                AddInputBlock(set, idSizeMax, "Max size", maxSize, baseX, baseY, LayoutColInputs, fy(2) + LayoutStepY);
                AddRandomBlock(set, idRandomSize, "Random size", idSizeMin, idSizeMax, LockPerParticle);
                AddLoc(set, idRandomSize, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(2) + LayoutStepY);
            }

            // Frame 3: Scale (we export as constant 1,1)
            AddInputBlockVector2(set, idScaleMin, "Scale", 1, 1, baseX, baseY, LayoutColInputs, fy(3));
            if (!scaleConst)
            {
                AddInputBlockVector2(set, idScaleMax, "Max Scale", 1, 1, baseX, baseY, LayoutColInputs, fy(3) + LayoutStepY);
                AddRandomBlock(set, idRandomScale, "Random Scale", idScaleMin, idScaleMax, LockPerParticle);
                AddLoc(set, idRandomScale, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(3) + LayoutStepY);
            }

            // Frame 4: Angle
            AddInputBlock(set, idAngleMin, angleConst ? "Rotation" : "Min Rotation", minRot, baseX, baseY, LayoutColInputs, fy(4));
            if (!angleConst)
            {
                AddInputBlock(set, idAngleMax, "Max Rotation", maxRot, baseX, baseY, LayoutColInputs, fy(4) + LayoutStepY);
                AddRandomBlock(set, idRandomAngle, "Random Rotation", idAngleMin, idAngleMax, LockPerParticle);
                AddLoc(set, idRandomAngle, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(4) + LayoutStepY);
            }

            // Frame 5: Color — Constant: one block; else Color1, Color2, Random step, Lerp. ColorDead always one block.
            AddInputBlockColor4(set, idColor1, colorConst ? "Start Color" : "Color 1", c1.r, c1.g, c1.b, c1.a, baseX, baseY, LayoutColInputs, fy(5));
            if (!colorConst)
            {
                AddInputBlockColor4(set, idColor2, "Color 2", c2.r, c2.g, c2.b, c2.a, baseX, baseY, LayoutColInputs, fy(5) + LayoutStepY);
                AddRandomBlock(set, idColorStep, "Random color step", 0, 1, LockOncePerParticle);
                AddLoc(set, idColorStep, baseX + LayoutColInputs * LayoutStepX, baseY + fy(5) + 2 * LayoutStepY);
                AddLerpBlock(set, idLerpColor, "Lerp color", idColor1, idColor2, idColorStep);
                AddLoc(set, idLerpColor, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(5) + LayoutStepY);
            }
            AddInputBlockColor4(set, idColorDead, "Dead Color", cDead.r, cDead.g, cDead.b, cDead.a, baseX, baseY, LayoutColInputs, fy(5) + (colorConst ? 1 : 3) * LayoutStepY);

            // Texture: serialize to base64 data URL when possible (NPE textureDataUrl)
            string textureDataUrl = GetTextureDataUrl(tex);
            var texBlock = new ParticleTextureSourceBlockJson
            {
                customType = "BABYLON.ParticleTextureSourceBlock",
                id = idTexture,
                name = "Texture",
                url = textureDataUrl != null ? "" : textureUrl,
                serializedCachedData = textureDataUrl != null,
                invertY = false,
                textureDataUrl = textureDataUrl
            };
            texBlock.outputs.Add(Out("texture"));
            AddLoc(set, idTexture, baseX + LayoutColTexture * LayoutStepX, baseY + 800f);
            set.blocks.Add(texBlock);
        }

        static void AddLoc(NodeParticleSystemSetJson set, int blockId, float x, float y)
        {
            set.editorData.locations.Add(new LocationJson { blockId = blockId, x = x, y = y, isCollapsed = false });
        }

        static InputPortJson Connection(string inputName, int targetBlockId, string targetConnectionName)
        {
            return new InputPortJson { name = inputName, inputName = inputName, targetBlockId = targetBlockId, targetConnectionName = targetConnectionName };
        }

        static InputPortJson EmptyInput(string name)
        {
            return new InputPortJson { name = name };
        }

        static InputPortJson ValueInput(string name, object value)
        {
            if (value is float f) return new InputPortJson { name = name, valueType = "number", value = f };
            if (value is int i) return new InputPortJson { name = name, valueType = "number", value = i };
            return new InputPortJson { name = name, valueType = "number", value = value };
        }

        static InputPortJson ValueVector3(string name, float x, float y, float z)
        {
            return new InputPortJson { name = name, valueType = "BABYLON.Vector3", value = new float[] { x, y, z } };
        }

        static OutputPortJson Out(string name) => new OutputPortJson { name = name };

        /// <summary>Place input block at (baseX + colIndex * LayoutStepX, baseY + yOffset). NPE format: value/valueType on block, inputs empty.</summary>
        static void AddInputBlock(NodeParticleSystemSetJson set, int id, string name, float value, float baseX, float baseY, int colIndex, float yOffset)
        {
            var b = new ParticleInputBlockJson
            {
                customType = "BABYLON.ParticleInputBlock",
                id = id,
                name = name,
                type = TypeFloat,
                valueType = "number",
                value = value,
                contextualValue = 0,
                systemSource = 0,
                min = 0,
                max = 0,
                groupInInspector = "",
                displayInInspector = true
            };
            b.outputs.Add(Out("output"));
            set.blocks.Add(b);
            AddLoc(set, id, baseX + colIndex * LayoutStepX, baseY + yOffset);
        }

        static void AddInputBlockVector2(NodeParticleSystemSetJson set, int id, string name, float x, float y, float baseX, float baseY, int colIndex, float yOffset)
        {
            var b = new ParticleInputBlockJson
            {
                customType = "BABYLON.ParticleInputBlock",
                id = id,
                name = name,
                type = TypeVector2,
                valueType = "BABYLON.Vector2",
                value = new float[] { x, y },
                contextualValue = 0,
                systemSource = 0,
                min = 0,
                max = 0,
                groupInInspector = "",
                displayInInspector = true
            };
            b.outputs.Add(Out("output"));
            set.blocks.Add(b);
            AddLoc(set, id, baseX + colIndex * LayoutStepX, baseY + yOffset);
        }

        static void AddInputBlockColor4(NodeParticleSystemSetJson set, int id, string name, float r, float g, float b, float a, float baseX, float baseY, int colIndex, float yOffset)
        {
            var block = new ParticleInputBlockJson
            {
                customType = "BABYLON.ParticleInputBlock",
                id = id,
                name = name,
                type = TypeColor4,
                valueType = "BABYLON.Color4",
                value = new float[] { r, g, b, a },
                contextualValue = 0,
                systemSource = 0,
                min = 0,
                max = 0,
                groupInInspector = "",
                displayInInspector = true
            };
            block.outputs.Add(Out("output"));
            set.blocks.Add(block);
            AddLoc(set, id, baseX + colIndex * LayoutStepX, baseY + yOffset);
        }

        static void AddRandomBlock(NodeParticleSystemSetJson set, int id, string name, int idMin, int idMax, int lockMode)
        {
            var b = new ParticleRandomBlockJson
            {
                customType = "BABYLON.ParticleRandomBlock",
                id = id,
                name = name,
                lockMode = lockMode
            };
            b.inputs.Add(Connection("min", idMin, "output"));
            b.inputs.Add(Connection("max", idMax, "output"));
            b.outputs.Add(Out("output"));
            set.blocks.Add(b);
        }

        static void AddLerpBlock(NodeParticleSystemSetJson set, int id, string name, int idLeft, int idRight, int idGradient)
        {
            var b = new ParticleLerpBlockJson
            {
                customType = "BABYLON.ParticleLerpBlock",
                id = id,
                name = name
            };
            b.inputs.Add(Connection("left", idLeft, "output"));
            b.inputs.Add(Connection("right", idRight, "output"));
            b.inputs.Add(Connection("gradient", idGradient, "output"));
            b.outputs.Add(Out("output"));
            set.blocks.Add(b);
        }
    }
}
