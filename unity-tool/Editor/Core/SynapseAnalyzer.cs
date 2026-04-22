#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Bộ máy điều phối chính, chạy các mã nguồn qua dây chuyền (Pipeline) phân tích.
    /// </summary>
    public class SynapseAnalyzer
    {
        private readonly List<string> _targetFolders;
        private readonly List<IClassProcessor> _pipeline;

        public SynapseAnalyzer(List<string> targetFolders)
        {
            _targetFolders = targetFolders;

            _pipeline = new List<IClassProcessor>
            {
                new BasicInfoProcessor(),
                new MemberProcessor(),
                new RoslynASTProcessor()
            };
        }

        /// <summary>
        /// Thực thi quét toàn bộ mã nguồn trong các thư mục mục tiêu.
        /// </summary>
        /// <returns>Dữ liệu tổng thể của Project (ProjectData).</returns>
        public ProjectData RunAnalysis()
        {
            ProjectData projectData = new ProjectData();
            string[] guids = AssetDatabase.FindAssets("t:MonoScript");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

                if (script != null)
                {
                    Type type = script.GetClass();

                    if (IsValidType(type, path))
                    {
                        ClassNode node = new ClassNode();
                        string rawCode = File.ReadAllText(path);

                        foreach (var processor in _pipeline)
                        {
                            processor.Process(type, path, rawCode, node);
                        }

                        projectData.Classes.Add(node);
                    }
                }
            }
            return projectData;
        }

        /// <summary>
        /// Kiểm tra xem Type hiện tại có hợp lệ để phân tích hay không.
        /// </summary>
        private bool IsValidType(Type type, string path)
        {
            if (type == null || string.IsNullOrEmpty(type.Namespace) || type.Name.Contains("<")) return false;

            if (path.StartsWith("Packages/com.gaconstudio.synapsegraph")) return false;

            return _targetFolders.Any(folder => path.StartsWith(folder));
        }
    }
}
#endif