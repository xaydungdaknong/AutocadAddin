using System;

namespace DwgToDxf
{
    /// <summary>Hàm dùng chung cho cả bản AutoCAD 2023 (.NET 4.8) và AutoCAD 2010 (.NET 3.5).</summary>
    internal static class Compat
    {
        /// <summary>Như string.IsNullOrWhiteSpace (không có trong .NET 3.5).</summary>
        public static bool IsBlank(string s) => s == null || s.Trim().Length == 0;
    }
}

#if ACAD2010
// .NET 3.5 không có ValueTuple; trình biên dịch C# chỉ cần các kiểu này tồn tại để dùng cú pháp (a, b).
namespace System
{
    internal struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string[] transformNames)
        {
            TransformNames = transformNames;
        }

        public string[] TransformNames { get; }
    }
}
#endif
