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
    /// Phân tích Logic Trace nội hàm sử dụng Microsoft Roslyn AST (Abstract Syntax Tree).
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
                string rawContext = inv.ToString();

                if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    string caller = memberAccess.Expression.ToString();
                    string methodName = memberAccess.Name.ToString();

                    if (caller.Contains("ServiceLocator"))
                    {
                        AnalyzerUtility.AddDependency(mNode, "ServiceLocator", ExtractGenericType(caller), methodName, rawContext);
                    }
                    else if (caller.EndsWith(".Instance") || caller == "Instance")
                    {
                        string className = caller.Replace(".Instance", "");
                        AnalyzerUtility.AddDependency(mNode, "Singleton", className, methodName, rawContext);
                    }
                    else if (caller == "Signal" || caller == "this" || caller.Contains("SignalHub"))
                    {
                        if (methodName == "Fire" || methodName == "Subscribe" || methodName == "Unsubscribe")
                            AnalyzerUtility.AddDependency(mNode, "SignalEvent", "SignalHub", methodName, rawContext);
                    }
                    else if (caller != "Debug" && caller != "Mathf" && caller != "UnityEngine.Debug")
                    {
                        AnalyzerUtility.AddDependency(mNode, "LogicCall", caller, methodName, rawContext);
                    }
                }
                else if (inv.Expression is IdentifierNameSyntax identifier)
                {
                    string methodName = identifier.Identifier.Text;
                    AnalyzerUtility.AddDependency(mNode, "InternalCall", selfName, methodName, rawContext);
                }
            }

            var creations = body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var create in creations)
            {
                string typeName = create.Type.ToString();
                if (!typeName.StartsWith("List") && !typeName.StartsWith("Dictionary") && !typeName.StartsWith("IEnumerable"))
                {
                    AnalyzerUtility.AddDependency(mNode, "LocalInit", typeName, "Constructor", create.ToString());
                }
            }

            var assignments = body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
            foreach (var assign in assignments)
            {
                if (assign.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken))
                {
                    AnalyzerUtility.AddDependency(mNode, "EventSubscription", assign.Left.ToString(), "Subscribe", assign.ToString());
                }
                else if (assign.OperatorToken.IsKind(SyntaxKind.MinusEqualsToken))
                {
                    AnalyzerUtility.AddDependency(mNode, "EventUnsubscription", assign.Left.ToString(), "Unsubscribe", assign.ToString());
                }
            }
        }

        private string ExtractGenericType(string caller)
        {
            int start = caller.IndexOf('<');
            int end = caller.LastIndexOf('>');
            if (start != -1 && end != -1) return caller.Substring(start + 1, end - start - 1);
            return "Unknown";
        }
    }
}
#endif