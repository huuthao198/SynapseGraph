#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Editor Window chính: scan project, export JSON + Markdown (Full / Summary / Chunked).
    /// 
    /// [v2 - MODE SELECTION]
    /// - Thêm dropdown chọn mode export.
    /// - Thêm panel Estimate (scan trước, xem số liệu, rồi quyết định mode).
    /// - Tách 2 nút: Scan & Estimate | Export.
    /// - Chunked mode chia file theo namespace.
    /// </summary>
    public class SynapseExporterWindow : EditorWindow
    {
        private enum ExportMode
        {
            Full,
            Summary,
            Chunked
        }

        private const string DEFAULT_SAVE_PATH = "Assets/SynapseData";
        private const string JSON_FILE_NAME = "SynapseData_Final.json";
        private const string FULL_MD_NAME = "_FULL.md";
        private const string SUMMARY_MD_NAME = "_SUMMARY.md";
        private const string CHUNK_PREFIX = "_CHUNK_";
        private const int DEFAULT_CHUNK_LINES = 8000;

        [SerializeField] private string m_savePath = DEFAULT_SAVE_PATH;
        [SerializeField] private List<DefaultAsset> m_targetFolders = new List<DefaultAsset>();
        [SerializeField] private ExportMode m_exportMode = ExportMode.Full;
        [SerializeField] private int m_maxLinesPerChunk = DEFAULT_CHUNK_LINES;

        // Transient state — không serialize, chỉ dùng trong session
        private ProjectData m_cachedProjectData;
        private ComplexityStats m_cachedStats;

        private SerializedObject m_so;
        private Vector2 m_scrollPos;

        [MenuItem("Tools/SynapseGraph/Export Architecture")]
        public static void ShowWindow()
        {
            var window = GetWindow<SynapseExporterWindow>("Synapse Exporter");
            window.minSize = new Vector2(420, 480);
        }

        private void OnEnable()
        {
            m_so = new SerializedObject(this);
        }

        private void OnGUI()
        {
            m_so.Update();

            DrawHeader();
            m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos);

            DrawOutputSettings();
            DrawTargetFolders();
            DrawExportMode();
            DrawEstimatePanel();

            EditorGUILayout.EndScrollView();

            DrawActionButtons();

            m_so.ApplyModifiedProperties();
        }

        #region UI SECTIONS

        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("🧠 SYNAPSE GRAPH EXPORTER", headerStyle);
            GUILayout.Label("Neural Architecture Analyzer", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            EditorGUILayout.Space(10);
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("1. Output Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(m_so.FindProperty("m_savePath"), new GUIContent("Save Folder"));
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private void DrawTargetFolders()
        {
            EditorGUILayout.LabelField("2. Target Folders to Scan", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Kéo thả các thư mục chứa Script của dự án vào đây. Tool sẽ quét toàn bộ file C# bên trong.",
                MessageType.Info);

            EditorGUILayout.BeginVertical("box");
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(m_so.FindProperty("m_targetFolders"), new GUIContent("Folders"), true);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private void DrawExportMode()
        {
            EditorGUILayout.LabelField("3. Export Mode", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            m_exportMode = (ExportMode)EditorGUILayout.EnumPopup("Mode", m_exportMode);

            switch (m_exportMode)
            {
                case ExportMode.Full:
                    EditorGUILayout.HelpBox(
                        "Xuất 1 file Markdown đầy đủ (overview + folder tree + index + chi tiết từng class).\n" +
                        "Phù hợp dự án < 8000 dòng markdown.",
                        MessageType.None);
                    break;

                case ExportMode.Summary:
                    EditorGUILayout.HelpBox(
                        "Xuất 1 file Markdown ngắn gọn (overview + index + signal flow + dependency).\n" +
                        "Không có chi tiết từng class. Phù hợp khi cần cái nhìn tổng quan nhanh.",
                        MessageType.None);
                    break;

                case ExportMode.Chunked:
                    EditorGUILayout.HelpBox(
                        "Xuất 1 file Summary + nhiều chunk chia theo namespace.\n" +
                        "Mỗi chunk không vượt quá số dòng cấu hình bên dưới.\n" +
                        "Phù hợp dự án lớn (200+ class).",
                        MessageType.None);

                    m_maxLinesPerChunk = EditorGUILayout.IntSlider(
                        new GUIContent("Max Lines / Chunk", "Chunk sẽ cố gắng không vượt quá giá trị này."),
                        m_maxLinesPerChunk, 1000, 20000);
                    break;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private void DrawEstimatePanel()
        {
            EditorGUILayout.LabelField("4. Estimate (từ lần scan gần nhất)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            if (m_cachedStats == null)
            {
                EditorGUILayout.HelpBox(
                    "Chưa có dữ liệu. Bấm 'Scan & Estimate' để phân tích project trước khi export.",
                    MessageType.Warning);
            }
            else
            {
                DrawEstimateRow("Total Classes", m_cachedStats.TotalClasses.ToString());
                DrawEstimateRow("Runtime Classes", m_cachedStats.RuntimeClasses.ToString());
                DrawEstimateRow("Editor Classes", m_cachedStats.EditorClasses.ToString());
                DrawEstimateRow("Total Methods", m_cachedStats.TotalMethods.ToString());
                DrawEstimateRow("Total Dependencies", m_cachedStats.TotalDependencies.ToString());
                DrawEstimateRow("Est. Full Markdown Lines", m_cachedStats.EstimatedFullLines.ToString());

                var suggested = m_cachedStats.SuggestMode();
                var suggestColor = suggested == "Full" ? Color.green : new Color(1f, 0.6f, 0f);
                var oldColor = GUI.contentColor;
                GUI.contentColor = suggestColor;
                DrawEstimateRow("Suggested Mode", suggested);
                GUI.contentColor = oldColor;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private void DrawEstimateRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(180));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionButtons()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();

            // Nút Scan & Estimate
            GUI.backgroundColor = new Color(0.4f, 0.6f, 0.9f);
            if (GUILayout.Button("🔍 SCAN & ESTIMATE", GUILayout.Height(42)))
            {
                RunScanAndEstimate();
            }

            // Nút Export
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.6f);
            if (GUILayout.Button("🚀 EXPORT", GUILayout.Height(42)))
            {
                RunExport();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        #endregion

        #region SCAN

        private void RunScanAndEstimate()
        {
            List<string> folderPaths = ResolveFolderPaths();
            if (folderPaths.Count == 0) return;

            var engine = new SynapseAnalyzer(folderPaths);
            m_cachedProjectData = engine.RunAnalysis();

            m_cachedStats = MarkdownExporter.Estimate(m_cachedProjectData);

            Debug.Log($"<color=#4ec9b0><b>[SynapseGraph]</b></color> Scan done: " +
                      $"{m_cachedStats.TotalClasses} classes, " +
                      $"~{m_cachedStats.EstimatedFullLines} markdown lines. " +
                      $"Suggest: <b>{m_cachedStats.SuggestMode()}</b>");

            Repaint();
        }

        #endregion

        #region EXPORT

        private void RunExport()
        {
            if (m_cachedProjectData == null)
            {
                EditorUtility.DisplayDialog(
                    "Chưa có dữ liệu",
                    "Mày phải bấm 'Scan & Estimate' trước để tool phân tích project.",
                    "OK");
                return;
            }

            EnsureSaveFolderExists();

            // Luôn ghi JSON
            WriteJson(m_cachedProjectData);

            // Ghi Markdown tùy mode
            switch (m_exportMode)
            {
                case ExportMode.Full:
                    WriteFullMarkdown(m_cachedProjectData);
                    break;

                case ExportMode.Summary:
                    WriteSummaryMarkdown(m_cachedProjectData);
                    break;

                case ExportMode.Chunked:
                    WriteChunkedMarkdown(m_cachedProjectData, m_maxLinesPerChunk);
                    break;
            }

            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(GetAbsoluteSavePath());

            Debug.Log($"<color=#4ec9b0><b>[SynapseGraph]</b></color> Export complete: mode = <b>{m_exportMode}</b>, " +
                      $"folder = {m_savePath}");
        }

        private void WriteJson(ProjectData data)
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(Path.Combine(GetAbsoluteSavePath(), JSON_FILE_NAME), json);
        }

        private void WriteFullMarkdown(ProjectData data)
        {
            string md = MarkdownExporter.ExportFull(data);
            File.WriteAllText(Path.Combine(GetAbsoluteSavePath(), FULL_MD_NAME), md);
        }

        private void WriteSummaryMarkdown(ProjectData data)
        {
            string md = MarkdownExporter.ExportSummary(data);
            File.WriteAllText(Path.Combine(GetAbsoluteSavePath(), SUMMARY_MD_NAME), md);
        }

        private void WriteChunkedMarkdown(ProjectData data, int maxLinesPerChunk)
        {
            // Luôn có Summary kèm Chunked
            string summary = MarkdownExporter.ExportSummary(data);
            File.WriteAllText(Path.Combine(GetAbsoluteSavePath(), SUMMARY_MD_NAME), summary);

            // Chunks
            var chunks = MarkdownExporter.ExportChunked(data, maxLinesPerChunk);
            foreach (var chunk in chunks)
            {
                string fileName = $"{CHUNK_PREFIX}{chunk.Suffix}.md";
                File.WriteAllText(Path.Combine(GetAbsoluteSavePath(), fileName), chunk.Content);
            }
        }

        #endregion

        #region HELPERS

        private List<string> ResolveFolderPaths()
        {
            List<string> folderPaths = new List<string>();
            foreach (var folder in m_targetFolders)
            {
                if (folder == null) continue;
                string path = AssetDatabase.GetAssetPath(folder);
                if (AssetDatabase.IsValidFolder(path)) folderPaths.Add(path);
            }

            if (folderPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Lỗi", "Mày phải chọn ít nhất 1 thư mục để quét chứ!", "OK");
            }

            return folderPaths;
        }

        private void EnsureSaveFolderExists()
        {
            if (!Directory.Exists(GetAbsoluteSavePath()))
            {
                Directory.CreateDirectory(GetAbsoluteSavePath());
            }
        }

        private string GetAbsoluteSavePath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), m_savePath);
        }

        #endregion
    }
}
#endif
