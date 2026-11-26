using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>What a passenger route flown by one coach is expected to do, at one number of round trips a day.</summary>
    public class RouteForecast
    {
        public int RoundTrips { get; set; }
        public double Load { get; set; }
        public double PassengersPerTrip { get; set; }
        public int Seats { get; set; }
        public double RevenuePerTrip { get; set; }
        public double CostPerTrip { get; set; }
        public double ProfitPerTrip => RevenuePerTrip - CostPerTrip;
        public double ProfitPerDay => ProfitPerTrip*RoundTrips*2;

        /// <summary>The rivals' share of the seats on the pair (0 when nobody else flies it).</summary>
        public int Rivals { get; set; }
    }

    /// <summary>Whether a route that flies would earn more with more or fewer round trips a day.</summary>
    public class TripAdvice
    {
        public int Now { get; set; }
        public int Max { get; set; }
        public RouteForecast Current { get; set; }
        public RouteForecast Best { get; set; }
        public double Gain => Best.ProfitPerDay - Current.ProfitPerDay;

        /// <summary>Flying Best.RoundTrips instead of Now earns clearly more (not just a rounding difference).</summary>
        public bool Change { get; set; }

        public string Text { get; set; }
    }

    /// <summary>
    ///     A forecast for a new passenger route before it is flown, worked out the way a trip's passengers and costs are:
    ///     the demand both ways (season, difficulty, the carrier's focus and hubs, a usual service score), split over all
    ///     the trips on the station pair and set against all the seats offered there, the carrier's reputation at the
    ///     station, the market fares, and the trip's costs (fuel, fees, crew, ground, maintenance, sales and overhead,
    ///     and the coach's ownership spread over its trips). It is an estimate: rivals change fares and schedules.
    /// </summary>
    public static class RouteForecastService
    {
        // a new route's service score (out of 10) when the carrier has no route flying yet: the passenger demand is
        // multiplied by score/5 for a trip
        private const double UsualScore = 6.0;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>> ScoreCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>>();

        /// <summary>
        ///     The service score a new route of the carrier can expect: the average of its passenger routes that fly
        ///     (coach, cabin, meals and fares much as on them), worked out once a game day; 6 without routes yet.
        /// </summary>
        public static double UsualScoreOf(Carrier carrier)
        {
            if (carrier == null) return UsualScore;
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (ScoreCache.TryGetValue(carrier, out cached) && cached.Item1 == today) return cached.Item2;
            var scores = new List<double>();
            foreach (Route r in carrier.Routes.ToArray())
            {
                if (r is PassengerRoute && r.HasCoach)
                {
                    try { scores.Add(PassengerHelpers.RouteScoreFor(r)); }
                    catch (Exception) { }
                }
            }
            double score = scores.Count > 0 ? scores.Average() : UsualScore;
            ScoreCache.AddOrUpdate(carrier, Tuple.Create(today, score));
            return score;
        }

        // the carrier's reputation share at an station as the route's trips will get it: for a route it flies already,
        // today's; for a new route, with that route counted (a route adds 7 to the carrier's score there and 6 for each
        // point of its service, and the score is ranked against the other carriers'). Today's alone, or the carrier's
        // average elsewhere, left a new route's forecast at the 75% floor where the trips soon got 100%.
        private static double ReputationFor(Carrier carrier, Station station, bool newRoute)
        {
            if (!newRoute) return PassengerHelpers.ReputationShare(station, carrier);
            return PassengerHelpers.ReputationShareWith(station, carrier, 7 + 6*NewRouteService(carrier));
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>> ServiceCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>>();

        /// <summary>
        ///     The economy service level a new route of the carrier gets: its routes' usual, or for an carrier without
        ///     routes the planner's standard facilities (once a game day).
        /// </summary>
        public static double NewRouteService(Carrier carrier)
        {
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (ServiceCache.TryGetValue(carrier, out cached) && cached.Item1 == today) return cached.Item2;
            var levels = carrier.Routes.ToArray().OfType<PassengerRoute>().Where(r => r.HasCoach && r.GetRouteCoachClass(CoachClass.ClassType.EconomyClass) != null)
                                .Select(r => r.GetServiceLevel(CoachClass.ClassType.EconomyClass)).ToList();
            double v;
            if (levels.Count > 0) v = levels.Average();
            else
            {
                int year = today.Year;
                v = 0;
                foreach (RouteFacility.FacilityType t in Enum.GetValues(typeof(RouteFacility.FacilityType)))
                {
                    if (year < (int) t || RouteFacilities.IsDiscontinued(t, year)) continue;
                    v += RouteCreator.PickFacility(carrier, t, ServiceTier.Standard)?.ServiceLevel ?? 0;
                }
            }
            ServiceCache.AddOrUpdate(carrier, Tuple.Create(today, v));
            return v;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>> ReputationCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Tuple<DateTime, double>>();

        /// <summary>The carrier's average reputation share (0.75 to 1) at the stations it flies from, once a game day.</summary>
        public static double UsualReputationOf(Carrier carrier)
        {
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (ReputationCache.TryGetValue(carrier, out cached) && cached.Item1 == today) return cached.Item2;
            var served = carrier.Routes.ToArray().Where(r => r is PassengerRoute && r.HasCoach).SelectMany(r => new[] {r.Destination1, r.Destination2}).Distinct().ToList();
            double v = served.Count > 0 ? served.Average(a => PassengerHelpers.ReputationShare(a, carrier)) : 0.75;
            ReputationCache.AddOrUpdate(carrier, Tuple.Create(today, v));
            return v;
        }

        private static double FocusAndHub(Carrier carrier, Station from, Station to, double km)
        {
            double f = 1;
            bool sameCountry = from.Profile.Country == to.Profile.Country;
            switch (carrier.MarketFocus)
            {
                case CarrierFocus.Global: if (km > 3000 && !sameCountry) f *= 1.15; break;
                case CarrierFocus.Regional: if (km < 1500) f *= 1.15; break;
                case CarrierFocus.Domestic: if (km < 1500 && sameCountry) f *= 1.15; break;
                case CarrierFocus.Local: if (km < 1000) f *= 1.15; break;
            }
            var hub = to.GetHubs().Find(h => h.Carrier == carrier);
            if (hub != null)
            {
                switch (hub.Type.Type)
                {
                    case HubType.TypeOfHub.FocusCity: if (sameCountry) f *= 1.15; break;
                    case HubType.TypeOfHub.RegionalHub: if (from.Profile.Country.Region == to.Profile.Country.Region) f *= 1.20; break;
                    case HubType.TypeOfHub.Hub: f *= 1.20; break;
                    case HubType.TypeOfHub.FortressHub: f *= 1.30; break;
                }
            }
            return f;
        }

        // the other carriers' (and the carrier's own) flying passenger routes on the pair
        private static List<Route> Others(Station a, Station b)
        {
            return RouteIndex.Direct(a, b).Concat(RouteIndex.WithLeg(a, b))
                             .Where(r => (r.Type == Route.RouteType.Passenger) && r.HasCoach).Distinct().ToList();
        }

        /// <summary>The trip costs of one trip with `pax` passengers paying `revenue`.</summary>
        private static double Cost(FleetCoach f, Carrier carrier, Station from, Station to, double km, double pax, double revenue, double tripsPerMonth)
        {
            Coach a = f.Coach;
            // as a landing charges them: the trip's own time, fuel by seats and passengers, the landing fee and the
            // passenger tax, cabin wages, and the extra costs (handling, maintenance, en-route, crews, ticket sales,
            // overhead) from the tickets
            double hours = MathHelpers.GetTripTime(from.Profile.Coordinates.ConvertToGeoCoordinate(), to.Profile.Coordinates.ConvertToGeoCoordinate(), a.Type).TotalHours;
            int seats = Math.Max(1, a.GetTotalSeatCapacity());
            double fuelPrice = StationHelpers.GetFuelPrice(from);
            int maxSeats = a.Type is CoachPassengerType pt ? pt.MaxSeatingCapacity : seats;
            double fuel = fuelPrice*km*maxSeats*a.FuelConsumption*0.55 + fuelPrice*km*a.FuelConsumption*pax*0.45;
            double fees = StationHelpers.GetLandingFee(to, a) + 5*pax;
            double cabin = (a.Type is CoachPassengerType p2 ? p2.Attendants : 0)*hours*carrier.Fees.GetValue(FeeTypes.GetType("Cabin Wage"));
            var parts = TripCosts.Calculate(a, km, hours, revenue);
            double ownership = tripsPerMonth > 0 ? OwnershipService.MonthlyCost(f)/tripsPerMonth : 0;
            return fuel + fees + cabin + parts.Total + ownership;
        }

        // what a passenger brings besides the ticket (in-trip sales less meals), from the route's last month (or
        // this month so far); 0 for a route that doesn't fly yet
        private static double ExtrasPerPassenger(Route route)
        {
            if (route == null) return 0;
            DateTime now = GameObject.GetInstance().GameTime;
            int connecting, carried;
            ConnectionService.LastMonth(route, out connecting, out carried);
            DateTime m = now.AddMonths(-1);
            if (carried <= 0)
            {
                carried = route.ConnectMonth == now.Year*12 + now.Month - 1 ? route.ConnectAllPassengers : 0;
                m = now;
            }
            if (carried <= 0) return 0;
            double extras = RouteProfitService.Month(route, Invoice.InvoiceType.Fees, m.Year, m.Month) + RouteProfitService.Month(route, Invoice.InvoiceType.OnTripIncome, m.Year, m.Month);
            return extras/carried;
        }

        // what doesn't depend on the number of round trips: the other trips and seats on the pair, and each class's
        // demand, share and fare both ways (worked out once, then tried for each number of round trips)
        private class Prepared
        {
            public Carrier Carrier;
            public Station A, B;
            public FleetCoach F;
            public double Km;
            public int Seats, Rivals;
            public double OtherTripsPerDay, Capacity, Extras;
            public readonly List<double[]> Classes = new List<double[]>(); // demand, seats, share, fare
        }

        private static Prepared Prepare(Carrier carrier, Station a, Station b, FleetCoach f, Route exclude, double fareFactor)
        {
            var p = new Prepared {Carrier = carrier, A = a, B = b, F = f, Seats = f.Coach.GetTotalSeatCapacity(), Km = MathHelpers.GetDistance(a, b)};
            if (p.Seats <= 0) return p;
            var game = GameObject.GetInstance();

            List<Route> others = Others(a, b).Where(r => r != exclude && !r.GetCoaches().Contains(f)).ToList();
            p.Rivals = others.Count(r => r.Carrier != carrier);
            // trips a day on the pair (both ways, as the trip engine counts them) and the seats offered
            p.OtherTripsPerDay = others.Sum(r => r.TimeTable.Entries.Count)/7.0;
            p.Capacity = others.Sum(r => r.GetCoaches().Select(x => x.Coach.GetTotalSeatCapacity()).DefaultIfEmpty(0).Max()) + p.Seats;

            // a route that flies already: its own service score, fares and competition, as its trips get them
            var flown = exclude as PassengerRoute;
            if (flown != null && !flown.HasCoach) flown = null;
            double score = flown != null ? PassengerHelpers.RouteScoreFor(flown) : UsualScoreOf(carrier);
            List<Route> pairRoutes = null;
            if (flown != null) pairRoutes = others.Concat(new[] {(Route) flown}).ToList();
            p.Extras = ExtrasPerPassenger(flown);
            bool newRoute = exclude == null && !Others(a, b).Any(r => r.Carrier == carrier);

            foreach (var dir in new[] {Tuple.Create(a, b), Tuple.Create(b, a)})
            {
                Station from = dir.Item1, to = dir.Item2;
                double factors = PassengerHelpers.SeasonFactorFor(to)*PassengerHelpers.HolidayFactorFor(to)*PassengerHelpers.HolidayFactorFor(from)
                                 *game.Difficulty.PassengersLevel*score/5*FocusAndHub(carrier, from, to, p.Km);
                // (a new route: one of the carrier's own on the pair counts in its reputation already)
                double reputation = ReputationFor(carrier, from, newRoute);
                foreach (CoachClass c in f.Coach.Classes)
                {
                    // as for a trip: the direct demand, passengers connecting to and from the carrier's other routes
                    // at both ends, and passengers from stations nearby
                    double direct = from.GetDestinationPassengersRate(to, c.Type);
                    double connecting = ConnectionService.TripDemand(carrier, exclude, from, to, c.Type);
                    double nearby = PassengerHelpers.NearbyDemandFor(from, to, f, c.Type);
                    double market = PricingService.MarketFare(from, to, c.Type);
                    double fare = market*fareFactor;
                    var own = flown?.GetRouteCoachClass(c.Type);
                    if (own != null && own.FarePrice > 0) fare = own.FarePrice;
                    // fewer passengers above the market fare, more below it; on a shared route, the share its
                    // service and fare win
                    double price = own != null ? PricingService.DemandFactor(flown, c.Type) : PricingService.DemandFactor(fare, market, c.Type);
                    double competition = 1;
                    if (PricingService.Realistic)
                    {
                        competition = flown != null
                                          ? PricingService.CompetitionFactor(flown, pairRoutes, c.Type)
                                          : PricingService.CompetitionFactor(carrier, fare, others, c.Type);
                    }

                    double demand = (direct + connecting + nearby)*factors*price;
                    p.Classes.Add(new[] {demand, c.SeatingCapacity, reputation*competition*game.Difficulty.PriceLevel, fare});
                }
            }
            return p;
        }

        private static RouteForecast Evaluate(Prepared p, int roundTrips)
        {
            var result = new RouteForecast {RoundTrips = roundTrips, Seats = p.Seats, Rivals = p.Rivals};
            if (roundTrips < 1 || p.Seats <= 0) return result;
            double tripsPerDay = p.OtherTripsPerDay + 2*roundTrips;
            double pax = 0, revenue = 0;
            foreach (double[] c in p.Classes)
            {
                double perTrip = c[0]/Math.Max(1, tripsPerDay);
                double capacityShare = p.Capacity > 0 ? Math.Min(1, perTrip/p.Capacity) : 1;
                double seats = c[1];

                // over the week, not over an average day. The seats are the same on a Tuesday as on a Friday, so
                // the busy days are capped by them and the quiet ones run short, and the average of the seven is
                // below what the average day would suggest. Forecasting the average day promises a load the week
                // cannot deliver.
                double week = 0;
                for (int d = 0; d < 7; d++)
                    week += Math.Min(seats, seats*c[2]*capacityShare*PassengerHelpers.DayOfWeekFactor(new DateTime(2001, 1, 1).AddDays(d)));
                double n = week/7;

                pax += n/2;
                revenue += n*c[3]/2;
            }
            result.PassengersPerTrip = pax;
            result.Load = Math.Min(1, pax/result.Seats);
            result.RevenuePerTrip = revenue + pax*p.Extras;
            result.CostPerTrip = Cost(p.F, p.Carrier, p.A, p.B, p.Km, pax, revenue, roundTrips*2*30.4);
            return result;
        }

        /// <summary>The forecast for the coach flying the pair `roundTrips` times a day (both ways averaged).</summary>
        /// <param name="fareFactor">the fares as a share of the market fares (a route that flies already uses its own fares)</param>
        public static RouteForecast Forecast(Carrier carrier, Station a, Station b, FleetCoach f, int roundTrips, Route exclude = null, double fareFactor = 1)
        {
            if (roundTrips < 1) return new RouteForecast {RoundTrips = roundTrips, Seats = f.Coach.GetTotalSeatCapacity()};
            return Evaluate(Prepare(carrier, a, b, f, exclude, fareFactor), roundTrips);
        }

        /// <summary>The forecasts for each of the given numbers of round trips a day (the demand worked out once).</summary>
        public static List<RouteForecast> ForecastEach(Carrier carrier, Station a, Station b, FleetCoach f, IEnumerable<int> roundTrips, Route exclude = null, double fareFactor = 1)
        {
            var p = Prepare(carrier, a, b, f, exclude, fareFactor);
            return roundTrips.Select(n => Evaluate(p, n)).ToList();
        }

        private static string Cash(double v)
        {
            return (v < 0 ? "-$" : "$") + Math.Abs(Math.Round(v)).ToString("N0");
        }

        /// <summary>
        ///     For a passenger route flown by one coach that flies nothing else, every day at the same times: the
        ///     forecast for each number of round trips from 1 to what fits between 06:00 and 22:00 (or more, if it flies
        ///     more already), with the route's own service score, fares and competition. Null when the route doesn't
        ///     qualify.
        /// </summary>
        public static TripAdvice AdviseTrips(Carrier carrier, Route route)
        {
            if (!(route is PassengerRoute) || route.HasStopovers || route.TimeTable?.Entries == null) return null;
            var coaches = route.GetCoaches();
            if (coaches.Count != 1) return null;
            FleetCoach f = coaches[0];
            if (f.Routes.Count != 1) return null;
            int now = route.TimeTable.Entries.Count(e => e.Day == DayOfWeek.Monday)/2;
            if (now < 1 || route.TimeTable.Entries.Count != 14*now) return null;
            int max = Math.Max(now, RouteCreator.GetMaxRoundTrips(route.Destination1, route.Destination2, f.Coach.Type, (int) FleetCoachHelpers.GetMinTimeBetweenTrips(f).TotalMinutes, OpsType.Regular));
            var all = ForecastEach(carrier, route.Destination1, route.Destination2, f, Enumerable.Range(1, max), route);
            var advice = new TripAdvice {Now = now, Max = max, Current = all[now - 1], Best = all.OrderByDescending(x => x.ProfitPerDay).ThenBy(x => Math.Abs(x.RoundTrips - now)).First()};
            advice.Change = advice.Best.RoundTrips != now && advice.Gain > Math.Max(300, Math.Abs(advice.Current.ProfitPerDay)*0.05);
            if (advice.Change)
            {
                int n = advice.Best.RoundTrips;
                string what = n == now + 1 ? "One more round trip a day (" + n + ")"
                            : n == now - 1 ? "One round trip a day fewer (" + n + ")"
                            : "Flying " + n + " round trips a day instead of " + now;
                advice.Text = string.Format("{0} is forecast to earn about {1} more a day ({2} a day, about {3:0}% full).", what, Cash(advice.Gain), Cash(advice.Best.ProfitPerDay), advice.Best.Load*100);
            }
            return advice;
        }

        /// <summary>The forecasts for 1 to `maxTrips` round trips a day, and the one that earns the most a day.</summary>
        public static RouteForecast Best(Carrier carrier, Station a, Station b, FleetCoach f, int maxTrips, out List<RouteForecast> all, Route exclude = null, double fareFactor = 1)
        {
            all = ForecastEach(carrier, a, b, f, Enumerable.Range(1, Math.Max(1, maxTrips)), exclude, fareFactor);
            // the most profit a day; when every choice loses money, the smallest loss
            return all.OrderByDescending(x => x.ProfitPerDay).ThenBy(x => x.RoundTrips).First();
        }
    }
}
