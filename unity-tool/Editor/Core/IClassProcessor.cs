#if UNITY_EDITOR
using System;
using GaconStudio.SynapseGraph.Runtime;

namespace GaconStudio.SynapseGraph.Editor
{
    /// <summary>
    /// Interface định nghĩa khuôn mẫu cho các bộ xử lý dữ liệu trong Pipeline.
    /// </summary>
    public interface IClassProcessor
    {
        /// <summary>
        /// Xử lý và trích xuất thông tin từ một kiểu dữ liệu (Type) vào Node tương ứng.
        /// </summary>
        /// <param name="type">Kiểu dữ liệu (Class, Struct, Enum, Interface) cần phân tích.</param>
        /// <param name="path">Đường dẫn vật lý tới file mã nguồn.</param>
        /// <param name="rawCode">Nội dung text thô của file mã nguồn.</param>
        /// <param name="node">Đối tượng lưu trữ dữ liệu kiến trúc đích.</param>
        void Process(Type type, string path, string rawCode, ClassNode node);
    }
}
#endif