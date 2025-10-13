using Microsoft.Maui.Graphics;

namespace MyMetronom.Controls;

public sealed class ActivityHeatmap : GraphicsView
{
    public static readonly BindableProperty StartDateProperty = BindableProperty.Create(
        nameof(StartDate), typeof(DateOnly), typeof(ActivityHeatmap), DateOnly.FromDateTime(DateTime.UtcNow.Date), propertyChanged: OnPropsChanged);

    public static readonly BindableProperty EndDateProperty = BindableProperty.Create(
        nameof(EndDate), typeof(DateOnly), typeof(ActivityHeatmap), DateOnly.FromDateTime(DateTime.UtcNow.Date), propertyChanged: OnPropsChanged);

    public static readonly BindableProperty DataProperty = BindableProperty.Create(
        nameof(Data), typeof(IReadOnlyDictionary<DateOnly, TimeSpan>), typeof(ActivityHeatmap), null, propertyChanged: OnPropsChanged);

    private readonly HeatmapDrawable _drawable = new();

    public ActivityHeatmap()
    {
        Drawable = _drawable;
        HeightRequest = 120;

        var ptr = new PointerGestureRecognizer();
        ptr.PointerEntered += (_, __) => { _drawable.IsPointerInside = true; Invalidate(); };
        ptr.PointerExited += (_, __) => { _drawable.IsPointerInside = false; _drawable.ClearHover(); Invalidate(); };
        ptr.PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (p is Point point)
            {
                _drawable.UpdateHover(new PointF((float)point.X, (float)point.Y));
                Invalidate();
            }
        };
        GestureRecognizers.Add(ptr);
    }

    public DateOnly StartDate
    {
        get => (DateOnly)GetValue(StartDateProperty);
        set => SetValue(StartDateProperty, value);
    }

    public DateOnly EndDate
    {
        get => (DateOnly)GetValue(EndDateProperty);
        set => SetValue(EndDateProperty, value);
    }

    public IReadOnlyDictionary<DateOnly, TimeSpan>? Data
    {
        get => (IReadOnlyDictionary<DateOnly, TimeSpan>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    private static void OnPropsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ActivityHeatmap view)
        {
            view._drawable.Start = view.StartDate;
            view._drawable.End = view.EndDate;
            view._drawable.Data = view.Data;
            view.Invalidate();
        }
    }

    private sealed class HeatmapDrawable : IDrawable
    {
        public DateOnly Start { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-83));
        public DateOnly End { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        public IReadOnlyDictionary<DateOnly, TimeSpan>? Data { get; set; }

        public bool IsPointerInside { get; set; }
        private DateOnly? _hoverDate;
        private TimeSpan _hoverValue;
        private RectF _hoverCell;
        private PointF _hoverPoint;

        // cached layout
        private DateOnly _firstSunday;
        private int _columns;
        private int _rows;
        private float _cell;
        private float _gap;
        private float _originX;
        private float _originY;

        private static readonly Color[] Palette =
        {
            Color.FromArgb("#e8edf2"), // level 0
            Color.FromArgb("#9be9a8"), // 1
            Color.FromArgb("#40c463"), // 2
            Color.FromArgb("#30a14e"), // 3
            Color.FromArgb("#216e39"), // 4
        };

        public void ClearHover()
        {
            _hoverDate = null;
            _hoverValue = TimeSpan.Zero;
            _hoverCell = default;
        }

        public void UpdateHover(PointF p)
        {
            _hoverPoint = p;
            // Map point to cell/date using last computed layout
            if (_columns <= 0 || _cell <= 0)
            {
                ClearHover();
                return;
            }

            var relX = p.X - _originX;
            var relY = p.Y - _originY;
            if (relX < 0 || relY < 0) { ClearHover(); return; }

            int col = (int)(relX / (_cell + _gap));
            int row = (int)(relY / (_cell + _gap));

            if (col < 0 || col >= _columns || row < 0 || row >= _rows) { ClearHover(); return; }

            // within cell bounds?
            float cellX = col * (_cell + _gap);
            float cellY = row * (_cell + _gap);
            if (relX > cellX + _cell || relY > cellY + _cell) { ClearHover(); return; }

            var date = _firstSunday.AddDays(col * 7 + row);
            if (date < Start || date > End) { ClearHover(); return; }

            _hoverDate = date;
            _hoverValue = Data != null && Data.TryGetValue(date, out var ts) ? ts : TimeSpan.Zero;
            _hoverCell = new RectF(_originX + cellX, _originY + cellY, _cell, _cell);
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var totalDays = (End.ToDateTime(TimeOnly.MinValue) - Start.ToDateTime(TimeOnly.MinValue)).Days + 1;
            if (totalDays <= 0)
                return;

            // Align start to previous Sunday like GitHub
            var startDate = Start;
            var dayOfWeek = (int)startDate.DayOfWeek; // Sunday=0
            _firstSunday = startDate.AddDays(-dayOfWeek);

            var endDate = End;
            var endDayOfWeek = (int)endDate.DayOfWeek;
            var lastSaturday = endDate.AddDays(6 - endDayOfWeek);

            var daysSpan = (lastSaturday.ToDateTime(TimeOnly.MinValue) - _firstSunday.ToDateTime(TimeOnly.MinValue)).Days + 1;
            _columns = (daysSpan + 6) / 7;
            _rows = 7;

            if (_columns <= 0)
                return;

            _gap = 2f;
            _cell = Math.Min((dirtyRect.Height - _gap * (_rows - 1)) / _rows, 14f);
            float widthNeeded = _columns * _cell + (_columns - 1) * _gap;
            _originX = (float)Math.Max(0, (dirtyRect.Width - widthNeeded) / 2);
            _originY = (float)Math.Max(0, (dirtyRect.Height - (_rows * _cell + (_rows - 1) * _gap)) / 2);

            var date = _firstSunday;
            for (int c = 0; c < _columns; c++)
            {
                for (int r = 0; r < _rows; r++)
                {
                    if (date < Start || date > End)
                    {
                        date = date.AddDays(1);
                        continue;
                    }

                    int level = 0;
                    if (Data != null && Data.TryGetValue(date, out var ts))
                    {
                        var minutes = ts.TotalMinutes;
                        level = minutes <= 0 ? 0 : minutes < 10 ? 1 : minutes < 30 ? 2 : minutes < 60 ? 3 : 4;
                    }

                    var rect = new RectF(_originX + c * (_cell + _gap), _originY + r * (_cell + _gap), _cell, _cell);
                    canvas.FillColor = Palette[level];
                    canvas.FillRectangle(rect);

                    date = date.AddDays(1);
                }
            }

            // Draw tooltip if hovering a valid day
            if (IsPointerInside && _hoverDate is DateOnly hd)
            {
                var text = $"{hd:yyyy-MM-dd}  {FormatDuration(_hoverValue)}";
                var padding = 6f;
                var fontSize = 12f;
                canvas.FontSize = fontSize;
                var textSize = canvas.GetStringSize(text, Microsoft.Maui.Graphics.Font.Default, fontSize);
                var boxW = textSize.Width + padding * 2;
                var boxH = textSize.Height + padding * 2;

                // place tooltip near the cell, avoid overflow
                float tx = _hoverCell.Right + 8;
                float ty = _hoverCell.Y - boxH - 4;
                if (ty < 0) ty = _hoverCell.Bottom + 4;
                if (tx + boxW > dirtyRect.Right) tx = _hoverCell.Left - 8 - boxW;
                if (tx < 0) tx = 0;
                if (ty + boxH > dirtyRect.Bottom) ty = dirtyRect.Bottom - boxH;

                var tooltipRect = new RectF(tx, ty, boxW, boxH);
                // shadow
                canvas.FillColor = Color.FromRgba(0,0,0,0.15f);
                canvas.FillRoundedRectangle(new RectF(tooltipRect.X + 1, tooltipRect.Y + 1, tooltipRect.Width, tooltipRect.Height), 6);
                // background
                canvas.FillColor = Colors.White;
                canvas.FillRoundedRectangle(tooltipRect, 6);
                // border
                canvas.StrokeColor = Color.FromArgb("#D0D7DE");
                canvas.StrokeSize = 1;
                canvas.DrawRoundedRectangle(tooltipRect, 6);
                // text
                canvas.FontColor = Colors.Black;
                var inner = new RectF(tooltipRect.X + padding, tooltipRect.Y + padding, tooltipRect.Width - padding * 2, tooltipRect.Height - padding * 2);
                canvas.DrawString(text, inner, HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds <= 0) return "0m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m";
            return $"{ts.Seconds}s";
        }
    }
}
