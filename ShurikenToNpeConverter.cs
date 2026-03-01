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
        private const int ContextDirectionScale = 32;  // speed/magnitude for "by Speed" modules
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
        private const int MathSubtract = 1;
        private const int MathDivide = 3;

        // Babylon Engine blend modes (SystemBlock.blendMode): 0=ALPHA_COMBINE, 1=ALPHA_ADD, 2=ALPHA_MULTIPLY
        private const int BlendCombine = 0;
        private const int BlendAdditive = 1;
        private const int BlendMultiply = 2;

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

        /// <summary>Get effective particles-per-second from Unity Emission: rateOverTime, rateOverDistance (heuristic), and Bursts. Burst contributes only when it repeats: cycleCount==0 (infinite) or cycleCount>1; cycleCount==1 is one-shot at time (no steady rate).</summary>
        static float GetEmitRateFromUnity(ParticleSystem.EmissionModule emission)
        {
            if (!emission.enabled) return 0f;
            float rate = Mathf.Max(0f, emission.rateOverTime.Evaluate(0.5f, 0.5f));
            // Rate over distance: particles per unit distance when emitter moves. Approximate as rate assuming 1 unit/s.
            rate += Mathf.Max(0f, emission.rateOverDistance.Evaluate(0.5f, 0.5f));
            // Burst: time=when, count=particles per burst, cycleCount=how many times (0=infinite), repeatInterval=seconds between repeats, probability=0..1 chance to trigger.
            // Only add to rate when burst actually repeats (cycleCount 0 or >1). cycleCount==1 => one-shot at time, no continuous rate.
            for (int i = 0; i < emission.burstCount; i++)
            {
                var burst = emission.GetBurst(i);
                bool repeats = burst.cycleCount == 0 || burst.cycleCount > 1;
                if (repeats && burst.repeatInterval > 0f)
                {
                    float count = Mathf.Max(0f, burst.count.Evaluate(0.5f, 0.5f));
                    float prob = Mathf.Clamp01(burst.probability);
                    rate += (count / burst.repeatInterval) * prob;
                }
            }
            return Mathf.Max(0f, rate);
        }

        /// <summary>Get manualEmitCount for SystemBlock from one-shot bursts (cycleCount==1). Babylon emits this many particles once on start; use for Unity bursts at time&lt;=0.</summary>
        static int GetManualEmitCountFromUnity(ParticleSystem.EmissionModule emission)
        {
            if (!emission.enabled) return -1;
            int total = 0;
            for (int i = 0; i < emission.burstCount; i++)
            {
                var burst = emission.GetBurst(i);
                if (burst.cycleCount != 1) continue; // only one-shot
                if (burst.time > 0.001f) continue;   // only at start; bursts at time>0 would need startDelay, not supported here
                float count = Mathf.Max(0f, burst.count.Evaluate(0.5f, 0.5f));
                float prob = Mathf.Clamp01(burst.probability);
                total += Mathf.RoundToInt(count * prob);
            }
            return total > 0 ? total : -1;
        }

        /// <summary>Get NPE blend mode from Unity material. Checks _ColorMode (particle Color Mode), then _BlendMode / _Blend (Rendering Mode), then _SrcBlend/_DstBlend.</summary>
        static int GetBlendModeFromUnity(Material mat)
        {
            if (mat == null) return BlendCombine;
            // 0) Particle Color Mode (Multiply/Additive) — often drives the actual blend in particle shaders (Rendering Mode Fade + Color Mode Multiply => multiply blend)
            if (mat.HasProperty("_ColorMode"))
            {
                int c = (int)mat.GetFloat("_ColorMode");
                if (c == 1) return BlendAdditive;   // Additive
                if (c == 0) return BlendMultiply;   // Multiply (common value for Multiply in particle shaders)
            }
            // 1) URP Particles Unlit / Lit: Blending Mode = _BlendMode (0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply)
            if (mat.HasProperty("_BlendMode"))
            {
                int b = (int)mat.GetFloat("_BlendMode");
                if (b == 2) return BlendAdditive;
                if (b == 3) return BlendMultiply;
                return BlendCombine; // 0 Alpha, 1 Premultiply
            }
            // 2) Alternative property name for blend mode (some URP shaders)
            if (mat.HasProperty("_Blend"))
            {
                int b = (int)mat.GetFloat("_Blend");
                if (b == 2) return BlendAdditive;
                if (b == 3) return BlendMultiply;
                return BlendCombine;
            }
            // 3) Legacy: _SrcBlend / _DstBlend (Unity BlendMode enum). Additive = (SrcAlpha, One), Alpha = (SrcAlpha, OneMinusSrcAlpha), Multiply = (Zero, DstColor).
            if (mat.HasProperty("_DstBlend"))
            {
                int dst = (int)mat.GetFloat("_DstBlend");
                if (dst == 1) return BlendAdditive;  // One
                if (dst == 9) return BlendCombine;  // OneMinusSrcAlpha
            }
            if (mat.HasProperty("_SrcBlend") && mat.HasProperty("_DstBlend"))
            {
                int src = (int)mat.GetFloat("_SrcBlend");
                int dst = (int)mat.GetFloat("_DstBlend");
                if (src == 0 && (dst == 2 || dst == 4)) return BlendMultiply;
            }
            return BlendCombine;
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
                return null;
            Texture2D t2d = tex as Texture2D;
            bool tempCreated = false;
            if (t2d == null)
            {
                if (tex is RenderTexture rt)
                {
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    t2d = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    t2d.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    t2d.Apply();
                    RenderTexture.active = prev;
                    tempCreated = true;
                }
                else
                    return null;
            }
            // Use Blit path for compressed or non-readable Texture2D (EncodeToPNG does not support compressed formats).
            bool useBlitPath = t2d != null && !tempCreated && (!t2d.isReadable || (t2d.format != TextureFormat.RGBA32 && t2d.format != TextureFormat.ARGB32));
            if (useBlitPath && t2d != null)
            {
                int w = t2d.width, h = t2d.height;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                Graphics.Blit(tex, rt);
                t2d = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t2d.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t2d.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                tempCreated = true;
            }
            try
            {
                byte[] png = t2d.EncodeToPNG();
                if (tempCreated && t2d != null)
                    UnityEngine.Object.DestroyImmediate(t2d);
                if (png == null || png.Length == 0)
                    return null;
                return "data:image/png;base64," + System.Convert.ToBase64String(png);
            }
            catch (System.Exception)
            {
                if (tempCreated && t2d != null)
                    UnityEngine.Object.DestroyImmediate(t2d);
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
            var rotationOverLifetime = ps.rotationOverLifetime;
            var colorBySpeed = ps.colorBySpeed;
            var sizeBySpeed = ps.sizeBySpeed;
            var rotationBySpeed = ps.rotationBySpeed;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            UnityEngine.Material mat = renderer != null ? renderer.sharedMaterial : null;
            // Only the albedo (main) texture from the material is needed; material itself is not used in NPE
            Texture tex = null;
            if (mat != null)
            {
                tex = mat.GetTexture("_MainTex");
                if (tex == null)
                    tex = mat.mainTexture;
                if (tex == null)
                {
                    var names = new List<string>();
                    mat.GetTexturePropertyNames(names);
                    foreach (string prop in names)
                    {
                        var t = mat.GetTexture(prop);
                        if (t != null) { tex = t; break; }
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
            int idUpdateAngle = 0, idAgeRot = 0, idAngleRotStart = 0, idAngleRotEnd = 0, idLerpAngleRot = 0;
            if (rotationOverLifetime.enabled)
            {
                idUpdateAngle = NextId(ref nextId);
                idAgeRot = NextId(ref nextId);
                idAngleRotStart = NextId(ref nextId);
                idAngleRotEnd = NextId(ref nextId);
                idLerpAngleRot = NextId(ref nextId);
            }
            bool needSpeedNorm = (colorBySpeed.enabled && !colorOverLifetime.enabled) || (sizeBySpeed.enabled && !sizeOverLifetime.enabled) || (rotationBySpeed.enabled && !rotationOverLifetime.enabled);
            int idDirScale = 0, idSpeedMinConst = 0, idSubSpeedMin = 0, idRangeSize = 0, idDivNorm = 0, idClampNorm = 0;
            if (needSpeedNorm) { idDirScale = NextId(ref nextId); idSpeedMinConst = NextId(ref nextId); idSubSpeedMin = NextId(ref nextId); idRangeSize = NextId(ref nextId); idDivNorm = NextId(ref nextId); idClampNorm = NextId(ref nextId); }
            int idColorBySpeedStart = 0, idColorBySpeedEnd = 0, idLerpColorBySpeed = 0, idUpdateColorBySpeed = 0;
            if (colorBySpeed.enabled && !colorOverLifetime.enabled) { idColorBySpeedStart = NextId(ref nextId); idColorBySpeedEnd = NextId(ref nextId); idLerpColorBySpeed = NextId(ref nextId); idUpdateColorBySpeed = NextId(ref nextId); }
            int idSizeBySpeedStart = 0, idSizeBySpeedEnd = 0, idLerpSizeBySpeed = 0, idUpdateSizeBySpeed = 0;
            if (sizeBySpeed.enabled && !sizeOverLifetime.enabled) { idSizeBySpeedStart = NextId(ref nextId); idSizeBySpeedEnd = NextId(ref nextId); idLerpSizeBySpeed = NextId(ref nextId); idUpdateSizeBySpeed = NextId(ref nextId); }
            int idAngleBySpeedStart = 0, idAngleBySpeedEnd = 0, idLerpAngleBySpeed = 0, idUpdateAngleBySpeed = 0;
            if (rotationBySpeed.enabled && !rotationOverLifetime.enabled) { idAngleBySpeedStart = NextId(ref nextId); idAngleBySpeedEnd = NextId(ref nextId); idLerpAngleBySpeed = NextId(ref nextId); idUpdateAngleBySpeed = NextId(ref nextId); }

            float fy(int frame) => frame * FrameHeight;

            GetMinMaxFromCurve(main.startLifetime, out float minLife, out float maxLife);
            GetMinMaxFromCurve(main.startSpeed, out float minSpeed, out float maxSpeed);
            GetMinMaxFromCurve(main.startSize, out float minSize, out float maxSize);
            GetMinMaxFromCurve(main.startRotation, out float minRot, out float maxRot);
            Color c1 = main.startColor.colorMin;
            Color c2 = main.startColor.colorMax;
            Color cDead = colorOverLifetime.enabled && colorOverLifetime.color.mode == ParticleSystemGradientMode.Color ? colorOverLifetime.color.color : new Color(0, 0, 0, 0);

            // SystemBlock: name = particle system name; billBoardMode from renderer (Unity RenderMode = Billboard/Stretch/Horizontal/Vertical/Mesh/None)
            int renderMode = (int)(renderer != null ? renderer.renderMode : ParticleSystemRenderMode.Billboard);
            bool isBillboard = renderer == null || (renderMode != (int)ParticleSystemRenderMode.Mesh && renderMode != (int)ParticleSystemRenderMode.None);
            var systemBlock = new SystemBlockJson
            {
                customType = "BABYLON.SystemBlock",
                id = idSystem,
                name = ps.name,
                capacity = main.maxParticles,
                manualEmitCount = GetManualEmitCountFromUnity(emission),
                blendMode = GetBlendModeFromUnity(mat),
                updateSpeed = 0.0167f,
                isBillboardBased = isBillboard,
                billBoardMode = isBillboard ? Math.Min(renderMode, 3) : 0,
                isLocal = main.simulationSpace == ParticleSystemSimulationSpace.Local,
                startDelay = main.startDelay.constant * 1000f,
                emitRate = GetEmitRateFromUnity(emission),
                targetStopDuration = main.duration * (main.loop ? 0 : 1),
                emitter = new float[] { 0, 0, 0 }
            };
            // Particle chain: UpdatePosition -> [UpdateColor] -> [UpdateSize] -> [UpdateAngle] -> System (each step optional)
            int idParticleSourceForSystem = idUpdatePosition;
            if (colorOverLifetime.enabled) idParticleSourceForSystem = idUpdateColor;
            else if (colorBySpeed.enabled) idParticleSourceForSystem = idUpdateColorBySpeed;
            if (sizeOverLifetime.enabled) idParticleSourceForSystem = idUpdateSize;
            else if (sizeBySpeed.enabled) idParticleSourceForSystem = idUpdateSizeBySpeed;
            if (rotationOverLifetime.enabled) idParticleSourceForSystem = idUpdateAngle;
            else if (rotationBySpeed.enabled) idParticleSourceForSystem = idUpdateAngleBySpeed;
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

            // Rotation over Lifetime: UpdateAngleBlock (particle from chain, angle from Lerp(angle0, angle1, age gradient)). Unity uses angular velocity (deg/s); we approximate as angle at 0 and 1.
            if (rotationOverLifetime.enabled)
            {
                var rotCurve = rotationOverLifetime.z;
                float angle0 = rotCurve.Evaluate(0f, 0f) * Mathf.Deg2Rad;
                float angle1 = rotCurve.Evaluate(1f, 0f) * Mathf.Deg2Rad;
                int ageRotId = (colorOverLifetime.enabled || sizeOverLifetime.enabled) ? (colorOverLifetime.enabled ? idAge : idAgeForSize) : idAgeRot;
                if (ageRotId == idAgeRot)
                {
                    var ageRotInput = new ParticleInputBlockJson { customType = "BABYLON.ParticleInputBlock", id = idAgeRot, name = "Age gradient (rotation)", type = TypeFloat, contextualValue = ContextAgeGradient, systemSource = 0, min = 0, max = 0, groupInInspector = "", displayInInspector = true };
                    ageRotInput.outputs.Add(Out("output"));
                    set.blocks.Add(ageRotInput);
                    AddLoc(set, idAgeRot, baseX + LayoutColInputs * LayoutStepX, baseY + fy(8));
                }
                AddInputBlock(set, idAngleRotStart, "Rotation over lifetime (start)", angle0, baseX, baseY, LayoutColInputs, baseY + fy(8) + LayoutStepY);
                AddInputBlock(set, idAngleRotEnd, "Rotation over lifetime (end)", angle1, baseX, baseY, LayoutColInputs, baseY + fy(8) + 2 * LayoutStepY);
                AddLerpBlock(set, idLerpAngleRot, "Lerp angle over lifetime", idAngleRotStart, idAngleRotEnd, ageRotId);
                AddLoc(set, idLerpAngleRot, baseX + LayoutColRandomLerp * LayoutStepX, baseY + fy(8) + LayoutStepY);
                int chainHeadForAngle = sizeOverLifetime.enabled ? idUpdateSize : (colorOverLifetime.enabled ? idUpdateColor : idUpdatePosition);
                var updateAngle = new UpdateAngleBlockJson { customType = "BABYLON.UpdateAngleBlock", id = idUpdateAngle, name = "Update angle" };
                updateAngle.inputs.Add(Connection("particle", chainHeadForAngle, "output"));
                updateAngle.inputs.Add(Connection("angle", idLerpAngleRot, "output"));
                updateAngle.outputs.Add(Out("output"));
                set.blocks.Add(updateAngle);
                AddLoc(set, idUpdateAngle, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + (sizeOverLifetime.enabled ? 1260f : (colorOverLifetime.enabled ? 1080f : 900f)));
            }

            // By Speed: shared speed normalization (Direction scale -> Subtract(min) -> Divide(range) -> Clamp(0,1)), then per-module Lerp + Update.
            if (needSpeedNorm)
            {
                Vector2 speedRange = (colorBySpeed.enabled && !colorOverLifetime.enabled) ? colorBySpeed.range : ((sizeBySpeed.enabled && !sizeOverLifetime.enabled) ? sizeBySpeed.range : rotationBySpeed.range);
                float speedMin = speedRange.x;
                float rangeSize = Mathf.Max(0.0001f, speedRange.y - speedRange.x);

                var dirScaleInput = new ParticleInputBlockJson
                {
                    customType = "BABYLON.ParticleInputBlock",
                    id = idDirScale,
                    name = "Direction scale",
                    type = TypeFloat,
                    contextualValue = ContextDirectionScale,
                    systemSource = 0,
                    min = 0,
                    max = 0,
                    groupInInspector = "",
                    displayInInspector = true
                };
                dirScaleInput.outputs.Add(Out("output"));
                set.blocks.Add(dirScaleInput);
                AddLoc(set, idDirScale, baseX + LayoutColPositionDir * LayoutStepX, baseY + 1700f);

                AddInputBlock(set, idSpeedMinConst, "Speed range min", speedMin, baseX, baseY, LayoutColInputs, 1700f);
                AddInputBlock(set, idRangeSize, "Speed range size", rangeSize, baseX, baseY, LayoutColInputs, 1780f);

                var subBlock = new ParticleMathBlockJson { customType = "BABYLON.ParticleMathBlock", id = idSubSpeedMin, name = "Speed minus min", operation = MathSubtract };
                subBlock.inputs.Add(Connection("left", idDirScale, "output"));
                subBlock.inputs.Add(Connection("right", idSpeedMinConst, "output"));
                subBlock.outputs.Add(Out("output"));
                set.blocks.Add(subBlock);
                AddLoc(set, idSubSpeedMin, baseX + LayoutColRandomLerp * LayoutStepX, baseY + 1700f);

                var divBlock = new ParticleMathBlockJson { customType = "BABYLON.ParticleMathBlock", id = idDivNorm, name = "Normalized speed", operation = MathDivide };
                divBlock.inputs.Add(Connection("left", idSubSpeedMin, "output"));
                divBlock.inputs.Add(Connection("right", idRangeSize, "output"));
                divBlock.outputs.Add(Out("output"));
                set.blocks.Add(divBlock);
                AddLoc(set, idDivNorm, baseX + LayoutColRandomLerp * LayoutStepX, baseY + 1780f);

                var clampBlock = new ParticleClampBlockJson { customType = "BABYLON.ParticleClampBlock", id = idClampNorm, name = "Clamp speed 0-1", minimum = 0f, maximum = 1f };
                clampBlock.inputs.Add(Connection("value", idDivNorm, "output"));
                clampBlock.outputs.Add(Out("output"));
                set.blocks.Add(clampBlock);
                AddLoc(set, idClampNorm, baseX + LayoutColRandomLerp * LayoutStepX, baseY + 1860f);
            }

            if (colorBySpeed.enabled && !colorOverLifetime.enabled)
            {
                Color colStart = colorBySpeed.color.Evaluate(0f, 0f);
                Color colEnd = colorBySpeed.color.Evaluate(1f, 0f);
                AddInputBlockColor4(set, idColorBySpeedStart, "Color by speed (start)", colStart.r, colStart.g, colStart.b, colStart.a, baseX, baseY, LayoutColInputs, baseY + 1900f);
                AddInputBlockColor4(set, idColorBySpeedEnd, "Color by speed (end)", colEnd.r, colEnd.g, colEnd.b, colEnd.a, baseX, baseY, LayoutColInputs, baseY + 1980f);
                AddLerpBlock(set, idLerpColorBySpeed, "Lerp color by speed", idColorBySpeedStart, idColorBySpeedEnd, idClampNorm);
                AddLoc(set, idLerpColorBySpeed, baseX + LayoutColRandomLerp * LayoutStepX, baseY + 1940f);
                var updateColorBySpeed = new UpdateColorBlockJson { customType = "BABYLON.UpdateColorBlock", id = idUpdateColorBySpeed, name = "Update color by speed" };
                updateColorBySpeed.inputs.Add(Connection("particle", idUpdatePosition, "output"));
                updateColorBySpeed.inputs.Add(Connection("color", idLerpColorBySpeed, "output"));
                updateColorBySpeed.outputs.Add(Out("output"));
                set.blocks.Add(updateColorBySpeed);
                AddLoc(set, idUpdateColorBySpeed, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + 1940f);
            }

            if (sizeBySpeed.enabled && !sizeOverLifetime.enabled)
            {
                float sizeStart = sizeBySpeed.size.Evaluate(0f, 0f);
                float sizeEnd = sizeBySpeed.size.Evaluate(1f, 0f);
                AddInputBlock(set, idSizeBySpeedStart, "Size by speed (start)", sizeStart, baseX, baseY, LayoutColInputs, baseY + 2020f);
                AddInputBlock(set, idSizeBySpeedEnd, "Size by speed (end)", sizeEnd, baseX, baseY, LayoutColInputs, baseY + 2100f);
                AddLerpBlock(set, idLerpSizeBySpeed, "Lerp size by speed", idSizeBySpeedStart, idSizeBySpeedEnd, idClampNorm);
                AddLoc(set, idLerpSizeBySpeed, baseX + LayoutColRandomLerp * LayoutStepX, baseY + 2060f);
                int chainHeadForSizeBySpeed = colorOverLifetime.enabled ? idUpdateColor : (colorBySpeed.enabled ? idUpdateColorBySpeed : idUpdatePosition);
                var updateSizeBySpeed = new UpdateSizeBlockJson { customType = "BABYLON.UpdateSizeBlock", id = idUpdateSizeBySpeed, name = "Update size by speed" };
                updateSizeBySpeed.inputs.Add(Connection("particle", chainHeadForSizeBySpeed, "output"));
                updateSizeBySpeed.inputs.Add(Connection("size", idLerpSizeBySpeed, "output"));
                updateSizeBySpeed.outputs.Add(Out("output"));
                set.blocks.Add(updateSizeBySpeed);
                AddLoc(set, idUpdateSizeBySpeed, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + 2060f);
            }

            if (rotationBySpeed.enabled && !rotationOverLifetime.enabled)
            {
                float angleStart = rotationBySpeed.z.Evaluate(0f, 0f) * Mathf.Deg2Rad;
                float angleEnd = rotationBySpeed.z.Evaluate(1f, 0f) * Mathf.Deg2Rad;
                AddInputBlock(set, idAngleBySpeedStart, "Angle by speed (start)", angleStart, baseX, baseY, LayoutColInputs, baseY + 2140f);
                AddInputBlock(set, idAngleBySpeedEnd, "Angle by speed (end)", angleEnd, baseX, baseY, LayoutColInputs, baseY + 2220f);
                AddLerpBlock(set, idLerpAngleBySpeed, "Lerp angle by speed", idAngleBySpeedStart, idAngleBySpeedEnd, idClampNorm);
                AddLoc(set, idLerpAngleBySpeed, baseX + LayoutColRandomLerp * LayoutStepX, baseY + 2180f);
                int chainHeadForAngleBySpeed = sizeOverLifetime.enabled ? idUpdateSize : (sizeBySpeed.enabled ? idUpdateSizeBySpeed : (colorOverLifetime.enabled ? idUpdateColor : (colorBySpeed.enabled ? idUpdateColorBySpeed : idUpdatePosition)));
                var updateAngleBySpeed = new UpdateAngleBlockJson { customType = "BABYLON.UpdateAngleBlock", id = idUpdateAngleBySpeed, name = "Update angle by speed" };
                updateAngleBySpeed.inputs.Add(Connection("particle", chainHeadForAngleBySpeed, "output"));
                updateAngleBySpeed.inputs.Add(Connection("angle", idLerpAngleBySpeed, "output"));
                updateAngleBySpeed.outputs.Add(Out("output"));
                set.blocks.Add(updateAngleBySpeed);
                AddLoc(set, idUpdateAngleBySpeed, baseX + LayoutColUpdatePosition * LayoutStepX, baseY + 2180f);
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

            // Shape: Box, Sphere, Hemisphere, Cone, ConeVolume, Circle→Cylinder, else Point
            AddShapeBlock(set, shape, idShape, idCreateParticle, baseX, baseY);

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

        /// <summary>Get single float from Unity ShapeModule MinMaxCurve (e.g. radius).</summary>
        static float GetShapeCurveValue(ParticleSystem.MinMaxCurve curve)
        {
            GetMinMaxFromCurve(curve, out float min, out float max);
            return (min + max) * 0.5f;
        }

        /// <summary>Add the correct shape block for Unity shape.type: Box, Sphere, Hemisphere, Cone, ConeVolume, Circle→Cylinder, else Point.</summary>
        static void AddShapeBlock(NodeParticleSystemSetJson set, ParticleSystem.ShapeModule shape, int idShape, int idCreateParticle, float baseX, float baseY)
        {
            float shapeX = baseX + LayoutColShape * LayoutStepX;
            float shapeY = baseY + 720f;
            void FinishShape(BlockJson block)
            {
                block.inputs.Insert(0, Connection("particle", idCreateParticle, "particle"));
                block.outputs.Add(Out("output"));
                set.blocks.Add(block);
                AddLoc(set, idShape, shapeX, shapeY);
            }

            switch (shape.shapeType)
            {
                case ParticleSystemShapeType.Box:
                    var box = new BoxShapeBlockJson { customType = "BABYLON.BoxShapeBlock", id = idShape, name = "Box shape" };
                    Vector3 size = shape.scale;
                    box.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                    box.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                    box.inputs.Add(ValueVector3("minEmitBox", -size.x * 0.5f, -size.y * 0.5f, -size.z * 0.5f));
                    box.inputs.Add(ValueVector3("maxEmitBox", size.x * 0.5f, size.y * 0.5f, size.z * 0.5f));
                    FinishShape(box);
                    break;
                case ParticleSystemShapeType.Sphere:
                    var sphere = new SphereShapeBlockJson { customType = "BABYLON.SphereShapeBlock", id = idShape, name = "Sphere shape", isHemispheric = false };
                    float radiusS = GetShapeCurveValue(shape.radius);
                    float radiusRangeS = 1f - Mathf.Clamp01(shape.radiusThickness);
                    sphere.inputs.Add(ValueInput("radius", radiusS));
                    sphere.inputs.Add(ValueInput("radiusRange", radiusRangeS));
                    sphere.inputs.Add(ValueInput("directionRandomizer", 0f));
                    sphere.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                    sphere.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                    FinishShape(sphere);
                    break;
                case ParticleSystemShapeType.Hemisphere:
                    var hemi = new SphereShapeBlockJson { customType = "BABYLON.SphereShapeBlock", id = idShape, name = "Hemisphere shape", isHemispheric = true };
                    float radiusH = GetShapeCurveValue(shape.radius);
                    float radiusRangeH = 1f - Mathf.Clamp01(shape.radiusThickness);
                    hemi.inputs.Add(ValueInput("radius", radiusH));
                    hemi.inputs.Add(ValueInput("radiusRange", radiusRangeH));
                    hemi.inputs.Add(ValueInput("directionRandomizer", 0f));
                    hemi.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                    hemi.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                    FinishShape(hemi);
                    break;
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeVolume:
                    var cone = new ConeShapeBlockJson { customType = "BABYLON.ConeShapeBlock", id = idShape, name = "Cone shape", emitFromSpawnPointOnly = false };
                    float radiusC = GetShapeCurveValue(shape.radius);
                    float angleC = shape.angle * Mathf.Deg2Rad;
                    float lengthC = shape.length;
                    cone.inputs.Add(ValueInput("radius", radiusC));
                    cone.inputs.Add(ValueInput("angle", angleC));
                    cone.inputs.Add(ValueInput("radiusRange", 1f - Mathf.Clamp01(shape.radiusThickness)));
                    cone.inputs.Add(ValueInput("heightRange", lengthC > 0 ? lengthC : 1f));
                    cone.inputs.Add(ValueInput("directionRandomizer", 0f));
                    cone.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                    cone.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                    FinishShape(cone);
                    break;
                case ParticleSystemShapeType.Circle:
                    var cyl = new CylinderShapeBlockJson { customType = "BABYLON.CylinderShapeBlock", id = idShape, name = "Cylinder shape" };
                    float radiusCy = GetShapeCurveValue(shape.radius);
                    float heightCy = shape.length > 0 ? shape.length : 0.01f;
                    float radiusRangeCy = 1f - Mathf.Clamp01(shape.radiusThickness);
                    cyl.inputs.Add(ValueInput("radius", radiusCy));
                    cyl.inputs.Add(ValueInput("height", heightCy));
                    cyl.inputs.Add(ValueInput("radiusRange", radiusRangeCy));
                    cyl.inputs.Add(ValueInput("directionRandomizer", 0f));
                    cyl.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                    cyl.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                    FinishShape(cyl);
                    break;
                default:
                    var point = new PointShapeBlockJson { customType = "BABYLON.PointShapeBlock", id = idShape, name = "Point shape" };
                    point.inputs.Add(ValueVector3("direction1", 0, 1, 0));
                    point.inputs.Add(ValueVector3("direction2", 0, 1, 0));
                    FinishShape(point);
                    break;
            }
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
