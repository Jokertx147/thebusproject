using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheBusProject.Infrastructure;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     The highway network as a graph, so a route can be drawn and driven the way a coach would really go: down
    ///     the interstate rather than straight across the country. The lines come from the same
    ///     graphics/maps/usmap.json the map is drawn from; the interstates and the US routes are used, the state
    ///     highways left out, which keeps the graph small and the paths sensible.
    /// </summary>
    public static class RoadNetwork
    {
        /// <summary>Points this close together are the same junction (about 8 km).</summary>
        private const double Snap = 0.08;

        private static bool _tried;
        private static List<double[]> _nodes;                 // lat, lon
        private static List<List<int>> _edges;                // neighbours
        private static List<List<double>> _costs;             // and what they cost
        private static Dictionary<long, List<int>> _buckets;  // nodes by whole degree, to find the nearest

        private static readonly ConcurrentDictionary<string, List<double[]>> Cache =
            new ConcurrentDictionary<string, List<double[]>>();

        /// <summary>True when there are roads to route along.</summary>
        public static bool Ready
        {
            get
            {
                Load();
                return _nodes != null && _nodes.Count > 0;
            }
        }

        /// <summary>
        ///     The way a coach would drive between two places, as latitude and longitude pairs from one to the other,
        ///     or null when the roads can't join them (or aren't loaded). The ends are the places themselves, so the
        ///     line still meets the station it belongs to.
        /// </summary>
        public static List<double[]> Path(double lat1, double lon1, double lat2, double lon2)
        {
            Load();
            if (_nodes == null || _nodes.Count == 0) return null;

            string key = Key(lat1, lon1) + "|" + Key(lat2, lon2);
            List<double[]> hit;
            if (Cache.TryGetValue(key, out hit)) return hit;

            List<double[]> path = Find(lat1, lon1, lat2, lon2);
            Cache[key] = path;
            return path;
        }

        /// <summary>The point a share <paramref name="fr" /> of the way along a path, by distance.</summary>
        public static double[] PointAt(List<double[]> path, double fr)
        {
            if (path == null || path.Count == 0) return null;
            if (path.Count == 1) return path[0];

            double total = 0;
            var steps = new double[path.Count - 1];
            for (int i = 0; i < path.Count - 1; i++)
            {
                steps[i] = Flat(path[i][0], path[i][1], path[i + 1][0], path[i + 1][1]);
                total += steps[i];
            }

            if (total <= 0) return path[0];
            double want = Math.Max(0, Math.Min(1, fr))*total, sofar = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                if (sofar + steps[i] >= want)
                {
                    double t = steps[i] <= 0 ? 0 : (want - sofar)/steps[i];
                    return new[]
                    {
                        path[i][0] + (path[i + 1][0] - path[i][0])*t,
                        path[i][1] + (path[i + 1][1] - path[i][1])*t
                    };
                }

                sofar += steps[i];
            }

            return path[path.Count - 1];
        }

        private static string Key(double lat, double lon)
        {
            return Math.Round(lat, 2).ToString("0.00") + "," + Math.Round(lon, 2).ToString("0.00");
        }

        /// <summary>Degrees, with longitude squeezed by the latitude: near enough for comparing two roads.</summary>
        private static double Flat(double lat1, double lon1, double lat2, double lon2)
        {
            double k = Math.Cos((lat1 + lat2)/2*Math.PI/180);
            double dx = (lon2 - lon1)*k, dy = lat2 - lat1;
            return Math.Sqrt(dx*dx + dy*dy);
        }

        private static void Load()
        {
            if (_tried) return;
            _tried = true;

            string file = System.IO.Path.Combine(AppSettings.GetDataPath(), "graphics", "maps", "usmap.json");
            if (!File.Exists(file)) return;

            try
            {
                var index = new Dictionary<long, int>();
                var nodes = new List<double[]>();
                var edges = new List<List<int>>();
                var costs = new List<List<double>>();

                Func<double, double, int> node = (lat, lon) =>
                {
                    long gx = (long) Math.Round(lon/Snap), gy = (long) Math.Round(lat/Snap);
                    long k = gx*100000 + gy;
                    int at;
                    if (index.TryGetValue(k, out at)) return at;
                    at = nodes.Count;
                    index[k] = at;
                    // the junction sits where the road really passes, not in the middle of the grid square that
                    // found it: a path drawn through the squares' middles staircases across the map and lies a few
                    // kilometres off the road it is supposed to be on
                    nodes.Add(new[] { lat, lon });
                    edges.Add(new List<int>());
                    costs.Add(new List<double>());
                    return at;
                };

                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(file)))
                {
                    JsonElement roads = doc.RootElement.GetProperty("roads");
                    foreach (string kind in new[] { "interstate", "us", "state" })
                    {
                        JsonElement set;
                        if (!roads.TryGetProperty(kind, out set)) continue;

                        // the interstates are what a coach would use where there is a choice; the rest are there to
                        // join the network up (Florida's turnpike and the like) and cost more to drive
                        double weight = kind == "interstate" ? 1.0 : kind == "us" ? 1.35 : 1.6;
                        foreach (JsonElement road in set.EnumerateArray())
                        {
                            int previous = -1;
                            double plat = 0, plon = 0;
                            foreach (JsonElement point in road.GetProperty("p").EnumerateArray())
                            {
                                double lon = point[0].GetDouble(), lat = point[1].GetDouble();
                                int here = node(lat, lon);
                                if (previous >= 0 && here != previous)
                                {
                                    double w = Flat(plat, plon, lat, lon)*weight;
                                    edges[previous].Add(here);
                                    costs[previous].Add(w);
                                    edges[here].Add(previous);
                                    costs[here].Add(w);
                                }

                                previous = here;
                                plat = lat;
                                plon = lon;
                            }
                        }
                    }
                }

                var buckets = new Dictionary<long, List<int>>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    long k = Bucket(nodes[i][0], nodes[i][1]);
                    List<int> list;
                    if (!buckets.TryGetValue(k, out list)) buckets[k] = list = new List<int>();
                    list.Add(i);
                }

                _nodes = nodes;
                _edges = edges;
                _costs = costs;
                _buckets = buckets;
                StartupLog.Write("Road network: " + nodes.Count + " junctions.");
            }
            catch (Exception ex)
            {
                StartupLog.Write("The road network could not be read: " + ex.Message);
                _nodes = null;
            }
        }

        private static long Bucket(double lat, double lon)
        {
            return (long) Math.Floor(lon)*1000 + (long) Math.Floor(lat);
        }

        private static int Nearest(double lat, double lon)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int ring = 0; ring <= 4 && best < 0; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                        List<int> list;
                        if (!_buckets.TryGetValue(Bucket(lat + dy, lon + dx), out list)) continue;
                        foreach (int i in list)
                        {
                            double d = Flat(lat, lon, _nodes[i][0], _nodes[i][1]);
                            if (d < bestD)
                            {
                                bestD = d;
                                best = i;
                            }
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>A* over the junctions, with the straight line as the guess.</summary>
        private static List<double[]> Find(double lat1, double lon1, double lat2, double lon2)
        {
            int from = Nearest(lat1, lon1), to = Nearest(lat2, lon2);
            if (from < 0 || to < 0) return null;
            if (from == to) return null;

            int n = _nodes.Count;
            var best = new double[n];
            var cameFrom = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                best[i] = double.MaxValue;
                cameFrom[i] = -1;
            }

            Func<int, double> guess = i => Flat(_nodes[i][0], _nodes[i][1], _nodes[to][0], _nodes[to][1]);
            var queue = new PriorityQueue<int, double>();
            best[from] = 0;
            queue.Enqueue(from, guess(from));

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                if (done[at]) continue;
                done[at] = true;
                if (at == to) break;

                List<int> near = _edges[at];
                List<double> cost = _costs[at];
                for (int i = 0; i < near.Count; i++)
                {
                    int next = near[i];
                    if (done[next]) continue;
                    double step = best[at] + cost[i];
                    if (step >= best[next]) continue;
                    best[next] = step;
                    cameFrom[next] = at;
                    queue.Enqueue(next, step + guess(next));
                }
            }

            if (!done[to]) return null;

            var back = new List<double[]>();
            for (int at = to; at >= 0; at = cameFrom[at]) back.Add(_nodes[at]);
            back.Reverse();

            // the ends are the places themselves, so the line reaches the station rather than the nearest junction
            var path = new List<double[]> { new[] { lat1, lon1 } };
            path.AddRange(back);
            path.Add(new[] { lat2, lon2 });
            return path;
        }
    }
}
