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

        // Layout: grid in (col, row). All block positions go through NpeLayout so new blocks get stable positions.
        private const float LayoutStepX = 260f;
        private const float LayoutStepY = 80f;
        private const float FrameHeight = 240f; // 3 rows per frame (FrameHeight/LayoutStepY)
        private const int LayoutColInputs = 0;
        private const int LayoutColRandomLerp = 1;
        private const int LayoutColCreateParticle = 2;
        private const int LayoutColShape = 3;
        private const int LayoutColPositionDir = 4;
        private const int LayoutColAdd = 5;
        private const int LayoutColUpdatePosition = 6;
        private const int LayoutColTexture = 7;
        private const int LayoutColSystem = 8;
        private const float SystemLayoutWidth = 2400f;
        // Row grid (row index * LayoutStepY = Y offset). Frames 0..8 = CreateParticle inputs; then Update blocks; then By Speed.
        private const float RowFrame0 = 0f;
        private const float RowFrame1 = 3f;
        private const float RowFrame2 = 6f;
        private const float RowFrame3 = 9f;   // CreateParticle, UpdatePosition, Shape, System
        private const float RowFrame4 = 12f;
        private const float RowFrame5 = 15f;
        private const float RowFrame6 = 18f;  // Color over Lifetime
        private const float RowFrame7 = 21f;   // Size over Lifetime
        private const float RowFrame8 = 24f;   // Rotation over Lifetime
        private const float RowUpdateColor = 11.25f;   // 900
        private const float RowUpdateSize = 13.5f;      // 1080
        private const float RowUpdateAngle = 15.75f;   // 1260
        private const float RowPosition = 19f;         // 1520
        private const float RowAdd = 19.5f;            // 1560
        private const float RowScaledDir = 20f;       // 1600
        private const float RowSpeedNorm = 21.25f;    // 1700
        private const float RowSpeedNorm2 = 22.25f;    // 1780
        private const float RowClampNorm = 23.25f;     // 1860
        private const float RowColorBySpeed = 23.75f;  // 1900
        private const float RowLerpColorBySpeed = 24.25f;  // 1940
        private const float RowColorBySpeedEnd = 24.75f;  // 1980
        private const float RowSizeBySpeed = 25.25f;  // 2020
        private const float RowLerpSizeBySpeed = 25.75f;
        private const float RowAngleBySpeed = 26.75f;  // 2140
        private const float RowLerpAngleBySpeed = 27.25f;
        private const float RowTexture = 10f;         // 800

        /// <summary>NPE canvas grid: all positions via X(col), Y(row). Row in LayoutStepY units.</summary>
        struct NpeLayout
        {
            public float BaseX;
            public float BaseY;
            public float X(int col) => BaseX + col * LayoutStepX;
            public float Y(float row) => BaseY + row * LayoutStepY;
            public float FrameRow(int frame, int subRow = 0) => frame * (FrameHeight / LayoutStepY) + subRow;
            public void Loc(NodeParticleSystemSetJson set, int blockId, int col, float row) => AddLoc(set, blockId, X(col), Y(row));
        }

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

        /// <summary>Get NPE blend mode from Unity material. Prefer _SrcBlend/_DstBlend (actual blend state), then _BlendMode/_Blend, then _ColorMode.</summary>
        static int GetBlendModeFromUnity(Material mat)
        {
            if (mat == null) return BlendCombine;
            // 0) _SrcBlend / _DstBlend — actual blend state (Unity enum: SrcAlpha=5, One=1, OneMinusSrcAlpha=10, Zero=0, DstColor=2/4). Takes precedence over _ColorMode.
            if (mat.HasProperty("_SrcBlend") && mat.HasProperty("_DstBlend"))
            {
                int src = (int)mat.GetFloat("_SrcBlend");
                int dst = (int)mat.GetFloat("_DstBlend");
                if (dst == 1) return BlendAdditive;   // One => Additive (e.g. SrcAlpha, One)
                if (dst == 9) return BlendCombine;   // OneMinusSrcAlpha => Alpha blend
                if (src == 0 && (dst == 2 || dst == 4)) return BlendMultiply;
            }
            // 1) URP Particles: _BlendMode (0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply)
            if (mat.HasProperty("_BlendMode"))
            {
                int b = (int)mat.GetFloat("_BlendMode");
                if (b == 2) return BlendAdditive;
                if (b == 3) return BlendMultiply;
                return BlendCombine;
            }
            if (mat.HasProperty("_Blend"))
            {
                int b = (int)mat.GetFloat("_Blend");
                if (b == 2) return BlendAdditive;
                if (b == 3) return BlendMultiply;
                return BlendCombine;
            }
            // 2) Particle _ColorMode (shader-dependent: 0 can be Additive or Multiply depending on shader)
            if (mat.HasProperty("_ColorMode"))
            {
                int c = (int)mat.GetFloat("_ColorMode");
                if (c == 1) return BlendAdditive;
                if (c == 0) return BlendMultiply;
            }
            return BlendCombine;
        }

        /// <summary>Convert Unity Gradient to sorted list of (time 0..1, color) for NPE ParticleGradientBlock. Merges color and alpha key times, evaluates at each.</summary>
        static void GetGradientStops(Gradient g, List<float> times, List<Color> colors)
        {
            times.Clear();
            colors.Clear();
            if (g == null) return;
            var colorKeys = g.colorKeys;
            var alphaKeys = g.alphaKeys;
            var timeSet = new HashSet<float> { 0f, 1f };
            for (int i = 0; i < colorKeys.Length; i++)
                timeSet.Add(Mathf.Clamp01(colorKeys[i].time));
            for (int i = 0; i < alphaKeys.Length; i++)
                timeSet.Add(Mathf.Clamp01(alphaKeys[i].time));
            var sortedTimes = new List<float>(timeSet);
            sortedTimes.Sort();
            for (int i = 0; i < sortedTimes.Count; i++)
            {
                float t = sortedTimes[i];
                times.Add(t);
                colors.Add(g.Evaluate(t));
            }
        }

        /// <summary>Add ParticleGradientBlock + ParticleGradientValueBlocks for a Unity gradient; gradientSelectorBlockId (e.g. Age) drives 0..1. Returns gradient block id. When reservedGradientBlockId is set, uses that id for the gradient block. Row in grid units (LayoutStepY).</summary>
        static int AddColorGradientBlockGroup(NodeParticleSystemSetJson set, Gradient unityGradient, int idGradientSelector, ref int nextId, NpeLayout layout, int colX, float rowBase, string blockNamePrefix, int? reservedGradientBlockId = null)
        {
            var times = new List<float>();
            var colors = new List<Color>();
            GetGradientStops(unityGradient, times, colors);
            if (times.Count == 0) return 0;
            int idGradientBlock = reservedGradientBlockId ?? NextId(ref nextId);
            var valueBlockIds = new List<int>();
            for (int i = 0; i < times.Count; i++)
            {
                int idColorIn = NextId(ref nextId);
                int idVal = NextId(ref nextId);
                valueBlockIds.Add(idVal);
                AddInputBlockColor4(set, idColorIn, blockNamePrefix + " color " + i, colors[i].r, colors[i].g, colors[i].b, colors[i].a, layout, colX, rowBase + i);
                var vb = new ParticleGradientValueBlockJson
                {
                    customType = "BABYLON.ParticleGradientValueBlock",
                    id = idVal,
                    name = blockNamePrefix + " value " + i,
                    reference = times[i]  // gradient key 0..1 for this color stop
                };
                vb.inputs.Add(Connection("value", idColorIn, "output"));
                vb.outputs.Add(Out("output"));
                set.blocks.Add(vb);
                layout.Loc(set, idVal, colX + 1, rowBase + i);
            }
            var gb = new ParticleGradientBlockJson
            {
                customType = "BABYLON.ParticleGradientBlock",
                id = idGradientBlock,
                name = blockNamePrefix + " gradient",
                _entryCount = times.Count
            };
            gb.inputs.Add(Connection("gradient", idGradientSelector, "output"));
            for (int i = 0; i < valueBlockIds.Count; i++)
                gb.inputs.Add(Connection("value" + i, valueBlockIds[i], "output"));
            gb.outputs.Add(Out("output"));
            set.blocks.Add(gb);
            layout.Loc(set, idGradientBlock, colX + 1, rowBase + times.Count);
            return idGradientBlock;
        }

        /// <summary>Get gradient end color for cDead (Evaluate(1)).</summary>
        static Color GetGradientEndColor(Gradient g)
        {
            return g != null ? g.Evaluate(1f) : new Color(0, 0, 0, 0);
        }

        /// <summary>Get start/end from MinMaxGradient (fallback when gradient is null; e.g. Color by Speed).</summary>
        static void GetMinMaxGradientEndpoints(ParticleSystem.MinMaxGradient mm, out Color start, out Color end)
        {
            start = mm.Evaluate(0f, 0f);
            end = mm.Evaluate(1f, 0f);
            if (start.a <= 0f && end.a <= 0f && mm.gradient != null)
            {
                start = mm.gradient.Evaluate(0f);
                end = mm.gradient.Evaluate(1f);
            }
        }

        /// <summary>Log Color over Lifetime diagnostics (mode, gradient keys, alpha, start/end/cDead) for debugging export.</summary>
        static void LogColorOverLifetimeDiagnostics(ParticleSystem.ColorOverLifetimeModule colorOverLifetime, Color colStart, Color colEnd, Color cDead)
        {
            if (!colorOverLifetime.enabled) return;
            var mm = colorOverLifetime.color;
            Debug.Log("[ColorOverLifetime] --- DIAG START ---");
            Debug.Log("[ColorOverLifetime] mode=" + mm.mode + " (Gradient or TwoGradients for this module)");
            Debug.Log("[ColorOverLifetime] gradient=" + (mm.gradient != null ? "set" : "null") + " gradientMax=" + (mm.gradientMax != null ? "set" : "null"));
            void LogGradient(string label, Gradient g)
            {
                if (g == null) { Debug.Log("[ColorOverLifetime] " + label + "=null"); return; }
                var cKeys = g.colorKeys;
                var aKeys = g.alphaKeys;
                Debug.Log("[ColorOverLifetime] " + label + " colorKeys=" + cKeys.Length + " alphaKeys=" + aKeys.Length);
                for (int i = 0; i < cKeys.Length; i++)
                    Debug.Log(string.Format("[ColorOverLifetime]   colorKey[{0}] time={1} r={2} g={3} b={4} a={5}", i, cKeys[i].time, cKeys[i].color.r, cKeys[i].color.g, cKeys[i].color.b, cKeys[i].color.a));
                for (int i = 0; i < aKeys.Length; i++)
                    Debug.Log(string.Format("[ColorOverLifetime]   alphaKey[{0}] time={1} alpha={2}", i, aKeys[i].time, aKeys[i].alpha));
                Debug.Log(string.Format("[ColorOverLifetime]   Evaluate(0)=({0},{1},{2},{3}) Evaluate(1)=({4},{5},{6},{7})",
                    g.Evaluate(0f).r, g.Evaluate(0f).g, g.Evaluate(0f).b, g.Evaluate(0f).a,
                    g.Evaluate(1f).r, g.Evaluate(1f).g, g.Evaluate(1f).b, g.Evaluate(1f).a));
            }
            LogGradient("gradient", mm.gradient);
            LogGradient("gradientMax", mm.gradientMax);
            Debug.Log(string.Format("[ColorOverLifetime] mm.Evaluate(0,0)=({0},{1},{2},{3}) mm.Evaluate(1,0)=({4},{5},{6},{7})",
                mm.Evaluate(0f, 0f).r, mm.Evaluate(0f, 0f).g, mm.Evaluate(0f, 0f).b, mm.Evaluate(0f, 0f).a,
                mm.Evaluate(1f, 0f).r, mm.Evaluate(1f, 0f).g, mm.Evaluate(1f, 0f).b, mm.Evaluate(1f, 0f).a));
            if (mm.mode == ParticleSystemGradientMode.TwoGradients && mm.gradientMax != null)
                Debug.Log(string.Format("[ColorOverLifetime] mm.Evaluate(0,1)=({0},{1},{2},{3}) mm.Evaluate(1,1)=({4},{5},{6},{7})",
                    mm.Evaluate(0f, 1f).r, mm.Evaluate(0f, 1f).g, mm.Evaluate(0f, 1f).b, mm.Evaluate(0f, 1f).a,
                    mm.Evaluate(1f, 1f).r, mm.Evaluate(1f, 1f).g, mm.Evaluate(1f, 1f).b, mm.Evaluate(1f, 1f).a));
            Debug.Log(string.Format("[ColorOverLifetime] EXPORT colStart=({0},{1},{2},{3}) colEnd=({4},{5},{6},{7})",
                colStart.r, colStart.g, colStart.b, colStart.a, colEnd.r, colEnd.g, colEnd.b, colEnd.a));
            Debug.Log(string.Format("[ColorOverLifetime] EXPORT cDead=({0},{1},{2},{3}) (gradient end)",
                cDead.r, cDead.g, cDead.b, cDead.a));
            Debug.Log("[ColorOverLifetime] --- DIAG END ---");
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

        /// <summary>Export shape for a MinMaxCurve: Constant (one value), TwoConstants (min/max), Curve/TwoCurves (sampled min/max).</summary>
        struct MinMaxCurveExport
        {
            public ParticleSystemCurveMode Mode;
            public float MinValue;
            public float MaxValue;
            public bool IsConstant => Mode == ParticleSystemCurveMode.Constant;
        }

        /// <summary>Fills export from Unity MinMaxCurve. Constant = one value; TwoConstants = constantMin/constantMax; Curve/TwoCurves = sampled min/max.</summary>
        static void GetMinMaxCurveExport(ParticleSystem.MinMaxCurve curve, out MinMaxCurveExport export)
        {
            export = new MinMaxCurveExport { Mode = curve.mode };
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    export.MinValue = export.MaxValue = curve.constant;
                    return;
                case ParticleSystemCurveMode.TwoConstants:
                    export.MinValue = curve.constantMin;
                    export.MaxValue = curve.constantMax;
                    return;
                case ParticleSystemCurveMode.Curve:
                    SampleCurveMinMax(curve, 0f, out export.MinValue, out export.MaxValue);
                    return;
                case ParticleSystemCurveMode.TwoCurves:
                    SampleTwoCurvesMinMax(curve, out export.MinValue, out export.MaxValue);
                    return;
                default:
                    export.MinValue = curve.Evaluate(0f, 0f);
                    export.MaxValue = curve.Evaluate(1f, 0f);
                    break;
            }
        }

        /// <summary>Sample single curve at several times and set min/max.</summary>
        static void SampleCurveMinMax(ParticleSystem.MinMaxCurve curve, float curveIndex, out float minVal, out float maxVal)
        {
            minVal = maxVal = curve.Evaluate(0f, curveIndex);
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 8f;
                float v = curve.Evaluate(t, curveIndex);
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        }

        /// <summary>Sample both curves over time; min/max are the overall range (for NPE we export as min+max+random).</summary>
        static void SampleTwoCurvesMinMax(ParticleSystem.MinMaxCurve curve, out float minVal, out float maxVal)
        {
            minVal = float.MaxValue;
            maxVal = float.MinValue;
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                float v0 = curve.Evaluate(t, 0f);
                float v1 = curve.Evaluate(t, 1f);
                if (v0 < minVal) minVal = v0;
                if (v1 < minVal) minVal = v1;
                if (v0 > maxVal) maxVal = v0;
                if (v1 > maxVal) maxVal = v1;
            }
        }

        /// <summary>Get min/max from Unity MinMaxCurve (convenience wrapper around GetMinMaxCurveExport).</summary>
        static void GetMinMaxFromCurve(ParticleSystem.MinMaxCurve curve, out float minVal, out float maxVal)
        {
            GetMinMaxCurveExport(curve, out MinMaxCurveExport e);
            minVal = e.MinValue;
            maxVal = e.MaxValue;
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
            int idStartColorRandMin = 0, idStartColorRandMax = 0, idRandomStartColorPos = 0, idStartColorGradient = 0, idStartColorGradient2 = 0, idRandomStartColorWhich = 0, idLerpStartColorGradientsTwo = 0;
            bool startColorIsGradient = main.startColor.mode == ParticleSystemGradientMode.Gradient;
            bool startColorIsTwoGradients = main.startColor.mode == ParticleSystemGradientMode.TwoGradients;
            if (startColorIsGradient || startColorIsTwoGradients)
            {
                idStartColorRandMin = NextId(ref nextId);
                idStartColorRandMax = NextId(ref nextId);
                idRandomStartColorPos = NextId(ref nextId);
                idStartColorGradient = NextId(ref nextId);
                if (startColorIsTwoGradients)
                {
                    idStartColorGradient2 = NextId(ref nextId);
                    idRandomStartColorWhich = NextId(ref nextId);
                    idLerpStartColorGradientsTwo = NextId(ref nextId);
                }
            }
            int idTexture = NextId(ref nextId);
            int idUpdateColor = 0, idAge = 0, idColorOverLifetimeSource = 0, idRandomColorGradTwo = 0, idLerpColorGradientsTwo = 0;
            int idUpdateSize = 0, idSizeOverLifetimeStart = 0, idSizeOverLifetimeEnd = 0, idLerpSizeOverLifetime = 0, idAgeForSize = 0;
            if (colorOverLifetime.enabled)
            {
                idUpdateColor = NextId(ref nextId);
                idAge = NextId(ref nextId);
                if (colorOverLifetime.color.mode == ParticleSystemGradientMode.TwoGradients)
                {
                    idRandomColorGradTwo = NextId(ref nextId);
                    idLerpColorGradientsTwo = NextId(ref nextId);
                }
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
            int idColorBySpeedStart = 0, idColorBySpeedEnd = 0, idLerpColorBySpeed = 0, idUpdateColorBySpeed = 0, idColorBySpeedSource = 0, idRandomColorBySpeedTwo = 0, idLerpColorBySpeedTwo = 0;
            if (colorBySpeed.enabled && !colorOverLifetime.enabled)
            {
                idColorBySpeedStart = NextId(ref nextId);
                idColorBySpeedEnd = NextId(ref nextId);
                idLerpColorBySpeed = NextId(ref nextId);
                idUpdateColorBySpeed = NextId(ref nextId);
                if (colorBySpeed.color.mode == ParticleSystemGradientMode.TwoGradients)
                {
                    idRandomColorBySpeedTwo = NextId(ref nextId);
                    idLerpColorBySpeedTwo = NextId(ref nextId);
                }
            }
            int idSizeBySpeedStart = 0, idSizeBySpeedEnd = 0, idLerpSizeBySpeed = 0, idUpdateSizeBySpeed = 0;
            if (sizeBySpeed.enabled && !sizeOverLifetime.enabled) { idSizeBySpeedStart = NextId(ref nextId); idSizeBySpeedEnd = NextId(ref nextId); idLerpSizeBySpeed = NextId(ref nextId); idUpdateSizeBySpeed = NextId(ref nextId); }
            int idAngleBySpeedStart = 0, idAngleBySpeedEnd = 0, idLerpAngleBySpeed = 0, idUpdateAngleBySpeed = 0;
            if (rotationBySpeed.enabled && !rotationOverLifetime.enabled) { idAngleBySpeedStart = NextId(ref nextId); idAngleBySpeedEnd = NextId(ref nextId); idLerpAngleBySpeed = NextId(ref nextId); idUpdateAngleBySpeed = NextId(ref nextId); }

            var layout = new NpeLayout { BaseX = baseX, BaseY = baseY };

            GetMinMaxCurveExport(main.startLifetime, out MinMaxCurveExport exportLifetime);
            GetMinMaxCurveExport(main.startSpeed, out MinMaxCurveExport exportEmitPower);
            GetMinMaxCurveExport(main.startSize, out MinMaxCurveExport exportSize);
            GetMinMaxCurveExport(main.startRotation, out MinMaxCurveExport exportAngle);
            // Start color with correct alpha: in Color mode Unity often stores alpha only in the gradient; colorMin/Evaluate can return .a=0. Prefer gradient.Evaluate when available.
            Color c1, c2;
            var startGrad = main.startColor.mode == ParticleSystemGradientMode.Gradient || main.startColor.mode == ParticleSystemGradientMode.TwoGradients ? main.startColor.gradient : null;
            var startGradMax = main.startColor.mode == ParticleSystemGradientMode.TwoGradients ? main.startColor.gradientMax : null;
            if (main.startColor.mode == ParticleSystemGradientMode.Color && main.startColor.gradient != null)
            {
                Color single = main.startColor.gradient.Evaluate(0.5f);
                c1 = c2 = single;
            }
            else if (startGrad != null)
            {
                c1 = startGrad.Evaluate(0f);
                c2 = startGradMax != null ? startGradMax.Evaluate(1f) : startGrad.Evaluate(1f);
            }
            else
            {
                c1 = main.startColor.Evaluate(0f, 0f);
                bool twoColorsOrGradients = main.startColor.mode == ParticleSystemGradientMode.TwoColors || main.startColor.mode == ParticleSystemGradientMode.TwoGradients;
                c2 = twoColorsOrGradients ? main.startColor.Evaluate(0f, 1f) : main.startColor.Evaluate(1f, 0f);
            }
            // Fallback: if alpha still 0 (Unity quirk in some versions), take from colorMin/colorMax
            if (c1.a <= 0f && c2.a <= 0f)
            {
                float a = main.startColor.colorMin.a > 0f ? main.startColor.colorMin.a : main.startColor.colorMax.a;
                if (a > 0f) { c1 = new Color(c1.r, c1.g, c1.b, a); c2 = new Color(c2.r, c2.g, c2.b, a); }
            }
            // Death color: from Color over Lifetime gradient end (Gradient or TwoGradients).
            Color cDead;
            if (!colorOverLifetime.enabled)
                cDead = new Color(0, 0, 0, 0);
            else
            {
                var mm = colorOverLifetime.color;
                if (mm.mode == ParticleSystemGradientMode.TwoGradients && mm.gradientMax != null)
                    cDead = GetGradientEndColor(mm.gradientMax);
                else if (mm.gradient != null)
                    cDead = GetGradientEndColor(mm.gradient);
                else
                    cDead = mm.Evaluate(1f, 0f);
            }

            // SystemBlock: name = particle system name; billBoardMode from renderer (Unity RenderMode = Billboard/Stretch/Horizontal/Vertical/Mesh/None)
            int renderMode = (int)(renderer != null ? renderer.renderMode : ParticleSystemRenderMode.Billboard);
            bool isBillboard = renderer == null || (renderMode != (int)ParticleSystemRenderMode.Mesh && renderMode != (int)ParticleSystemRenderMode.None);
            int blendMode = GetBlendModeFromUnity(mat);
            var systemBlock = new SystemBlockJson
            {
                customType = "BABYLON.SystemBlock",
                id = idSystem,
                name = ps.name,
                capacity = main.maxParticles,
                manualEmitCount = GetManualEmitCountFromUnity(emission),
                blendMode = blendMode,
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
            layout.Loc(set, idSystem, LayoutColSystem, RowFrame3);
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
            layout.Loc(set, idUpdatePosition, LayoutColUpdatePosition, RowFrame3);
            set.blocks.Add(updatePos);

            // Color over Lifetime: UpdateColorBlock (particle from UpdatePosition, color from ParticleGradientBlock(age) or TwoGradients: Lerp(grad1, grad2, random))
            if (colorOverLifetime.enabled)
            {
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
                layout.Loc(set, idAge, LayoutColInputs, RowFrame6);

                var mmCol = colorOverLifetime.color;
                if (mmCol.mode == ParticleSystemGradientMode.TwoGradients && mmCol.gradient != null && mmCol.gradientMax != null)
                {
                    int idGrad1 = AddColorGradientBlockGroup(set, mmCol.gradient, idAge, ref nextId, layout, LayoutColInputs, layout.FrameRow(6, 1), "Color over lifetime 1");
                    int idGrad2 = AddColorGradientBlockGroup(set, mmCol.gradientMax, idAge, ref nextId, layout, LayoutColInputs + 2, layout.FrameRow(6, 1), "Color over lifetime 2");
                    AddRandomBlock(set, idRandomColorGradTwo, "Random color gradient", 0, 1, LockOncePerParticle);
                    layout.Loc(set, idRandomColorGradTwo, LayoutColInputs, layout.FrameRow(6, 3));
                    AddLerpBlock(set, idLerpColorGradientsTwo, "Lerp color gradients", idGrad1, idGrad2, idRandomColorGradTwo);
                    layout.Loc(set, idLerpColorGradientsTwo, LayoutColRandomLerp, layout.FrameRow(6, 1));
                    idColorOverLifetimeSource = idLerpColorGradientsTwo;
                }
                else if (mmCol.gradient != null)
                {
                    idColorOverLifetimeSource = AddColorGradientBlockGroup(set, mmCol.gradient, idAge, ref nextId, layout, LayoutColInputs, layout.FrameRow(6, 1), "Color over lifetime");
                }
                else
                {
                    Color fallbackStart = mmCol.Evaluate(0f, 0f), fallbackEnd = mmCol.Evaluate(1f, 0f);
                    int idStart = NextId(ref nextId), idEnd = NextId(ref nextId), idLerp = NextId(ref nextId);
                    AddInputBlockColor4(set, idStart, "Color over lifetime (start)", fallbackStart.r, fallbackStart.g, fallbackStart.b, fallbackStart.a, layout, LayoutColInputs, layout.FrameRow(6, 1));
                    AddInputBlockColor4(set, idEnd, "Color over lifetime (end)", fallbackEnd.r, fallbackEnd.g, fallbackEnd.b, fallbackEnd.a, layout, LayoutColInputs, layout.FrameRow(6, 2));
                    AddLerpBlock(set, idLerp, "Lerp color over lifetime", idStart, idEnd, idAge);
                    layout.Loc(set, idLerp, LayoutColRandomLerp, layout.FrameRow(6, 1));
                    idColorOverLifetimeSource = idLerp;
                }

                Color diagStart = mmCol.gradient != null ? mmCol.gradient.Evaluate(0f) : mmCol.Evaluate(0f, 0f);
                Color diagEnd = (mmCol.mode == ParticleSystemGradientMode.TwoGradients && mmCol.gradientMax != null) ? mmCol.gradientMax.Evaluate(1f) : (mmCol.gradient != null ? mmCol.gradient.Evaluate(1f) : mmCol.Evaluate(1f, 0f));
                LogColorOverLifetimeDiagnostics(colorOverLifetime, diagStart, diagEnd, cDead);

                var updateColor = new UpdateColorBlockJson
                {
                    customType = "BABYLON.UpdateColorBlock",
                    id = idUpdateColor,
                    name = "Update color"
                };
                updateColor.inputs.Add(Connection("particle", idUpdatePosition, "output"));
                updateColor.inputs.Add(Connection("color", idColorOverLifetimeSource, "output"));
                updateColor.outputs.Add(Out("output"));
                set.blocks.Add(updateColor);
                layout.Loc(set, idUpdateColor, LayoutColUpdatePosition, RowUpdateColor);
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
                    layout.Loc(set, idAgeForSize, LayoutColInputs, RowFrame7);
                }
                AddInputBlock(set, idSizeOverLifetimeStart, "Size over lifetime (start)", sizeStart, layout, LayoutColInputs, colorOverLifetime.enabled ? RowFrame7 : layout.FrameRow(7, 1));
                AddInputBlock(set, idSizeOverLifetimeEnd, "Size over lifetime (end)", sizeEnd, layout, LayoutColInputs, colorOverLifetime.enabled ? layout.FrameRow(7, 1) : layout.FrameRow(7, 2));
                AddLerpBlock(set, idLerpSizeOverLifetime, "Lerp size over lifetime", idSizeOverLifetimeStart, idSizeOverLifetimeEnd, ageBlockId);
                layout.Loc(set, idLerpSizeOverLifetime, LayoutColRandomLerp, layout.FrameRow(7, 1));

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
                layout.Loc(set, idUpdateSize, LayoutColUpdatePosition, colorOverLifetime.enabled ? RowUpdateSize : RowUpdateColor);
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
                    layout.Loc(set, idAgeRot, LayoutColInputs, RowFrame8);
                }
                AddInputBlock(set, idAngleRotStart, "Rotation over lifetime (start)", angle0, layout, LayoutColInputs, layout.FrameRow(8, 1));
                AddInputBlock(set, idAngleRotEnd, "Rotation over lifetime (end)", angle1, layout, LayoutColInputs, layout.FrameRow(8, 2));
                AddLerpBlock(set, idLerpAngleRot, "Lerp angle over lifetime", idAngleRotStart, idAngleRotEnd, ageRotId);
                layout.Loc(set, idLerpAngleRot, LayoutColRandomLerp, layout.FrameRow(8, 1));
                int chainHeadForAngle = sizeOverLifetime.enabled ? idUpdateSize : (colorOverLifetime.enabled ? idUpdateColor : idUpdatePosition);
                var updateAngle = new UpdateAngleBlockJson { customType = "BABYLON.UpdateAngleBlock", id = idUpdateAngle, name = "Update angle" };
                updateAngle.inputs.Add(Connection("particle", chainHeadForAngle, "output"));
                updateAngle.inputs.Add(Connection("angle", idLerpAngleRot, "output"));
                updateAngle.outputs.Add(Out("output"));
                set.blocks.Add(updateAngle);
                layout.Loc(set, idUpdateAngle, LayoutColUpdatePosition, sizeOverLifetime.enabled ? RowUpdateAngle : (colorOverLifetime.enabled ? RowUpdateSize : RowUpdateColor));
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
                layout.Loc(set, idDirScale, LayoutColPositionDir, RowSpeedNorm);

                AddInputBlock(set, idSpeedMinConst, "Speed range min", speedMin, layout, LayoutColInputs, RowSpeedNorm);
                AddInputBlock(set, idRangeSize, "Speed range size", rangeSize, layout, LayoutColInputs, RowSpeedNorm2);

                var subBlock = new ParticleMathBlockJson { customType = "BABYLON.ParticleMathBlock", id = idSubSpeedMin, name = "Speed minus min", operation = MathSubtract };
                subBlock.inputs.Add(Connection("left", idDirScale, "output"));
                subBlock.inputs.Add(Connection("right", idSpeedMinConst, "output"));
                subBlock.outputs.Add(Out("output"));
                set.blocks.Add(subBlock);
                layout.Loc(set, idSubSpeedMin, LayoutColRandomLerp, RowSpeedNorm);

                var divBlock = new ParticleMathBlockJson { customType = "BABYLON.ParticleMathBlock", id = idDivNorm, name = "Normalized speed", operation = MathDivide };
                divBlock.inputs.Add(Connection("left", idSubSpeedMin, "output"));
                divBlock.inputs.Add(Connection("right", idRangeSize, "output"));
                divBlock.outputs.Add(Out("output"));
                set.blocks.Add(divBlock);
                layout.Loc(set, idDivNorm, LayoutColRandomLerp, RowSpeedNorm2);

                var clampBlock = new ParticleClampBlockJson { customType = "BABYLON.ParticleClampBlock", id = idClampNorm, name = "Clamp speed 0-1", minimum = 0f, maximum = 1f };
                clampBlock.inputs.Add(Connection("value", idDivNorm, "output"));
                clampBlock.outputs.Add(Out("output"));
                set.blocks.Add(clampBlock);
                layout.Loc(set, idClampNorm, LayoutColRandomLerp, RowClampNorm);
            }

            if (colorBySpeed.enabled && !colorOverLifetime.enabled)
            {
                var mmSpeed = colorBySpeed.color;
                if (mmSpeed.mode == ParticleSystemGradientMode.TwoGradients && mmSpeed.gradient != null && mmSpeed.gradientMax != null)
                {
                    int idGrad1 = AddColorGradientBlockGroup(set, mmSpeed.gradient, idClampNorm, ref nextId, layout, LayoutColInputs, RowColorBySpeed, "Color by speed 1");
                    int idGrad2 = AddColorGradientBlockGroup(set, mmSpeed.gradientMax, idClampNorm, ref nextId, layout, LayoutColInputs + 2, RowColorBySpeed, "Color by speed 2");
                    AddRandomBlock(set, idRandomColorBySpeedTwo, "Random color by speed", 0, 1, LockOncePerParticle);
                    layout.Loc(set, idRandomColorBySpeedTwo, LayoutColInputs, RowLerpSizeBySpeed);
                    AddLerpBlock(set, idLerpColorBySpeedTwo, "Lerp color by speed gradients", idGrad1, idGrad2, idRandomColorBySpeedTwo);
                    layout.Loc(set, idLerpColorBySpeedTwo, LayoutColRandomLerp, RowLerpColorBySpeed);
                    idColorBySpeedSource = idLerpColorBySpeedTwo;
                }
                else if (mmSpeed.gradient != null)
                {
                    idColorBySpeedSource = AddColorGradientBlockGroup(set, mmSpeed.gradient, idClampNorm, ref nextId, layout, LayoutColInputs, RowColorBySpeed, "Color by speed");
                }
                else
                {
                    GetMinMaxGradientEndpoints(colorBySpeed.color, out Color colorBySpeedStart, out Color colorBySpeedEnd);
                    AddInputBlockColor4(set, idColorBySpeedStart, "Color by speed (start)", colorBySpeedStart.r, colorBySpeedStart.g, colorBySpeedStart.b, colorBySpeedStart.a, layout, LayoutColInputs, RowColorBySpeed);
                    AddInputBlockColor4(set, idColorBySpeedEnd, "Color by speed (end)", colorBySpeedEnd.r, colorBySpeedEnd.g, colorBySpeedEnd.b, colorBySpeedEnd.a, layout, LayoutColInputs, RowColorBySpeedEnd);
                    AddLerpBlock(set, idLerpColorBySpeed, "Lerp color by speed", idColorBySpeedStart, idColorBySpeedEnd, idClampNorm);
                    layout.Loc(set, idLerpColorBySpeed, LayoutColRandomLerp, RowLerpColorBySpeed);
                    idColorBySpeedSource = idLerpColorBySpeed;
                }
                var updateColorBySpeed = new UpdateColorBlockJson { customType = "BABYLON.UpdateColorBlock", id = idUpdateColorBySpeed, name = "Update color by speed" };
                updateColorBySpeed.inputs.Add(Connection("particle", idUpdatePosition, "output"));
                updateColorBySpeed.inputs.Add(Connection("color", idColorBySpeedSource, "output"));
                updateColorBySpeed.outputs.Add(Out("output"));
                set.blocks.Add(updateColorBySpeed);
                layout.Loc(set, idUpdateColorBySpeed, LayoutColUpdatePosition, RowLerpColorBySpeed);
            }

            if (sizeBySpeed.enabled && !sizeOverLifetime.enabled)
            {
                float sizeStart = sizeBySpeed.size.Evaluate(0f, 0f);
                float sizeEnd = sizeBySpeed.size.Evaluate(1f, 0f);
                AddInputBlock(set, idSizeBySpeedStart, "Size by speed (start)", sizeStart, layout, LayoutColInputs, RowSizeBySpeed);
                AddInputBlock(set, idSizeBySpeedEnd, "Size by speed (end)", sizeEnd, layout, LayoutColInputs, RowSizeBySpeed + 1f);
                AddLerpBlock(set, idLerpSizeBySpeed, "Lerp size by speed", idSizeBySpeedStart, idSizeBySpeedEnd, idClampNorm);
                layout.Loc(set, idLerpSizeBySpeed, LayoutColRandomLerp, RowLerpSizeBySpeed);
                int chainHeadForSizeBySpeed = colorOverLifetime.enabled ? idUpdateColor : (colorBySpeed.enabled ? idUpdateColorBySpeed : idUpdatePosition);
                var updateSizeBySpeed = new UpdateSizeBlockJson { customType = "BABYLON.UpdateSizeBlock", id = idUpdateSizeBySpeed, name = "Update size by speed" };
                updateSizeBySpeed.inputs.Add(Connection("particle", chainHeadForSizeBySpeed, "output"));
                updateSizeBySpeed.inputs.Add(Connection("size", idLerpSizeBySpeed, "output"));
                updateSizeBySpeed.outputs.Add(Out("output"));
                set.blocks.Add(updateSizeBySpeed);
                layout.Loc(set, idUpdateSizeBySpeed, LayoutColUpdatePosition, RowLerpSizeBySpeed);
            }

            if (rotationBySpeed.enabled && !rotationOverLifetime.enabled)
            {
                float angleStart = rotationBySpeed.z.Evaluate(0f, 0f) * Mathf.Deg2Rad;
                float angleEnd = rotationBySpeed.z.Evaluate(1f, 0f) * Mathf.Deg2Rad;
                AddInputBlock(set, idAngleBySpeedStart, "Angle by speed (start)", angleStart, layout, LayoutColInputs, RowAngleBySpeed);
                AddInputBlock(set, idAngleBySpeedEnd, "Angle by speed (end)", angleEnd, layout, LayoutColInputs, RowAngleBySpeed + 1f);
                AddLerpBlock(set, idLerpAngleBySpeed, "Lerp angle by speed", idAngleBySpeedStart, idAngleBySpeedEnd, idClampNorm);
                layout.Loc(set, idLerpAngleBySpeed, LayoutColRandomLerp, RowLerpAngleBySpeed);
                int chainHeadForAngleBySpeed = sizeOverLifetime.enabled ? idUpdateSize : (sizeBySpeed.enabled ? idUpdateSizeBySpeed : (colorOverLifetime.enabled ? idUpdateColor : (colorBySpeed.enabled ? idUpdateColorBySpeed : idUpdatePosition)));
                var updateAngleBySpeed = new UpdateAngleBlockJson { customType = "BABYLON.UpdateAngleBlock", id = idUpdateAngleBySpeed, name = "Update angle by speed" };
                updateAngleBySpeed.inputs.Add(Connection("particle", chainHeadForAngleBySpeed, "output"));
                updateAngleBySpeed.inputs.Add(Connection("angle", idLerpAngleBySpeed, "output"));
                updateAngleBySpeed.outputs.Add(Out("output"));
                set.blocks.Add(updateAngleBySpeed);
                layout.Loc(set, idUpdateAngleBySpeed, LayoutColUpdatePosition, RowLerpAngleBySpeed);
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
            layout.Loc(set, idAdd, LayoutColAdd, RowAdd);
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
            layout.Loc(set, idPosition, LayoutColPositionDir, RowPosition);
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
            layout.Loc(set, idScaledDir, LayoutColPositionDir, RowScaledDir);
            set.blocks.Add(dirInput);

            // CreateParticle
            var createBlock = new CreateParticleBlockJson
            {
                customType = "BABYLON.CreateParticleBlock",
                id = idCreateParticle,
                name = "Create particle"
            };
            bool lifetimeConst = exportLifetime.IsConstant;
            bool emitPowerConst = exportEmitPower.IsConstant;
            bool sizeConst = exportSize.IsConstant;
            bool scaleConst = true;
            bool angleConst = exportAngle.IsConstant;
            bool colorConst = main.startColor.mode == ParticleSystemGradientMode.Color;
            bool colorTwoColors = main.startColor.mode == ParticleSystemGradientMode.TwoColors;
            bool startColorGradientFallback = (startColorIsGradient && main.startColor.gradient == null) || (startColorIsTwoGradients && (main.startColor.gradient == null || main.startColor.gradientMax == null));
            int idColorForCreate = colorConst ? idColor1 : (colorTwoColors || startColorGradientFallback ? idLerpColor : (startColorIsGradient ? idStartColorGradient : idLerpStartColorGradientsTwo));
            int idLifetimeForCreate = lifetimeConst ? idLifetimeMin : idRandomLifetime;
            int idEmitPowerForCreate = emitPowerConst ? idEmitPowerMin : idRandomEmitPower;
            int idSizeForCreate = sizeConst ? idSizeMin : idRandomSize;
            int idScaleForCreate = scaleConst ? idScaleMin : idRandomScale;
            int idAngleForCreate = angleConst ? idAngleMin : idRandomAngle;
            createBlock.inputs.Add(Connection("lifeTime", idLifetimeForCreate, "output"));
            createBlock.inputs.Add(Connection("emitPower", idEmitPowerForCreate, "output"));
            createBlock.inputs.Add(Connection("size", idSizeForCreate, "output"));
            createBlock.inputs.Add(Connection("scale", idScaleForCreate, "output"));
            createBlock.inputs.Add(Connection("angle", idAngleForCreate, "output"));
            createBlock.inputs.Add(Connection("color", idColorForCreate, "output"));
            createBlock.inputs.Add(Connection("colorDead", idColorDead, "output"));
            createBlock.outputs.Add(Out("particle"));
            layout.Loc(set, idCreateParticle, LayoutColCreateParticle, RowFrame3);
            set.blocks.Add(createBlock);

            // Shape: Box, Sphere, Hemisphere, Cone, ConeVolume, Circle→Cylinder, else Point
            AddShapeBlock(set, shape, idShape, idCreateParticle, layout);

            // Frame 0: Lifetime — Constant: one block; TwoConstants/Curve/TwoCurves: Min, Max, Random
            AddInputBlock(set, idLifetimeMin, lifetimeConst ? "Lifetime" : "Min Lifetime", exportLifetime.MinValue, layout, LayoutColInputs, layout.FrameRow(0, 0));
            if (!lifetimeConst)
            {
                AddInputBlock(set, idLifetimeMax, "Max Lifetime", exportLifetime.MaxValue, layout, LayoutColInputs, layout.FrameRow(0, 1));
                AddRandomBlock(set, idRandomLifetime, "Random Lifetime", idLifetimeMin, idLifetimeMax, LockPerParticle);
                layout.Loc(set, idRandomLifetime, LayoutColRandomLerp, layout.FrameRow(0, 1));
            }

            // Frame 1: Emit Power
            AddInputBlock(set, idEmitPowerMin, emitPowerConst ? "Emit Power" : "Min Emit Power", exportEmitPower.MinValue, layout, LayoutColInputs, layout.FrameRow(1, 0));
            if (!emitPowerConst)
            {
                AddInputBlock(set, idEmitPowerMax, "Max Emit Power", exportEmitPower.MaxValue, layout, LayoutColInputs, layout.FrameRow(1, 1));
                AddRandomBlock(set, idRandomEmitPower, "Random Emit Power", idEmitPowerMin, idEmitPowerMax, LockPerParticle);
                layout.Loc(set, idRandomEmitPower, LayoutColRandomLerp, layout.FrameRow(1, 1));
            }

            // Frame 2: Size
            AddInputBlock(set, idSizeMin, sizeConst ? "Size" : "Min size", exportSize.MinValue, layout, LayoutColInputs, layout.FrameRow(2, 0));
            if (!sizeConst)
            {
                AddInputBlock(set, idSizeMax, "Max size", exportSize.MaxValue, layout, LayoutColInputs, layout.FrameRow(2, 1));
                AddRandomBlock(set, idRandomSize, "Random size", idSizeMin, idSizeMax, LockPerParticle);
                layout.Loc(set, idRandomSize, LayoutColRandomLerp, layout.FrameRow(2, 1));
            }

            // Frame 3: Scale (we export as constant 1,1)
            AddInputBlockVector2(set, idScaleMin, "Scale", 1, 1, layout, LayoutColInputs, layout.FrameRow(3, 0));
            if (!scaleConst)
            {
                AddInputBlockVector2(set, idScaleMax, "Max Scale", 1, 1, layout, LayoutColInputs, layout.FrameRow(3, 1));
                AddRandomBlock(set, idRandomScale, "Random Scale", idScaleMin, idScaleMax, LockPerParticle);
                layout.Loc(set, idRandomScale, LayoutColRandomLerp, layout.FrameRow(3, 1));
            }

            // Frame 4: Angle
            AddInputBlock(set, idAngleMin, angleConst ? "Rotation" : "Min Rotation", exportAngle.MinValue, layout, LayoutColInputs, layout.FrameRow(4, 0));
            if (!angleConst)
            {
                AddInputBlock(set, idAngleMax, "Max Rotation", exportAngle.MaxValue, layout, LayoutColInputs, layout.FrameRow(4, 1));
                AddRandomBlock(set, idRandomAngle, "Random Rotation", idAngleMin, idAngleMax, LockPerParticle);
                layout.Loc(set, idRandomAngle, LayoutColRandomLerp, layout.FrameRow(4, 1));
            }

            // Frame 5: Color — Color: one block; TwoColors: Color1, Color2, Random, Lerp; Gradient: Random(0,1) + gradient block; TwoGradients: two gradient blocks + Lerp; fallback: Lerp(c1,c2). ColorDead always one block.
            if (colorConst || colorTwoColors || startColorGradientFallback)
                AddInputBlockColor4(set, idColor1, colorConst ? "Start Color" : "Color 1", c1.r, c1.g, c1.b, c1.a, layout, LayoutColInputs, layout.FrameRow(5, 0));
            if (colorTwoColors || startColorGradientFallback)
            {
                AddInputBlockColor4(set, idColor2, "Color 2", c2.r, c2.g, c2.b, c2.a, layout, LayoutColInputs, layout.FrameRow(5, 1));
                AddRandomBlock(set, idColorStep, "Random color step", 0, 1, LockOncePerParticle);
                layout.Loc(set, idColorStep, LayoutColInputs, layout.FrameRow(5, 2));
                AddLerpBlock(set, idLerpColor, "Lerp color", idColor1, idColor2, idColorStep);
                layout.Loc(set, idLerpColor, LayoutColRandomLerp, layout.FrameRow(5, 1));
            }
            else if (startColorIsGradient && main.startColor.gradient != null)
            {
                AddInputBlock(set, idStartColorRandMin, "Start color grad min", 0f, layout, LayoutColInputs, layout.FrameRow(5, 1));
                AddInputBlock(set, idStartColorRandMax, "Start color grad max", 1f, layout, LayoutColInputs, layout.FrameRow(5, 2));
                AddRandomBlock(set, idRandomStartColorPos, "Random start color pos", idStartColorRandMin, idStartColorRandMax, LockOncePerParticle);
                layout.Loc(set, idRandomStartColorPos, LayoutColInputs, layout.FrameRow(5, 3));
                AddColorGradientBlockGroup(set, main.startColor.gradient, idRandomStartColorPos, ref nextId, layout, LayoutColInputs, layout.FrameRow(5, 1), "Start color", idStartColorGradient);
            }
            else if (startColorIsTwoGradients && main.startColor.gradient != null && main.startColor.gradientMax != null)
            {
                AddInputBlock(set, idStartColorRandMin, "Start color grad min", 0f, layout, LayoutColInputs, layout.FrameRow(5, 1));
                AddInputBlock(set, idStartColorRandMax, "Start color grad max", 1f, layout, LayoutColInputs, layout.FrameRow(5, 2));
                AddRandomBlock(set, idRandomStartColorPos, "Random start color pos", idStartColorRandMin, idStartColorRandMax, LockOncePerParticle);
                layout.Loc(set, idRandomStartColorPos, LayoutColInputs, layout.FrameRow(5, 3));
                AddColorGradientBlockGroup(set, main.startColor.gradient, idRandomStartColorPos, ref nextId, layout, LayoutColInputs, layout.FrameRow(5, 1), "Start color 1", idStartColorGradient);
                AddColorGradientBlockGroup(set, main.startColor.gradientMax, idRandomStartColorPos, ref nextId, layout, LayoutColInputs + 2, layout.FrameRow(5, 1), "Start color 2", idStartColorGradient2);
                AddRandomBlock(set, idRandomStartColorWhich, "Random which start gradient", idStartColorRandMin, idStartColorRandMax, LockOncePerParticle);
                layout.Loc(set, idRandomStartColorWhich, LayoutColInputs, layout.FrameRow(5, 4));
                AddLerpBlock(set, idLerpStartColorGradientsTwo, "Lerp start color gradients", idStartColorGradient, idStartColorGradient2, idRandomStartColorWhich);
                layout.Loc(set, idLerpStartColorGradientsTwo, LayoutColRandomLerp, layout.FrameRow(5, 2));
            }
            int colorFrameRows = colorConst ? 1 : (colorTwoColors || startColorGradientFallback ? 3 : (startColorIsGradient ? 4 : (startColorIsTwoGradients ? 5 : 3)));
            AddInputBlockColor4(set, idColorDead, "Dead Color", cDead.r, cDead.g, cDead.b, cDead.a, layout, LayoutColInputs, layout.FrameRow(5, 0) + colorFrameRows);

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
            layout.Loc(set, idTexture, LayoutColTexture, RowTexture);
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
        static void AddShapeBlock(NodeParticleSystemSetJson set, ParticleSystem.ShapeModule shape, int idShape, int idCreateParticle, NpeLayout layout)
        {
            void FinishShape(BlockJson block)
            {
                block.inputs.Insert(0, Connection("particle", idCreateParticle, "particle"));
                block.outputs.Add(Out("output"));
                set.blocks.Add(block);
                layout.Loc(set, idShape, LayoutColShape, RowFrame3);
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
                    // Do not set direction1/direction2: NPE then uses radial direction (center→particle), so negative emitPower = inward
                    FinishShape(sphere);
                    break;
                case ParticleSystemShapeType.Hemisphere:
                    var hemi = new SphereShapeBlockJson { customType = "BABYLON.SphereShapeBlock", id = idShape, name = "Hemisphere shape", isHemispheric = true };
                    float radiusH = GetShapeCurveValue(shape.radius);
                    float radiusRangeH = 1f - Mathf.Clamp01(shape.radiusThickness);
                    hemi.inputs.Add(ValueInput("radius", radiusH));
                    hemi.inputs.Add(ValueInput("radiusRange", radiusRangeH));
                    hemi.inputs.Add(ValueInput("directionRandomizer", 0f));
                    // Do not set direction1/direction2: NPE uses radial direction
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

        static void AddInputBlock(NodeParticleSystemSetJson set, int id, string name, float value, NpeLayout layout, int col, float row)
        {
            AddInputBlock(set, id, name, value, layout.BaseX, layout.BaseY, col, row * LayoutStepY);
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

        static void AddInputBlockVector2(NodeParticleSystemSetJson set, int id, string name, float x, float y, NpeLayout layout, int col, float row)
        {
            AddInputBlockVector2(set, id, name, x, y, layout.BaseX, layout.BaseY, col, row * LayoutStepY);
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

        static void AddInputBlockColor4(NodeParticleSystemSetJson set, int id, string name, float r, float g, float b, float a, NpeLayout layout, int col, float row)
        {
            AddInputBlockColor4(set, id, name, r, g, b, a, layout.BaseX, layout.BaseY, col, row * LayoutStepY);
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
