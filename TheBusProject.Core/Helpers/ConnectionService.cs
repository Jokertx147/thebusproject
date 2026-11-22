using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     One connection market of a route: passengers between Far and Near who change planes at Via, flying the
    ///     route between Near and Via and the feeder route between Via and Far.
    /// </summary>
    public class ConnectionMarket
    {
        public Route Route, Feeder;
        public Station Near, Via, Far;
        public bool Partner;
        public string HubName;

        /// <summary>Passengers a day wanting to travel between Near and Far (both ways, all classes).</summary>
        public double Demand;

        /// <summary>The share of that demand that books the connection when it counts.</summary>
        public double Weight;

        /// <summary>Why the market brings no passengers, or null.</summary>
        public string Problem;

        public bool Counted => Problem == null;
        public double Added => Counted ? Demand*Weight : 0;
    }

    /// <summary>
    ///     Connecting passengers. A trip from A to B also carries passengers going on from B (or coming from
    ///     beyond A) on another route of the carrier or of its alliance and codeshare partners. A connection counts
    ///     when the trip is less than three times the nonstop distance and fewer than two carriers fly the nonstop.
    ///     The carrier's own feeder routes bring a quarter of the market, more through its hubs (30% at a focus
    ///     city, 33% at a regional hub, 35% at a hub, 40% at a fortress hub); a partner's routes bring a fifth.
    ///     The routes count connecting passengers each month.
    /// </summary>
    public static class ConnectionService
    {
        public const double OwnWeight = 0.25, PartnerWeight = 0.2;

        // ---- how many carriers fly each station pair nonstop, built once a day ----
        private static readonly object CacheLock = new object();
        private static Dictionary<Station, Dictionary<Station, int>> _nonstop;
        private static DateTime _nonstopDate;

        public static void Invalidate()
        {
            lock (CacheLock) _nonstop = null;
        }

        private static Dictionary<Station, Dictionary<Station, int>> Nonstop()
        {
            var day = GameObject.GetInstance()?.GameTime.Date ?? DateTime.MinValue;
            var cache = _nonstop;
            if (cache != null && _nonstopDate == day) return cache;
            lock (CacheLock)
            {
                if (_nonstop != null && _nonstopDate == day) return _nonstop;
                var map = new Dictionary<Station, Dictionary<Station, int>>();
                int count = 0;
                foreach (var carrier in Carriers.GetAllCarriers().ToList())
                {
                    foreach (var r in carrier.Routes.ToList())
                    {
                        if (r?.Destination1 == null || r.Destination2 == null) continue;
                        Add(map, r.Destination1, r.Destination2);
                        Add(map, r.Destination2, r.Destination1);
                        count++;
                    }
                }
                _nonstop = map;
                _nonstopDate = day;
                return map;
            }
        }

        private static void Add(Dictionary<Station, Dictionary<Station, int>> map, Station a, Station b)
        {
            Dictionary<Station, int> inner;
            if (!map.TryGetValue(a, out inner)) map[a] = inner = new Dictionary<Station, int>();
            int n;
            inner.TryGetValue(b, out n);
            inner[b] = n + 1;
        }

        /// <summary>How many routes (of all carriers) fly between two stations.</summary>
        public static int NonstopRoutes(Station a, Station b)
        {
            Dictionary<Station, int> inner;
            int n;
            return Nonstop().TryGetValue(a, out inner) && inner.TryGetValue(b, out n) ? n : 0;
        }

        // ---- the carrier's network ----

        /// <summary>The partners whose routes can feed the carrier's: alliance members and codeshare partners.</summary>
        public static List<Carrier> Partners(Carrier carrier)
        {
            var list = new List<Carrier>();
            foreach (var al in carrier.Alliances.ToList())
                foreach (var m in al.Members.ToList())
                    if (m.Carrier != carrier && !list.Contains(m.Carrier)) list.Add(m.Carrier);
            foreach (var c in carrier.Codeshares.ToList())
            {
                if (!(c.Carrier2 == carrier && c.Type == CodeshareAgreement.CodeshareType.OneWay) && c.Type != CodeshareAgreement.CodeshareType.BothWays) continue;
                var other = c.Carrier1 == carrier ? c.Carrier2 : c.Carrier1;
                if (other != carrier && !list.Contains(other)) list.Add(other);
            }
            return list;
        }

        /// <summary>The share of a market the carrier's own feeder brings through an station (more at its hubs).</summary>
        public static double OwnWeightAt(Carrier carrier, Station via, out string hubName)
        {
            hubName = null;
            var hub = via.GetHubs().Find(h => h.Carrier == carrier);
            if (hub == null) return OwnWeight;
            switch (hub.Type.Type)
            {
                case HubType.TypeOfHub.FocusCity: hubName = "focus city"; return 0.30;
                case HubType.TypeOfHub.RegionalHub: hubName = "regional hub"; return 0.33;
                case HubType.TypeOfHub.FortressHub: hubName = "fortress hub"; return 0.40;
                default: hubName = "hub"; return 0.35;
            }
        }

        /// <summary>The demand between two stations, both ways and all classes.</summary>
        public static double PairDemand(Station a, Station b)
        {
            return Rate(a, b, null) + Rate(b, a, null);
        }

        private static double Rate(Station from, Station to, CoachClass.ClassType? type)
        {
            if (type.HasValue) return from.GetDestinationPassengersRate(to, type.Value);
            double v = 0;
            foreach (CoachClass.ClassType t in Enum.GetValues(typeof (CoachClass.ClassType)))
                v += from.GetDestinationPassengersRate(to, t);
            return v;
        }

        /// <summary>
        ///     The connection markets at one end (via) of a leg between near and via. With a class, the demand is the
        ///     one-way daily demand from near (or to near when inbound) in that class, as the trip uses it; without one,
        ///     both ways in all classes.
        /// </summary>
        private static IEnumerable<ConnectionMarket> MarketsAt(Carrier carrier, Route route, Station near, Station via, CoachClass.ClassType? type, bool outbound)
        {
            double leg = MathHelpers.GetDistance(near, via);
            string hubName;
            double own = OwnWeightAt(carrier, via, out hubName);
            // only routes touching via can feed it: take them from the route index instead of every route of the network
            var atVia = RouteIndex.At(via);
            var feeders = atVia.Where(r => r.Carrier == carrier).Select(r => new { r, partner = false })
                               .Concat(Partners(carrier).SelectMany(p => atVia.Where(r => r.Carrier == p)).Select(r => new { r, partner = true }));
            var seen = new HashSet<Station>();
            foreach (var f in feeders)
            {
                var r = f.r;
                if (r == null || r == route || r.IsCargoRoute) continue;
                if (r.Destination1 != via && r.Destination2 != via) continue;
                if (r.Destination1 == near || r.Destination2 == near) continue;
                var far = r.Destination1 == via ? r.Destination2 : r.Destination1;
                // one market per far station: the own route first, then partners
                if (!seen.Add(far)) continue;
                var m = new ConnectionMarket
                {
                    Route = route, Feeder = r, Near = near, Via = via, Far = far, Partner = f.partner,
                    Weight = f.partner ? PartnerWeight : own, HubName = f.partner ? null : hubName
                };
                m.Demand = type.HasValue
                    ? (outbound ? Rate(near, far, type) : Rate(far, near, type))
                    : Rate(near, far, null) + Rate(far, near, null);
                double direct = MathHelpers.GetDistance(near, far);
                if (!r.HasCoach) m.Problem = "no coach runs " + Code(r.Destination1) + "-" + Code(r.Destination2) + " yet";
                else if (r.GetDistance() + leg >= direct*3) m.Problem = "too far out of the way (" + ((r.GetDistance() + leg)/Math.Max(1, direct)).ToString("0.0") + " times the nonstop distance)";
                else
                {
                    int n = NonstopRoutes(near, far);
                    if (n >= 2) m.Problem = n + " routes already run " + Code(near) + "-" + Code(far) + " nonstop";
                }
                if (m.Demand <= 0 && m.Problem == null) m.Problem = "nobody wants to travel " + Code(near) + "-" + Code(far);
                yield return m;
            }
        }

        // a route that can feed connections at an station: the far end, whether a partner flies it, its length
        private sealed class Feeder
        {
            public Route Route;
            public Station Far;
            public bool Partner;
            public double Distance;
        }

        // the carrier's (then its partners') passenger routes from an station, as MarketsAt goes through them; kept until
        // the routes change
        private static List<Feeder> FeedersAt(Carrier carrier, Station via)
        {
            return RouteIndex.Derived(Tuple.Create("feeders", carrier, via), () =>
                {
                    var atVia = RouteIndex.At(via);
                    var list = new List<Feeder>();
                    foreach (var x in atVia.Where(r => r.Carrier == carrier).Select(r => new {r, partner = false})
                                           .Concat(Partners(carrier).SelectMany(p => atVia.Where(r => r.Carrier == p)).Select(r => new {r, partner = true})))
                    {
                        Route r = x.r;
                        if (r == null || r.IsCargoRoute) continue;
                        if (r.Destination1 != via && r.Destination2 != via) continue;
                        list.Add(new Feeder {Route = r, Far = r.Destination1 == via ? r.Destination2 : r.Destination1, Partner = x.partner, Distance = r.GetDistance()});
                    }
                    return list;
                });
        }

        // the passengers a day the connection markets at via add to a trip between near and via (MarketsAt's Added, summed)
        private static double AddedAt(Carrier carrier, Route route, Station near, Station via, CoachClass.ClassType type, bool outbound)
        {
            List<Feeder> feeders = FeedersAt(carrier, via);
            if (feeders.Count == 0) return 0;
            double leg = MathHelpers.GetDistance(near, via);
            string hubName;
            double own = OwnWeightAt(carrier, via, out hubName);
            var seen = new HashSet<Station>();
            double v = 0;
            foreach (Feeder f in feeders)
            {
                Route r = f.Route;
                if (r == route) continue;
                if (r.Destination1 == near || r.Destination2 == near) continue;
                if (!seen.Add(f.Far)) continue;
                double demand = outbound ? Rate(near, f.Far, type) : Rate(f.Far, near, type);
                if (demand <= 0 || !r.HasCoach) continue;
                if (f.Distance + leg >= MathHelpers.GetDistance(near, f.Far)*3) continue;
                if (NonstopRoutes(near, f.Far) >= 2) continue;
                v += demand*(f.Partner ? PartnerWeight : own);
            }
            return v;
        }

        public static string Code(Station a)
        {
            return a?.Profile.IATACode ?? "?";
        }

        /// <summary>All connection markets of a route, at both ends, both ways and all classes.</summary>
        public static List<ConnectionMarket> Markets(Route route)
        {
            var carrier = route.Carrier;
            var list = new List<ConnectionMarket>();
            if (carrier == null || route.IsCargoRoute) return list;
            list.AddRange(MarketsAt(carrier, route, route.Destination1, route.Destination2, null, true));
            list.AddRange(MarketsAt(carrier, route, route.Destination2, route.Destination1, null, true));
            return list.OrderByDescending(m => m.Added).ThenByDescending(m => m.Demand).ToList();
        }

        /// <summary>
        ///     The connecting demand a trip from current to destination adds in one class (passengers a day), as the
        ///     trip engine uses it: passengers going on beyond the destination and passengers from beyond the start.
        /// </summary>
        public static double TripDemand(Carrier carrier, Route route, Station current, Station destination, CoachClass.ClassType type)
        {
            if (carrier == null) return 0;
            // the same for every trip of the route in that direction and class on one day: work it out once a day
            DateTime day = GameObject.GetInstance().GameTime.Date;
            var key = Tuple.Create(carrier, current, destination, type);
            DemandDay cached = route == null ? null : DemandCache.GetOrCreateValue(route);
            if (cached != null)
            {
                lock (cached)
                {
                    double hit;
                    if (cached.Day == day && cached.Values.TryGetValue(key, out hit)) return hit;
                }
            }
            long pfMiss = Profiler.Start();
            // the same sum as the markets' (MarketsAt), without building them
            double v = AddedAt(carrier, route, current, destination, type, true) + AddedAt(carrier, route, destination, current, type, false);
            if (cached != null)
            {
                lock (cached)
                {
                    if (cached.Day != day)
                    {
                        cached.Day = day;
                        cached.Values.Clear();
                    }
                    cached.Values[key] = v;
                }
            }
            Profiler.Stop("pax: connecting demand (worked out)", pfMiss);
            return v;
        }

        /// <summary>TripDemand worked out the long way, from the markets (for the tests: the two must agree).</summary>
        public static double TripDemandByMarkets(Carrier carrier, Route route, Station current, Station destination, CoachClass.ClassType type)
        {
            double v = 0;
            foreach (var m in MarketsAt(carrier, route, current, destination, type, true)) v += m.Added;
            foreach (var m in MarketsAt(carrier, route, destination, current, type, false)) v += m.Added;
            return v;
        }

        /// <summary>TripDemand without the day's cache (for the tests).</summary>
        public static double TripDemandFresh(Carrier carrier, Route route, Station current, Station destination, CoachClass.ClassType type)
        {
            return AddedAt(carrier, route, current, destination, type, true) + AddedAt(carrier, route, destination, current, type, false);
        }

        private sealed class DemandDay
        {
            public DateTime Day;
            public readonly Dictionary<Tuple<Carrier, Station, Station, CoachClass.ClassType>, double> Values =
                new Dictionary<Tuple<Carrier, Station, Station, CoachClass.ClassType>, double>();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Route, DemandDay> DemandCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Route, DemandDay>();

        // ---- counting ----

        private static int MonthKey(DateTime d)
        {
            return d.Year*12 + d.Month - 1;
        }

        /// <summary>Counts a trip's passengers, how many of them connect, and the seats it flew.</summary>
        public static void Record(Route route, int passengers, double connecting, int seats = 0)
        {
            if (passengers < 0) passengers = 0;
            if (route == null || (passengers == 0 && seats <= 0)) return;
            int key = MonthKey(GameObject.GetInstance().GameTime);
            lock (route)
            {
                if (route.ConnectMonth != key)
                {
                    bool previous = route.ConnectMonth == key - 1;
                    route.ConnectLastPassengers = previous ? route.ConnectPassengers : 0;
                    route.ConnectLastAllPassengers = previous ? route.ConnectAllPassengers : 0;
                    route.ConnectLastSeats = previous ? route.ConnectSeats : 0;
                    route.ConnectPassengers = 0;
                    route.ConnectAllPassengers = 0;
                    route.ConnectSeats = 0;
                    route.ConnectMonth = key;
                }
                route.ConnectAllPassengers += passengers;
                route.ConnectSeats += Math.Max(0, seats);
                route.ConnectPassengers += (int) Math.Round(Math.Max(0, Math.Min(passengers, connecting)));
            }
        }

        /// <summary>Last month's passengers and connecting passengers of a route.</summary>
        public static void LastMonth(Route route, out int connecting, out int all)
        {
            int key = MonthKey(GameObject.GetInstance().GameTime);
            if (route.ConnectMonth == key) { connecting = route.ConnectLastPassengers; all = route.ConnectLastAllPassengers; }
            else if (route.ConnectMonth == key - 1) { connecting = route.ConnectPassengers; all = route.ConnectAllPassengers; }
            else { connecting = 0; all = 0; }
        }

        /// <summary>
        ///     The share of seats filled: last month when it flew enough, else this month so far, else since the route
        ///     started; 0 when unknown.
        /// </summary>
        public static double MonthLoad(Route route)
        {
            int key = MonthKey(GameObject.GetInstance().GameTime);
            int lastPax = 0, lastSeats = 0, nowPax = 0, nowSeats = 0;
            lock (route)
            {
                if (route.ConnectMonth == key)
                {
                    lastPax = route.ConnectLastAllPassengers; lastSeats = route.ConnectLastSeats;
                    nowPax = route.ConnectAllPassengers; nowSeats = route.ConnectSeats;
                }
                else if (route.ConnectMonth == key - 1)
                {
                    lastPax = route.ConnectAllPassengers; lastSeats = route.ConnectSeats;
                }
            }
            if (lastSeats >= 200) return Math.Min(1, (double) lastPax/lastSeats);
            if (nowSeats + lastSeats >= 200) return Math.Min(1, (double) (nowPax + lastPax)/(nowSeats + lastSeats));
            double v;
            try { v = route.GetFillingDegree(); }
            catch (Exception) { v = 0; }
            return double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Max(0, Math.Min(1, v));
        }

        /// <summary>This month's counts so far.</summary>
        public static void ThisMonth(Route route, out int connecting, out int all)
        {
            int key = MonthKey(GameObject.GetInstance().GameTime);
            if (route.ConnectMonth == key) { connecting = route.ConnectPassengers; all = route.ConnectAllPassengers; }
            else { connecting = 0; all = 0; }
        }

        // ---- planning ----

        public class HubIdea
        {
            public Station Destination;
            public double Nonstop, Connecting;
            public int Feeders;
            public double Total => Nonstop + Connecting;
        }

        /// <summary>
        ///     New destinations from an station the carrier flies from, ranked by the passengers a day a route there
        ///     would get: the nonstop demand both ways and the connecting demand from the carrier's other routes there.
        /// </summary>
        public static List<HubIdea> HubIdeas(Carrier carrier, Station hub, int count, double maxKm)
        {
            string hubName;
            double weight = OwnWeightAt(carrier, hub, out hubName);
            var spokes = carrier.Routes.ToList()
                                .Where(r => !r.IsCargoRoute && r.HasCoach && (r.Destination1 == hub || r.Destination2 == hub))
                                .Select(r => r.Destination1 == hub ? r.Destination2 : r.Destination1)
                                .Distinct().ToList();
            var served = new HashSet<Station>(spokes);
            var ideas = new List<HubIdea>();
            foreach (var d in hub.GetDestinationDemands())
            {
                if (d == null || d == hub || served.Contains(d) || !GeneralHelpers.IsStationActive(d)) continue;
                double km = MathHelpers.GetDistance(hub, d);
                if (km < 50 || km > maxKm) continue;
                var idea = new HubIdea { Destination = d, Nonstop = Rate(hub, d, null) + Rate(d, hub, null) };
                foreach (var s in spokes)
                {
                    if (s == d) continue;
                    double direct = MathHelpers.GetDistance(s, d);
                    if (MathHelpers.GetDistance(s, hub) + km >= direct*3 || NonstopRoutes(s, d) >= 2) continue;
                    double dem = Rate(s, d, null) + Rate(d, s, null);
                    if (dem <= 0) continue;
                    idea.Connecting += dem*weight;
                    idea.Feeders++;
                }
                if (idea.Total > 0) ideas.Add(idea);
            }
            return ideas.OrderByDescending(i => i.Total).Take(count).ToList();
        }
    }
}
