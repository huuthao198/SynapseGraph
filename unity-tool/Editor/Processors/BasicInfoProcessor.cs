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
    /// Bộ xử lý trích xuất các thông tin nền tảng: Namespace, Base Class, Interfaces, Attributes và Usings.
    /// </summary>
    public class BasicInfoProcessor : IClassProcessor
    {
        public void Process(Type type, string path, string rawCode, ClassNode node)
        {
            node.Kind = AnalyzerUtility.GetBaseKind(type);
            node.Traits = AnalyzerUtility.GetClassTraits(type);
            node.Name = AnalyzerUtility.GetCleanTypeName(type);
            node.Namespace = type.Namespace;
            node.FolderPath = Path.GetDirectoryName(path).Replace("\\", "/");
            node.BaseClass = (type.BaseType != null && type.BaseType != typeof(object)) ? AnalyzerUtility.GetCleanTypeName(type.BaseType) : "None";
            node.Interfaces = type.GetInterfaces().Select(i => AnalyzerUtility.GetCleanTypeName(i)).ToList();

            node.Usings = new List<string>();
            node.Attributes = new List<string>();
            node.EnumValues = new List<string>();
            node.Fields = new List<FieldNode>();
            node.Properties = new List<PropertyNode>();
            node.Methods = new List<MethodNode>();

            if (type.IsEnum)
            {
                node.EnumValues = Enum.GetNames(type).ToList();
                return;
            }

            var attributes = type.GetCustomAttributesData();
            foreach (var attr in attributes)
            {
                string attrName = attr.AttributeType.Name.Replace("Attribute", "");
                if (attrName == "CreateAssetMenu")
                {
                    var cam = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                    if (cam != null) node.Attributes.Add($"CreateAssetMenu(fileName = \"{cam.fileName}\", menuName = \"{cam.menuName}\", order = {cam.order})");
                }
                else if (attrName == "Serializable" || attrName == "RequireComponent")
                {
                    node.Attributes.Add(attrName);
                }
            }

            var usingMatches = Regex.Matches(rawCode, @"^using\s+([A-Za-z0-9_\.]+);", RegexOptions.Multiline);
            foreach (Match m in usingMatches)
            {
                string ns = m.Groups[1].Value;
                if (!node.Usings.Contains(ns)) node.Usings.Add(ns);
            }
        }
    }
}
#endif