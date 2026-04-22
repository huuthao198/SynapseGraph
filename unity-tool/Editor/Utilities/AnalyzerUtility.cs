#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Cung cấp các hàm tiện ích tái sử dụng trong quá trình phân tích Reflection và AST.
    /// </summary>
    public static class AnalyzerUtility
    {
        public static string GetBaseKind(Type t)
        {
            if (t.IsInterface) return "Interface";
            if (t.IsEnum) return "Enum";
            if (t.IsValueType && !t.IsPrimitive) return "Struct";
            return "Class";
        }

        public static List<string> GetClassTraits(Type t)
        {
            List<string> traits = new List<string>();
            if (t.IsAbstract && t.IsSealed) traits.Add("Static");
            else
            {
                if (t.IsAbstract) traits.Add("Abstract");
                if (t.IsSealed) traits.Add("Sealed");
            }
            return traits;
        }

        public static string GetCleanTypeName(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type.IsByRef) type = type.GetElementType();

            if (!type.IsGenericType) return type.Name;
            string genericName = type.Name.Substring(0, type.Name.IndexOf('`'));
            string typeArgs = string.Join(", ", type.GetGenericArguments().Select(t => GetCleanTypeName(t)));
            return $"{genericName}<{typeArgs}>";
        }

        public static string GetAccessModifier(MemberInfo member)
        {
            if (member is FieldInfo f) { if (f.IsPublic) return "public"; if (f.IsPrivate) return "private"; if (f.IsFamily) return "protected"; if (f.IsAssembly) return "internal"; }
            else if (member is MethodInfo m) { if (m.IsPublic) return "public"; if (m.IsPrivate) return "private"; if (m.IsFamily) return "protected"; if (m.IsAssembly) return "internal"; }
            else if (member is ConstructorInfo c) { if (c.IsPublic) return "public"; if (c.IsPrivate) return "private"; if (c.IsFamily) return "protected"; if (c.IsAssembly) return "internal"; }
            return "private";
        }

        public static List<string> GetFieldTraits(FieldInfo f)
        {
            List<string> traits = new List<string>();
            if (f.IsLiteral) traits.Add("const");
            else { if (f.IsStatic) traits.Add("static"); if (f.IsInitOnly) traits.Add("readonly"); }
            return traits;
        }

        public static List<string> GetMethodTraits(MethodInfo m)
        {
            List<string> traits = new List<string>();
            if (m.IsStatic) traits.Add("static");
            if (m.IsAbstract) traits.Add("abstract");
            else if (m.GetBaseDefinition() != m) traits.Add("override");
            else if (m.IsVirtual && !m.IsFinal) traits.Add("virtual");
            if (m.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null) traits.Add("async");
            return traits;
        }

        public static string GetMethodSignature(MethodInfo m)
        {
            string name = m.Name;
            if (m.IsGenericMethod)
            {
                Type[] genArgs = m.GetGenericArguments();
                name += $"<{string.Join(", ", genArgs.Select(a => a.Name))}>";
            }
            return name;
        }

        public static string GetParameterModifier(ParameterInfo p)
        {
            if (p.IsOut) return "out";
            if (p.ParameterType.IsByRef) return "ref";
            if (p.GetCustomAttribute<ParamArrayAttribute>() != null) return "params";
            if (p.IsIn) return "in";
            return "";
        }

        /// <summary>
        /// Bổ sung Dependency vào danh sách nếu chưa tồn tại.
        /// </summary>
        public static void AddDependency(MethodNode node, string type, string targetClass, string targetMethod, string rawContext)
        {
            if (!node.MethodDependencies.Any(d => d.DependencyType == type && d.TargetClass == targetClass && d.TargetMethod == targetMethod))
            {
                node.MethodDependencies.Add(new DependencyNode
                {
                    DependencyType = type,
                    TargetClass = targetClass,
                    TargetMethod = string.IsNullOrEmpty(targetMethod) ? "Unknown" : targetMethod,
                    RawContext = rawContext
                });
            }
        }
    }
}
#endif