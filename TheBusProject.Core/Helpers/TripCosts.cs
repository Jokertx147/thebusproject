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
        public double Ground, Maintenance, EnRoute, Crew, Distribution, Overhead, Insurance;
        public double Total => Ground + Maintenance + EnRoute + Crew + Distribution + Overhead + Insurance;
    }

    /// <summary>
    ///     What a trip really costs. Besides fuel, station fees and refreshments, every trip pays for the station
    ///     stand, the garage per hour on the road, tolls and road tax per km, the driver's time (a coach working
    ///     all day needs several drivers, not the one the game hires), liability insurance, ticket sales and
    ///     overhead. All amounts are in the game's base-year money and follow inflation. Options sets the level
    ///     (original, realistic, tough).
    ///
    ///     The weights come from Greyhound's own accounts for 2004, where $956.7m of revenue earned an operating
    ///     margin of nought and a net loss of 2.4%: maintenance 10.6% of revenue, transportation 24.5%, agents'
    ///     commissions and station costs 17.7%, insurance and safety 7.8%, general and administrative 13.4%,
    ///     depreciation 6.3%, rents 8.0%. Intercity coach is a thin business and the game should feel like it.
    ///     Most of what was added here is charged by the hour and the mile rather than off the ticket, because a
    ///     coach that runs half empty still burns the fuel, the driver and the garage, and ought to lose money
    ///     rather than merely earn less.
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
                case 0: return "Original: trips pay only fuel, station fees, passenger taxes and refreshments. Almost every route makes money.";
                case 2: return "Tough: like Realistic, but the added costs are 40% higher. Only well-run routes make money.";
                default: return "Realistic: trips also pay the station's stand, the garage per hour on the road, tolls and road tax per km, the driver's time, liability insurance, ticket sales (5 to 8%) and overhead (9%), and owned coaches cost 0.5% of their value a month whether they run or not. The weights follow a real coach company's accounts, where the margin on the whole business was about nothing. A busy route earns well; a half-empty one loses money rather than merely earning less.";
            }
        }

        /// <summary>The game's usual economy fare for a distance (as the route creator suggests it).</summary>
        public static double UsualFare(double km)
        {
            return PricingService.MarketFare(km);
        }

        /// <summary>The coach's seats.</summary>
        public static double Capacity(Coach a)
        {
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
            // the stand and the people who work it. Greyhound's agents' commissions and station costs together
            // were 17.7% of revenue, so a departure is dearer than the $20 this used to charge
            p.Ground = GeneralHelpers.GetInflationPrice(2.6 + 0.055*cap);
            // The garage: a part per hour on the road plus a part that grows with the coach's price. Greyhound
            // spent 10.6% of a revenue of $3.32 a bus mile here, so 35 cents of every bus mile in 2004 and about
            // 44 in 2015 money, and this charged four times that: a playthrough's whole upkeep came to between
            // $1.05 and $1.93 a bus mile, four fifths of it from this line. The share of revenue was what hid
            // it, because the game earns $8.67 a bus mile against Greyhound's $3.32, so a cost four times too
            // high still read as 12% of revenue against their 10.6%.
            p.Maintenance = hours*(GeneralHelpers.GetInflationPrice(0.64 + 0.024*cap) + 0.0000021*coach.Type.Price);
            // tolls, road tax and tyres per km, more for a heavier coach (about 3 cents a km in 2007)
            p.EnRoute = km*GeneralHelpers.GetInflationPrice(0.002 + 0.00004*cap);
            // The driver's time, at the real wage: the BLS put intercity and transit drivers at $23.56 an hour in
            // 2023, which deflates to about $18.26 in 2015, and 2.16 in 1960 dollars comes to that. The standing
            // part of a named driver's pay - benefits, payroll tax, standby - is the monthly packet instead, and
            // the attendants are not counted here because the landing charges them separately at the Cabin Wage.
            int crew = Math.Max(1, coach.Type.DriverCrew);
            p.Crew = hours*crew*GeneralHelpers.GetInflationPrice(2.16);
            // liability cover, which a coach carries whether anybody is aboard or not: 7.8% of Greyhound's revenue
            // and 26 cents of every bus mile. By the hour and the seat, so an empty run pays it in full
            p.Insurance = hours*GeneralHelpers.GetInflationPrice(2.4 + 0.05*cap) + 0.0000028*coach.Type.Price;
            p.Distribution = Math.Max(0, tickets)*DistributionShare(coach.Carrier);
            // offices, IT, marketing and everything else that isn't a trip
            p.Overhead = Math.Max(0, tickets)*0.09;
            double f = Factor;
            p.Ground *= f;
            p.Maintenance *= f;
            p.EnRoute *= f;
            p.Crew *= f;
            p.Distribution *= f;
            p.Overhead *= f;
            p.Insurance *= f;
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
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Fees, -p.Insurance);
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Wages, -p.Crew);
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.CarrierExpenses, -(p.Distribution + p.Overhead));

            route?.AddRouteInvoice(new Invoice(now, Invoice.InvoiceType.TripExpenses, -(p.Ground + p.EnRoute + p.Distribution + p.Overhead + p.Insurance)));
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
                d.AddOperatingValue(new OperatingValue("Insurance", now.Year, now.Month, -p.Insurance));
            }
            coach.Statistics?.AddStatisticsValue(now.Year, Models.General.Statistics.StatisticsTypes.GetStatisticsType("Coach_Income"), -p.Total);
            return p.Total;
        }
    }
}
