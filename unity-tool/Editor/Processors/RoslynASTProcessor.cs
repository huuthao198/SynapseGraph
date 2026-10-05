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
    /// [v3 - FIX CRITICAL]
    /// - FIX BUG 3: Không còn match nhầm method do StartsWith. So khớp exact + param types.
    /// - FIX BUG 4: Dependency được gắn ConditionalContext (if/loop/switch).
    /// - FIX BUG 5: MutatedFields chỉ chứa field thực sự của class, bỏ property/local var.
    /// - ENHANCE: ExtractSignals nhận diện cả Fire<T> và Fire(string) overload.
    /// - ENHANCE: Filter Debug/Mathf mở rộng.
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

            // Pre-compute field names để check mutation (bug 5)
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

        /// <summary>
        /// [FIX BUG 3] Match chính xác: identifier exact + parameter list khớp.
        /// Không dùng StartsWith để tránh "Preload" match nhầm "PreloadInternal".
        /// </summary>
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

        /// <summary>
        /// Chuẩn hóa tên type: "Int32" (Reflection) ↔ "int" (Roslyn) → "int".
        /// Dùng \b word boundary để tránh replace nhầm UInt32 → Uint.
        /// </summary>
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

                // Filter noise
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

        /// <summary>
        /// [FIX BUG 4] Tìm ngữ cảnh điều kiện gần nhất của node.
        /// Ưu tiên: if → loop → switch → "always".
        /// </summary>
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

                // [FIX BUG 5] Chỉ ghi nhận nếu là field thực sự của class
                if (!classFieldNames.Contains(fieldName)) continue;

                if (!mNode.MutatedFields.Contains(fieldName))
                {
                    mNode.MutatedFields.Add(fieldName);
                }
            }
        }

        private string GetAssignmentTargetName(ExpressionSyntax expr)
        {
            // "this.field = ..." → "field"
            if (expr is MemberAccessExpressionSyntax ma)
            {
                return ma.Name.Identifier.Text;
            }
            // "field = ..." → "field"
            if (expr is IdentifierNameSyntax id)
            {
                return id.Identifier.Text;
            }
            return null;
        }

        #endregion

        #region SIGNALS (ENHANCE)

        /// <summary>
        /// [ENHANCE] Nhận diện cả Fire<T>(signal) và Fire(string id, payload) và Fire(string id).
        /// Code cũ chỉ match generic Fire → miss hết overload string-based.
        /// </summary>
        private void ExtractSignals(InvocationExpressionSyntax inv, MethodNode mNode)
        {
            string methodName = null;
            string signalName = null;

            if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name is GenericNameSyntax genericName)
                {
                    methodName = genericName.Identifier.Text;
                    var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                    if (typeArg != null) signalName = typeArg.ToString();
                }
                else if (memberAccess.Name is IdentifierNameSyntax idName)
                {
                    methodName = idName.Identifier.Text;
                }
            }
            else if (inv.Expression is IdentifierNameSyntax id)
            {
                methodName = id.Identifier.Text;
            }

            if (methodName != "Fire") return;

            // Non-generic Fire → tìm string literal đầu tiên
            if (string.IsNullOrEmpty(signalName))
            {
                var literal = inv.ArgumentList.Arguments
                    .Select(a => a.Expression)
                    .OfType<LiteralExpressionSyntax>()
                    .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));

                if (literal != null)
                {
                    signalName = literal.Token.ValueText;
                }
            }

            if (!string.IsNullOrEmpty(signalName) && !mNode.FiredSignals.Contains(signalName))
            {
                mNode.FiredSignals.Add(signalName);
            }
        }

        #endregion
    }
}
#endif
