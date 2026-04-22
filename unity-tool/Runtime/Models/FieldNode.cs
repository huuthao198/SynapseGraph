using System;
using System.Collections.Generic;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class FieldNode
    {
        public string Access;
        public List<string> Modifiers = new List<string>();
        public List<string> Attributes = new List<string>();
        public string Type;
        public string Name;
    }
}
