#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Chuyển ProjectData → Markdown thân thiện với AI/người đọc.
    /// 
    /// [v2 - SIZE HANDLING]
    /// - Thêm ExportSummary() — bản ngắn gọn luôn dùng được.
    /// - Thêm ExportChunked() — chia nhỏ khi project lớn.
    /// - Thêm Estimate() — dự đoán độ phức tạp để chọn mode.
    /// - Export() cũ → alias của ExportFull() để backward-compatible.
    /// </summary>
    public static class MarkdownExporter
    {
        // Ước lượng dòng cho 1 class detail (không tính fields/properties/methods).
        private const int BASE_LINES_PER_CLASS = 8;

        #region PUBLIC API

        /// <summary>
        /// [Backward-compatible] Xuất full markdown. Dùng ExportFull() cho code mới.
        /// </summary>
        public static string Export(ProjectData data) => ExportFull(data);

        public static string ExportFull(ProjectData data)
        {
            if (IsEmpty(data))
                return BuildEmptyDoc();

            var runtime = GetSortedRuntime(data);
            var editor = GetSortedEditor(data);

            StringBuilder sb = new StringBuilder(16384);
            WriteTitle(sb);
            WriteOverview(sb, data.Classes, runtime, editor);
            WriteFolderTree(sb, data.Classes);
            WriteClassIndex(sb, runtime, editor);
            WriteClassDetails(sb, runtime, "4. RUNTIME CLASSES");
            if (editor.Count > 0)
                WriteClassDetails(sb, editor, "5. EDITOR CLASSES", compact: true);
            WriteSignalFlow(sb, runtime);
            WriteDependencySummary(sb, runtime);
            return sb.ToString();
        }

        public static string ExportSummary(ProjectData data)
        {
            if (IsEmpty(data))
                return BuildEmptyDoc();

            var runtime = GetSortedRuntime(data);
            var editor = GetSortedEditor(data);

            StringBuilder sb = new StringBuilder(4096);
            WriteTitle(sb);
            WriteOverview(sb, data.Classes, runtime, editor);
            WriteClassIndex(sb, runtime, editor);
            WriteSignalFlow(sb, runtime);
            WriteDependencySummary(sb, runtime);

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("*Full class details available in `_FULL.md` or chunked files.*");
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>
        /// Chia markdown thành nhiều chunk theo namespace, đảm bảo mỗi chunk <= maxLinesPerChunk.
        /// Trả về danh sách ChunkFile (chưa bao gồm file Summary — caller tự gọi ExportSummary).
        /// </summary>
        public static List<ChunkFile> ExportChunked(ProjectData data, int maxLinesPerChunk = 8000)
        {
            List<ChunkFile> result = new List<ChunkFile>();
            if (IsEmpty(data)) return result;

            if (maxLinesPerChunk < 500) maxLinesPerChunk = 500;

            var runtime = GetSortedRuntime(data);

            // Gom class runtime theo namespace, sắp xếp theo tên namespace.
            var nsGroups = runtime
                .GroupBy(c => c.Namespace)
                .OrderBy(g => g.Key)
                .ToList();

            int chunkIndex = 1;
            int currentLines = 0;
            var currentNss = new List<IGrouping<string, ClassNode>>();
            int chunkNumber = 1;

            Action flushChunk = () =>
            {
                if (currentNss.Count == 0) return;

                StringBuilder sb = new StringBuilder(8192);
                WriteChunkHeader(sb, chunkNumber, currentNss);
                result.Add(new ChunkFile
                {
                    Suffix = BuildChunkSuffix(chunkNumber, currentNss),
                    Content = sb.ToString(),
                    LineCount = sb.ToString().Count(c => c == '\n')
                });

                chunkNumber++;
                currentNss.Clear();
                currentLines = 0;
            };

            foreach (var nsGroup in nsGroups)
            {
                int nsLines = EstimateNamespaceLines(nsGroup);

                if (currentLines + nsLines > maxLinesPerChunk && currentNss.Count > 0)
                {
                    flushChunk();
                }

                currentNss.Add(nsGroup);
                currentLines += nsLines;

                // Edge case: 1 namespace quá lớn → vẫn phải ghi 1 chunk riêng (không split namespace).
                if (currentLines >= maxLinesPerChunk)
                {
                    flushChunk();
                }
            }

            flushChunk();
            return result;
        }

        public static ComplexityStats Estimate(ProjectData data)
        {
            ComplexityStats stats = new ComplexityStats();
            if (IsEmpty(data)) return stats;

            stats.TotalClasses = data.Classes.Count;
            stats.RuntimeClasses = data.Classes.Count(c => !c.IsEditorOnly);
            stats.EditorClasses = data.Classes.Count(c => c.IsEditorOnly);
            stats.TotalMethods = data.Classes.Sum(c => c.Methods?.Count ?? 0);
            stats.TotalDependencies = data.Classes
                .SelectMany(c => c.Methods ?? new List<MethodNode>())
                .Sum(m => m.MethodDependencies?.Count ?? 0);

            // Ước lượng dòng Full
            int estimated = 50; // overview + folder tree + index
            foreach (var c in data.Classes)
            {
                estimated += BASE_LINES_PER_CLASS;
                estimated += (c.Fields?.Count ?? 0);
                estimated += (c.Properties?.Count ?? 0);
                estimated += (c.Methods?.Count ?? 0) * 2;
                estimated += (c.Methods ?? new List<MethodNode>())
                    .Sum(m => m.MethodDependencies?.Count ?? 0);
            }
            stats.EstimatedFullLines = estimated;

            return stats;
        }

        #endregion

        #region CHUNK HELPERS

        private static void WriteChunkHeader(StringBuilder sb, int chunkNumber, List<IGrouping<string, ClassNode>> nsGroups)
        {
            sb.AppendLine($"# 📐 ARCHITECTURE CHUNK {chunkNumber:D2}");
            sb.AppendLine();
            sb.AppendLine($"> Namespaces: {string.Join(", ", nsGroups.Select(g => $"`{g.Key}`"))}");
            sb.AppendLine($"> Classes in chunk: {nsGroups.Sum(g => g.Count())}");
            sb.AppendLine();

            foreach (var nsGroup in nsGroups)
            {
                sb.AppendLine($"## Namespace: `{nsGroup.Key}`");
                sb.AppendLine();
                foreach (var c in nsGroup.OrderBy(c => c.Name))
                    WriteSingleClass(sb, c, compact: false);
            }
        }

        private static string BuildChunkSuffix(int chunkNumber, List<IGrouping<string, ClassNode>> nsGroups)
        {
            if (nsGroups.Count == 1)
            {
                string ns = nsGroups[0].Key;
                string shortName = ns.Contains('.') ? ns.Substring(ns.LastIndexOf('.') + 1) : ns;
                return $"{chunkNumber:D2}_{SanitizeName(shortName)}";
            }
            return $"{chunkNumber:D2}_Mixed_{nsGroups.Count}ns";
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        private static int EstimateNamespaceLines(IGrouping<string, ClassNode> nsGroup)
        {
            int lines = 3; // header namespace
            foreach (var c in nsGroup)
            {
                lines += BASE_LINES_PER_CLASS;
                lines += (c.Fields?.Count ?? 0);
                lines += (c.Properties?.Count ?? 0);
                lines += (c.Methods?.Count ?? 0) * 2;
                lines += (c.Methods ?? new List<MethodNode>())
                    .Sum(m => m.MethodDependencies?.Count ?? 0);
            }
            return lines;
        }

        #endregion

        #region COMMON SECTIONS

        private static bool IsEmpty(ProjectData data)
        {
            return data == null || data.Classes == null || data.Classes.Count == 0;
        }

        private static string BuildEmptyDoc()
        {
            return "# 📐 PROJECT ARCHITECTURE SNAPSHOT\n\n*No data.*\n";
        }

        private static List<ClassNode> GetSortedRuntime(ProjectData data) =>
            data.Classes.Where(c => !c.IsEditorOnly).OrderBy(c => c.FolderPath).ThenBy(c => c.Name).ToList();

        private static List<ClassNode> GetSortedEditor(ProjectData data) =>
            data.Classes.Where(c => c.IsEditorOnly).OrderBy(c => c.FolderPath).ThenBy(c => c.Name).ToList();

        private static void WriteTitle(StringBuilder sb)
        {
            sb.AppendLine("# 📐 PROJECT ARCHITECTURE SNAPSHOT");
            sb.AppendLine();
            sb.AppendLine($"> Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        private static void WriteOverview(StringBuilder sb, List<ClassNode> all, List<ClassNode> runtime, List<ClassNode> editor)
        {
            sb.AppendLine("## 1. OVERVIEW");
            sb.AppendLine();
            sb.AppendLine($"- **Total classes:** {all.Count}");
            sb.AppendLine($"- **Runtime classes:** {runtime.Count}");
            sb.AppendLine($"- **Editor-only classes:** {editor.Count}");
            sb.AppendLine($"- **Namespaces:** {all.Select(c => c.Namespace).Distinct().Count()}");
            sb.AppendLine($"- **Folders:** {all.Select(c => c.FolderPath).Distinct().Count()}");
            sb.AppendLine();

            var byKind = all.GroupBy(c => c.Kind).OrderByDescending(g => g.Count());
            sb.AppendLine("**By Kind:**");
            foreach (var g in byKind)
                sb.AppendLine($"- {g.Key}: {g.Count()}");
            sb.AppendLine();
        }

        private static void WriteFolderTree(StringBuilder sb, List<ClassNode> all)
        {
            sb.AppendLine("## 2. FOLDER TREE");
            sb.AppendLine();
            sb.AppendLine("```");

            var byFolder = all.GroupBy(c => c.FolderPath).OrderBy(g => g.Key);
            foreach (var g in byFolder)
            {
                sb.AppendLine(g.Key);
                foreach (var c in g.OrderBy(c => c.Name))
                    sb.AppendLine($"    - {c.Name} ({c.Kind})");
            }

            sb.AppendLine("```");
            sb.AppendLine();
        }

        private static void WriteClassIndex(StringBuilder sb, List<ClassNode> runtime, List<ClassNode> editor)
        {
            sb.AppendLine("## 3. CLASS INDEX");
            sb.AppendLine();
            sb.AppendLine("| Class | Kind | Namespace | Base | Interfaces |");
            sb.AppendLine("|-------|------|-----------|------|------------|");

            foreach (var c in runtime) WriteIndexRow(sb, c);

            if (editor.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("*Editor-only:*");
                sb.AppendLine();
                sb.AppendLine("| Class | Kind | Namespace | Base | Interfaces |");
                sb.AppendLine("|-------|------|-----------|------|------------|");
                foreach (var c in editor) WriteIndexRow(sb, c);
            }
            sb.AppendLine();
        }

        private static void WriteIndexRow(StringBuilder sb, ClassNode c)
        {
            string ifaces = c.Interfaces != null && c.Interfaces.Count > 0 ? string.Join(", ", c.Interfaces) : "-";
            string baseName = string.IsNullOrEmpty(c.BaseClass) ? "-" : c.BaseClass;
            sb.AppendLine($"| `{c.Name}` | {c.Kind} | {c.Namespace} | `{baseName}` | {ifaces} |");
        }

        private static void WriteClassDetails(StringBuilder sb, List<ClassNode> classes, string title, bool compact = false)
        {
            sb.AppendLine($"## {title}");
            sb.AppendLine();

            var byNs = classes.GroupBy(c => c.Namespace).OrderBy(g => g.Key);
            foreach (var nsGroup in byNs)
            {
                sb.AppendLine($"### Namespace: `{nsGroup.Key}`");
                sb.AppendLine();
                foreach (var c in nsGroup.OrderBy(c => c.Name))
                    WriteSingleClass(sb, c, compact);
            }
        }

        private static void WriteSingleClass(StringBuilder sb, ClassNode c, bool compact)
        {
            sb.AppendLine($"#### `{c.Name}`");
            sb.AppendLine();
            sb.AppendLine($"- **Kind:** {c.Kind}{(c.Traits.Count > 0 ? " (" + string.Join(", ", c.Traits) + ")" : "")}");
            sb.AppendLine($"- **Path:** `{c.FolderPath}/{c.Name}.cs`");

            if (c.BaseChain != null && c.BaseChain.Count > 0)
                sb.AppendLine($"- **Base Chain:** {string.Join(" → ", c.BaseChain)}");
            else if (!string.IsNullOrEmpty(c.BaseClass) && c.BaseClass != "None")
                sb.AppendLine($"- **Base:** `{c.BaseClass}`");

            if (c.Interfaces != null && c.Interfaces.Count > 0)
                sb.AppendLine($"- **Interfaces:** {string.Join(", ", c.Interfaces.Select(i => $"`{i}`"))}");

            if (c.Attributes != null && c.Attributes.Count > 0)
                sb.AppendLine($"- **Attributes:** {string.Join(", ", c.Attributes.Select(a => $"[{a}]"))}");

            if (c.Kind == "Enum")
            {
                sb.AppendLine($"- **Values:** {string.Join(", ", c.EnumValues)}");
                sb.AppendLine();
                return;
            }

            if (!compact)
            {
                WriteFields(sb, c);
                WriteProperties(sb, c);
                WriteMethods(sb, c);
            }
            else
            {
                if (c.Methods != null && c.Methods.Count > 0)
                    sb.AppendLine($"- **Methods ({c.Methods.Count}):** {string.Join(", ", c.Methods.Select(m => m.Name))}");
            }
            sb.AppendLine();
        }

        private static void WriteFields(StringBuilder sb, ClassNode c)
        {
            if (c.Fields == null || c.Fields.Count == 0) return;
            sb.AppendLine("- **Fields:**");
            foreach (var f in c.Fields)
            {
                string mods = f.Modifiers != null && f.Modifiers.Count > 0 ? string.Join(" ", f.Modifiers) + " " : "";
                string attrs = f.Attributes != null && f.Attributes.Count > 0
                    ? string.Join(" ", f.Attributes.Select(a => $"[{a}]")) + " " : "";
                sb.AppendLine($"  - `{f.Access} {mods}{attrs}{f.Type} {f.Name}`");
            }
        }

        private static void WriteProperties(StringBuilder sb, ClassNode c)
        {
            if (c.Properties == null || c.Properties.Count == 0) return;
            sb.AppendLine("- **Properties:**");
            foreach (var p in c.Properties)
            {
                string accessors = (p.HasGetter ? "get; " : "") + (p.HasSetter ? "set; " : "");
                string mods = p.Modifiers != null && p.Modifiers.Count > 0 ? string.Join(" ", p.Modifiers) + " " : "";
                string attrs = p.Attributes != null && p.Attributes.Count > 0
                    ? string.Join(" ", p.Attributes.Select(a => $"[{a}]")) + " " : "";
                sb.AppendLine($"  - `{p.Access} {mods}{attrs}{p.Type} {p.Name} {{ {accessors}}}`");
            }
        }

        private static void WriteMethods(StringBuilder sb, ClassNode c)
        {
            if (c.Methods == null || c.Methods.Count == 0) return;
            sb.AppendLine($"- **Methods ({c.Methods.Count}):**");
            foreach (var m in c.Methods) WriteSingleMethod(sb, m);
        }

        private static void WriteSingleMethod(StringBuilder sb, MethodNode m)
        {
            string mods = m.Modifiers != null && m.Modifiers.Count > 0 ? string.Join(" ", m.Modifiers) + " " : "";
            string attrs = m.Attributes != null && m.Attributes.Count > 0
                ? string.Join(" ", m.Attributes.Select(a => $"[{a}]")) + " " : "";
            string paramStr = string.Join(", ", m.Parameters.Select(p =>
                $"{(string.IsNullOrEmpty(p.Modifier) ? "" : p.Modifier + " ")}{p.Type} {p.Name}"));

            string retType = m.ReturnType == "Constructor" ? "" : m.ReturnType + " ";
            string iface = !string.IsNullOrEmpty(m.ImplementedInterface) ? $" [impl: {m.ImplementedInterface}]" : "";

            sb.AppendLine($"  - `{m.Access} {mods}{attrs}{retType}{m.Name}({paramStr})`{iface}");

            if (m.FiredSignals != null && m.FiredSignals.Count > 0)
                sb.AppendLine($"    - 📡 Fires: {string.Join(", ", m.FiredSignals.Select(s => $"`{s}`"))}");
            if (m.MutatedFields != null && m.MutatedFields.Count > 0)
                sb.AppendLine($"    - ✏️ Mutates: {string.Join(", ", m.MutatedFields.Select(f => $"`{f}`"))}");

            if (m.MethodDependencies != null && m.MethodDependencies.Count > 0)
            {
                foreach (var d in m.MethodDependencies)
                {
                    sb.AppendLine($"    - 🔗 `[{d.DependencyType}]` → `{d.TargetClass}.{d.TargetMethod}`");
                    if (!string.IsNullOrEmpty(d.RawContext))
                        sb.AppendLine($"      - context: `{Truncate(d.RawContext, 120)}`");
                }
            }
        }

        private static void WriteSignalFlow(StringBuilder sb, List<ClassNode> runtime)
        {
            sb.AppendLine("## 6. SIGNAL FLOW");
            sb.AppendLine();

            var signalMap = new Dictionary<string, List<string>>();

            foreach (var c in runtime)
            {
                if (c == null || string.IsNullOrEmpty(c.Name) || c.Methods == null) continue;

                foreach (var m in c.Methods)
                {
                    if (m?.FiredSignals == null) continue;

                    foreach (var sig in m.FiredSignals)
                    {
                        if (string.IsNullOrEmpty(sig)) continue;
                        if (!signalMap.ContainsKey(sig)) signalMap[sig] = new List<string>();

                        string entry = $"{c.Name}.{m.Name}";
                        if (!signalMap[sig].Contains(entry)) signalMap[sig].Add(entry);
                    }
                }
            }

            if (signalMap.Count == 0)
            {
                sb.AppendLine("*No signals fired.*");
                sb.AppendLine();
                return;
            }

            foreach (var kv in signalMap.OrderBy(k => k.Key))
            {
                sb.AppendLine($"- 📡 `{kv.Key}`");
                foreach (var entry in kv.Value)
                    sb.AppendLine($"  - ← {entry}");
            }
            sb.AppendLine();
        }

        private static void WriteDependencySummary(StringBuilder sb, List<ClassNode> runtime)
        {
            sb.AppendLine("## 7. DEPENDENCY SUMMARY");
            sb.AppendLine();

            var depMap = new Dictionary<string, HashSet<string>>();

            // [FIX] Filter null/empty trước khi tạo HashSet — tránh ArgumentNullException
            var runtimeNames = new HashSet<string>(
                runtime
                    .Where(c => c != null && !string.IsNullOrEmpty(c.Name))
                    .Select(c => c.Name)
            );

            foreach (var c in runtime)
            {
                if (c == null || string.IsNullOrEmpty(c.Name) || c.Methods == null) continue;

                foreach (var m in c.Methods)
                {
                    if (m?.MethodDependencies == null) continue;

                    foreach (var d in m.MethodDependencies)
                    {
                        if (d == null) continue;
                        if (string.IsNullOrEmpty(d.TargetClass)) continue;
                        if (!runtimeNames.Contains(d.TargetClass)) continue;
                        if (d.TargetClass == c.Name) continue;

                        if (!depMap.ContainsKey(c.Name))
                            depMap[c.Name] = new HashSet<string>();

                        depMap[c.Name].Add(d.TargetClass);
                    }
                }
            }

            if (depMap.Count == 0)
            {
                sb.AppendLine("*No cross-class dependencies found.*");
                sb.AppendLine();
                return;
            }

            foreach (var kv in depMap.OrderBy(k => k.Key))
                sb.AppendLine($"- `{kv.Key}` → {string.Join(", ", kv.Value.Select(v => $"`{v}`"))}");
            sb.AppendLine();
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 3) + "...";
        }

        #endregion
    }

    /// <summary>
    /// Kết quả 1 chunk markdown (chưa ghi file).
    /// </summary>
    public class ChunkFile
    {
        public string Suffix;
        public string Content;
        public int LineCount;
    }

    /// <summary>
    /// Thống kê độ phức tạp project — dùng để chọn export mode.
    /// </summary>
    public class ComplexityStats
    {
        public int TotalClasses;
        public int RuntimeClasses;
        public int EditorClasses;
        public int TotalMethods;
        public int TotalDependencies;
        public int EstimatedFullLines;

        public string SuggestMode(int fullThreshold = 8000)
        {
            return EstimatedFullLines <= fullThreshold ? "Full" : "Chunked";
        }
    }
}
#endif
