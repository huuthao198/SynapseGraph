using System;
using System.Collections.Generic;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class PropertyNode
    {
        public string Access = "public";
        public List<string> Modifiers = new List<string>();
        public List<string> Attributes = new List<string>();
        public string Type;
        public string Name;
        public bool HasGetter;
        public bool HasSetter;
    }
}
