using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Every carrier's routes by station pair, so the passenger and cargo calculation for one trip doesn't have to
    ///     look through all routes of all carriers. Rebuilt when a route is added or removed, a stopover changes, or the
    ///     day changes.
    /// </summary>
    public static class RouteIndex
    {
        private sealed class Snapshot
        {
            public int Version;
            public System.DateTime Day;
            public readonly Dictionary<Station, Dictionary<Station, List<Route>>> Direct = new Dictionary<Station, Dictionary<Station, List<Route>>>();
            public readonly Dictionary<Station, Dictionary<Station, List<Route>>> Legs = new Dictionary<Station, Dictionary<Station, List<Route>>>();
            public readonly Dictionary<Station, List<Route>> At = new Dictionary<Station, List<Route>>();

            // results worked out from this snapshot, dropped with it
            public readonly System.Collections.Concurrent.ConcurrentDictionary<object, object> Derived = new System.Collections.Concurrent.ConcurrentDictionary<object, object>();
        }

        private static readonly object BuildLock = new object();
        private static Snapshot _snapshot;
        private static int _version;

        private static readonly List<Route> None = new List<Route>();

        /// <summary>Call when routes change (added, removed, stopover added) or a game is loaded.</summary>
        public static void Invalidate()
        {
            Interlocked.Increment(ref _version);
        }

        private static Snapshot Current()
        {
            var day = GameObject.GetInstance().GameTime.Date;
            int version = Volatile.Read(ref _version);
            var s = Volatile.Read(ref _snapshot);
            if (s != null && s.Version == version && s.Day == day) return s;
            lock (BuildLock)
            {
                version = Volatile.Read(ref _version);
                s = _snapshot;
                if (s != null && s.Version == version && s.Day == day) return s;
                s = new Snapshot { Version = version, Day = day };
                foreach (Carrier carrier in Carriers.GetAllCarriers().ToList())
                {
                    foreach (Route r in carrier.Routes)
                    {
                        if (r?.Destination1 == null || r.Destination2 == null) continue;
                        AddBoth(s.Direct, r.Destination1, r.Destination2, r);
                        AddAt(s.At, r.Destination1, r);
                        if (r.Destination2 != r.Destination1) AddAt(s.At, r.Destination2, r);
                        foreach (var stop in r.Stopovers.ToList())
                        {
                            foreach (var leg in stop.Legs.ToList())
                            {
                                if (leg?.Destination1 == null || leg.Destination2 == null) continue;
                                AddBoth(s.Legs, leg.Destination1, leg.Destination2, r);
                            }
                        }
                    }
                }
                Volatile.Write(ref _snapshot, s);
                return s;
            }
        }

        private static void AddAt(Dictionary<Station, List<Route>> map, Station a, Route r)
        {
            List<Route> list;
            if (!map.TryGetValue(a, out list)) map[a] = list = new List<Route>();
            list.Add(r);
        }

        /// <summary>
        ///     Something worked out from the routes, kept until the routes change (or the day does). The key must be
        ///     unique to the caller (include a tag); the result is shared between threads, so don't change it.
        /// </summary>
        public static T Derived<T>(object key, System.Func<T> make) where T : class
        {
            var d = Current().Derived;
            object v;
            if (d.TryGetValue(key, out v)) return (T) v;
            return (T) d.GetOrAdd(key, make());
        }

        /// <summary>Every carrier's routes that start or end at the station, in carrier order. Don't change the list.</summary>
        public static List<Route> At(Station station)
        {
            List<Route> list;
            if (station != null && Current().At.TryGetValue(station, out list)) return list;
            return None;
        }

        private static void AddBoth(Dictionary<Station, Dictionary<Station, List<Route>>> map, Station a, Station b, Route r)
        {
            Add(map, a, b, r);
            if (a != b) Add(map, b, a, r);
        }

        private static void Add(Dictionary<Station, Dictionary<Station, List<Route>>> map, Station a, Station b, Route r)
        {
            Dictionary<Station, List<Route>> inner;
            if (!map.TryGetValue(a, out inner)) map[a] = inner = new Dictionary<Station, List<Route>>();
            List<Route> list;
            if (!inner.TryGetValue(b, out list)) inner[b] = list = new List<Route>();
            // a route with several legs on the same pair is listed once
            if (list.Count == 0 || !ReferenceEquals(list[list.Count - 1], r)) list.Add(r);
        }

        private static List<Route> Get(Dictionary<Station, Dictionary<Station, List<Route>>> map, Station a, Station b)
        {
            Dictionary<Station, List<Route>> inner;
            List<Route> list;
            if (a != null && b != null && map.TryGetValue(a, out inner) && inner.TryGetValue(b, out list)) return list;
            return None;
        }

        /// <summary>Routes flown nonstop between the two stations (either direction). Don't change the list.</summary>
        public static List<Route> Direct(Station a, Station b)
        {
            return Get(Current().Direct, a, b);
        }

        /// <summary>Routes with a stopover leg between the two stations (either direction). Don't change the list.</summary>
        public static List<Route> WithLeg(Station a, Station b)
        {
            return Get(Current().Legs, a, b);
        }

        /// <summary>Whether any carrier has a route between the two stations.</summary>
        public static bool HasRoute(Station a, Station b)
        {
            return Direct(a, b).Count > 0;
        }
    }
}
