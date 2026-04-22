using System;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class PropertyNode
    {
        public string Type;
        public string Name;
        public bool HasGetter;
        public bool HasSetter;
    }
}