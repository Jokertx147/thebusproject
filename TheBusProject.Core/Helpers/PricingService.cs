using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Fares and how passengers react to them. The market fare follows distance with a falling rate per km
    ///     (about $105 for 400 km, $160 for 1,200 km, $250 for 3,000 km and $415 for 7,000 km in 2007). With
    ///     realistic costs on, demand falls when a fare is above the market fare and rises when it is below
    ///     (business and first class mind less), and on shared routes the cheaper, better carrier gets more of the
    ///     passengers. With the original costs the game's original fares and price behaviour are kept.
    /// </summary>
    public static class PricingService
    {
        public static bool Realistic => TripCosts.Enabled;

        /// <summary>The market economy fare for a distance, in today's money.</summary>
        public static double MarketFare(double km)
        {
            if (!Realistic)
            {
                double price = km*GeneralHelpers.GetInflationPrice(0.0084);
                double minimum = GeneralHelpers.GetInflationPrice(18);
                return price < minimum ? minimum + price/4 : price;
            }
            // a coach fare, in base-year dollars: about $18 for 100 km in 2007 money, $40 for 364 (Atlanta to
            // Charlotte), $85 for 1,200, and the rate per km falls the further you go
            double baseYear = 1.3
                              + 0.012*Math.Min(km, 400)
                              + 0.0075*Math.Max(0, Math.Min(km, 1200) - 400)
                              + 0.0045*Math.Max(0, km - 1200);
            return GeneralHelpers.GetInflationPrice(baseYear);
        }

        public static double MarketFare(Station a, Station b, CoachClass.ClassType type)
        {
            return MarketFare(MathHelpers.GetDistance(a, b))*GeneralHelpers.ClassToPriceFactor(type);
        }

        /// <summary>How much passengers mind the price: economy most, first class least.</summary>
        public static double Elasticity(CoachClass.ClassType type)
        {
            switch (type)
            {
                case CoachClass.ClassType.FirstClass: return 0.5;
                case CoachClass.ClassType.BusinessClass: return 0.8;
                default: return 1.3;
            }
        }

        /// <summary>The share of the normal demand at a fare: 1 at the market fare, less above it, more below it.</summary>
        public static double DemandFactor(double fare, double market, CoachClass.ClassType type)
        {
            if (!Realistic || market <= 0) return 1;
            if (fare <= 0) return 2;
            return Math.Max(0.03, Math.Min(2.0, Math.Pow(fare/market, -Elasticity(type))));
        }

        public static double DemandFactor(PassengerRoute route, CoachClass.ClassType type)
        {
            var c = route.GetRouteCoachClass(type);
            if (c == null) return 1;
            return DemandFactor(c.FarePrice, MarketFare(route.Destination1, route.Destination2, type), type);
        }

        /// <summary>
        ///     On a route several carriers fly: this route's attractiveness (service per dollar) against the average of
        ///     all of them, between 0.65 and 1.5. With one carrier it is 1.
        /// </summary>
        public static double CompetitionFactor(Route current, List<Route> routes, CoachClass.ClassType type)
        {
            var mine = Attractiveness(current, type);
            var all = routes.Distinct().Select(r => Attractiveness(r, type)).Where(v => v > 0).ToList();
            if (mine <= 0 || all.Count < 2) return 1;
            // a gentle effect: twice as attractive gives about 1.4 times the share
            return Math.Max(0.65, Math.Min(1.5, Math.Sqrt(mine/all.Average())));
        }

        /// <summary>
        ///     The share a route would win on a pair it does not run yet. A planned route has no fare and no
        ///     facilities to read, so its attractiveness is worked out from the fare it would charge and the name of
        ///     the carrier that would run it, at the service a route starts with, which is none. Without this a
        ///     forecast for a new route assumes the pair to itself however many coach lines already work it.
        /// </summary>
        public static double CompetitionFactor(Carrier carrier, double fare, List<Route> rivals, CoachClass.ClassType type)
        {
            if (carrier == null || fare <= 0 || rivals == null) return 1;
            double mine = Math.Sqrt(10)*AdvertisingService.ReputationFactor(carrier)*RatingsService.CustomerFactor(carrier)/fare;
            var all = rivals.Distinct().Select(r => Attractiveness(r, type)).Where(v => v > 0).ToList();
            if (all.Count == 0) return 1;
            all.Add(mine);
            return Math.Max(0.65, Math.Min(1.5, Math.Sqrt(mine/all.Average())));
        }

        private static double Attractiveness(Route r, CoachClass.ClassType type)
        {
            var pr = r as PassengerRoute;
            var c = pr?.GetRouteCoachClass(type);
            if (c == null || c.FarePrice <= 0) return 0;
            double service = Math.Sqrt(10 + Math.Max(0, pr.GetServiceLevel(type)));
            double reputation = AdvertisingService.ReputationFactor(r.Carrier)*RatingsService.CustomerFactor(r.Carrier);
            return service*reputation/c.FarePrice;
        }


        // ---- the numbers for the route page ----

        public class RouteMonth
        {
            public double Income, Costs;
            public double Profit => Income + Costs;
        }

        public static RouteMonth LastMonth(Route route)
        {
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            var inv = route.Invoices;
            var m = new RouteMonth();
            foreach (Invoice.InvoiceType t in Enum.GetValues(typeof (Invoice.InvoiceType)))
            {
                if (t == Invoice.InvoiceType.Total) continue;
                double v = inv.GetAmount(t, last.Year, last.Month);
                if (v > 0) m.Income += v;
                else m.Costs += v;
            }
            return m;
        }

        /// <summary>
        ///     The economy fare at which a trip with the route's coach, at the given share of seats filled, covers
        ///     its trip costs (fuel, fees, crew, maintenance, handling, sales and overhead) and its share of what the
        ///     coach costs to own; not salaries or rents.
        /// </summary>
        public static double BreakEvenFare(PassengerRoute route, double load)
        {
            var f = route.GetCoaches().FirstOrDefault();
            if (f?.Coach == null) return 0;
            var a = f.Coach;
            double km = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            double hours = km/Math.Max(100, a.Type.CruisingSpeed) + 0.5;
            int seats = Math.Max(1, a.GetTotalSeatCapacity());
            double pax = seats*Math.Max(0.1, load);
            double fuelPrice = StationHelpers.GetFuelPrice(route.Destination1);
            int maxSeats = a.Type is CoachPassengerType pt ? pt.MaxSeatingCapacity : seats;
            double fuel = fuelPrice*km*maxSeats*a.FuelConsumption*0.55 + fuelPrice*km*a.FuelConsumption*pax*0.45;
            double fees = StationHelpers.GetLandingFee(route.Destination2, a) + 5*pax;
            double cabin = (a.Type is CoachPassengerType p2 ? p2.Attendants : 0)*hours*route.Carrier.Fees.GetValue(Models.Carriers.FeeTypes.GetType("Cabin Wage"));
            // the share-of-sales costs grow with the fare: solve fare*pax = fixed + fare*pax*share
            var parts = TripCosts.Calculate(a, km, hours, 0);
            double fixedCost = fuel + fees + cabin + parts.Ground + parts.Maintenance + parts.EnRoute + parts.Crew + OwnershipPerTrip(f);
            double share = TripCosts.Enabled ? (TripCosts.DistributionShare(route.Carrier) + 0.07)*(TripCosts.Level >= 2 ? 1.4 : 1) : 0;
            // Business and first seats pay more, so they count as several economy seats. Only the classes the coach
            // really has: a route may sell a class the coach has no cabin for, and Coach.GetCoachClass answers
            // for such a class with the first cabin it does have, which would count the whole coach once per class.
            double weight = route.Classes.Sum(c =>
            {
                CoachClass cabin = a.Classes.FirstOrDefault(x => x.Type == c.Type);
                return cabin == null ? 0 : cabin.SeatingCapacity*GeneralHelpers.ClassToPriceFactor(c.Type);
            });
            double economyEquivalents = weight > 0 ? weight*Math.Max(0.1, load) : pax;
            return fixedCost/Math.Max(1, economyEquivalents*(1 - share));
        }

        /// <summary>An owned coach's monthly ownership cost spread over the trips it makes in a month.</summary>
        public static double OwnershipPerTrip(FleetCoach f)
        {
            double monthly = OwnershipService.MonthlyCost(f);
            if (monthly <= 0) return 0;
            int weekly;
            try { weekly = f.Routes.ToList().SelectMany(r => r.TimeTable.Entries.ToList()).Count(e => e.Coach == f); }
            catch (Exception) { weekly = 0; }
            double perMonth = weekly > 0 ? weekly*4.33 : 60;
            return monthly/perMonth;
        }

        /// <summary>The share of seats the route filled in the last trips it flew (0 when unknown).</summary>
        public static double RecentLoad(PassengerRoute route)
        {
            double v;
            try { v = route.GetFillingDegree(); }
            catch (Exception) { v = 0; }
            return double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Max(0, Math.Min(1, v));
        }
    }
}
