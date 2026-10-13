using System.Collections.Generic;
using System.Linq;

namespace DwgToDxf.Printing
{
    /// <summary>Sắp xếp thứ tự in các khung.</summary>
    public static class FrameOrder
    {
        /// <summary>
        /// Theo hàng: từ trên xuống dưới, trong mỗi hàng từ trái sang phải.
        /// Khung có tâm nằm trong chiều cao của khung đầu hàng được xem là cùng hàng,
        /// nên các khung lệch nhau một chút hoặc khác cỡ vẫn được xếp đúng hàng.
        /// </summary>
        public static List<Frame> ByRows(IEnumerable<Frame> frames)
        {
            var rows = new List<List<Frame>>();
            foreach (var f in frames.OrderByDescending(f => f.MaxY).ThenBy(f => f.MinX))
            {
                var row = rows.FirstOrDefault(r => f.CenterY >= r[0].MinY && f.CenterY <= r[0].MaxY);
                if (row == null) rows.Add(row = new List<Frame>());
                row.Add(f);
            }
            return rows.SelectMany(r => r.OrderBy(f => f.MinX)).ToList();
        }

        /// <summary>Theo cột: từ trái sang phải, trong mỗi cột từ trên xuống dưới.</summary>
        public static List<Frame> ByColumns(IEnumerable<Frame> frames)
        {
            var cols = new List<List<Frame>>();
            foreach (var f in frames.OrderBy(f => f.MinX).ThenByDescending(f => f.MaxY))
            {
                var col = cols.FirstOrDefault(c => f.CenterX >= c[0].MinX && f.CenterX <= c[0].MaxX);
                if (col == null) cols.Add(col = new List<Frame>());
                col.Add(f);
            }
            return cols.SelectMany(c => c.OrderByDescending(f => f.MaxY)).ToList();
        }

        /// <summary>Khung nhỏ nhất chứa điểm (x, y), hoặc null.</summary>
        public static Frame At(IEnumerable<Frame> frames, double x, double y) =>
            frames.Where(f => f.Contains(x, y)).OrderBy(f => f.Area).FirstOrDefault();

        /// <summary>Chuyển khung tới vị trí mới (1..n), các khung khác dồn theo.</summary>
        public static void MoveTo(List<Frame> order, Frame frame, int newNumber)
        {
            order.Remove(frame);
            var index = System.Math.Max(0, System.Math.Min(order.Count, newNumber - 1));
            order.Insert(index, frame);
        }
    }
}
