using System;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>The extra costs of one trip that the original game did not charge.</summary>
    public class TripCostParts
    {
        public double Ground, Maintenance, EnRoute, Crew, Distribution, Overhead;
        public double Total => Ground + Maintenance + EnRoute + Crew + Distribution + Overhead;
    }

    /// <summary>
    ///     Realistic trip costs. Besides fuel, landing fees and catering, every trip now pays for ground handling,
    ///     line maintenance per trip hour, en-route air traffic charges, trip crew time (an coach flying all
    ///     day needs several crews, not the one the game hires), ticket sales and distribution, and overhead. All
    ///     amounts are in the game's base-year money and follow inflation. Options sets the level (original,
    ///     realistic, tough).
    /// </summary>
    public static class TripCosts
    {
        public static readonly string[] LevelNames = { "Original", "Realistic", "Tough" };

        /// <summary>0 original game costs, 1 realistic, 2 tough (the added costs count 40% more).</summary>
        public static int Level => GameObject.GetInstance()?.CostLevel ?? 1;

        public static bool Enabled => Level > 0;

        private static double Factor => Level >= 2 ? 1.4 : 1.0;

        public static string Describe(int level)
        {
            switch (level)
            {
                case 0: return "Original: trips pay only fuel, landing fees, passenger taxes, catering and cabin crew. Almost every route makes money.";
                case 2: return "Tough: like Realistic, but the added costs are 40% higher. Only well-run routes make money.";
                default: return "Realistic: trips also pay ground handling, maintenance per trip hour, en-route charges, crew time, ticket sales (5 to 8%) and overhead (7%), and owned coach cost 0.5% of their value a month whether they run or not. Short busy routes still earn well, long thin ones can lose.";
            }
        }

        /// <summary>The game's usual economy fare for a distance (as the route creator suggests it).</summary>
        public static double UsualFare(double km)
        {
            return PricingService.MarketFare(km);
        }

        /// <summary>Seats, or for cargo coach 1.5 "seats" per unit of cargo space (a 757 freighter is about 360).</summary>
        public static double Capacity(Coach a)
        {
            if (a.Type is CoachCargoType c) return Math.Max(10, c.CargoSize*1.5);
            return Math.Max(4, a.GetTotalSeatCapacity());
        }

        /// <summary>The share of ticket income paid for booking systems, agents and card fees.</summary>
        public static double DistributionShare(Carrier carrier)
        {
            bool online = carrier.Facilities != null && carrier.Facilities.Any(f => f != null && f.Uid == "105");
            return online ? 0.05 : 0.08;
        }

        public static TripCostParts Calculate(Coach coach, double km, double hours, double tickets)
        {
            var p = new TripCostParts();
            if (!Enabled || coach == null) return p;
            double cap = Capacity(coach);
            hours = Math.Max(0.3, hours);
            // base-year amounts (about 1/7 of 2007 dollars): a 150 seat jet pays about $1,050 handling in 2007
            p.Ground = GeneralHelpers.GetInflationPrice(10 + 1.1*cap);
            // line maintenance: a part per trip hour plus a part that grows with the coach's price
            p.Maintenance = hours*(GeneralHelpers.GetInflationPrice(4 + 0.75*cap) + 0.000008*coach.Type.Price);
            // en-route charges per km, more for bigger coach
            p.EnRoute = km*GeneralHelpers.GetInflationPrice(0.012 + 0.00045*cap);
            // the extra crews needed to keep the coach flying (the hired pilots are paid separately)
            int crew = Math.Max(1, coach.Type.CockpitCrew) + (coach.Type is CoachPassengerType pt ? pt.CabinCrew : 0);
            p.Crew = hours*crew*GeneralHelpers.GetInflationPrice(18);
            p.Distribution = Math.Max(0, tickets)*DistributionShare(coach.Carrier);
            // offices, IT, marketing and everything else that isn't a trip
            p.Overhead = Math.Max(0, tickets)*0.07;
            double f = Factor;
            // the game pays little for cargo: freighters (no cabin, cheaper handling per load) pay half
            if (coach.Type is CoachCargoType) f *= 0.5;
            p.Ground *= f;
            p.Maintenance *= f;
            p.EnRoute *= f;
            p.Crew *= f;
            p.Distribution *= f;
            p.Overhead *= f;
            return p;
        }

        /// <summary>Charges the extra costs of a landed trip to the carrier, the route and the coach.</summary>
        /// <returns>what was charged (0 with the original costs)</returns>
        public static double Charge(FleetCoach coach, Route route, double km, double hours, double tickets)
        {
            if (!Enabled) return 0;
            var p = Calculate(coach.Coach, km, hours, tickets);
            if (p.Total <= 0 || double.IsNaN(p.Total)) return 0;
            var carrier = coach.Coach.Carrier;
            var now = GameObject.GetInstance().GameTime;

            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.TripExpenses, -(p.Ground + p.EnRoute));
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Maintenances, -p.Maintenance);
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Wages, -p.Crew);
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.CarrierExpenses, -(p.Distribution + p.Overhead));

            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.TripExpenses, -(p.Ground + p.EnRoute + p.Distribution + p.Overhead)));
            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.Maintenances, -p.Maintenance));
            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.Wages, -p.Crew));

            var d = coach.Data;
            if (d != null)
            {
                d.AddOperatingValue(new OperatingValue("Ground handling", now.Year, now.Month, -p.Ground));
                d.AddOperatingValue(new OperatingValue("Maintenance", now.Year, now.Month, -p.Maintenance));
                d.AddOperatingValue(new OperatingValue("En-route charges", now.Year, now.Month, -p.EnRoute));
                d.AddOperatingValue(new OperatingValue("Crew", now.Year, now.Month, -p.Crew));
                d.AddOperatingValue(new OperatingValue("Ticket sales", now.Year, now.Month, -p.Distribution));
                d.AddOperatingValue(new OperatingValue("Overhead", now.Year, now.Month, -p.Overhead));
            }
            coach.Statistics?.AddStatisticsValue(now.Year, Models.General.Statistics.StatisticsTypes.GetStatisticsType("Coach_Income"), -p.Total);
            return p.Total;
        }
    }
}
