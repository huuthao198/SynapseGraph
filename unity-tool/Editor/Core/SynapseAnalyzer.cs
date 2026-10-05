#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEditor;
using UnityEngine;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Bộ máy điều phối chính, chạy các mã nguồn qua dây chuyền (Pipeline) phân tích.
    /// 
    /// [v3 - FIX NOISE]
    /// - FIX BUG: Filter compiler-generated types (<>c, <>c__DisplayClass, <>f__AnonymousType).
    /// - FIX BUG: Skip ClassNode có Name empty sau khi process (thay vì add vào projectData với null).
    /// - ENHANCE: Log warning để debug khi skip.
    /// </summary>
    public class SynapseAnalyzer
    {
        private readonly List<string> m_targetFolders;
        private readonly List<IClassProcessor> m_pipeline;
        private readonly Dictionary<Assembly, Type[]> m_assemblyTypeCache = new Dictionary<Assembly, Type[]>();

        private int m_skippedCompilerGenCount;
        private int m_skippedEmptyNameCount;

        public SynapseAnalyzer(List<string> targetFolders)
        {
            m_targetFolders = targetFolders;

            m_pipeline = new List<IClassProcessor>
            {
                new BasicInfoProcessor(),
                new MemberProcessor(),
                new RoslynASTProcessor()
            };
        }

        /// <summary>
        /// Thực thi quét toàn bộ mã nguồn trong các thư mục mục tiêu.
        /// </summary>
        public ProjectData RunAnalysis()
        {
            ProjectData projectData = new ProjectData();

            m_skippedCompilerGenCount = 0;
            m_skippedEmptyNameCount = 0;

            string[] guids = AssetDatabase.FindAssets("t:MonoScript");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!IsValidPath(path)) continue;

                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                string rawCode;
                try
                {
                    rawCode = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SynapseGraph] Không đọc được file {path}: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(rawCode)) continue;

                List<DeclaredTypeInfo> declaredTypes = ExtractDeclaredTypes(rawCode);
                if (declaredTypes.Count == 0) continue;

                foreach (var decl in declaredTypes)
                {
                    // [FIX] Skip compiler-generated types ngay từ đầu
                    if (IsCompilerGenerated(decl.Name))
                    {
                        m_skippedCompilerGenCount++;
                        continue;
                    }

                    Type type = FindReflectionType(script, decl);
                    if (type == null) continue;

                    ClassNode node = new ClassNode();
                    foreach (var processor in m_pipeline)
                    {
                        try
                        {
                            processor.Process(type, path, rawCode, node);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[SynapseGraph] Processor '{processor.GetType().Name}' failed on '{type.Name}': {ex.Message}");
                        }
                    }

                    // [FIX] Post-process validation: skip nếu Name vẫn empty/null
                    if (string.IsNullOrEmpty(node.Name))
                    {
                        m_skippedEmptyNameCount++;
                        Debug.LogWarning($"[SynapseGraph] Skipped '{decl.Namespace}.{decl.Name}' (path: {path}) — Name empty after processing.");
                        continue;
                    }

                    projectData.Classes.Add(node);
                }
            }

            if (m_skippedCompilerGenCount > 0)
            {
                Debug.Log($"<color=#4ec9b0>[SynapseGraph]</color> Skipped <b>{m_skippedCompilerGenCount}</b> compiler-generated types.");
            }
            if (m_skippedEmptyNameCount > 0)
            {
                Debug.LogWarning($"[SynapseGraph] Skipped {m_skippedEmptyNameCount} types with empty Name after processing.");
            }

            return projectData;
        }

        /// <summary>
        /// Check xem tên type có phải compiler-generated không.
        /// C# compiler sinh ra các type ẩn có tên bắt đầu bằng '<':
        /// - &lt;&gt;c                        — static lambda container
        /// - &lt;&gt;c__DisplayClass         — captured variable closure
        /// - &lt;&gt;f__AnonymousType        — anonymous type
        /// </summary>
        private static bool IsCompilerGenerated(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return true;
            return typeName.StartsWith("<");
        }

        /// <summary>
        /// Filter path: chỉ scan file nằm trong folder mục tiêu, bỏ qua code của chính tool.
        /// </summary>
        private bool IsValidPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("Packages/com.gaconstudio.synapsegraph")) return false;

            return m_targetFolders.Any(folder => path.StartsWith(folder));
        }

        /// <summary>
        /// Parse file bằng Roslyn, lấy TẤT CẢ top-level type declaration (Class, Struct, Interface, Enum, Record).
        /// Nested class hiện CHƯA hỗ trợ (TODO phase sau).
        /// </summary>
        private List<DeclaredTypeInfo> ExtractDeclaredTypes(string rawCode)
        {
            List<DeclaredTypeInfo> result = new List<DeclaredTypeInfo>();

            try
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(rawCode);
                CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

                var allTypeDecls = root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .Where(t => !(t.Parent is BaseTypeDeclarationSyntax));

                foreach (var typeDecl in allTypeDecls)
                {
                    var nsDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                    string nsName = nsDecl?.Name.ToString() ?? string.Empty;

                    result.Add(new DeclaredTypeInfo
                    {
                        Namespace = nsName,
                        Name = typeDecl.Identifier.Text
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SynapseGraph] Roslyn parse failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Tìm Reflection Type tương ứng với declaration.
        /// Ưu tiên assembly của MonoScript → fallback scan toàn bộ AppDomain.
        /// </summary>
        private Type FindReflectionType(MonoScript script, DeclaredTypeInfo decl)
        {
            Type mainType = script.GetClass();
            if (mainType != null)
            {
                Type found = LookupInAssembly(mainType.Assembly, decl);
                if (found != null) return found;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = LookupInAssembly(asm, decl);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Lookup trong 1 assembly, có cache lại danh sách types.
        /// </summary>
        private Type LookupInAssembly(Assembly assembly, DeclaredTypeInfo decl)
        {
            if (!m_assemblyTypeCache.TryGetValue(assembly, out Type[] types))
            {
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    types = Array.Empty<Type>();
                }
                m_assemblyTypeCache[assembly] = types;
            }

            string targetNs = decl.Namespace ?? string.Empty;
            string targetName = decl.Name;

            return types.FirstOrDefault(t =>
                StripGenericSuffix(t.Name) == targetName &&
                (t.Namespace ?? string.Empty) == targetNs
            );
        }

        private static string StripGenericSuffix(string name)
        {
            int idx = name.IndexOf('`');
            return idx > 0 ? name.Substring(0, idx) : name;
        }

        private class DeclaredTypeInfo
        {
            public string Namespace;
            public string Name;
        }
    }
}
#endif