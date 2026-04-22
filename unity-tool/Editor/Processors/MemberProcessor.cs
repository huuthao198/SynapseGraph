#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Bộ xử lý phân tích và bóc tách cấu trúc Fields, Properties, và Methods bằng Reflection.
    /// </summary>
    public class MemberProcessor : IClassProcessor
    {
        public void Process(Type type, string path, string rawCode, ClassNode node)
        {
            if (type.IsEnum) return;

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            ExtractFields(type, flags, node);
            ExtractProperties(type, flags, node);
            ExtractMethods(type, flags, node);
            ExtractConstructors(type, flags, node);
        }

        private void ExtractFields(Type type, BindingFlags flags, ClassNode node)
        {
            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                if (!field.Name.Contains("<") && !field.Name.Contains("k__BackingField"))
                {
                    var fNode = new FieldNode
                    {
                        Access = AnalyzerUtility.GetAccessModifier(field),
                        Modifiers = AnalyzerUtility.GetFieldTraits(field),
                        Type = AnalyzerUtility.GetCleanTypeName(field.FieldType),
                        Name = field.Name
                    };

                    var fAttrs = field.GetCustomAttributesData();
                    foreach (var attr in fAttrs)
                    {
                        fNode.Attributes.Add(attr.AttributeType.Name.Replace("Attribute", ""));
                    }
                    node.Fields.Add(fNode);
                }
            }
        }

        private void ExtractProperties(Type type, BindingFlags flags, ClassNode node)
        {
            var properties = type.GetProperties(flags);
            foreach (var prop in properties)
            {
                if (prop.Name != "Item" || prop.GetIndexParameters().Length == 0)
                {
                    node.Properties.Add(new PropertyNode
                    {
                        Type = AnalyzerUtility.GetCleanTypeName(prop.PropertyType),
                        Name = prop.Name,
                        HasGetter = prop.CanRead,
                        HasSetter = prop.CanWrite
                    });
                }
            }
        }

        private void ExtractMethods(Type type, BindingFlags flags, ClassNode node)
        {
            var methods = type.GetMethods(flags);
            foreach (var method in methods)
            {
                if (method.Name.Contains("<") || method.IsSpecialName) continue;

                MethodNode mNode = new MethodNode
                {
                    Access = AnalyzerUtility.GetAccessModifier(method),
                    Modifiers = AnalyzerUtility.GetMethodTraits(method),
                    ReturnType = AnalyzerUtility.GetCleanTypeName(method.ReturnType),
                    Name = AnalyzerUtility.GetMethodSignature(method)
                };

                foreach (var p in method.GetParameters())
                {
                    mNode.Parameters.Add(new ParameterNode
                    {
                        Modifier = AnalyzerUtility.GetParameterModifier(p),
                        Type = AnalyzerUtility.GetCleanTypeName(p.ParameterType),
                        Name = p.Name
                    });
                }
                node.Methods.Add(mNode);
            }
        }

        private void ExtractConstructors(Type type, BindingFlags flags, ClassNode node)
        {
            var constructors = type.GetConstructors(flags);
            string classNamePure = type.Name.Split('`')[0];

            foreach (var ctor in constructors)
            {
                MethodNode cNode = new MethodNode
                {
                    Access = AnalyzerUtility.GetAccessModifier(ctor),
                    Modifiers = ctor.IsStatic ? new List<string> { "static" } : new List<string>(),
                    ReturnType = "Constructor",
                    Name = classNamePure
                };

                foreach (var p in ctor.GetParameters())
                {
                    cNode.Parameters.Add(new ParameterNode
                    {
                        Modifier = AnalyzerUtility.GetParameterModifier(p),
                        Type = AnalyzerUtility.GetCleanTypeName(p.ParameterType),
                        Name = p.Name
                    });
                }
                node.Methods.Add(cNode);
            }
        }
    }
}
#endif