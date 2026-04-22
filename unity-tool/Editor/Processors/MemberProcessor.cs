#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
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
                    node.Fields.Add(fNode);
                }
            }
        }

        private void ExtractProperties(Type type, BindingFlags flags, ClassNode node)
        {
            var properties = type.GetProperties(flags);
            foreach (var prop in properties)
            {
                var pNode = new PropertyNode
                {
                    Type = AnalyzerUtility.GetCleanTypeName(prop.PropertyType),
                    Name = prop.Name,
                    HasGetter = prop.CanRead,
                    HasSetter = prop.CanWrite
                };
                node.Properties.Add(pNode);
            }
        }

        private void ExtractMethods(Type type, BindingFlags flags, ClassNode node)
        {
            var methods = type.GetMethods(flags);
            
            Dictionary<MethodInfo, string> interfaceMapping = new Dictionary<MethodInfo, string>();
            foreach (Type iface in type.GetInterfaces())
            {
                try
                {
                    var map = type.GetInterfaceMap(iface);
                    for (int i = 0; i < map.TargetMethods.Length; i++)
                    {
                        if (!interfaceMapping.ContainsKey(map.TargetMethods[i]))
                        {
                            interfaceMapping.Add(map.TargetMethods[i], iface.Name);
                        }
                    }
                }
                catch { }
            }

            foreach (var m in methods)
            {
                if (m.IsSpecialName || m.DeclaringType != type) continue;

                var mNode = new MethodNode
                {
                    Access = AnalyzerUtility.GetAccessModifier(m),
                    Modifiers = AnalyzerUtility.GetMethodTraits(m),
                    ReturnType = AnalyzerUtility.GetCleanTypeName(m.ReturnType),
                    Name = AnalyzerUtility.GetMethodSignature(m)
                };

                if (interfaceMapping.TryGetValue(m, out string interfaceName))
                {
                    mNode.ImplementedInterface = interfaceName;
                }

                foreach (var p in m.GetParameters())
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