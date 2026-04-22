using System;
using System.Collections.Generic;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class MethodNode
    {
        public string Access;
        public List<string> Modifiers = new List<string>();
        public string ReturnType;
        public string Name;
        public List<ParameterNode> Parameters = new List<ParameterNode>();
        public List<DependencyNode> MethodDependencies = new List<DependencyNode>();
        public string ImplementedInterface = "";
        public List<string> MutatedFields = new List<string>();
        public List<string> FiredSignals = new List<string>();
    }
}