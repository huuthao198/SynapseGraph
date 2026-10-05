#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Cung cấp các hàm tiện ích tái sử dụng trong quá trình phân tích Reflection và AST.
    /// 
    /// [v2 - FIX & ENHANCE]
    /// - FIX BUG: GetMethodTraits không còn gán "abstract"/"virtual" cho interface method.
    /// - NEW: GetFullBaseChain, GetAttributeNames, IsUnityClass.
    /// - ENHANCE: GetCleanTypeName hỗ trợ Array + Nested type.
    /// </summary>
    public static class AnalyzerUtility
    {
        #region TYPE INFO

        public static string GetBaseKind(Type t)
        {
            if (t == null) return "Unknown";
            if (t.IsInterface) return "Interface";
            if (t.IsEnum) return "Enum";
            if (t.IsValueType && !t.IsPrimitive) return "Struct";
            return "Class";
        }

        public static List<string> GetClassTraits(Type t)
        {
            List<string> traits = new List<string>();
            if (t == null) return traits;

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
            if (type == null) return "null";
            if (type == typeof(void)) return "void";

            if (type.IsByRef) type = type.GetElementType();

            if (type.IsArray)
                return GetCleanTypeName(type.GetElementType()) + "[]";

            if (!type.IsGenericType)
            {
                if (type.IsNested && type.DeclaringType != null)
                    return $"{GetCleanTypeName(type.DeclaringType)}.{type.Name}";
                return type.Name;
            }

            string genericName = type.Name.Substring(0, type.Name.IndexOf('`'));
            string typeArgs = string.Join(", ", type.GetGenericArguments().Select(GetCleanTypeName));
            return $"{genericName}<{typeArgs}>";
        }

        /// <summary>
        /// Trả về toàn bộ chuỗi kế thừa từ base gần nhất lên cao, bỏ qua System.Object.
        /// VD: LifeManager : PersistentSingleton → [PersistentSingleton, SingletonBehavior, MonoBehaviour, Behaviour, Component, Object]
        /// </summary>
        public static List<string> GetFullBaseChain(Type type)
        {
            List<string> chain = new List<string>();
            if (type == null) return chain;

            Type current = type.BaseType;
            while (current != null && current != typeof(object))
            {
                chain.Add(GetCleanTypeName(current));
                current = current.BaseType;
            }
            return chain;
        }

        /// <summary>
        /// Check Type có kế thừa UnityEngine.Object không (MonoBehaviour, ScriptableObject, ...).
        /// Dùng để filter implicit constructor không có nghĩa với Unity.
        /// </summary>
        public static bool IsUnityClass(Type t)
        {
            if (t == null) return false;

            Type current = t;
            while (current != null)
            {
                if (current == typeof(UnityEngine.Object) ||
                    current == typeof(MonoBehaviour) ||
                    current == typeof(ScriptableObject))
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        #endregion

        #region MEMBER INFO

        public static string GetAccessModifier(MemberInfo member)
        {
            if (member is FieldInfo f)
            {
                if (f.IsPublic) return "public";
                if (f.IsPrivate) return "private";
                if (f.IsFamilyOrAssembly) return "protected internal";
                if (f.IsFamilyAndAssembly) return "private protected";
                if (f.IsFamily) return "protected";
                if (f.IsAssembly) return "internal";
            }
            else if (member is MethodInfo m)
            {
                if (m.IsPublic) return "public";
                if (m.IsPrivate) return "private";
                if (m.IsFamilyOrAssembly) return "protected internal";
                if (m.IsFamilyAndAssembly) return "private protected";
                if (m.IsFamily) return "protected";
                if (m.IsAssembly) return "internal";
            }
            else if (member is ConstructorInfo c)
            {
                if (c.IsPublic) return "public";
                if (c.IsPrivate) return "private";
                if (c.IsFamilyOrAssembly) return "protected internal";
                if (c.IsFamily) return "protected";
                if (c.IsAssembly) return "internal";
            }
            return "private";
        }

        public static List<string> GetFieldTraits(FieldInfo f)
        {
            List<string> traits = new List<string>();
            if (f == null) return traits;

            if (f.IsLiteral) traits.Add("const");
            else
            {
                if (f.IsStatic) traits.Add("static");
                if (f.IsInitOnly) traits.Add("readonly");
            }
            return traits;
        }

        /// <summary>
        /// Trích xuất modifier của method.
        /// [FIX] Interface method KHÔNG thêm "abstract"/"virtual" — đúng ngữ nghĩa C#.
        /// [FIX] Detect sealed override.
        /// </summary>
        public static List<string> GetMethodTraits(MethodInfo m)
        {
            List<string> traits = new List<string>();
            if (m == null) return traits;

            bool isInterfaceMethod = m.DeclaringType != null && m.DeclaringType.IsInterface;

            if (m.IsStatic) traits.Add("static");

            if (!isInterfaceMethod)
            {
                if (m.IsAbstract)
                {
                    traits.Add("abstract");
                }
                else if (m.GetBaseDefinition() != m)
                {
                    traits.Add("override");
                    if (m.IsFinal) traits.Add("sealed");
                }
                else if (m.IsVirtual && !m.IsFinal)
                {
                    traits.Add("virtual");
                }
            }

            if (m.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null)
                traits.Add("async");

            return traits;
        }

        public static string GetMethodSignature(MethodInfo m)
        {
            if (m == null) return string.Empty;

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
            if (p == null) return string.Empty;

            if (p.IsOut) return "out";
            if (p.ParameterType.IsByRef) return "ref";
            if (p.GetCustomAttribute<ParamArrayAttribute>() != null) return "params";
            if (p.IsIn) return "in";
            return string.Empty;
        }

        #endregion

        #region ATTRIBUTE INFO

        /// <summary>
        /// Trả về danh sách tên attribute (đã strip suffix "Attribute") trên member/type.
        /// VD: [SerializeField] → "SerializeField", [Inject] → "Inject".
        /// </summary>
        public static List<string> GetAttributeNames(MemberInfo member)
        {
            List<string> result = new List<string>();
            if (member == null) return result;

            try
            {
                foreach (var attr in member.GetCustomAttributesData())
                {
                    string name = attr.AttributeType.Name;
                    if (name.EndsWith("Attribute"))
                        name = name.Substring(0, name.Length - "Attribute".Length);
                    result.Add(name);
                }
            }
            catch
            {
                // Dynamic / emit assemblies có thể throw — bỏ qua an toàn
            }
            return result;
        }

        #endregion

        #region DEPENDENCY

        public static void AddDependency(MethodNode node, string type, string targetClass, string targetMethod, string rawContext)
        {
            if (node == null) return;

            if (!node.MethodDependencies.Any(d =>
                d.DependencyType == type &&
                d.TargetClass == targetClass &&
                d.TargetMethod == targetMethod))
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

        #endregion
    }
}
#endif
