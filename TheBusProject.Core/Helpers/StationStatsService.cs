using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>One scheduled arrival or departure at an station.</summary>
    public class StationMovement
    {
        public Carrier Carrier { get; set; }
        public Route Route { get; set; }
        public Station Other { get; set; }
        public bool IsDeparture { get; set; }
        public DayOfWeek Day { get; set; }
        public TimeSpan Time { get; set; }
        public string TripCode { get; set; }
        public string Coach { get; set; }
    }

    /// <summary>One carrier's numbers at an station.</summary>
    public class StationCarrierStats
    {
        public Carrier Carrier { get; set; }
        public int Routes { get; set; }
        public int DeparturesPerWeek { get; set; }
        public double PassengersThisYear { get; set; }
        public double PassengersLastYear { get; set; }
        public double ArrivalsThisYear { get; set; }
        public int PassengerGates { get; set; }
    }

    /// <summary>Traffic, carrier shares and the weekly schedule of an station.</summary>
    public static class StationStatsService
    {
        private static StatisticsType Type(string name)
        {
            return StatisticsTypes.GetStatisticsType(name);
        }

        public static double Passengers(Station station, int year)
        {
            var t = Type("Passengers");
            return t == null ? 0 : station.Statistics.GetTotalValue(year, t);
        }

        public static double Arrivals(Station station, int year)
        {
            var t = Type("Arrivals");
            return t == null ? 0 : station.Statistics.GetTotalValue(year, t);
        }

        /// <summary>Every carrier that flies here or leases gates here, busiest first.</summary>
        public static List<StationCarrierStats> Carriers(Station station)
        {
            int year = GameObject.GetInstance().GameTime.Year;
            var pax = Type("Passengers");
            var arr = Type("Arrivals");
            var routes = StationHelpers.GetStationRoutes(station);
            var carriers = routes.Select(r => r.Carrier)
                                 .Concat(station.CarrierContracts.Select(c => c.Carrier))
                                 .Where(a => a != null)
                                 .Distinct()
                                 .ToList();
            var list = new List<StationCarrierStats>();
            foreach (var a in carriers)
            {
                var mine = routes.Where(r => r.Carrier == a).ToList();
                list.Add(new StationCarrierStats
                {
                    Carrier = a,
                    Routes = mine.Count,
                    DeparturesPerWeek = mine.Sum(r => r.TimeTable.Entries.Count(e => e.DepartureStation == station)),
                    PassengersThisYear = pax == null ? 0 : station.Statistics.GetStatisticsValue(year, a, pax),
                    PassengersLastYear = pax == null ? 0 : station.Statistics.GetStatisticsValue(year - 1, a, pax),
                    ArrivalsThisYear = arr == null ? 0 : station.Statistics.GetStatisticsValue(year, a, arr),
                    PassengerGates = station.CarrierContracts.Where(c => c.Carrier == a && c.TerminalType == Terminal.TerminalType.Passenger).Sum(c => c.NumberOfGates)
                });
            }
            return list.OrderByDescending(s => s.PassengersThisYear).ThenByDescending(s => s.DeparturesPerWeek).ToList();
        }

        /// <summary>The destinations people from here travelled to most (this game's passenger statistics).</summary>
        public static List<KeyValuePair<Station, long>> TopDestinations(Station station, int count)
        {
            return Stations.GetAllActiveStations()
                           .Select(a => new KeyValuePair<Station, long>(a, station.GetDestinationPassengerStatistics(a)))
                           .Where(p => p.Value > 0)
                           .OrderByDescending(p => p.Value)
                           .Take(count)
                           .ToList();
        }

        private static TimeSpan TripTime(Route route, RouteTimeTableEntry entry)
        {
            var type = entry.Coach?.Coach?.Type ?? route.GetCoaches().FirstOrDefault()?.Coach?.Type;
            if (type != null) return route.GetTripTime(type);
            double km = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            return TimeSpan.FromHours(km/750 + 0.5);
        }

        /// <summary>All the scheduled arrivals and departures here in a week.</summary>
        public static List<StationMovement> Movements(Station station)
        {
            var list = new List<StationMovement>();
            foreach (var route in StationHelpers.GetStationRoutes(station))
            {
                foreach (var e in route.TimeTable.Entries.ToList())
                {
                    if (e?.Destination == null) continue;
                    string coach = e.Coach?.Coach?.Type?.Name ?? "";
                    if (e.DepartureStation == station)
                    {
                        list.Add(new StationMovement
                        {
                            Carrier = route.Carrier, Route = route, Other = e.Destination.Station, IsDeparture = true,
                            Day = e.Day, Time = e.Time, TripCode = e.Destination.TripCode, Coach = coach
                        });
                    }
                    else if (e.Destination.Station == station)
                    {
                        var arrival = e.Time + TripTime(route, e);
                        int extraDays = (int) Math.Floor(arrival.TotalDays);
                        list.Add(new StationMovement
                        {
                            Carrier = route.Carrier, Route = route, Other = e.DepartureStation, IsDeparture = false,
                            Day = (DayOfWeek) (((int) e.Day + extraDays)%7), Time = TimeSpan.FromMinutes(Math.Floor(arrival.TotalMinutes%1440)),
                            TripCode = e.Destination.TripCode, Coach = coach
                        });
                    }
                }
            }
            return list.OrderBy(m => ((int) m.Day + 6)%7).ThenBy(m => m.Time).ToList();
        }

        /// <summary>Movements per hour of one day (24 numbers).</summary>
        public static int[] PerHour(IEnumerable<StationMovement> movements, DayOfWeek day)
        {
            var hours = new int[24];
            foreach (var m in movements.Where(m => m.Day == day))
                hours[Math.Max(0, Math.Min(23, m.Time.Hours))]++;
            return hours;
        }

        /// <summary>The gates of each built terminal and who uses them.</summary>
        public static List<string> GateLines(Station station)
        {
            var lines = new List<string>();
            var now = GameObject.GetInstance().GameTime;
            foreach (var t in station.Terminals.StationTerminals.OrderBy(t => t.DeliveryDate))
            {
                const string kind = "passenger";
                if (t.DeliveryDate > now)
                {
                    lines.Add(string.Format("{0} ({1}): {2} {3} gates, opens {4}", t.Name, kind, t.Gates.NumberOfGates, kind, t.DeliveryDate.ToShortDateString()));
                    continue;
                }
                string owner = t.Carrier != null ? "owned by " + t.Carrier.Profile.Name : "station terminal";
                lines.Add(string.Format("{0} ({1}, {2}): {3} gates", t.Name, kind, owner, t.Gates.NumberOfDeliveredGates));
            }
            foreach (Terminal.TerminalType type in new[] { Terminal.TerminalType.Passenger })
            {
                int total = station.Terminals.GetNumberOfGates(type);
                if (total == 0) continue;
                var users = station.CarrierContracts.Where(c => c.TerminalType == type)
                                   .GroupBy(c => c.Carrier)
                                   .Select(g => new { Carrier = g.Key, Gates = g.Sum(c => c.NumberOfGates) })
                                   .OrderByDescending(x => x.Gates)
                                   .ToList();
                int free = station.Terminals.GetFreeGates(type);
                lines.Add(string.Format(
                    "{0} gates: {1} in all, {2} free{3}",
                    "Passenger", total, free,
                    users.Count == 0 ? "" : "  |  " + string.Join(", ", users.Select(u => u.Carrier.Profile.IATACode + " " + u.Gates))));
            }
            return lines;
        }
    }
}
