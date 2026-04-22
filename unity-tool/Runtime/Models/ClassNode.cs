using System;
using System.Collections.Generic;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class ClassNode
    {
        public string Kind;              // Class, Interface, Enum, Struct
        public List<string> Traits = new List<string>();
        public List<string> Attributes = new List<string>();
        public string Name;
        public string Namespace;
        public string FolderPath;

        public string BaseClass;
        public List<string> Usings = new List<string>();
        public List<string> Interfaces = new List<string>();
        public List<string> EnumValues = new List<string>();
        public List<FieldNode> Fields = new List<FieldNode>();
        public List<PropertyNode> Properties = new List<PropertyNode>();
        public List<MethodNode> Methods = new List<MethodNode>();
    }
}