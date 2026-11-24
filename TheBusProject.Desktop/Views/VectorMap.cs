using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using TheBusProject.Infrastructure;
using TheBusProject.Presentation;

namespace TheBusProject.Desktop.Views
{
    /// <summary>
    ///     The map itself: the states and the highways, drawn from outlines rather than from a picture, so they stay
    ///     sharp at any zoom and a road can be a road rather than a few grey pixels. The data is
    ///     graphics/maps/usmap.json, in degrees; everything here is held in projected world units (0 to 1, the same
    ///     Web Mercator the stations and routes use) and drawn through one transform.
    /// </summary>
    internal static class VectorMap
    {
        private static bool _tried;
        private static List<Geometry> _land;
        private static Geometry _borders;
        private static Geometry _interstates;
        private static Geometry _usHighways;
        private static Geometry _stateHighways;

        private static readonly IBrush Land = new SolidColorBrush(Color.Parse("#f3efe4"));
        private static readonly IBrush LandEdgeBrush = new SolidColorBrush(Color.Parse("#b9b2a0"));
        private static readonly IBrush InterstateBrush = new SolidColorBrush(Color.Parse("#2f6ab0"));
        private static readonly IBrush UsBrush = new SolidColorBrush(Color.Parse("#9a8f78"));
        private static readonly IBrush StateBrush = new SolidColorBrush(Color.Parse("#c0b8a4"));
        private static readonly Typeface Face = new Typeface("sans-serif");

        /// <summary>True once the outlines are in memory and there is something to draw.</summary>
        public static bool Ready => _land != null;

        /// <summary>Reads the outlines, once per run. Quietly leaves nothing to draw if the file isn't there.</summary>
        public static void Load()
        {
            if (_tried) return;
            _tried = true;

            string file = Path.Combine(AppSettings.GetDataPath(), "graphics", "maps", "usmap.json");
            if (!File.Exists(file)) return;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(file)))
                {
                    JsonElement root = doc.RootElement;

                    var land = new List<Geometry>();
                    var borders = new StreamGeometry();
                    using (StreamGeometryContext bc = borders.Open())
                    {
                        foreach (JsonElement state in root.GetProperty("states").EnumerateArray())
                        {
                            var shape = new StreamGeometry();
                            using (StreamGeometryContext sc = shape.Open())
                            {
                                foreach (JsonElement ring in state.GetProperty("r").EnumerateArray())
                                {
                                    AddRing(sc, ring);
                                    AddRing(bc, ring);
                                }
                            }

                            land.Add(shape);
                        }
                    }

                    _land = land;
                    _borders = borders;

                    JsonElement roads = root.GetProperty("roads");
                    _interstates = Lines(roads, "interstate");
                    _usHighways = Lines(roads, "us");
                    _stateHighways = Lines(roads, "state");
                }
            }
            catch (Exception ex)
            {
                StartupLog.Write("The map outlines could not be read: " + ex.Message);
                _land = null;
            }
        }

        /// <summary>
        ///     Draws the land, the state lines and the roads under everything else. <paramref name="scale" /> is the
        ///     pixels the whole world is wide, which is what decides both the line widths and how much road is worth
        ///     showing: every interstate at any zoom, the US routes once the view is a few states wide, and the state
        ///     highways only when it is down to one.
        /// </summary>
        public static void Draw(DrawingContext ctx, Matrix worldToScreen, double scale)
        {
            Load();
            if (_land == null) return;

            double px = 1 / Math.Max(1, scale);   // one screen pixel, in world units

            using (ctx.PushTransform(worldToScreen))
            {
                foreach (Geometry g in _land) ctx.DrawGeometry(Land, null, g);
                ctx.DrawGeometry(null, new Pen(LandEdgeBrush, px), _borders);

                // The US routes stay back until the view narrows to a few states, because 4,247 of them at the
                // whole country is a web and not a map. The state highways do not: there are only 356 of them in
                // the whole file, a thousand points against the US routes' fifteen thousand, and some of them are
                // the only road a city has. California's 99 is a freeway the length of the San Joaquin Valley and
                // the way into Fresno, Bakersfield and Modesto, none of which is within forty miles of an
                // interstate; holding it back until the view was down to one state left those cities looking as
                // though no road reached them at all. They are drawn pale and thin so they read as the lesser
                // roads they mostly are.
                if (_stateHighways != null)
                    ctx.DrawGeometry(null, new Pen(StateBrush, px*(scale > 14000 ? 1.0 : 0.8)), _stateHighways);
                if (scale > 6000 && _usHighways != null)
                    ctx.DrawGeometry(null, new Pen(UsBrush, px*1.1), _usHighways);
                if (_interstates != null)
                    ctx.DrawGeometry(null, new Pen(InterstateBrush, px*(scale > 6000 ? 1.8 : 1.4)), _interstates);
            }
        }

        /// <summary>Says so on the map itself when the outlines are missing, rather than leaving a blank sea.</summary>
        public static void DrawMissing(DrawingContext ctx)
        {
            var msg = new FormattedText(
                "The map outlines are not in the game data (graphics/maps/usmap.json).",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, Brushes.Black);
            ctx.DrawText(msg, new Point(10, 10));
        }

        private static Geometry Lines(JsonElement roads, string key)
        {
            JsonElement set;
            if (!roads.TryGetProperty(key, out set)) return null;

            var geo = new StreamGeometry();
            using (StreamGeometryContext c = geo.Open())
            {
                foreach (JsonElement road in set.EnumerateArray())
                {
                    bool started = false;
                    foreach (JsonElement point in road.GetProperty("p").EnumerateArray())
                    {
                        Point p = World(point);
                        if (!started)
                        {
                            c.BeginFigure(p, false);
                            started = true;
                        }
                        else
                        {
                            c.LineTo(p);
                        }
                    }

                    if (started) c.EndFigure(false);
                }
            }

            return geo;
        }

        private static void AddRing(StreamGeometryContext c, JsonElement ring)
        {
            bool started = false;
            foreach (JsonElement point in ring.EnumerateArray())
            {
                Point p = World(point);
                if (!started)
                {
                    c.BeginFigure(p, true);
                    started = true;
                }
                else
                {
                    c.LineTo(p);
                }
            }

            if (started) c.EndFigure(true);
        }

        /// <summary>A [longitude, latitude] pair from the file, in projected world units.</summary>
        private static Point World(JsonElement pair)
        {
            double lon = pair[0].GetDouble(), lat = pair[1].GetDouble();
            double[] uv = WorldMapViewModel.Project(lat, lon);
            return new Point(uv[0], uv[1]);
        }
    }
}
