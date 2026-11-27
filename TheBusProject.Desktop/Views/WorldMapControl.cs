using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TheBusProject.Infrastructure;
using TheBusProject.Presentation;

namespace TheBusProject.Desktop.Views
{
    /// <summary>Draws the Web Mercator map (the level 3 tiles of the game data) with the carrier's stations, routes and coach. Drag to pan, wheel to zoom, click an coach to select it.</summary>
    public class WorldMapControl : Control
    {

        private WorldMapViewModel _vm;
        private double _scale;       // pixels per world width
        private double _cx = 0.5, _cy = 0.4;
        private bool _fitted;
        private Point _down, _last;
        private bool _dragging, _moved, _leftDrag;
        private string _hoverKey;

        private static readonly IBrush Sea = new SolidColorBrush(Color.Parse("#b5d0d0"));
        // the player's routes: a deep crimson that reads against the blue of the interstates they run on, with a
        // pale casing under it so the line stays visible where it lies along a road
        // A route runs down the interstate, and the point of drawing it on a map of the interstates is to see that it
        // does. The casing used to be a near-solid white band wider than the road itself, which rubbed the road out
        // along the whole length of the route and left it looking like a line drawn across open country. It is now a
        // soft halo the road reads through, and the route itself is drawn a shade narrower than an interstate so the
        // blue shows at both its edges.
        private static readonly Pen RouteCasing = new Pen(new SolidColorBrush(Color.FromArgb(105, 255, 252, 245)), 3.6);
        private static readonly Pen RoutePen = new Pen(new SolidColorBrush(Color.FromArgb(255, 170, 20, 55)), 1.5);
        private static readonly IBrush StationBrush = new SolidColorBrush(Color.Parse("#1d3f8a"));
        private static readonly IBrush HubBrush = new SolidColorBrush(Color.Parse("#c01818"));
        private static readonly IBrush FlyBrush = new SolidColorBrush(Color.Parse("#e8a800"));
        private static readonly IBrush GroundBrush = new SolidColorBrush(Color.Parse("#555555"));
        private static readonly Pen Outline = new Pen(Brushes.Black, 1);
        private static readonly Pen SelectPen = new Pen(Brushes.Red, 2);
        private static readonly Typeface Face = new Typeface("sans-serif");

