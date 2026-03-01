// Writes NodeParticleSystemSetJson to a JSON string without external dependencies.
// Output matches what Babylon.js NodeParticleSystemSet.parseSerializedObject() expects.

using System.Globalization;
using System.Collections.Generic;
using System.Text;
using BabylonNodeParticle;

namespace ShurikenToBabylonNpe
{
    public static class NpeJsonWriter
    {
        public static string ToJson(NodeParticleSystemSetJson set, bool indented = true)
        {
            var sb = new StringBuilder();
            var indent = indented ? new Indent(0) : null;
            WriteSet(sb, set, indent);
            return sb.ToString();
        }

        static void WriteSet(StringBuilder sb, NodeParticleSystemSetJson set, Indent ind)
        {
            sb.Append("{");
            if (ind != null) ind++;
            CommaNewlineIndent(sb, ind);
            sb.Append("\"tags\":null");
            Prop(sb, "name", set.name, ind);
            Prop(sb, "editorData", set.editorData, ind, (s, o) => WriteEditorData(s, (EditorDataJson)o, ind));
            Prop(sb, "customType", set.customType, ind);
            Prop(sb, "blocks", set.blocks, ind, (s, list) => WriteBlocks(s, (List<BlockJson>)list, ind));
            if (!string.IsNullOrEmpty(set.comment))
                Prop(sb, "comment", set.comment, ind);
            if (ind != null) ind--;
            sb.Append(ind != null ? "\n}" : "}");
        }

        static void WriteEditorData(StringBuilder sb, EditorDataJson ed, Indent ind)
        {
            sb.Append("{");
            if (ind != null) ind++;
            Prop(sb, "locations", ed.locations, ind, (s, list) => WriteLocations(s, (List<LocationJson>)list, ind));
            Prop(sb, "frames", new List<object>(), ind, (s, list) => { s.Append("[]"); });
            Prop(sb, "x", 0f, ind);
            Prop(sb, "y", 0f, ind);
            Prop(sb, "zoom", 1f, ind);
            if (ind != null) ind--;
            sb.Append(ind != null ? "\n}" : "}");
        }

        static void WriteLocations(StringBuilder sb, List<LocationJson> list, Indent ind)
        {
            sb.Append("[");
            if (ind != null) ind++;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(",");
                if (ind != null) sb.Append("\n").Append(ind);
                var loc = list[i];
                sb.Append("{\"blockId\":").Append(loc.blockId).Append(",\"x\":").Append(loc.x.ToString("G")).Append(",\"y\":").Append(loc.y.ToString("G")).Append(",\"isCollapsed\":").Append(loc.isCollapsed ? "true" : "false").Append("}");
            }
            if (ind != null) ind--;
            if (list.Count > 0 && ind != null) sb.Append("\n").Append(ind);
            sb.Append("]");
        }

