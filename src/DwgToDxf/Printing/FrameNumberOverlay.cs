using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace DwgToDxf.Printing
{
    /// <summary>
    /// Số thứ tự ảo vẽ đè lên các khung bằng TransientManager.
    /// Chỉ hiển thị trên màn hình, không ghi gì vào bản vẽ; mất đi khi lệnh kết thúc.
    /// </summary>
    public sealed class FrameNumberOverlay : IDisposable
    {
        private const short NumberColor = 1;   // đỏ
        private const short OutlineColor = 4;  // xanh lơ: khung đã nhận diện
        private const short PendingColor = 8;  // xám: khung chưa có số (lúc đang bấm chọn)

        private readonly Database _db;
        private readonly List<Drawable> _drawn = new List<Drawable>();

        public FrameNumberOverlay(Database db)
        {
            _db = db;
        }

        /// <summary>Vẽ lại: viền mọi khung, số thứ tự cho các khung trong <paramref name="numbered"/>.</summary>
        public void Show(IList<Frame> all, IList<Frame> numbered)
        {
            Clear();
            foreach (var f in all)
            {
                var index = numbered.IndexOf(f);
                Add(Outline(f, index >= 0 ? OutlineColor : PendingColor));
                if (index >= 0) Add(Number(f, index + 1));
            }
        }

        public void Clear()
        {
            if (_drawn.Count == 0) return;
            var tm = TransientManager.CurrentTransientManager;
            foreach (var d in _drawn)
            {
                tm.EraseTransient(d, new IntegerCollection());
                d.Dispose();
            }
            _drawn.Clear();
        }

        public void Dispose() => Clear();

        private void Add(Entity ent)
        {
            TransientManager.CurrentTransientManager.AddTransient(
                ent, TransientDrawingMode.DirectTopmost, 128, new IntegerCollection());
            _drawn.Add(ent);
        }

        private Entity Outline(Frame f, short color)
        {
            var pl = new Polyline(4);
            pl.SetDatabaseDefaults(_db);
            pl.AddVertexAt(0, new Point2d(f.MinX, f.MinY), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(f.MaxX, f.MinY), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(f.MaxX, f.MaxY), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(f.MinX, f.MaxY), 0, 0, 0);
            pl.Closed = true;
            pl.ColorIndex = color;
            return pl;
        }

        private Entity Number(Frame f, int number)
        {
            var center = new Point3d(f.CenterX, f.CenterY, 0);
            var text = new DBText();
            text.SetDatabaseDefaults(_db);
            text.TextString = number.ToString();
            text.Height = Math.Min(f.Width, f.Height) * 0.3;
            text.ColorIndex = NumberColor;
            text.Position = center;
            text.HorizontalMode = TextHorizontalMode.TextCenter;
            text.VerticalMode = TextVerticalMode.TextVerticalMid;
            text.AlignmentPoint = center;
            text.AdjustAlignment(_db);
            return text;
        }
    }
}
