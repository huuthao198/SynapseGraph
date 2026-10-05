#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Bộ xử lý trích xuất thông tin nền tảng: Namespace, Base Class, Base Chain,
    /// Interfaces, Attributes, Usings, và cờ Editor-only.
    /// 
    /// [v2 - FIX & ENHANCE]
    /// - FIX BUG 7: Extract TẤT CẢ attribute (không chỉ CreateAssetMenu/Serializable/RequireComponent).
    /// - NEW: Fill BaseChain (full inheritance chain).
    /// - NEW: Detect Editor-only (path /Editor/, editor attributes, namespace .Editor).
    /// - ENHANCE: Regex using hỗ trợ "using static".
    /// </summary>
    public class BasicInfoProcessor : IClassProcessor
    {
        private static readonly HashSet<string> EditorSpecificAttributes = new HashSet<string>
        {
            "EditorWindow", "CustomEditor", "CustomPropertyDrawer",
            "CustomSceneGUI", "MenuItem", "InitializeOnLoad",
            "InitializeOnLoadMethod", "DidReloadScripts"
        };

        public void Process(Type type, string path, string rawCode, ClassNode node)
        {
            if (type == null) return;

            // --- Thông tin cơ bản ---
            node.Kind = AnalyzerUtility.GetBaseKind(type);
            node.Traits = AnalyzerUtility.GetClassTraits(type);
            node.Name = AnalyzerUtility.GetCleanTypeName(type);
            node.Namespace = string.IsNullOrEmpty(type.Namespace) ? "Global" : type.Namespace;
            node.FolderPath = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? string.Empty;

            // --- Base class + chain ---
            node.BaseClass = (type.BaseType != null && type.BaseType != typeof(object))
                ? AnalyzerUtility.GetCleanTypeName(type.BaseType)
                : "None";
            node.BaseChain = AnalyzerUtility.GetFullBaseChain(type);

            // --- Interfaces ---
            node.Interfaces = type.GetInterfaces()
                .Select(AnalyzerUtility.GetCleanTypeName)
                .ToList();

            // --- Attributes (extract hết, sau đó enhance CreateAssetMenu) ---
            node.Attributes = AnalyzerUtility.GetAttributeNames(type);
            EnhanceCreateAssetMenu(type, node);

            // --- Editor-only flag ---
            node.IsEditorOnly = DetectEditorOnly(type, path, node.Attributes);

            // --- Init collections ---
            node.Usings = new List<string>();
            node.EnumValues = new List<string>();
            node.Fields = new List<FieldNode>();
            node.Properties = new List<PropertyNode>();
            node.Methods = new List<MethodNode>();

            // --- Enum đặc biệt: chỉ lấy values ---
            if (type.IsEnum)
            {
                node.EnumValues = Enum.GetNames(type).ToList();
                ExtractUsings(rawCode, node);
                return;
            }

            // --- Usings ---
            ExtractUsings(rawCode, node);
        }

        /// <summary>
        /// Giữ format chi tiết cho CreateAssetMenu vì nó có giá trị cụ thể (fileName, menuName, order).
        /// Ghi đè entry "CreateAssetMenu" đơn giản bằng entry chi tiết.
        /// </summary>
        private void EnhanceCreateAssetMenu(Type type, ClassNode node)
        {
            try
            {
                var cam = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                if (cam == null) return;

                string detailed = $"CreateAssetMenu(fileName = \"{cam.fileName}\", menuName = \"{cam.menuName}\", order = {cam.order})";

                int idx = node.Attributes.IndexOf("CreateAssetMenu");
                if (idx >= 0) node.Attributes[idx] = detailed;
                else node.Attributes.Add(detailed);
            }
            catch
            {
                // Attribute reflection có thể throw trong một số edge case → bỏ qua an toàn
            }
        }

        /// <summary>
        /// Detect class chỉ tồn tại trong Editor.
        /// 3 rule: path /Editor/, editor attribute, namespace .Editor.
        /// </summary>
        private bool DetectEditorOnly(Type type, string path, List<string> classAttributes)
        {
            // Rule 1: Path chứa /Editor/
            string normalizedPath = path.Replace("\\", "/");
            if (normalizedPath.Contains("/Editor/") || normalizedPath.StartsWith("Assets/Editor/"))
                return true;

            // Rule 2: Có attribute Editor-specific
            if (classAttributes != null && classAttributes.Any(a => EditorSpecificAttributes.Contains(a)))
                return true;

            // Rule 3: Namespace kết thúc bằng .Editor
            string ns = type.Namespace ?? string.Empty;
            if (ns.EndsWith(".Editor") || ns == "Editor")
                return true;

            return false;
        }

        /// <summary>
        /// Extract using directives từ raw code.
        /// Hỗ trợ: using System; / using static System.Math;
        /// </summary>
        private void ExtractUsings(string rawCode, ClassNode node)
        {
            if (string.IsNullOrEmpty(rawCode)) return;

            var usingMatches = Regex.Matches(
                rawCode,
                @"^using\s+(?:static\s+)?([A-Za-z0-9_\.]+)\s*;",
                RegexOptions.Multiline);

            foreach (Match m in usingMatches)
            {
                string ns = m.Groups[1].Value;
                if (!string.IsNullOrEmpty(ns) && !node.Usings.Contains(ns))
                {
                    node.Usings.Add(ns);
                }
            }
        }
    }
}
#endif
