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
        public List<ParameterNode> Parameters = new List<ParameterNode>(); // THÊM DÒNG NÀY (Bắt Parameter)
        public List<DependencyNode> MethodDependencies = new List<DependencyNode>();
    }
}