        static void WriteBlocks(StringBuilder sb, List<BlockJson> blocks, Indent ind)
        {
            sb.Append("[");
            if (ind != null) ind++;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i > 0) sb.Append(",");
                if (ind != null) sb.Append("\n").Append(ind);
                WriteBlock(sb, blocks[i], ind);
            }
            if (ind != null) ind--;
            if (blocks.Count > 0 && ind != null) sb.Append("\n").Append(ind);
            sb.Append("]");
        }

        static void WriteBlock(StringBuilder sb, BlockJson b, Indent ind)
        {
            sb.Append("{");
            if (ind != null) ind++;
            Prop(sb, "customType", b.customType, ind);
            Prop(sb, "id", b.id, ind);
            Prop(sb, "name", b.name, ind);
            Prop(sb, "visibleOnFrame", false, ind);
            Prop(sb, "inputs", b.inputs, ind, (s, list) => WriteInputs(s, (List<InputPortJson>)list, ind));
            Prop(sb, "outputs", b.outputs, ind, (s, list) => WriteOutputs(s, (List<OutputPortJson>)list, ind));

            if (b is SystemBlockJson sys)
            {
                Prop(sb, "capacity", sys.capacity, ind);
                Prop(sb, "manualEmitCount", sys.manualEmitCount, ind);
                Prop(sb, "blendMode", sys.blendMode, ind);
                Prop(sb, "updateSpeed", sys.updateSpeed, ind);
                Prop(sb, "preWarmCycles", sys.preWarmCycles, ind);
                Prop(sb, "preWarmStepOffset", sys.preWarmStepOffset, ind);
                Prop(sb, "isBillboardBased", sys.isBillboardBased, ind);
                Prop(sb, "billBoardMode", sys.billBoardMode, ind);
                Prop(sb, "isLocal", sys.isLocal, ind);
                Prop(sb, "disposeOnStop", sys.disposeOnStop, ind);
                Prop(sb, "doNoStart", sys.doNoStart, ind);
                Prop(sb, "renderingGroupId", sys.renderingGroupId, ind);
                Prop(sb, "startDelay", sys.startDelay, ind);
                if (sys.emitRate != null) Prop(sb, "emitRate", sys.emitRate, ind);
                if (sys.emitter != null) Prop(sb, "emitter", sys.emitter, ind);
                Prop(sb, "targetStopDuration", sys.targetStopDuration, ind);
                CommaNewlineIndent(sb, ind);
                sb.Append("\"customShader\":null");
            }
            else if (b is ParticleRandomBlockJson rnd)
            {
                Prop(sb, "lockMode", rnd.lockMode, ind);
            }
            else if (b is ParticleMathBlockJson math)
            {
                Prop(sb, "operation", math.operation, ind);
            }
            else if (b is ParticleInputBlockJson inp)
            {
                Prop(sb, "type", inp.type, ind);
                if (inp.contextualValue.HasValue) Prop(sb, "contextualValue", inp.contextualValue.Value, ind);
                if (inp.systemSource.HasValue) Prop(sb, "systemSource", inp.systemSource.Value, ind);
                Prop(sb, "min", inp.min, ind);
                Prop(sb, "max", inp.max, ind);
                Prop(sb, "groupInInspector", inp.groupInInspector ?? "", ind);
                Prop(sb, "displayInInspector", inp.displayInInspector, ind);
                if (inp.valueType != null) Prop(sb, "valueType", inp.valueType, ind);
                if (inp.value != null) { CommaNewlineIndent(sb, ind); sb.Append("\"value\":"); WriteValue(sb, inp.value, ind); }
            }
            else if (b is ParticleTextureSourceBlockJson tex)
            {
                if (tex.url != null) Prop(sb, "url", tex.url, ind);
                Prop(sb, "serializedCachedData", tex.serializedCachedData, ind);
                Prop(sb, "invertY", tex.invertY, ind);
                if (tex.textureDataUrl != null) Prop(sb, "textureDataUrl", tex.textureDataUrl, ind);
            }
            else if (b is SphereShapeBlockJson sphere)
            {
                Prop(sb, "isHemispheric", sphere.isHemispheric, ind);
            }
            else if (b is ConeShapeBlockJson cone)
            {
                Prop(sb, "emitFromSpawnPointOnly", cone.emitFromSpawnPointOnly, ind);
            }
            else if (b is ParticleClampBlockJson clamp)
            {
                Prop(sb, "minimum", clamp.minimum, ind);
                Prop(sb, "maximum", clamp.maximum, ind);
            }

            if (ind != null) ind--;
            sb.Append(ind != null ? "\n}" : "}");
        }

        static void WriteInputs(StringBuilder sb, List<InputPortJson> list, Indent ind)
        {
            sb.Append("[");
            if (ind != null) ind++;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(",");
                if (ind != null) sb.Append("\n").Append(ind);
                WriteInput(sb, list[i], ind);
            }
            if (ind != null) ind--;
            if (list.Count > 0 && ind != null) sb.Append("\n").Append(ind);
            sb.Append("]");
        }

        static void WriteInput(StringBuilder sb, InputPortJson p, Indent ind)
        {
            sb.Append("{");
            if (ind != null) ind++;
            string portName = p.name ?? p.inputName ?? "";
            if (portName != "") Prop(sb, "name", portName, ind);
            // NPE requires "inputName" on connection ports to draw connections
            if (p.targetBlockId.HasValue)
            {
                Prop(sb, "inputName", p.inputName ?? portName, ind);
                Prop(sb, "targetBlockId", p.targetBlockId.Value, ind);
                if (p.targetConnectionName != null) Prop(sb, "targetConnectionName", p.targetConnectionName, ind);
                Prop(sb, "isExposedOnFrame", true, ind);
                Prop(sb, "exposedPortPosition", -1, ind);
            }
            else
            {
                if (p.inputName != null && p.inputName != portName) Prop(sb, "inputName", p.inputName, ind);
            }
            if (p.valueType != null) Prop(sb, "valueType", p.valueType, ind);
            if (p.value != null) { CommaNewlineIndent(sb, ind); sb.Append("\"value\":"); WriteValue(sb, p.value, ind); }
            if (ind != null) ind--;
            sb.Append(ind != null ? "\n}" : "}");
        }

        static void WriteOutputs(StringBuilder sb, List<OutputPortJson> list, Indent ind)
        {
            sb.Append("[");
            if (ind != null) ind++;
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(",");
                if (ind != null) sb.Append("\n").Append(ind);
                sb.Append("{\"name\":").Append(Esc(list[i].name)).Append("}");
            }
            if (ind != null) ind--;
            if (list.Count > 0 && ind != null) sb.Append("\n").Append(ind);
            sb.Append("]");
        }

        static void CommaNewlineIndent(StringBuilder sb, Indent ind)
        {
            if (sb.Length > 0 && sb[sb.Length - 1] != '{' && sb[sb.Length - 1] != '[')
                sb.Append(",");
            if (ind != null) sb.Append("\n").Append(ind);
        }

        static void Prop(StringBuilder sb, string key, string val, Indent ind)
        {
            CommaNewlineIndent(sb, ind);
            sb.Append("\"").Append(key).Append("\":").Append(Esc(val));
        }

        static void Prop(StringBuilder sb, string key, int val, Indent ind)
        {
            CommaNewlineIndent(sb, ind);
            sb.Append("\"").Append(key).Append("\":").Append(val);
        }

        static void Prop(StringBuilder sb, string key, float val, Indent ind)
        {
            CommaNewlineIndent(sb, ind);
            sb.Append("\"").Append(key).Append("\":").Append(val.ToString("G", CultureInfo.InvariantCulture));
        }

        static void Prop(StringBuilder sb, string key, bool val, Indent ind)
        {
            CommaNewlineIndent(sb, ind);
            sb.Append("\"").Append(key).Append("\":").Append(val ? "true" : "false");
        }

        static void Prop(StringBuilder sb, string key, object val, Indent ind)
        {
            if (val == null) return;
            CommaNewlineIndent(sb, ind);
            sb.Append("\"").Append(key).Append("\":");
            WriteValue(sb, val, ind);
        }

        static void WriteValue(StringBuilder sb, object val, Indent ind)
        {
            if (val == null) return;
            if (val is int i) sb.Append(i);
            else if (val is float f) sb.Append(f.ToString("G", CultureInfo.InvariantCulture));
            else if (val is bool b) sb.Append(b ? "true" : "false");
            else if (val is float[] arr)
            {
                sb.Append("[");
                for (int j = 0; j < arr.Length; j++) { if (j > 0) sb.Append(","); sb.Append(arr[j].ToString("G", CultureInfo.InvariantCulture)); }
                sb.Append("]");
            }
            else sb.Append(Esc(val.ToString()));
        }

        static void Prop<T>(StringBuilder sb, string key, T obj, Indent ind, System.Action<StringBuilder, object> write)
        {
            if (obj == null) return;
            CommaNewlineIndent(sb, ind);
            sb.Append("\"").Append(key).Append("\":");
            write(sb, obj);
        }

        static string Esc(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }

        class Indent
        {
            int n;
            string s;
            public Indent(int n) { this.n = n; s = new string(' ', n * 2); }
            public static Indent operator ++(Indent i) { i.n++; i.s = new string(' ', i.n * 2); return i; }
            public static Indent operator --(Indent i) { i.n--; i.s = new string(' ', i.n * 2); return i; }
            public override string ToString() => s;
        }
    }
}
