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
    /// [v5 - DEBUG GENERIC MISS]
    /// - FIX: Log chi tiết tên class bị skip empty name.
    /// - FIX: Log chi tiết tên class bị reflection miss.
    /// - ADD: Diagnostic generic type definition count.
    /// </summary>
    public class SynapseAnalyzer
    {
        private const bool DEBUG_VERBOSE = true;

        private readonly List<string> m_targetFolders;
        private readonly List<IClassProcessor> m_pipeline;
        private readonly Dictionary<Assembly, Type[]> m_assemblyTypeCache = new Dictionary<Assembly, Type[]>();

        private int m_skippedCompilerGenCount;
        private int m_skippedEmptyNameCount;
        private int m_missingReflectionCount;

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

        public ProjectData RunAnalysis()
        {
            ProjectData projectData = new ProjectData();

            m_skippedCompilerGenCount = 0;
            m_skippedEmptyNameCount = 0;
            m_missingReflectionCount = 0;

            string[] guids = AssetDatabase.FindAssets("t:MonoScript");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsValidPath(path)) continue;

                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;

                string rawCode;
                try { rawCode = File.ReadAllText(path); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SynapseGraph] Cannot read {path}: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(rawCode)) continue;

                List<DeclaredTypeInfo> declaredTypes = ExtractDeclaredTypes(rawCode);
                if (declaredTypes.Count == 0) continue;

                foreach (var decl in declaredTypes)
                {
                    if (IsCompilerGenerated(decl.Name))
                    {
                        m_skippedCompilerGenCount++;
                        continue;
                    }

                    Type type = FindReflectionType(script, decl, path);
                    if (type == null)
                    {
                        m_missingReflectionCount++;
                        if (DEBUG_VERBOSE)
                            Debug.LogWarning($"[SynapseGraph] ❌ Reflection MISS: '{decl.Namespace}.{decl.Name}' — path: {path}");
                        continue;
                    }

                    ClassNode node = new ClassNode();
                    foreach (var processor in m_pipeline)
                    {
                        try { processor.Process(type, path, rawCode, node); }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[SynapseGraph] ⚠️ Processor '{processor.GetType().Name}' FAILED on '{type.FullName}': {ex.Message}\n{ex.StackTrace}");
                        }
                    }

                    if (string.IsNullOrEmpty(node.Name))
                    {
                        m_skippedEmptyNameCount++;
                        if (DEBUG_VERBOSE)
                            Debug.LogWarning(
                                $"[SynapseGraph] ❌ Empty Name node:\n" +
                                $"   - decl: '{decl.Namespace}.{decl.Name}'\n" +
                                $"   - type: '{type.FullName}'\n" +
                                $"   - type.IsGenericTypeDef: {type.IsGenericTypeDefinition}\n" +
                                $"   - type.Name: '{type.Name}'\n" +
                                $"   - path: {path}");
                        continue;
                    }

                    projectData.Classes.Add(node);
                }
            }

            // Diagnostic: đếm generic type definitions trong các assembly SDK
            LogGenericDiagnostic();

            if (m_skippedCompilerGenCount > 0)
                Debug.Log($"<color=#4ec9b0>[SynapseGraph]</color> Skipped {m_skippedCompilerGenCount} compiler-generated.");
            if (m_skippedEmptyNameCount > 0)
                Debug.LogWarning($"[SynapseGraph] Skipped {m_skippedEmptyNameCount} types with empty Name. See above.");
            if (m_missingReflectionCount > 0)
                Debug.LogWarning($"[SynapseGraph] Reflection lookup FAILED for {m_missingReflectionCount} types. See above.");

            return projectData;
        }

        /// <summary>
        /// Diagnostic: đếm generic type definitions có trong các assembly SDK.
        /// Nếu count = 0 → assembly không expose generic defs → confirm H3.
        /// </summary>
        private void LogGenericDiagnostic()
        {
            int totalGenericDefs = 0;
            int totalTypes = 0;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var types = LoadTypesSafe(asm);
                    totalTypes += types.Length;
                    totalGenericDefs += types.Count(t => t != null && t.IsGenericTypeDefinition);
                }
                catch { }
            }

            Debug.Log($"<color=#4ec9b0>[SynapseGraph DIAG]</color> Total types loaded: {totalTypes}, Generic defs: {totalGenericDefs}");
        }

        private static bool IsCompilerGenerated(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return true;
            return typeName.StartsWith("<");
        }

        private bool IsValidPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("Packages/com.gaconstudio.synapsegraph")) return false;
            return m_targetFolders.Any(folder => path.StartsWith(folder));
        }

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

        private Type FindReflectionType(MonoScript script, DeclaredTypeInfo decl, string path)
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

        private Type LookupInAssembly(Assembly assembly, DeclaredTypeInfo decl)
        {
            if (!m_assemblyTypeCache.TryGetValue(assembly, out Type[] types))
            {
                types = LoadTypesSafe(assembly);
                m_assemblyTypeCache[assembly] = types;
            }

            string targetNs = decl.Namespace ?? string.Empty;
            string targetName = decl.Name;

            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null) continue;

                // Ưu tiên match generic type definition trước
                if (t.IsGenericTypeDefinition)
                {
                    if (StripGenericSuffix(t.Name) != targetName) continue;
                }
                else
                {
                    if (t.Name != targetName) continue;
                }

                if ((t.Namespace ?? string.Empty) != targetNs) continue;
                return t;
            }

            return null;
        }

        private Type[] LoadTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Type[] loaded = ex.Types.Where(t => t != null).ToArray();
                Debug.LogWarning(
                    $"[SynapseGraph] Partial load '{assembly.GetName().Name}': " +
                    $"{loaded.Length}/{ex.Types.Length} loaded. " +
                    $"First error: {ex.LoaderExceptions?.FirstOrDefault()?.Message}");
                return loaded;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SynapseGraph] Cannot load '{assembly.GetName().Name}': {ex.Message}");
                return Array.Empty<Type>();
            }
        }

        private static string StripGenericSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
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