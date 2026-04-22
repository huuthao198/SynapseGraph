using System;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class DependencyNode
    {
        public string DependencyType;
        public string TargetClass;
        public string TargetMethod;
        public string RawContext;
    }
}