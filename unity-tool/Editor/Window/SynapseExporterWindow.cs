#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    public class SynapseExporterWindow : EditorWindow
    {
        [SerializeField] private string m_savePath = "Assets/SynapseData";

        // Dùng DefaultAsset để user có thể kéo thả Thư mục trực tiếp trên Inspector
        [SerializeField] private List<DefaultAsset> m_targetFolders = new List<DefaultAsset>();

        private SerializedObject _so;
        private Vector2 _scrollPos;

        [MenuItem("Tools/SynapseGraph/Export Architecture")]
        public static void ShowWindow()
        {
            var window = GetWindow<SynapseExporterWindow>("Synapse Exporter");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            _so = new SerializedObject(this);
        }

        private void OnGUI()
        {
            _so.Update();

            // Header xịn xò
            EditorGUILayout.Space(10);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("🧠 SYNAPSE GRAPH EXPORTER", headerStyle);
            GUILayout.Label("Neural Architecture Analyzer", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            EditorGUILayout.Space(10);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Khung Cài đặt Đầu ra
            EditorGUILayout.LabelField("1. Output Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(_so.FindProperty("m_savePath"), new GUIContent("Save Folder"));
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            // Khung Kéo thả Thư mục
            EditorGUILayout.LabelField("2. Target Folders to Scan", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Kéo thả các thư mục chứa Script của dự án vào đây. Tool sẽ quét toàn bộ file C# bên trong.", MessageType.Info);
            EditorGUILayout.BeginVertical("box");
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_so.FindProperty("m_targetFolders"), new GUIContent("Folders"), true);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();

            // Nút Export chà bá
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.6f); // Xanh lá ngọc
            if (GUILayout.Button("🚀 SCAN & EXPORT JSON", GUILayout.Height(50)))
            {
                ExportData();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(5);

            _so.ApplyModifiedProperties();
        }

        private void ExportData()
        {
            // Chuyển DefaultAsset thành đường dẫn String để gửi cho Analyzer
            List<string> folderPaths = new List<string>();
            foreach (var folder in m_targetFolders)
            {
                if (folder != null)
                {
                    string path = AssetDatabase.GetAssetPath(folder);
                    if (AssetDatabase.IsValidFolder(path)) folderPaths.Add(path);
                }
            }

            if (folderPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Lỗi", "Mày phải chọn ít nhất 1 thư mục để quét chứ!", "OK");
                return;
            }

            SynapseAnalyzer engine = new SynapseAnalyzer(folderPaths);
            ProjectData projectData = engine.RunAnalysis();

            if (!Directory.Exists(m_savePath))
            {
                Directory.CreateDirectory(m_savePath);
            }

            string jsonFormat = JsonUtility.ToJson(projectData, true);
            string fileName = "SynapseData_Final.json";
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), m_savePath, fileName);

            File.WriteAllText(fullPath, jsonFormat);
            AssetDatabase.Refresh();

            EditorUtility.RevealInFinder(fullPath);
            Debug.Log($"<color=#4ec9b0><b>[SynapseGraph]</b></color> Đã xuất thành công {projectData.Classes.Count} nodes tại: {m_savePath}/{fileName}");
        }
    }
}
#endif