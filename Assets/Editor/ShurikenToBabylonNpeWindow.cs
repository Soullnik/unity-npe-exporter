// Editor window: select GameObjects in hierarchy; each selection exports as one JSON (root + child ParticleSystems = multiple SystemBlocks).

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using BabylonNodeParticle;
using ShurikenToBabylonNpe;

namespace ShurikenToBabylonNpeEditor
{
    public class ShurikenToBabylonNpeWindow : EditorWindow
    {
        const string DefaultTextureUrl = "https://assets.babylonjs.com/core/textures/flare.png";

        struct ExportGroup
        {
            public GameObject root;
            public List<ParticleSystem> systems;
        }

        List<ExportGroup> _groups = new List<ExportGroup>();
        Vector2 _scroll;
        string _exportFolder = "ExportedParticles";
        string _defaultTextureUrl = DefaultTextureUrl;
        string _status = "";
        bool _includeDisabled = false;

        [MenuItem("Tools/Babylon NPE/Export Shuriken to Node Particle Editor JSON")]
        static void Open()
        {
            var w = GetWindow<ShurikenToBabylonNpeWindow>(false, "Shuriken → Babylon NPE", true);
            w.minSize = new Vector2(360, 320);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Shuriken → Babylon.js Node Particle Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select root GameObjects in the hierarchy. Each selection exports as one JSON (root + all child ParticleSystems as multiple SystemBlocks).", MessageType.Info);
            EditorGUILayout.Space(4);

            _includeDisabled = EditorGUILayout.Toggle("Include disabled ParticleSystems", _includeDisabled);
            if (GUILayout.Button("Refresh from selection"))
            {
                RefreshFromSelection();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Export groups", EditorStyles.miniLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(140));
            foreach (var g in _groups)
            {
                if (g.root == null || g.systems == null || g.systems.Count == 0) continue;
                EditorGUILayout.LabelField(g.root.name);
                for (int i = 0; i < g.systems.Count; i++)
                {
                    var ps = g.systems[i];
                    if (ps == null) continue;
                    if (ps.gameObject == g.root) continue; // root already shown
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField(ps.gameObject.name);
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            _exportFolder = EditorGUILayout.TextField("Export folder", _exportFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string start = string.IsNullOrEmpty(_exportFolder) ? Application.dataPath : _exportFolder;
                if (!System.IO.Path.IsPathRooted(start))
                    start = System.IO.Path.Combine(Application.dataPath, start);
                string chosen = EditorUtility.OpenFolderPanel("Choose export folder", start, "");
                if (!string.IsNullOrEmpty(chosen))
                    _exportFolder = chosen;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Relative path (e.g. ExportedParticles) exports under Assets. Use Browse to pick a folder outside the project.", MessageType.None);
            _defaultTextureUrl = EditorGUILayout.TextField("Default texture URL", _defaultTextureUrl);
            EditorGUILayout.Space(4);

            GUI.enabled = _groups.Count > 0;
            if (GUILayout.Button("Export selected to JSON", GUILayout.Height(28)))
            {
                Export();
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }
        }

        void RefreshFromSelection()
        {
            _groups.Clear();
            var selected = new HashSet<GameObject>(Selection.gameObjects ?? new GameObject[0]);
            if (selected.Count == 0)
            {
                _status = "No selection.";
                return;
            }

            // Only add a group for roots: selected GOs that have no ancestor in selection
            foreach (var go in selected)
            {
                if (go == null) continue;
                var isChildOfSelection = false;
                var p = go.transform.parent;
                while (p != null)
                {
                    if (selected.Contains(p.gameObject)) { isChildOfSelection = true; break; }
                    p = p.parent;
                }
                if (isChildOfSelection) continue;

                var systems = new List<ParticleSystem>(go.GetComponentsInChildren<ParticleSystem>(_includeDisabled));
                if (systems.Count == 0) continue;

                _groups.Add(new ExportGroup { root = go, systems = systems });
            }

            _status = _groups.Count > 0 ? $"Found {_groups.Count} group(s)." : "No ParticleSystem in selection.";
        }

        void Export()
        {
            string folder = string.IsNullOrEmpty(_exportFolder)
                ? Application.dataPath
                : (Path.IsPathRooted(_exportFolder) ? _exportFolder.Trim() : Path.Combine(Application.dataPath, _exportFolder.Trim()));
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            int ok = 0;
            foreach (var g in _groups)
            {
                if (g.root == null || g.systems == null || g.systems.Count == 0) continue;

                var set = ShurikenToNpeConverter.ConvertMultiple(g.systems, g.root.name, _defaultTextureUrl);
                if (set == null) continue;

                string json = SerializeToJson(set);
                if (string.IsNullOrEmpty(json)) continue;

                string safeName = string.IsNullOrEmpty(g.root.name) ? "ParticleSystem" : SanitizeFileName(g.root.name);
                string path = Path.Combine(folder, safeName + ".json");
                File.WriteAllText(path, json);
                ok++;
            }

            _status = ok > 0 ? $"Exported {ok} file(s) to {folder}" : "Export failed (no systems or serialization error).";
            if (ok > 0)
                AssetDatabase.Refresh();
        }

        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static string SerializeToJson(NodeParticleSystemSetJson set)
        {
            return NpeJsonWriter.ToJson(set, true);
        }
    }
}
