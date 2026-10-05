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
    /// Output gồm 7 section:
    /// 1. Overview  — thống kê tổng quan
    /// 2. Folder Tree — cấu trúc thư mục
    /// 3. Class Index — bảng tóm tắt tất cả class
    /// 4. Runtime Classes — chi tiết class runtime
    /// 5. Editor Classes — chi tiết class editor (compact)
    /// 6. Signal Flow — class nào fire signal nào
    /// 7. Dependency Summary — class nào gọi class nào
    /// </summary>
    public static class MarkdownExporter
    {
        public static string Export(ProjectData data)
        {
            if (data == null || data.Classes == null || data.Classes.Count == 0)
                return "# 📐 PROJECT ARCHITECTURE SNAPSHOT\n\n*No data.*\n";

            var runtimeClasses = data.Classes.Where(c => !c.IsEditorOnly).OrderBy(c => c.FolderPath).ThenBy(c => c.Name).ToList();
            var editorClasses = data.Classes.Where(c => c.IsEditorOnly).OrderBy(c => c.FolderPath).ThenBy(c => c.Name).ToList();

            StringBuilder sb = new StringBuilder(8192);

            WriteTitle(sb);
            WriteOverview(sb, data.Classes, runtimeClasses, editorClasses);
            WriteFolderTree(sb, data.Classes);
            WriteClassIndex(sb, runtimeClasses, editorClasses);
            WriteClassDetails(sb, runtimeClasses, "4. RUNTIME CLASSES");
            if (editorClasses.Count > 0)
                WriteClassDetails(sb, editorClasses, "5. EDITOR CLASSES", compact: true);
            WriteSignalFlow(sb, runtimeClasses);
            WriteDependencySummary(sb, runtimeClasses);

            return sb.ToString();
        }

        #region SECTIONS

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

            var byFolder = all
                .GroupBy(c => c.FolderPath)
                .OrderBy(g => g.Key);

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

            foreach (var c in runtime)
                WriteIndexRow(sb, c);

            if (editor.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("*Editor-only:*");
                sb.AppendLine();
                sb.AppendLine("| Class | Kind | Namespace | Base | Interfaces |");
                sb.AppendLine("|-------|------|-----------|------|------------|");
                foreach (var c in editor)
                    WriteIndexRow(sb, c);
            }
            sb.AppendLine();
        }

        private static void WriteIndexRow(StringBuilder sb, ClassNode c)
        {
            string ifaces = c.Interfaces != null && c.Interfaces.Count > 0
                ? string.Join(", ", c.Interfaces)
                : "-";
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
                {
                    WriteSingleClass(sb, c, compact);
                }
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
                // Editor class → compact: chỉ liệt kê tên method
                if (c.Methods.Count > 0)
                {
                    sb.AppendLine($"- **Methods ({c.Methods.Count}):** {string.Join(", ", c.Methods.Select(m => m.Name))}");
                }
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
                    ? string.Join(" ", f.Attributes.Select(a => $"[{a}]")) + " "
                    : "";
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
                    ? string.Join(" ", p.Attributes.Select(a => $"[{a}]")) + " "
                    : "";
                sb.AppendLine($"  - `{p.Access} {mods}{attrs}{p.Type} {p.Name} {{ {accessors}}}`");
            }
        }

        private static void WriteMethods(StringBuilder sb, ClassNode c)
        {
            if (c.Methods == null || c.Methods.Count == 0) return;

            sb.AppendLine($"- **Methods ({c.Methods.Count}):**");
            foreach (var m in c.Methods)
            {
                WriteSingleMethod(sb, m);
            }
        }

        private static void WriteSingleMethod(StringBuilder sb, MethodNode m)
        {
            string mods = m.Modifiers != null && m.Modifiers.Count > 0 ? string.Join(" ", m.Modifiers) + " " : "";
            string attrs = m.Attributes != null && m.Attributes.Count > 0
                ? string.Join(" ", m.Attributes.Select(a => $"[{a}]")) + " "
                : "";
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
                foreach (var m in c.Methods)
                {
                    if (m.FiredSignals == null) continue;
                    foreach (var sig in m.FiredSignals)
                    {
                        if (!signalMap.ContainsKey(sig))
                            signalMap[sig] = new List<string>();
                        string entry = $"{c.Name}.{m.Name}";
                        if (!signalMap[sig].Contains(entry))
                            signalMap[sig].Add(entry);
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
            var runtimeNames = new HashSet<string>(runtime.Select(c => c.Name));

            foreach (var c in runtime)
            {
                foreach (var m in c.Methods)
                {
                    if (m.MethodDependencies == null) continue;
                    foreach (var d in m.MethodDependencies)
                    {
                        // Chỉ quan tâm dependency tới class khác trong project (không phải System/Unity)
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
            {
                sb.AppendLine($"- `{kv.Key}` → {string.Join(", ", kv.Value.Select(v => $"`{v}`"))}");
            }
            sb.AppendLine();
        }

        #endregion

        #region HELPERS

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 3) + "...";
        }

        #endregion
    }
}
#endif
