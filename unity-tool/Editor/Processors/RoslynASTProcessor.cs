#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Phân tích AST bằng Roslyn để trích xuất dependency, mutation, signal flow.
    /// 
    /// [v4 - FIX SIGNAL FLOW]
    /// - FIX BUG: ExtractSignals nhận diện signal fire qua Constant/MemberAccess.
    /// - FIX BUG: ExtractSignals nhận diện signal fire qua Identifier (local const).
    /// - Không resolve constant value (cần SemanticModel) → giữ tên reference dạng "CoreEvents.LivesChanged".
    /// </summary>
    public class RoslynASTProcessor : IClassProcessor
    {
        public void Process(Type type, string path, string rawCode, ClassNode node)
        {
            if (type == null || type.IsEnum || string.IsNullOrEmpty(rawCode)) return;

            SyntaxTree tree;
            try
            {
                tree = CSharpSyntaxTree.ParseText(rawCode);
            }
            catch
            {
                return;
            }

            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            string pureClassName = type.Name.Split('`')[0];

            var classDecl = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.Text == pureClassName);

            if (classDecl == null) return;

            HashSet<string> classFieldNames = new HashSet<string>(node.Fields.Select(f => f.Name));

            foreach (var mNode in node.Methods)
            {
                BaseMethodDeclarationSyntax methodSyntax = FindMatchingMethod(classDecl, mNode);
                if (methodSyntax == null) continue;

                BlockSyntax body = GetMethodBody(methodSyntax);
                if (body == null) continue;

                ProcessInvocations(body, mNode, pureClassName);
                ProcessObjectCreations(body, mNode);
                ProcessMutations(body, mNode, classFieldNames);
            }
        }

        #region METHOD MATCHING (FIX BUG 3)

        private BaseMethodDeclarationSyntax FindMatchingMethod(TypeDeclarationSyntax classDecl, MethodNode mNode)
        {
            foreach (var method in classDecl.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                if (IsMatch(method, mNode)) return method;
            }
            return null;
        }

        private bool IsMatch(BaseMethodDeclarationSyntax syntax, MethodNode mNode)
        {
            string rawName = GetRawMethodName(mNode.Name);

            string identifier;
            ParameterListSyntax paramList;

            if (syntax is MethodDeclarationSyntax m)
            {
                identifier = m.Identifier.Text;
                paramList = m.ParameterList;
            }
            else if (syntax is ConstructorDeclarationSyntax c)
            {
                identifier = c.Identifier.Text;
                paramList = c.ParameterList;
            }
            else
            {
                return false;
            }

            if (identifier != rawName) return false;
            return ParametersMatch(paramList, mNode.Parameters);
        }

        private bool ParametersMatch(ParameterListSyntax syntaxParams, List<ParameterNode> refParams)
        {
            if (syntaxParams.Parameters.Count != refParams.Count) return false;

            for (int i = 0; i < refParams.Count; i++)
            {
                string synType = NormalizeTypeString(syntaxParams.Parameters[i].Type?.ToString() ?? "");
                string refType = NormalizeTypeString(refParams[i].Type);

                if (synType != refType) return false;
            }
            return true;
        }

        private static string GetRawMethodName(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return string.Empty;
            int idx = signature.IndexOf('<');
            return idx > 0 ? signature.Substring(0, idx) : signature;
        }

        private static string NormalizeTypeString(string t)
        {
            if (string.IsNullOrEmpty(t)) return string.Empty;
            t = t.Replace(" ", "");

            (string from, string to)[] map = new (string, string)[]
            {
                ("Int32", "int"), ("Int64", "long"), ("Int16", "short"),
                ("Single", "float"), ("Double", "double"),
                ("Boolean", "bool"), ("String", "string"), ("Object", "object"),
                ("Byte", "byte"), ("Char", "char")
            };

            foreach (var (from, to) in map)
            {
                t = Regex.Replace(t, $@"\b{from}\b", to);
            }
            return t;
        }

        private BlockSyntax GetMethodBody(BaseMethodDeclarationSyntax syntax)
        {
            if (syntax is MethodDeclarationSyntax m) return m.Body;
            if (syntax is ConstructorDeclarationSyntax c) return c.Body;
            return null;
        }

        #endregion

        #region INVOCATIONS

        private void ProcessInvocations(BlockSyntax body, MethodNode mNode, string selfName)
        {
            var invocations = body.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocations)
            {
                HandleInvocation(inv, mNode, selfName);
                ExtractSignals(inv, mNode);
            }
        }

        private void HandleInvocation(InvocationExpressionSyntax inv, MethodNode mNode, string selfName)
        {
            string conditionalContext = GetConditionalContext(inv);

            if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                string caller = memberAccess.Expression.ToString();
                string methodName = memberAccess.Name.ToString();

                if (IsNoiseCaller(caller)) return;

                string depType = "LogicCall";

                if (methodName == "AddListener" || methodName == "RemoveListener")
                {
                    depType = "UnityEventLink";
                }
                else if (methodName == "Invoke" && (caller.Contains("Signal") || caller.Contains("Hub")))
                {
                    depType = "SignalFire";
                }
                else if (caller.Contains("Instance") || caller.Contains("Service"))
                {
                    depType = "PatternAccess";
                }

                string rawContext = FormatContext(inv.ToString(), conditionalContext);
                AnalyzerUtility.AddDependency(mNode, depType, GetRootIdentifier(memberAccess.Expression), methodName, rawContext);
            }
            else if (inv.Expression is IdentifierNameSyntax identifier)
            {
                string rawContext = FormatContext(inv.ToString(), conditionalContext);
                AnalyzerUtility.AddDependency(mNode, "InternalCall", selfName, identifier.Identifier.Text, rawContext);
            }
        }

        private static bool IsNoiseCaller(string caller)
        {
            return caller == "Debug"
                || caller == "UnityEngine.Debug"
                || caller == "Mathf"
                || caller == "UnityEngine.Mathf";
        }

        private string GetConditionalContext(SyntaxNode node)
        {
            var ifAncestor = node.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
            if (ifAncestor != null)
            {
                string cond = ifAncestor.Condition.ToString();
                if (cond.Length > 80) cond = cond.Substring(0, 77) + "...";
                return $"if ({cond})";
            }

            var elseClause = node.Ancestors().OfType<ElseClauseSyntax>().FirstOrDefault();
            if (elseClause != null) return "else";

            var loopAncestor = node.Ancestors().FirstOrDefault(a =>
                a is ForStatementSyntax
                || a is WhileStatementSyntax
                || a is ForEachStatementSyntax
                || a is DoStatementSyntax);
            if (loopAncestor != null) return "loop";

            var switchAncestor = node.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault();
            if (switchAncestor != null) return "switch";

            return "always";
        }

        private string FormatContext(string raw, string conditional)
        {
            if (string.IsNullOrEmpty(conditional) || conditional == "always")
                return raw;
            return $"{raw} [COND: {conditional}]";
        }

        private string GetRootIdentifier(ExpressionSyntax expr)
        {
            string full = expr.ToString();
            if (full.Contains(".")) return full.Split('.')[0];
            return full;
        }

        #endregion

        #region OBJECT CREATIONS

        private void ProcessObjectCreations(BlockSyntax body, MethodNode mNode)
        {
            var creations = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var creation in creations)
            {
                string targetType = creation.Type.ToString();
                string rawContext = creation.ToString();
                string conditionalContext = GetConditionalContext(creation);

                AnalyzerUtility.AddDependency(
                    mNode,
                    "Instantiation",
                    targetType,
                    "Constructor",
                    FormatContext(rawContext, conditionalContext));
            }
        }

        #endregion

        #region MUTATIONS (FIX BUG 5)

        private void ProcessMutations(BlockSyntax body, MethodNode mNode, HashSet<string> classFieldNames)
        {
            var assignments = body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
            foreach (var assignment in assignments)
            {
                string fieldName = GetAssignmentTargetName(assignment.Left);
                if (string.IsNullOrEmpty(fieldName)) continue;

                if (!classFieldNames.Contains(fieldName)) continue;

                if (!mNode.MutatedFields.Contains(fieldName))
                {
                    mNode.MutatedFields.Add(fieldName);
                }
            }
        }

        private string GetAssignmentTargetName(ExpressionSyntax expr)
        {
            if (expr is MemberAccessExpressionSyntax ma)
            {
                return ma.Name.Identifier.Text;
            }
            if (expr is IdentifierNameSyntax id)
            {
                return id.Identifier.Text;
            }
            return null;
        }

        #endregion

        #region SIGNALS (FIX BUG — v4)

        /// <summary>
        /// [FIX] Nhận diện signal fire qua 4 pattern:
        /// 1. Fire<T>(signal)                  → dùng type argument (VD: "MySignal")
        /// 2. Fire("string_id", payload)        → dùng literal string
        /// 3. Fire(CoreEvents.LivesChanged, x)  → dùng member access text (VD: "CoreEvents.LivesChanged")
        /// 4. Fire(localConstant)               → dùng identifier name
        /// </summary>
        private void ExtractSignals(InvocationExpressionSyntax inv, MethodNode mNode)
        {
            // Bước 1: Xác định method name có phải "Fire"
            string methodName = GetInvocationMethodName(inv);
            if (methodName != "Fire") return;

            // Bước 2: Lấy argument đầu tiên
            var arguments = inv.ArgumentList.Arguments;
            if (arguments.Count == 0) return;

            ExpressionSyntax firstArg = arguments[0].Expression;

            // Bước 3: Trích xuất signal name theo pattern
            string signalName = ExtractSignalName(firstArg);
            if (string.IsNullOrEmpty(signalName)) return;

            // Bước 4: Add vào list nếu chưa có
            if (!mNode.FiredSignals.Contains(signalName))
            {
                mNode.FiredSignals.Add(signalName);
            }
        }

        /// <summary>
        /// Lấy tên method được invoke.
        /// VD: "this.Fire(...)" → "Fire"; "Signal.Fire(...)" → "Fire"; "Fire(...)" → "Fire".
        /// </summary>
        private string GetInvocationMethodName(InvocationExpressionSyntax inv)
        {
            if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name is GenericNameSyntax genericName)
                    return genericName.Identifier.Text;

                if (memberAccess.Name is IdentifierNameSyntax idName)
                    return idName.Identifier.Text;
            }
            else if (inv.Expression is IdentifierNameSyntax id)
            {
                return id.Identifier.Text;
            }
            else if (inv.Expression is GenericNameSyntax generic)
            {
                return generic.Identifier.Text;
            }

            return null;
        }

        /// <summary>
        /// Trích xuất signal name từ argument đầu tiên của Fire().
        /// 
        /// Priority:
        /// 1. String literal → value
        /// 2. Object creation (new XxxSignal) → type name
        /// 3. Member access (CoreEvents.X) → full text
        /// 4. Identifier (local const) → identifier name
        /// 5. Khác → null
        /// </summary>
        private string ExtractSignalName(ExpressionSyntax expr)
        {
            if (expr == null) return null;

            // Pattern 1: String literal → value
            if (expr is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            // [FIX M3] Pattern 2: Object creation — Fire(new XxxSignal(...))
            // Bao gồm cả object initializer: Fire(new XxxSignal { Value = 5 })
            if (expr is ObjectCreationExpressionSyntax creation)
            {
                string typeName = creation.Type.ToString();

                // Strip generic args nếu có (VD: MyGenericSignal<int> → MyGenericSignal)
                int genericIdx = typeName.IndexOf('<');
                if (genericIdx > 0)
                {
                    typeName = typeName.Substring(0, genericIdx);
                }

                return typeName.Replace(" ", string.Empty);
            }

            // Pattern 3: MemberAccess (CoreEvents.LivesChanged, AppEvents.X)
            if (expr is MemberAccessExpressionSyntax memberAccess)
            {
                string full = memberAccess.ToString();
                return full.Replace(" ", string.Empty);
            }

            // Pattern 4: Identifier (local const hoặc field)
            if (expr is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text;
            }

            // Pattern 5: Bỏ qua — không trace được
            return null;
        }

        #endregion
    }
}
#endif