        private static readonly IBrush FlowBrush = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        private static readonly Pen PathPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 200, 30, 30)), 2, new DashStyle(new double[] { 5, 4 }, 0));

        // about 30 frames a second while coach are moving
        private readonly DispatcherTimer _frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        // the route lines in screen points, made again only when the view or the routes change
        private List<StreamGeometry> _routeGeo;
        private readonly Dictionary<StreamGeometry, Pen> _routePen = new Dictionary<StreamGeometry, Pen>();
        private static readonly Dictionary<string, Pen> RivalPens = new Dictionary<string, Pen>();
        private static readonly Dictionary<string, IBrush> RivalBrushes = new Dictionary<string, IBrush>();
        private static readonly IBrush RivalPlaceBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
        private static readonly Pen RivalPlaceOutline = new Pen(new SolidColorBrush(Color.Parse("#555555")), 1);

        private static IBrush RivalBrush(string hex)
        {
            lock (RivalBrushes)
            {
                IBrush b;
                if (!RivalBrushes.TryGetValue(hex, out b)) RivalBrushes[hex] = b = new SolidColorBrush(Color.Parse(hex));
                return b;
            }
        }

        private static Pen PenFor(MapLine line)
        {
            if (line.Color == null) return RoutePen;
            string key = line.Color;
            lock (RivalPens)
            {
                Pen p;
                if (!RivalPens.TryGetValue(key, out p))
                {
                    var c = Color.Parse(line.Color);
                    var brush = new SolidColorBrush(Color.FromArgb(160, c.R, c.G, c.B));
                    RivalPens[key] = p = new Pen(brush, 1.2);
                }
                return p;
            }
        }
        private object _routeLines;
        private (double, double, double, double, double) _routeView;

        public WorldMapControl()
        {
            ClipToBounds = true;
            Focusable = true;
            DataContextChanged += (s, e) => Attach();
            _frames.Tick += (s, e) =>
            {
                if (_vm != null && _vm.Animate && (_vm.ShowCoach || _vm.ShowRoutes) && _vm.Clock.Running && IsVisible) InvalidateVisual();
            };
            AttachedToVisualTree += (s, e) => _frames.Start();
            DetachedFromVisualTree += (s, e) => _frames.Stop();
        }

        private void Attach()
        {
            if (_vm != null)
            {
                _vm.Changed -= OnChanged;
            }

            _vm = DataContext as WorldMapViewModel;
            if (_vm != null)
            {
                _vm.Changed += OnChanged;
                _vm.Reload();
                _fitted = false;
            }
        }

        private void OnChanged()
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
        }

        private double MinScale => Math.Max(Bounds.Width, Bounds.Height);
        private double MaxScale => Math.Max(MinScale, 2048 * 6);

        private Point ToScreen(double u, double v)
        {
            return new Point(Bounds.Width / 2 + (u - _cx) * _scale, Bounds.Height / 2 + (v - _cy) * _scale);
        }

        /// <summary>Zooms around the middle of the view (buttons and keys), or around a point.</summary>
        public void ZoomBy(double factor, Point? around = null)
        {
            Point p = around ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
            double u = _cx + (p.X - Bounds.Width / 2) / _scale, v = _cy + (p.Y - Bounds.Height / 2) / _scale;
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
            _cx = u - (p.X - Bounds.Width / 2) / _scale;
            _cy = v - (p.Y - Bounds.Height / 2) / _scale;
            Clamp();
            InvalidateVisual();
        }

        public void PanBy(double dx, double dy)
        {
            StopFollowing();
            _cx += dx / _scale;
            _cy += dy / _scale;
            Clamp();
            InvalidateVisual();
        }

        /// <summary>Back to the view that fits the carrier's network.</summary>
        public void FitView()
        {
            Fit();
            InvalidateVisual();
        }

        private DateTime _frameNow;
        private bool _wasFollowing;

        // following: the selected coach in the middle, where this frame draws it (zoomed in a little when following
        // starts on a wide view)
        private void FollowSelected()
        {
            bool following = _vm != null && _vm.Follow && _vm.Selected != null;
            if (following)
            {
                var a = _vm.Selected;
                double u = a.U, v = a.V, heading;
                if (_vm.Animate) a.At(_frameNow, out u, out v, out heading);
                if (!_wasFollowing && _scale < MinScale * 3) _scale = MinScale * 3;
                _cx = u;
                _cy = v;
                Clamp();
            }
            _wasFollowing = following;
        }

        // moving the map by hand stops following
        private void StopFollowing()
        {
            if (_vm != null && _vm.Follow) _vm.Follow = false;
        }

        /// <summary>Centres the view on the selected coach and zooms in a little if the view is wide.</summary>
        public void CenterOnSelected()
        {
            if (_vm?.Selected == null) return;
            _cx = _vm.Selected.ScreenU;
            _cy = _vm.Selected.ScreenV;
            if (_scale < MinScale * 3) _scale = MinScale * 3;
            Clamp();
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double step = 60;
            switch (e.Key)
            {
                case Key.Left: PanBy(-step, 0); break;
                case Key.Right: PanBy(step, 0); break;
                case Key.Up: PanBy(0, -step); break;
                case Key.Down: PanBy(0, step); break;
                case Key.OemPlus: case Key.Add: ZoomBy(1.25); break;
                case Key.OemMinus: case Key.Subtract: ZoomBy(0.8); break;
                case Key.Home: FitView(); break;
                default: return;
            }
            e.Handled = true;
        }

        private void Clamp()
        {
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale));
            double halfW = Bounds.Width / 2 / _scale, halfH = Bounds.Height / 2 / _scale;
            _cx = Math.Max(halfW, Math.Min(1 - halfW, _cx));
            _cy = Math.Max(halfH, Math.Min(1 - halfH, _cy));
        }

        private void Fit()
        {
            _fitted = true;
            _scale = MinScale * 1.0;
            if (_vm != null && _vm.Places.Count > 0)
            {
                _cx = _vm.Places.Average(p => p.U);
                _cy = _vm.Places.Average(p => p.V);
                // zoom in on the network when it is compact
                double spanU = _vm.Places.Max(p => p.U) - _vm.Places.Min(p => p.U);
                double spanV = _vm.Places.Max(p => p.V) - _vm.Places.Min(p => p.V);
                double need = Math.Min(Bounds.Width / Math.Max(0.05, spanU * 1.5), Bounds.Height / Math.Max(0.05, spanV * 1.5));
                _scale = Math.Max(_scale, Math.Min(need, MaxScale / 2));
            }
            else
            {
                // nothing of your own on the map yet: the country the game is played in, rather than the whole globe
                double[] nw = WorldMapViewModel.Project(49.6, -125.5);
                double[] se = WorldMapViewModel.Project(24.0, -66.5);
                _cx = (nw[0] + se[0]) / 2;
                _cy = (nw[1] + se[1]) / 2;
                double need = Math.Min(Bounds.Width / Math.Max(0.05, (se[0] - nw[0]) * 1.1),
                                       Bounds.Height / Math.Max(0.05, (se[1] - nw[1]) * 1.1));
                _scale = Math.Max(_scale, Math.Min(need, MaxScale / 2));
            }

            Clamp();
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            ctx.DrawRectangle(Sea, null, new Rect(Bounds.Size));
            if (Bounds.Width < 10 || Bounds.Height < 10)
            {
                return;
            }

            if (!_fitted)
            {
                Fit();
            }
            else
            {
                Clamp();
            }

            // the game time this frame shows (worked out once: the clock moves on each time it is asked)
            _frameNow = _vm == null ? default(DateTime) : _vm.Animate ? _vm.Clock.Now(WorldMapViewModel.WallSeconds) : _vm.Clock.Latest;
            FollowSelected();

            // the land and the roads, drawn from outlines: world units (0..1) through one transform to the screen
            VectorMap.Load();
            if (!VectorMap.Ready)
            {
                VectorMap.DrawMissing(ctx);
            }
            else
            {
                var toScreen = new Matrix(
                    _scale, 0,
                    0, _scale,
                    Bounds.Width / 2 - _cx * _scale, Bounds.Height / 2 - _cy * _scale);
                VectorMap.Draw(ctx, toScreen, _scale);
            }

            if (_vm == null)
            {
                return;
            }

            if (_vm.ShowRoutes)
            {
                var lines = _vm.Lines;
                var view = (_scale, _cx, _cy, Bounds.Width, Bounds.Height);
                if (_routeGeo == null || !ReferenceEquals(_routeLines, lines) || _routeView != view)
                {
                    _routeGeo = new List<StreamGeometry>();
                    _routePen.Clear();
                    _routeLines = lines;
                    _routeView = view;
                    foreach (var line in lines.ToList())
                    {
                        foreach (var piece in line.Pieces)
                        {
                            if (piece.Count < 2) continue;
                            var geo = new StreamGeometry();
                            using (var g = geo.Open())
                            {
                                g.BeginFigure(ToScreen(piece[0][0], piece[0][1]), false);
                                for (int i = 1; i < piece.Count; i++) g.LineTo(ToScreen(piece[i][0], piece[i][1]));
                                g.EndFigure(false);
                            }

                            _routeGeo.Add(geo);
                            _routePen[geo] = PenFor(line);
                        }
                    }
                }

                // the rivals' routes first, the player's on top
                foreach (bool rivals in new[] {true, false})
                {
                    foreach (var geo in _routeGeo)
                    {
                        Pen pen;
                        if (!_routePen.TryGetValue(geo, out pen)) continue;
                        bool isRival = pen != RoutePen;
                        if (isRival != rivals) continue;
                        if (pen == RoutePen) ctx.DrawGeometry(null, RouteCasing, geo);
                        ctx.DrawGeometry(null, pen, geo);
                    }
                }
            }

            bool animate = _vm.Animate;
            DateTime now = _frameNow;

            // dashes flowing along each route that has an coach on the road, the way it runs
            if (animate && _vm.ShowRoutes) DrawFlow(ctx, now);

            if (_vm.ShowStations)
            {
                foreach (var pl in _vm.Places.ToList().OrderByDescending(x => x.IsRivalOnly))
                {
                    Point p = ToScreen(pl.U, pl.V);
                    if (p.X < -20 || p.Y < -20 || p.X > Bounds.Width + 20 || p.Y > Bounds.Height + 20) continue;
                    if (pl.IsRivalOnly)
                    {
                        ctx.DrawEllipse(RivalPlaceBrush, RivalPlaceOutline, p, 2.4, 2.4);
                        if (_scale > MinScale * 3)
                        {
                            var rt = new FormattedText(pl.Label ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 10, Brushes.DimGray);
                            ctx.DrawText(rt, new Point(p.X + 4, p.Y - 6));
                        }
                        continue;
                    }

                    double r = pl.IsHub ? 5 : 3.2;
                    ctx.DrawEllipse(pl.IsHub ? HubBrush : StationBrush, Outline, p, r, r);
                    if (pl.IsHub || _scale > MinScale * 1.8)
                    {
                        var t = new FormattedText(pl.Label ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Brushes.Black);
                        ctx.DrawText(t, new Point(p.X + r + 2, p.Y - 7));
                    }
                }
            }

            if (_vm.ShowCoach)
            {
                var coach = _vm.Coach.ToList();

                // the selected coach's whole trip
                var sel = _vm.Selected;
                if (sel?.Path != null && sel.Fraction(now) > 0 && sel.Fraction(now) < 1)
                {
                    foreach (var piece in sel.Path.Pieces)
                    {
                        if (piece.Count < 2) continue;
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(ToScreen(piece[0][0], piece[0][1]), false);
                            for (int i = 1; i < piece.Count; i++) g.LineTo(ToScreen(piece[i][0], piece[i][1]));
                            g.EndFigure(false);
                        }

                        ctx.DrawGeometry(null, PathPen, geo);
                    }
                }

                // the rivals' coach first, the player's on top
                foreach (var a in coach.Where(x => !x.IsMine).Concat(coach.Where(x => x.IsMine)))
                {
                    double u = a.U, v = a.V, heading = a.Heading;
                    bool flying = a.InTrip;
                    if (animate) flying = a.At(now, out u, out v, out heading);

                    Point p = ToScreen(u, v);
                    if (p.X < -40 || p.Y < -40 || p.X > Bounds.Width + 40 || p.Y > Bounds.Height + 40) continue;
                    a.ScreenU = u;
                    a.ScreenV = v;
                    if (flying)
                    {
                        if (animate && a.IsMine) DrawTrail(ctx, a, now);
                        double size = a.IsMine ? 1 : 0.8;
                        double rad = heading * Math.PI / 180, s = Math.Sin(rad) * size, c = Math.Cos(rad) * size;
                        Point R(double x, double y) => new Point(p.X + x * c - y * s, p.Y + x * s + y * c);
                        var plane = new StreamGeometry();
                        using (var g = plane.Open())
                        {
                            g.BeginFigure(R(0, -9), true);
                            g.LineTo(R(6, 6));
                            g.LineTo(R(0, 3));
                            g.LineTo(R(-6, 6));
                            g.EndFigure(true);
                        }

                        ctx.DrawGeometry(a.IsMine ? FlyBrush : RivalBrush(a.Color), Outline, plane);
                    }
                    else if (a.IsMine)
                    {
                        ctx.DrawEllipse(GroundBrush, Outline, p, 4, 4);
                    }
                    else
                    {
                        ctx.DrawEllipse(RivalBrush(a.Color), Outline, p, 2.5, 2.5);
                    }

                    if (_vm.Selected == a)
                    {
                        ctx.DrawEllipse(null, SelectPen, p, 12, 12);
                    }
                }
            }
        }

        // the trips' paths in screen points, made again only when the view changes
        private readonly Dictionary<(double, double, double, double), List<StreamGeometry>> _flowGeo = new Dictionary<(double, double, double, double), List<StreamGeometry>>();
        private (double, double, double, double, double) _flowView;

        private const double FlowSpeed = 14; // screen points a second

        private void DrawFlow(DrawingContext ctx, DateTime now)
        {
            var view = (_scale, _cx, _cy, Bounds.Width, Bounds.Height);
            if (_flowView != view || _flowGeo.Count > 2000)
            {
                _flowGeo.Clear();
                _flowView = view;
            }

            // the dash pattern moves along the line: a negative offset carries it from the start towards the end
            // (pen units are the pen's thickness)
            const double thickness = 2.2;
            double offset = -((WorldMapViewModel.WallSeconds * FlowSpeed / thickness) % 4.8); // one dash and gap: 1.6 + 3.2
            var pen = new Pen(FlowBrush, thickness, new DashStyle(new double[] { 1.6, 3.2 }, offset), PenLineCap.Round);
            var drawn = new HashSet<(double, double, double, double)>();
            foreach (var a in _vm.Coach.ToList())
            {
                if (!a.HasTrip || !a.IsMine) continue;
                double fr = a.Fraction(now);
                if (fr <= 0 || fr >= 1) continue;
                var key = (a.Lat1, a.Lon1, a.Lat2, a.Lon2);
                if (!drawn.Add(key)) continue; // one flow a direction, however many coach fly it
                List<StreamGeometry> pieces;
                if (!_flowGeo.TryGetValue(key, out pieces))
                {
                    pieces = new List<StreamGeometry>();
                    foreach (var piece in a.Path.Pieces)
                    {
                        if (piece.Count < 2) continue;
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(ToScreen(piece[0][0], piece[0][1]), false);
                            for (int i = 1; i < piece.Count; i++) g.LineTo(ToScreen(piece[i][0], piece[i][1]));
                            g.EndFigure(false);
                        }

                        pieces.Add(geo);
                    }

                    _flowGeo[key] = pieces;
                }

                foreach (var geo in pieces) ctx.DrawGeometry(null, pen, geo);
            }
        }

        // a short fading line behind a flying coach: a few segments, lighter towards the tail
        private void DrawTrail(DrawingContext ctx, MapCoach a, DateTime now)
        {
            // about 40 screen points long, whatever the zoom and the trip's length
            double tripPx = 0;
            var ends = new[] { WorldMapViewModel.Project(a.Lat1, a.Lon1), WorldMapViewModel.Project(a.Lat2, a.Lon2) };
            double du = Math.Abs(ends[1][0] - ends[0][0]);
            if (du > 0.5) du = 1 - du;
            tripPx = Math.Sqrt(du * du + Math.Pow(ends[1][1] - ends[0][1], 2)) * _scale;
            if (tripPx < 1) return;
            double back = Math.Min(0.5, 40 / tripPx);
            var points = a.Trail(now, back, 6);
            for (int i = 1; i < points.Count; i++)
            {
                if (Math.Abs(points[i][0] - points[i - 1][0]) > 0.5) continue; // across the edge of the picture
                byte alpha = (byte) (30 + 170 * i / points.Count);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 232, 168, 0)), 1 + 2.0 * i / points.Count);
                ctx.DrawLine(pen, ToScreen(points[i - 1][0], points[i - 1][1]), ToScreen(points[i][0], points[i][1]));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed && !props.IsRightButtonPressed) return;
            Focus();
            _leftDrag = props.IsLeftButtonPressed;
            if (e.ClickCount == 2 && _leftDrag)
            {
                ZoomBy(2, e.GetPosition(this));
                e.Handled = true;
                return;
            }
            _dragging = true;
            _moved = false;
            _down = _last = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging)
            {
                UpdateTip(e.GetPosition(this));
                return;
            }

            if (_hoverKey != null) { _hoverKey = null; ToolTip.SetTip(this, null); }
            Point p = e.GetPosition(this);
            if (Math.Abs(p.X - _down.X) + Math.Abs(p.Y - _down.Y) > 4) _moved = true;
            if (_moved)
            {
                StopFollowing();
                _cx -= (p.X - _last.X) / _scale;
                _cy -= (p.Y - _last.Y) / _scale;
                Clamp();
                InvalidateVisual();
            }

            _last = p;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            if (!_moved && _vm != null && _leftDrag)
            {
                Point p = e.GetPosition(this);
                var hit = _vm.Coach.ToList()
                             .Select(a => new { a, d = Distance(ToScreen(a.ScreenU, a.ScreenV), p) })
                             .Where(x => x.d < 14)
                             .OrderBy(x => x.d)
                             .FirstOrDefault();
                _vm.Selected = hit?.a;
            }
        }

        /// <summary>Shows the tooltip of the coach or station under the pointer (coach first), and a hand cursor over them.</summary>
        private void UpdateTip(Point p)
        {
            string key = null, text = null;
            if (_vm != null)
            {
                if (_vm.ShowCoach)
                {
                    var plane = _vm.Coach.ToList().Select(a => new { a, d = Distance(ToScreen(a.ScreenU, a.ScreenV), p) }).Where(x => x.d < 12).OrderBy(x => x.d).FirstOrDefault();
                    if (plane != null)
                    {
                        key = "a:" + plane.a.Coach.Name + plane.a.Info;
                        if (key != _hoverKey) text = _vm.CoachTip(plane.a);
                    }
                }

                if (key == null && _vm.ShowStations)
                {
                    var place = _vm.Places.ToList().Select(a => new { a, d = Distance(ToScreen(a.U, a.V), p) }).Where(x => x.d < (x.a.IsHub ? 9 : 7)).OrderBy(x => x.d).FirstOrDefault();
                    if (place != null)
                    {
                        key = "p:" + place.a.Label + _vm.Caption;
                        if (key != _hoverKey) text = _vm.StationTip(place.a);
                    }
                }
            }

            if (key == _hoverKey) return;
            _hoverKey = key;
            Cursor = key != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            ToolTip.SetShowDelay(this, 150);
            ToolTip.SetTip(this, key == null ? null : text);
        }

        private static double Distance(Point a, Point b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _hoverKey = null;
            ToolTip.SetTip(this, null);
            Point p = e.GetPosition(this);
            double u = _cx + (p.X - Bounds.Width / 2) / _scale, v = _cy + (p.Y - Bounds.Height / 2) / _scale;
            _scale *= e.Delta.Y > 0 ? 1.25 : 0.8;
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale));
            _cx = u - (p.X - Bounds.Width / 2) / _scale;
            _cy = v - (p.Y - Bounds.Height / 2) / _scale;
            Clamp();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
        }
    }
}
