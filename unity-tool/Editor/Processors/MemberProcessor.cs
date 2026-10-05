#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Trích xuất Fields, Properties, Methods, Constructors từ Reflection.
    /// 
    /// [v3 - FIX & ENHANCE]
    /// - FIX BUG: Không còn export implicit constructor của MonoBehaviour/ScriptableObject.
    /// - FIX BUG: Filter constructor chính xác bằng Roslyn parse source (cache theo path).
    /// - ENHANCE: Bổ sung attribute + access + modifier extraction cho Field/Property/Method.
    /// </summary>
    public class MemberProcessor : IClassProcessor
    {
        /// <summary>
        /// Cache danh sách explicit constructor signature có trong source code.
        /// Key: file path. Value: HashSet signature (VD: "MyClass()", "MyClass(int)").
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> m_explicitCtorCache = new Dictionary<string, HashSet<string>>();

        public void Process(Type type, string path, string rawCode, ClassNode node)
        {
            if (type == null || type.IsEnum) return;

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                               | BindingFlags.Instance | BindingFlags.Static
                               | BindingFlags.DeclaredOnly;

            ExtractFields(type, flags, node);
            ExtractProperties(type, flags, node);
            ExtractMethods(type, flags, node);
            ExtractConstructors(type, path, rawCode, node);
        }

        #region FIELDS

        private void ExtractFields(Type type, BindingFlags flags, ClassNode node)
        {
            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                // Loại bỏ backing field của auto-property
                if (field.Name.Contains("<") || field.Name.Contains("k__BackingField")) continue;

                var fNode = new FieldNode
                {
                    Access = AnalyzerUtility.GetAccessModifier(field),
                    Modifiers = AnalyzerUtility.GetFieldTraits(field),
                    Attributes = AnalyzerUtility.GetAttributeNames(field),
                    Type = AnalyzerUtility.GetCleanTypeName(field.FieldType),
                    Name = field.Name
                };
                node.Fields.Add(fNode);
            }
        }

        #endregion

        #region PROPERTIES

        private void ExtractProperties(Type type, BindingFlags flags, ClassNode node)
        {
            var properties = type.GetProperties(flags);
            foreach (var prop in properties)
            {
                var pNode = new PropertyNode
                {
                    Access = GetPropertyAccess(prop),
                    Modifiers = GetPropertyTraits(prop),
                    Attributes = AnalyzerUtility.GetAttributeNames(prop),
                    Type = AnalyzerUtility.GetCleanTypeName(prop.PropertyType),
                    Name = prop.Name,
                    HasGetter = prop.CanRead,
                    HasSetter = prop.CanWrite
                };
                node.Properties.Add(pNode);
            }
        }

        /// <summary>
        /// Lấy access modifier của property.
        /// Ưu tiên getter, nếu không có thì dùng setter.
        /// </summary>
        private static string GetPropertyAccess(PropertyInfo prop)
        {
            MethodInfo accessor = prop.GetMethod ?? prop.SetMethod;
            if (accessor == null) return "private";
            return AnalyzerUtility.GetAccessModifier(accessor);
        }

        /// <summary>
        /// Lấy modifier của property (static, abstract, virtual, override, sealed).
        /// Property không có IsVirtual trực tiếp → phải check qua accessor.
        /// </summary>
        private static List<string> GetPropertyTraits(PropertyInfo prop)
        {
            List<string> traits = new List<string>();
            if (prop == null) return traits;

            MethodInfo accessor = prop.GetMethod ?? prop.SetMethod;
            if (accessor == null) return traits;

            bool isInterfaceProperty = prop.DeclaringType != null && prop.DeclaringType.IsInterface;

            if (accessor.IsStatic) traits.Add("static");

            if (!isInterfaceProperty)
            {
                if (accessor.IsAbstract)
                {
                    traits.Add("abstract");
                }
                else if (accessor.GetBaseDefinition() != accessor)
                {
                    traits.Add("override");
                    if (accessor.IsFinal) traits.Add("sealed");
                }
                else if (accessor.IsVirtual && !accessor.IsFinal)
                {
                    traits.Add("virtual");
                }
            }

            return traits;
        }

        #endregion

        #region METHODS

        private void ExtractMethods(Type type, BindingFlags flags, ClassNode node)
        {
            var methods = type.GetMethods(flags);

            Dictionary<MethodInfo, string> interfaceMapping = BuildInterfaceMap(type);

            foreach (var m in methods)
            {
                // Bỏ property accessors (get_/set_), event add/remove, operator overload
                if (m.IsSpecialName) continue;

                // Bỏ method kế thừa từ base class (DeclaredOnly đã filter, double-check an toàn)
                if (m.DeclaringType != type) continue;

                var mNode = new MethodNode
                {
                    Access = AnalyzerUtility.GetAccessModifier(m),
                    Modifiers = AnalyzerUtility.GetMethodTraits(m),
                    Attributes = AnalyzerUtility.GetAttributeNames(m),
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

        private Dictionary<MethodInfo, string> BuildInterfaceMap(Type type)
        {
            Dictionary<MethodInfo, string> mapping = new Dictionary<MethodInfo, string>();

            foreach (Type iface in type.GetInterfaces())
            {
                try
                {
                    var map = type.GetInterfaceMap(iface);
                    for (int i = 0; i < map.TargetMethods.Length; i++)
                    {
                        if (!mapping.ContainsKey(map.TargetMethods[i]))
                        {
                            mapping.Add(map.TargetMethods[i], iface.Name);
                        }
                    }
                }
                catch
                {
                    // Một số generic interface có thể throw — bỏ qua an toàn
                }
            }
            return mapping;
        }

        #endregion

        #region CONSTRUCTORS

        private void ExtractConstructors(Type type, string path, string rawCode, ClassNode node)
        {
            // [FIX BUG 2] Unity class → KHÔNG có constructor thực sự.
            // Reflection luôn trả về implicit default constructor dù source không có.
            if (AnalyzerUtility.IsUnityClass(type)) return;

            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (constructors.Length == 0) return;

            HashSet<string> explicitSignatures = GetExplicitConstructors(path, rawCode);
            string classNamePure = type.Name.Split('`')[0];

            foreach (var ctor in constructors)
            {
                // Skip static constructor
                if (ctor.IsStatic) continue;

                ParameterInfo[] parameters = ctor.GetParameters();
                string signature = BuildSignature(classNamePure, parameters);

                // [FIX BUG 2] Chỉ export nếu constructor có trong source code.
                // Nếu không match → đây là implicit default constructor → skip.
                if (!explicitSignatures.Contains(signature)) continue;

                MethodNode cNode = new MethodNode
                {
                    Access = AnalyzerUtility.GetAccessModifier(ctor),
                    Modifiers = new List<string>(),
                    Attributes = AnalyzerUtility.GetAttributeNames(ctor),
                    ReturnType = "Constructor",
                    Name = classNamePure
                };

                foreach (var p in parameters)
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

        /// <summary>
        /// Parse Roslyn để list signature constructor thực sự có trong source.
        /// Cache theo path để tránh parse lại nhiều lần cho cùng file.
        /// </summary>
        private HashSet<string> GetExplicitConstructors(string path, string rawCode)
        {
            if (m_explicitCtorCache.TryGetValue(path, out var cached))
                return cached;

            HashSet<string> signatures = new HashSet<string>();

            try
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(rawCode);
                var ctorDecls = tree.GetRoot().DescendantNodes()
                    .OfType<ConstructorDeclarationSyntax>();

                foreach (var ctor in ctorDecls)
                {
                    string className = ctor.Identifier.Text;
                    var paramTypes = ctor.ParameterList.Parameters
                        .Select(p => NormalizeParamType(p.Type?.ToString() ?? ""))
                        .ToArray();

                    signatures.Add(BuildSignature(className, paramTypes));
                }
            }
            catch
            {
                // Parse fail → trả về set rỗng → sẽ skip hết constructor
                // (an toàn hơn là export implicit constructor sai)
            }

            m_explicitCtorCache[path] = signatures;
            return signatures;
        }

        private static string BuildSignature(string className, ParameterInfo[] parameters)
        {
            var types = parameters.Select(p => NormalizeParamType(p.ParameterType.Name)).ToArray();
            return BuildSignature(className, types);
        }

        private static string BuildSignature(string className, string[] paramTypes)
        {
            return $"{className}({string.Join(",", paramTypes)})";
        }

        /// <summary>
        /// Chuẩn hóa tên type để so sánh giữa Reflection và Roslyn.
        /// VD: "Int32" (Reflection) vs "int" (Roslyn) → "int".
        /// </summary>
        private static string NormalizeParamType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "";

            // Bỏ generic arg
            int idx = typeName.IndexOf('<');
            if (idx > 0) typeName = typeName.Substring(0, idx);

            switch (typeName.Trim())
            {
                case "Int32": return "int";
                case "Int64": return "long";
                case "Single": return "float";
                case "Double": return "double";
                case "Boolean": return "bool";
                case "String": return "string";
                case "Object": return "object";
                default: return typeName.Trim();
            }
        }

        #endregion
    }
}
#endif
