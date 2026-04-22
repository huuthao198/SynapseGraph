using System;

namespace GaconStudio.SynapseGraph.Runtime
{
    [Serializable]
    public class ParameterNode
    {
        public string Modifier; // ref, out, in, params
        public string Type;     // Kiểu dữ liệu
        public string Name;     // Tên biến tham số
    }
}