using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Helpers
{
    /// <summary>One month of an coach's money.</summary>
    public class CoachMonth
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double Income { get; set; }
        public double Costs { get; set; }
        public double Profit => Income + Costs;
        public Dictionary<string, double> Parts { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>One year of an coach's flying.</summary>
    public class CoachYear
    {
        public int Year { get; set; }
        public double Trips { get; set; }
        public double Passengers { get; set; }
        public double Cargo { get; set; }
        public double LoadFactor { get; set; }
    }

    /// <summary>Per-coach history (money per month, flying per year) and comparison of coach types.</summary>
    public static class CoachHistoryService
    {
        public static readonly string[] IncomeTypes = { "Tickets", "In-trip Services" };

        /// <summary>The last months of money, newest first (months without any value are left out).</summary>
        public static List<CoachMonth> Months(FleetCoach f, int count)
        {
            var values = f.Data?.Values?.ToList() ?? new List<OperatingValue>();
            return values.GroupBy(v => v.Year*100 + v.Month)
                         .OrderByDescending(g => g.Key)
                         .Take(count)
                         .Select(g => new CoachMonth
                         {
                             Year = g.Key/100,
                             Month = g.Key%100,
                             Income = g.Where(v => IncomeTypes.Contains(v.Type)).Sum(v => v.Value),
                             Costs = g.Where(v => !IncomeTypes.Contains(v.Type)).Sum(v => v.Value),
                             Parts = g.GroupBy(v => v.Type).ToDictionary(x => x.Key, x => x.Sum(v => v.Value))
                         })
                         .ToList();
        }

        /// <summary>The profit of the last 12 months of records.</summary>
        public static double ProfitLast12(FleetCoach f)
        {
            var now = GameObject.GetInstance().GameTime;
            int from = now.AddMonths(-11).Year*100 + now.AddMonths(-11).Month;
            return (f.Data?.Values?.ToList() ?? new List<OperatingValue>()).Where(v => v.Year*100 + v.Month >= from).Sum(v => v.Value);
        }

        private static double Stat(FleetCoach f, int year, string type)
        {
            var t = StatisticsTypes.GetStatisticsType(type);
            return t == null || f.Statistics == null ? 0 : f.Statistics.GetStatisticsValue(year, t);
        }

        /// <summary>Trips, passengers and load factor for each year the coach has flown.</summary>
        public static List<CoachYear> Years(FleetCoach f)
        {
            var arrivals = StatisticsTypes.GetStatisticsType("Arrivals");
            var years = f.Statistics?.StatValues?.Where(s => s.Stat != null && s.Stat.Shortname == arrivals?.Shortname).Select(s => s.Year).Distinct().ToList()
                        ?? new List<int>();
            int now = GameObject.GetInstance().GameTime.Year;
            if (!years.Contains(now)) years.Add(now);
            int seats = f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Cargo ? 0 : f.Coach.GetTotalSeatCapacity();
            return years.OrderByDescending(y => y).Select(y =>
                {
                    double trips = Stat(f, y, "Arrivals");
                    double pax = Stat(f, y, "Passengers");
                    return new CoachYear
                    {
                        Year = y,
                        Trips = trips,
                        Passengers = pax,
                        Cargo = Stat(f, y, "Cargo"),
                        LoadFactor = trips > 0 && seats > 0 ? pax/trips/seats : 0
                    };
                }).ToList();
        }

        public static string MonthName(int year, int month)
        {
            return new DateTime(year, Math.Max(1, Math.Min(12, month)), 1).ToString("MMM yyyy");
        }

        // ---- comparing coach types ----

        public static List<CoachType> AvailableTypes()
        {
            var now = GameObject.GetInstance().GameTime;
            return CoachTypes.GetTypes(t => t.Produced.From <= now && t.Produced.To >= now.AddYears(-30))
                                .OrderBy(t => t.Manufacturer?.Name).ThenBy(t => t.Name)
                                .ToList();
        }

        public static int Capacity(CoachType t)
        {
            if (t is CoachPassengerType p) return p.MaxSeatingCapacity;
            if (t is CoachCargoType c) return (int) c.CargoSize;
            return t.GetCapacity();
        }

        public static TimeSpan TripTime(CoachType t, double km)
        {
            return TimeSpan.FromHours(km/Math.Max(100, t.CruisingSpeed) + 0.5);
        }

        /// <summary>The fuel cost of one trip with every seat (or all cargo space) filled, at today's average fuel price.</summary>
        public static double FuelPerTrip(CoachType t, double km)
        {
            double price = GameObject.GetInstance().FuelPrice;
            return price*km*Capacity(t)*t.FuelConsumption;
        }

        /// <summary>The fuel cost per seat (or per ton) and 100 km.</summary>
        public static double FuelPerSeat100(CoachType t)
        {
            return GameObject.GetInstance().FuelPrice*100*t.FuelConsumption;
        }

        /// <summary>Why the type can't fly the distance or use the stations, or null.</summary>
        public static string CanFly(CoachType t, double km, Station a = null, Station b = null)
        {
            if (km > t.Range) return "too far (range " + t.Range.ToString("N0") + " km)";
            foreach (var ap in new[] { a, b })
            {
                if (ap != null && ap.GetMaxBayLength() < t.MinBayLength)
                    return "bay at " + ap.Profile.IATACode + " too short (" + ap.GetMaxBayLength().ToString("N0") + " m, needs " + t.MinBayLength.ToString("N0") + " m)";
            }
            return null;
        }
    }
}
