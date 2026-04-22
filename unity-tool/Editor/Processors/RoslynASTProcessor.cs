#if UNITY_EDITOR
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Bộ xử lý phân tích logic nội hàm dựa trên cú pháp Roslyn AST.
    /// Đã được nâng cấp để bắt mọi loại logic: Call, Event, New, Cast...
    /// </summary>
    public class RoslynASTProcessor : IClassProcessor
    {
        public void Process(Type type, string path, string rawCode, ClassNode node)
        {
            if (type.IsEnum || string.IsNullOrEmpty(rawCode)) return;

            SyntaxTree tree = CSharpSyntaxTree.ParseText(rawCode);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            string pureClassName = type.Name.Split('`')[0];
            var classDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                                .FirstOrDefault(t => t.Identifier.Text == pureClassName);

            if (classDecl == null) return;

            foreach (var mNode in node.Methods)
            {
                var methodSyntax = classDecl.DescendantNodes().OfType<BaseMethodDeclarationSyntax>()
                                            .FirstOrDefault(m => IsMatch(m, mNode.Name));

                if (methodSyntax != null && methodSyntax.Body != null)
                {
                    ParseMethodBodyLogic(methodSyntax.Body, mNode, node.Name);
                }
            }
        }

        private bool IsMatch(BaseMethodDeclarationSyntax syntax, string reflectionName)
        {
            if (syntax is MethodDeclarationSyntax m) return reflectionName.StartsWith(m.Identifier.Text);
            if (syntax is ConstructorDeclarationSyntax c) return reflectionName == c.Identifier.Text || reflectionName == "Constructor";
            return false;
        }

        private void ParseMethodBodyLogic(BlockSyntax body, MethodNode mNode, string selfName)
        {
            var invocations = body.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocations)
            {
                HandleInvocation(inv, mNode, selfName);
            }

            var creations = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var create in creations)
            {
                AnalyzerUtility.AddDependency(mNode, "Instantiation", create.Type.ToString(), "Constructor", create.ToString());
            }

            var assignments = body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
            foreach (var assign in assignments)
            {
                if (assign.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken) || assign.OperatorToken.IsKind(SyntaxKind.MinusEqualsToken))
                {
                    string type = assign.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken) ? "EventSubscribe" : "EventUnsubscribe";
                    string target = GetRootIdentifier(assign.Left);
                    AnalyzerUtility.AddDependency(mNode, type, target, assign.Left.ToString(), assign.ToString());
                }
            }

            var casts = body.DescendantNodes().OfType<CastExpressionSyntax>();
            foreach (var cast in casts)
            {
                AnalyzerUtility.AddDependency(mNode, "TypeCast", cast.Type.ToString(), "Cast", cast.ToString());
            }
        }

        private void HandleInvocation(InvocationExpressionSyntax inv, MethodNode mNode, string selfName)
        {
            string rawContext = inv.ToString();

            if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                string caller = memberAccess.Expression.ToString();
                string methodName = memberAccess.Name.ToString();

                if (caller == "Debug" || caller == "UnityEngine.Debug" || caller == "Mathf") return;

                string depType = "LogicCall";
                if (methodName == "AddListener" || methodName == "RemoveListener") depType = "UnityEventLink";
                else if (methodName == "Invoke") depType = "SignalFire";
                else if (caller.Contains("Instance") || caller.Contains("Service")) depType = "PatternAccess";

                AnalyzerUtility.AddDependency(mNode, depType, GetRootIdentifier(memberAccess.Expression), methodName, rawContext);
            }
            else if (inv.Expression is IdentifierNameSyntax identifier)
            {
                AnalyzerUtility.AddDependency(mNode, "InternalCall", selfName, identifier.Identifier.Text, rawContext);
            }
        }

        private string GetRootIdentifier(ExpressionSyntax expr)
        {
            string full = expr.ToString();
            if (full.Contains(".")) return full.Split('.')[0];
            return full;
        }
    }
}
#endif
