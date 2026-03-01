// Dumps all Unity ParticleSystem (and ParticleSystemRenderer) properties to a readable text file
// via SerializedObject so nothing is missed. Written alongside NPE JSON export.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace ShurikenToBabylonNpeEditor
{
    /// <summary>Dumps Shuriken ParticleSystem and renderer properties to a human-readable format.</summary>
    public static class UnityParticleSystemDumper
    {
        const int MaxDepth = 25;

        /// <summary>Dump all properties of the given ParticleSystems (and their renderers) to a text string.</summary>
        public static string Dump(System.Collections.Generic.List<ParticleSystem> systems, string rootName)
        {
            if (systems == null || systems.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine("# Unity Particle System properties dump");
            sb.AppendLine("# Root: " + (rootName ?? ""));
            sb.AppendLine("# Format: path = value (one property per line, indent = nesting)");
            sb.AppendLine();

            for (int i = 0; i < systems.Count; i++)
            {
                var ps = systems[i];
                if (ps == null) continue;
                string sysName = ps.gameObject != null ? ps.gameObject.name : ("System" + i);
                sb.AppendLine("## ParticleSystem[" + i + "] " + sysName);
                sb.AppendLine();

                var so = new SerializedObject(ps);
                SerializedProperty it = so.GetIterator();
                if (it.NextVisible(true))
                {
                    do
                    {
                        DumpProperty(sb, it, "", 0);
                    }
                    while (it.NextVisible(false));
                }

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("### ParticleSystemRenderer: " + renderer.gameObject.name);
                    var rso = new SerializedObject(renderer);
                    SerializedProperty rit = rso.GetIterator();
                    if (rit.NextVisible(true))
                    {
                        do
                        {
                            DumpProperty(sb, rit, "renderer.", 0);
                        }
                        while (rit.NextVisible(false));
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        static void DumpProperty(StringBuilder sb, SerializedProperty prop, string prefix, int depth)
        {
            if (depth >= MaxDepth) return;

            string path = prefix + prop.name;
            bool isArray = prop.isArray && prop.propertyType != SerializedPropertyType.String;
            bool expandChildren = prop.hasVisibleChildren && !isArray;

            if (expandChildren)
            {
                SerializedProperty copy = prop.Copy();
                SerializedProperty end = prop.GetEndProperty();
                if (copy.NextVisible(true))
                {
                    do
                    {
                        if (SerializedProperty.EqualContents(copy, end)) break;
                        DumpProperty(sb, copy, path + ".", depth + 1);
                    }
                    while (copy.NextVisible(false));
                }
                return;
            }

            if (isArray)
            {
                int count = prop.arraySize;
                sb.AppendLine(path + ".arraySize = " + count);
                for (int i = 0; i < count && i < 64; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    DumpProperty(sb, elem, path + "[" + i + "].", depth + 1);
                }
                if (count >= 64)
                    sb.AppendLine(path + " ... (" + count + " elements, first 64 shown)");
                return;
            }

            string valueStr = GetPropertyValueString(prop);
            sb.AppendLine(path + " = " + valueStr);
        }

        static string GetPropertyValueString(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return prop.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return prop.floatValue.ToString("G6");
                case SerializedPropertyType.String:
                    return Escape(prop.stringValue);
                case SerializedPropertyType.Enum:
                    return prop.enumNames[prop.enumValueIndex];
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue == null) return "null";
                    return Escape(prop.objectReferenceValue.name) + " (" + prop.objectReferenceValue.GetType().Name + ")";
                case SerializedPropertyType.Color:
                    return prop.colorValue.ToString();
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return prop.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return prop.rectValue.ToString();
                case SerializedPropertyType.Bounds:
                    return prop.boundsValue.ToString();
                case SerializedPropertyType.LayerMask:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Character:
                    return prop.intValue.ToString();
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Gradient:
                    return "(" + prop.propertyType + ")";
                default:
                    return "(" + prop.propertyType + ")";
            }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (s.IndexOfAny(new[] { '\n', '\r', '=' }) >= 0)
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
            return s;
        }
    }
}
