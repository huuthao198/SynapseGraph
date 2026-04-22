#if UNITY_EDITOR
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
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
                    var invocations = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

                    foreach (var inv in invocations)
                    {
                        HandleInvocation(inv, mNode, pureClassName);
                        ExtractSignals(inv, mNode);
                    }

                    var creations = methodSyntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
                    foreach (var creation in creations)
                    {
                        string targetType = creation.Type.ToString();
                        string rawContext = creation.ToString();
                        AnalyzerUtility.AddDependency(mNode, "Instantiation", targetType, "Constructor", rawContext);
                    }

                    var assignments = methodSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>();
                    foreach (var assignment in assignments)
                    {
                        ExtractMutations(assignment, mNode);
                    }
                }
            }
        }

        private bool IsMatch(BaseMethodDeclarationSyntax syntax, string signature)
        {
            if (syntax is MethodDeclarationSyntax m) return signature.StartsWith(m.Identifier.Text);
            if (syntax is ConstructorDeclarationSyntax c) return signature.StartsWith(c.Identifier.Text);
            return false;
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

        private void ExtractMutations(AssignmentExpressionSyntax assignment, MethodNode mNode)
        {
            string fieldName = "";
            if (assignment.Left is IdentifierNameSyntax id)
                fieldName = id.Identifier.Text;
            else if (assignment.Left is MemberAccessExpressionSyntax ma)
                fieldName = ma.Name.Identifier.Text;

            if (!string.IsNullOrEmpty(fieldName) && !mNode.MutatedFields.Contains(fieldName))
            {
                mNode.MutatedFields.Add(fieldName);
            }
        }

        private void ExtractSignals(InvocationExpressionSyntax inv, MethodNode mNode)
        {
            if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name is GenericNameSyntax genericName && genericName.Identifier.Text == "Fire")
                {
                    var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                    if (typeArg != null)
                    {
                        string signalName = typeArg.ToString();
                        if (!mNode.FiredSignals.Contains(signalName))
                        {
                            mNode.FiredSignals.Add(signalName);
                        }
                    }
                }
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