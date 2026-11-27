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
    /// <summary>A route an idle coach could fly, with the forecast for its best number of round trips.</summary>
    public class RouteSuggestion
    {
        public FleetCoach Coach { get; set; }
        public Station From { get; set; }
        public Station To { get; set; }
        public double Km { get; set; }

        /// <summary>The route advisor's passengers a day, both ways.</summary>
        public int DailyDemand { get; set; }

        /// <summary>The forecast for the best number of round trips (with the planner's turnaround).</summary>
        public RouteForecast Forecast { get; set; }

        public int Turnaround { get; set; }

        /// <summary>The stations where gates must be leased first (none: the player has free gates at both ends).</summary>
        public List<Station> GatesToLease { get; set; } = new List<Station>();

        /// <summary>What the gates to lease cost a day (the planner's first contract, 2 years).</summary>
        public double GatesPerDay { get; set; }

        /// <summary>What the coach costs a day to own or lease, flying or not.</summary>
        public double OwnershipPerDay { get; set; }

        /// <summary>The trips' profit a day less the new gates: what the route adds to the carrier.</summary>
        public double GainPerDay => Forecast.ProfitPerDay - GatesPerDay;

        /// <summary>After the coach's own cost too.</summary>
        public double NetPerDay => GainPerDay - OwnershipPerDay;

        public string Name => From.Profile.IATACode + "-" + To.Profile.IATACode;
    }

    /// <summary>
    ///     Routes for the player's idle passenger coach: the route advisor's best destinations from the player's main
    ///     stations, each forecast with that coach (the same forecast the route planner and the computer carriers use),
    ///     ranked by what they add a day.
    /// </summary>
    public static class RouteSuggestionService
    {
        /// <summary>Stations suggested from: the coach's home base and the player's busiest stations.</summary>
        public const int Origins = 5;

        /// <summary>Destinations the advisor gives for each station, before the forecast.</summary>
        public const int PerOrigin = 12;

        /// <summary>Towns the carrier does not serve yet, offered once it has worked out the ones it does.</summary>
        public const int NewTowns = 6;

        /// <summary>The player's delivered passenger coach without a route (not leased out).</summary>
        public static List<FleetCoach> IdleCoach(Carrier carrier)
        {
            if (carrier == null) return new List<FleetCoach>();
            return carrier.DeliveredFleet
                          .Where(f => (f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Passenger)
                                      && f.Routes.Count == 0 && !CoachDealService.IsLeasedOut(f) && f.UsableBy(carrier) && f.Coach.GetTotalSeatCapacity() > 0)
                          .OrderBy(f => f.Name).ToList();
        }

        /// <summary>The planner's turnaround for the coach: the shortest choice that its minimum time between trips fits.</summary>
        public static int Turnaround(FleetCoach f)
        {
            int minutes = (int) FleetCoachHelpers.GetMinTimeBetweenTrips(f).TotalMinutes;
            int best = RouteCreator.TurnaroundOptions.FirstOrDefault(o => o >= minutes);
            return best == 0 ? RouteCreator.TurnaroundOptions.Last() : best;
        }

        private static List<Station> OriginsFor(Carrier carrier, FleetCoach f)
        {
            return OriginsFor(carrier, f, false);
        }

        /// <summary>
        ///     The places to suggest routes from: the coach's home base and the carrier's busiest stations. A coach
        ///     line that has worked every pair between the towns it already serves is not finished -- it grows by
        ///     opening somewhere new -- so when <paramref name="reachOut" /> is set this also offers the biggest
        ///     towns within the coach's range that the carrier does not serve yet.
        /// </summary>
        private static List<Station> OriginsFor(Carrier carrier, FleetCoach f, bool reachOut)
        {
            var list = new List<Station>();
            if (f.Homebase != null) list.Add(f.Homebase);
            var routes = carrier.Routes.ToList();
            var mine = carrier.Stations.ToList();
            foreach (Station a in mine
                                  .OrderByDescending(a => routes.Count(r => r.Destination1 == a || r.Destination2 == a))
                                  .ThenByDescending(a => (int) a.Profile.Size))
            {
                if (list.Count >= Origins) break;
                if (!list.Contains(a)) list.Add(a);
            }

            if (!reachOut || f.Homebase == null) return list;

            // somewhere new, within reach of home and big enough to be worth a terminal
            foreach (Station a in Stations.GetAllActiveStations()
                                          .Where(a => !mine.Contains(a) && !list.Contains(a)
                                                      && f.Coach.MinBayLength <= a.GetMaxBayLength()
                                                      && MathHelpers.GetDistance(f.Homebase, a) <= f.Coach.Range)
                                          .OrderByDescending(a => (int) a.Profile.Size)
                                          .ThenBy(a => MathHelpers.GetDistance(f.Homebase, a)))
            {
                if (list.Count >= Origins + NewTowns) break;
                list.Add(a);
            }

            return list;
        }

        // the gates the player must lease at an station for the route; false when there are none to be had
        private static bool Gates(Carrier carrier, Station a, RouteSuggestion s)
        {
            if (StationHelpers.HasFreeGates(a, carrier, Terminal.TerminalType.Passenger)) return true;
            if (!RouteCreator.NeedsContract(a, Terminal.TerminalType.Passenger)) return false; // all its gates there are in use
            int gates = Math.Min(2, RouteCreator.GetFreeGates(a, Terminal.TerminalType.Passenger));
            if (gates <= 0) return false;
            s.GatesToLease.Add(a);
            s.GatesPerDay += StationHelpers.GetYearlyContractPayment(a, StationContract.ContractType.Full, gates, 2)/365.0;
            return true;
        }

        /// <summary>The best routes for an coach, the most added a day first.</summary>
        /// <param name="advice">The advisor's destinations by station, shared between the coach of one search.</param>
        public static List<RouteSuggestion> For(Carrier carrier, FleetCoach f, int max = 5, Dictionary<Station, List<RouteAdvice>> advice = null)
        {
            advice = advice ?? new Dictionary<Station, List<RouteAdvice>>();
            var result = new List<RouteSuggestion>();
            var flown = new HashSet<string>(carrier.Routes.ToList().Where(r => r.Destination1 != null && r.Destination2 != null)
                                                   .Select(r => Key(r.Destination1, r.Destination2)));
            int turnaround = Turnaround(f);
            double ownership = OwnershipService.MonthlyCost(f)/30.4;
            var seen = new HashSet<string>();
            // the carrier's own towns first; if every pair between them is already worked, look further afield
            // rather than telling the player their new coach has nowhere to go
            foreach (bool reachOut in new[] {false, true})
            {
            if (reachOut && result.Count > 0) break;
            foreach (Station from in OriginsFor(carrier, f, reachOut))
            {
                List<RouteAdvice> dests;
                if (!advice.TryGetValue(from, out dests))
                {
                    try { dests = RouteAdvisor.GetAdvice(from, PerOrigin*2); }
                    catch (Exception) { dests = new List<RouteAdvice>(); } // another carrier changed its routes meanwhile
                    advice[from] = dests;
                }
                foreach (RouteAdvice a in dests.Take(PerOrigin*2))
                {
                    Station to = a.Destination;
                    if (to == null || to == from) continue;
                    string key = Key(from, to);
                    if (flown.Contains(key) || !seen.Add(key)) continue;
                    if (a.Distance > f.Coach.Range || f.Coach.MinBayLength > Math.Min(from.GetMaxBayLength(), to.GetMaxBayLength())) continue;
                    // a ride of twelve hours or more is scheduled by the week, not as round trips a day: the planner
                    // forecasts nothing for it, so suggesting it would offer a route the player cannot open from here
                    if (RouteCreator.IsLongRoute(from, to, f.Coach.Type, turnaround)) continue;
                    var s = new RouteSuggestion {Coach = f, From = from, To = to, Km = a.Distance, DailyDemand = a.DailyDemand, Turnaround = turnaround, OwnershipPerDay = ownership};
                    if (!Gates(carrier, from, s) || !Gates(carrier, to, s)) continue;
                    int maxTrips = RouteCreator.GetMaxRoundTrips(from, to, f.Coach.Type, turnaround, OpsType.Regular);
                    List<RouteForecast> all;
                    s.Forecast = RouteForecastService.Best(carrier, from, to, f, maxTrips, out all);
                    if (s.Forecast == null) continue;
                    result.Add(s);
                }
            }
            }
            return result.OrderByDescending(s => s.GainPerDay).Take(max).ToList();
        }

        /// <summary>Suggestions for every idle coach (an station's destinations are looked up once).</summary>
        public static Dictionary<FleetCoach, List<RouteSuggestion>> ForIdle(Carrier carrier, int max = 5)
        {
            var advice = new Dictionary<Station, List<RouteAdvice>>();
            var result = new Dictionary<FleetCoach, List<RouteSuggestion>>();
            foreach (FleetCoach f in IdleCoach(carrier)) result[f] = For(carrier, f, max, advice);
            return result;
        }

        private static string Key(Station a, Station b)
        {
            string x = a.Profile.IATACode, y = b.Profile.IATACode;
            return string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x;
        }
    }
}
