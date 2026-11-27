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
    ///     How the computer carriers use the route forecast (the same one the route planner shows): which of the
    ///     destinations with the most demand earns the most, and when a passenger route that loses money is given up.
    ///     (Tried and measured over a game year: cutting round trips by the forecast earned the carriers less than filling
    ///     the day, so they keep filling it.)
    /// </summary>
    public static class AIRouteChoice
    {
        // how many of the destinations with the most demand are forecast
        public const int Candidates = 6;

        /// <summary>
        ///     A route the forecast says would fly emptier than this is not opened. Measured over 93 new routes of the
        ///     computer carriers: every one of the 54 forecast under 15% full lost money a month later, together about
        ///     2.7 million a day, and not one recovered (the ones forecast under 5% full ended at 7% full). Above it
        ///     they are mixed, and by 30% most pay, so the floor only rules out the hopeless.
        /// </summary>
        public static double MinLoad = 0.15;

        /// <summary>
        ///     When the destinations with the most demand are all too empty, this many are forecast before the carrier
        ///     gives up for now: a thin home station should send it further afield, not stop it growing.
        /// </summary>
        public const int WideCandidates = 24;

        // a route is given time to find its passengers before it is given up
        public const int DaysBeforeDrop = 60;

        /// <summary>Counters for the tests: destinations forecast, not the one with the most demand, all forecast to lose, routes given up.</summary>
        public static int Picked, PickedOther, AllLosing, Dropped;

        /// <summary>Counters for the tests: searches that went past the first candidates to find a route with passengers, and that found none.</summary>
        public static int Widened, TooEmpty;

        /// <summary>Counters for the tests: route searches that found no start station, no destination, no coach; that used an idle coach; that bought or leased one.</summary>
        public static int NoStart, NoDestination, NoCoach, UsedIdle, GotCoach, IdleRoutes, EmptyDropped;

        /// <summary>For the probes: called when a computer carrier's passenger route gets its timetable (route, coach, round trips).</summary>
        public static Action<Route, FleetCoach, int> RecordCreation;

        /// <summary>How many round trips a day fit between 06:00 and 22:00 (or in the morning and evening for a business route).</summary>
        public static int DayTrips(Station a, Station b, FleetCoach f, bool business = false)
        {
            TimeSpan min = MathHelpers.GetTripTime(a.Profile.Coordinates.ConvertToGeoCoordinate(), b.Profile.Coordinates.ConvertToGeoCoordinate(), f.Coach.Type)
                                      .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(f));
            if (min.TotalMinutes <= 0) return 1;
            return (int) ((business ? 4 : 16)*60/(2*min.TotalMinutes));
        }

        /// <summary>
        ///     What the carrier charges against the market fare on the routes it flies (its new routes are priced the same
        ///     way soon after they open), within its mentality's band; 1 for an carrier without routes yet.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>> FareCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>>();

        public static double FareFactor(Carrier carrier)
        {
            // worked out once a game day per carrier
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (FareCache.TryGetValue(carrier, out cached) && cached.Item1 == today) return cached.Item2;
            double factor = WorkOutFareFactor(carrier);
            FareCache.AddOrUpdate(carrier, Tuple.Create(today, factor));
            return factor;
        }

        private static double WorkOutFareFactor(Carrier carrier)
        {
            double low, high;
            AIPricingService.Band(carrier.Mentality, out low, out high);
            var ratios = new List<double>();
            foreach (Route r in carrier.Routes.ToArray())
            {
                if (!(r is PassengerRoute pr) || !pr.HasCoach) continue;
                double market = PricingService.MarketFare(r.Destination1, r.Destination2, CoachClass.ClassType.EconomyClass);
                double fare = AIPricingService.EconomyFare(pr);
                if (market > 0 && fare > 0) ratios.Add(fare/market);
            }
            if (ratios.Count == 0) return 1;
            return Math.Max(low, Math.Min(high, ratios.Average()));
        }

        /// <summary>A passenger coach of the carrier's fleet to forecast a destination with: the idle one it would fly, else the middle one by seats that reaches it.</summary>
        public static FleetCoach Reference(Carrier carrier, double km)
        {
            return new FleetSnapshot(carrier).Reference(km);
        }

        // the carrier's passenger coach, read once for all the destinations of one search
        private class FleetSnapshot
        {
            private readonly List<FleetCoach> _bySeats, _idleByRange;

            public FleetSnapshot(Carrier carrier)
            {
                List<FleetCoach> fleet;
                lock (carrier.Fleet)
                {
                    fleet = carrier.Fleet.Where(f => f.Coach.GetTotalSeatCapacity() > 0).ToList();
                }
                DateTime now = GameObject.GetInstance().GameTime;
                _bySeats = fleet.OrderBy(f => f.Coach.GetTotalSeatCapacity()).ToList();
                _idleByRange = fleet.Where(f => !f.HasRoute && f.Coach.BuiltDate <= now && f.UsableBy(carrier))
                                    .OrderBy(f => f.Coach.Range).ToList();
            }

            public FleetCoach Reference(double km)
            {
                // the idle coach the route would get (as AIHelpers picks one: the shortest range that reaches)
                FleetCoach idle = _idleByRange.FirstOrDefault(f => f.Coach.Range > km);
                if (idle != null) return idle;
                var reach = _bySeats.Where(f => f.Coach.Range >= km).ToList();
                return reach.Count == 0 ? null : reach[reach.Count/2];
            }
        }

        /// <summary>The forecast for the best number of round trips (up to what fits in a day).</summary>
        public static RouteForecast Best(Carrier carrier, Station a, Station b, FleetCoach f, Route exclude = null, bool business = false, double fareFactor = 1)
        {
            int max = Math.Max(1, DayTrips(a, b, f, business));
            List<RouteForecast> all;
            if (!business) return RouteForecastService.Best(carrier, a, b, f, max, out all, exclude, fareFactor);
            // a business route flies the same number of round trips in the morning and in the evening
            RouteForecast best = null;
            foreach (var fc in RouteForecastService.ForecastEach(carrier, a, b, f, Enumerable.Range(1, max).Select(k => 2*k), exclude, fareFactor))
            {
                if (best == null || fc.ProfitPerDay > best.ProfitPerDay) best = fc;
            }
            return best;
        }

        /// <summary>
        ///     Of the destinations with the most demand, the one forecast to earn the most a day. (Not opening a route the
        ///     forecast says won't pay was tried: new carriers then couldn't grow, and most lost money all year.)
        /// </summary>
        public static Station PickDestination(Carrier carrier, Station origin, List<Station> ranked)
        {
            if (ranked == null || ranked.Count == 0) return null;
            using (Profiler.Measure("route search: forecast destinations"))
            {
                double fares = FareFactor(carrier);
                var fleet = new FleetSnapshot(carrier);
                Station best = null, bestAnyLoad = null;
                double bestProfit = double.MinValue;
                bool forecast = false;
                int looked = 0;
                foreach (Station d in ranked)
                {
                    // the destinations with the most demand first; further down the list only while they are all empty
                    if (looked >= Candidates && best != null) break;
                    if (looked >= WideCandidates) break;
                    double km = MathHelpers.GetDistance(origin, d);
                    FleetCoach f = fleet.Reference(km);
                    if (f == null) continue;
                    looked++;
                    // at the round trips that earn the most, which asks whether there is a route here worth flying
                    // at all. (Judging it instead at the trips the carrier will really fly, which fill the day, was
                    // measured over five game years and came out worse: it turned away routes that are fine at a
                    // lower frequency, and the carrier's habit of filling the day is a separate matter.)
                    RouteForecast fc = Best(carrier, origin, d, f, null, false, fares);
                    forecast = true;
                    if (bestAnyLoad == null) bestAnyLoad = d;
                    // a route the forecast leaves nearly empty never fills up: it is passed over, however it compares
                    // with the other empty ones
                    if (fc.Load < MinLoad) continue;
                    if (best == null || fc.ProfitPerDay > bestProfit)
                    {
                        bestProfit = fc.ProfitPerDay;
                        best = d;
                    }
                }
                if (!forecast) return ranked[0]; // nothing to forecast with yet: the most demand
                if (best == null)
                {
                    // nowhere within reach has the passengers to fill a trip: no route today, and the coach is
                    // left for another search (or, once it has sat long enough, for sale)
                    System.Threading.Interlocked.Increment(ref TooEmpty);
                    return null;
                }
                if (looked > Candidates) System.Threading.Interlocked.Increment(ref Widened);
                System.Threading.Interlocked.Increment(ref Picked);
                // a route that fills its seats but is still forecast to lose money is flown: an idle coach costs its
                // ownership too, and a new route finds more passengers as the carrier becomes known there
                if (bestProfit < 0) System.Threading.Interlocked.Increment(ref AllLosing);
                if (best != ranked[0]) System.Threading.Interlocked.Increment(ref PickedOther);
                return best;
            }
        }

        /// <summary>Round trips a day the route flies now (one coach; a business route counts its morning and evening).</summary>
        public static int CurrentTrips(Route route)
        {
            // a Monday's trips, both ways (a business route doesn't fly at the weekend)
            return route.TimeTable.Entries.Count(e => e.Day == DayOfWeek.Monday)/2;
        }

        /// <summary>
        ///     Whether the pricing could still raise this route's fares. A computer carrier prices a route as a share of
        ///     the market fare and goes no higher than its mentality's ceiling (AIPricingService.Band), so a route
        ///     whose costs need more than the ceiling can never be priced into profit, however full it flies.
        /// </summary>
        public static bool FaresCanStillRise(Carrier carrier, PassengerRoute route)
        {
            double market = PricingService.MarketFare(route.Destination1, route.Destination2, CoachClass.ClassType.EconomyClass);
            double fare = AIPricingService.EconomyFare(route);
            // no market fare to go by, or no fare set yet: leave the route to the pricing
            if (market <= 0 || fare <= 0) return true;
            double low, high;
            AIPricingService.Band(carrier.Mentality, out low, out high);
            return fare/market < high*0.98;
        }

        /// <summary>
        ///     Whether a computer carrier gives up a passenger route: it has flown two months, loses money, lost money
        ///     over those two months, and the forecast says no number of round trips would pay for its trips (the
        ///     coach's ownership is paid whether it flies or not). A route that loses money with half its seats or
        ///     more sold is the pricing's business first: its fares rise a little each week, and only once they have
        ///     reached the most this carrier will ever charge, and it still loses, is the route itself the problem.
        /// </summary>
        public static bool ShouldDrop(Carrier carrier, Route route, double balance)
        {
            if (!(route is PassengerRoute) || !route.HasCoach || route.TimeTable.Entries.Count == 0) return false;
            if (balance >= -1000) return false;
            if (route.FillingDegree >= 0.5 && FaresCanStillRise(carrier, (PassengerRoute) route)) return false;
            var coaches = route.GetCoaches();
            if (coaches.Count != 1) return false;
            DateTime today = GameObject.GetInstance().GameTime;
            if ((today - route.StartDate).TotalDays < DaysBeforeDrop) return false;
            if (route.GetBalance(today.AddDays(-DaysBeforeDrop), today) >= 0) return false;

            FleetCoach f = coaches[0];
            RouteForecast best = Best(carrier, route.Destination1, route.Destination2, f, route, IsBusiness(route, f));
            return best.ProfitPerDay + OwnershipService.MonthlyCost(f)/30.4 < 0;
        }

        public static bool IsBusiness(Route route, FleetCoach f)
        {
            TimeSpan min = route.GetTripTime(f.Coach.Type).Add(FleetCoachHelpers.GetMinTimeBetweenTrips(f));
            return min.TotalMinutes <= 120;
        }
    }
}
