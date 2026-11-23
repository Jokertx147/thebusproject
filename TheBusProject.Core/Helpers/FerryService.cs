using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Automatic ferry trips. An coach that has been idle (it did not fly yesterday) and is scheduled to fly from an station
    ///     other than where it stands gets an empty ferry trip there first. It costs fuel and takes time; a long ferry costs the
    ///     trips of that day.
    /// </summary>
    public static class FerryService
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        public static bool IsFerrying(FleetCoach f)
        {
            return f.FerryTo != null && f.FerryUntil > GameObject.GetInstance().GameTime;
        }

        /// <summary>Hours a ferry of this length takes: flying time plus taxi, take-off and landing.</summary>
        public static double Hours(FleetCoach f, double km)
        {
            return km/Math.Max(300, f.Coach.Type.CruisingSpeed) + 1.5;
        }

        public static double LegCost(FleetCoach f, Station from, Station to)
        {
            double km = MathHelpers.GetDistance(from, to);
            int seats = CharterService.CapacityOf(f);
            double fuel = StationHelpers.GetFuelPrice(from)*km*f.Coach.FuelConsumption*Math.Max(1, seats)*0.55;
            return Math.Round(fuel*1.1 + GeneralHelpers.GetInflationPrice(800));
        }

        public static double Cost(FleetCoach f, Station from, Station to)
        {
            return LegCost(f, from, to);
        }

        /// <summary>
        ///     Plans a ferry as a list of landings (the last one is the destination). A single hop when the coach can fly it,
        ///     otherwise stops at stations on the way that its range and bay allow. Returns null when there is no way.
        /// </summary>
        public static List<Station> Plan(FleetCoach f, Station from, Station to)
        {
            double range = f.Coach.Range*0.92;
            long bay = f.Coach.MinBayLength;
            if (to.GetMaxBayLength() < bay || from.GetMaxBayLength() < bay) return null;
            var path = new List<Station>();
            Station at = from;
            List<Station> pool = null;
            for (int hop = 0; hop < 10; hop++)
            {
                double left = MathHelpers.GetDistance(at, to);
                if (left <= range)
                {
                    path.Add(to);
                    return path;
                }
                if (pool == null) pool = Stations.GetAllActiveStations().Where(a => a.GetMaxBayLength() >= bay && a.GetMaxBayLength() >= 1000).ToList();
                Station best = null;
                double bestLeft = left*0.97;
                foreach (Station c in pool)
                {
                    if (c == at || path.Contains(c)) continue;
                    if (MathHelpers.GetDistance(at, c) > range) continue;
                    double l = MathHelpers.GetDistance(c, to);
                    if (l < bestLeft)
                    {
                        bestLeft = l;
                        best = c;
                    }
                }
                if (best == null) return null;
                path.Add(best);
                at = best;
            }
            return null;
        }

        public static double PlanKm(Station from, List<Station> plan)
        {
            double km = 0;
            Station at = from;
            foreach (Station a in plan)
            {
                km += MathHelpers.GetDistance(at, a);
                at = a;
            }
            return km;
        }

        public static double PlanHours(FleetCoach f, Station from, List<Station> plan)
        {
            double h = 0;
            Station at = from;
            foreach (Station a in plan)
            {
                h += Hours(f, MathHelpers.GetDistance(at, a));
                at = a;
            }
            return h;
        }

        public static double PlanCost(FleetCoach f, Station from, List<Station> plan)
        {
            double c = 0;
            Station at = from;
            foreach (Station a in plan)
            {
                c += LegCost(f, at, a);
                at = a;
            }
            return c;
        }

        /// <summary>Why the coach cannot ferry between two stations, or null when it can.</summary>
        public static string GetBlocker(FleetCoach f, Station from, Station to)
        {
            if (f.Coach.MinBayLength > to.GetMaxBayLength()) return "the bay at " + to.Profile.IATACode + " is too short for it";
            if (f.Coach.MinBayLength > from.GetMaxBayLength()) return "the bay at " + from.Profile.IATACode + " is too short for it";
            if (Plan(f, from, to) == null) return "no way found with its range of " + DistanceFormatter(f.Coach.Range) + " and bay needs (" + DistanceFormatter(MathHelpers.GetDistance(from, to)) + " away)";
            return null;
        }

        private static string DistanceFormatter(double km)
        {
            return MaintenanceService.DistanceText(km);
        }

        private static void Post(string title, string text)
        {
            GameObject game = GameObject.GetInstance();
            game.NewsBox.AddNews(new News(News.NewsType.CarrierNews, game.GameTime, title, text));
        }

        /// <summary>
        ///     Called for each coach before its trips of the day. Returns false when it must not fly today (ferrying, or no ferry possible).
        /// </summary>
        public static bool BeforeTrips(FleetCoach f, List<RouteTimeTableEntry> entries)
        {
            GameObject game = GameObject.GetInstance();
            DateTime today = game.GameTime.Date;
            Carrier carrier = f.Coach.Carrier;

            if (f.FerryTo != null)
            {
                if (f.FerryUntil > game.GameTime) return false;
                f.CurrentPosition = f.FerryTo;
                f.FerryTo = null;
                f.LastTripDate = today;
            }

            if (entries.Count == 0 || f.CurrentPosition == null) return true;
            RouteTimeTableEntry first = entries.OrderBy(e => e.Time).First();
            Station dep = first.DepartureStation;
            if (dep == null || dep == f.CurrentPosition) return true;
            //an coach that flew yesterday just carries on, as before
            if (f.LastTripDate >= today.AddDays(-1) && f.LastTripDate != DateTime.MinValue) return true;

            if (MathHelpers.GetDistance(f.CurrentPosition, dep) < 50) return true;

            string key = f.Name + ">" + dep.Profile.IATACode;
            string message;
            if (!Start(f, dep, out message, first.Time.TotalHours - 0.5))
            {
                if (Warned.Add(key))
                {
                    Post("No ferry trip possible", f.Name + " stands at " + f.CurrentPosition.Profile.IATACode + " but is scheduled from " + dep.Profile.IATACode + ". " + message + " It stays on the ground until that is fixed.");
                }
                return false;
            }
            Warned.Remove(key);
            //true when the ferry was short enough to be done before the first trip of the day
            return f.FerryTo == null;
        }

        /// <summary>
        ///     Starts a ferry trip to an station (planned over stops when it is far). When <paramref name="hoursBeforeTrip" /> is
        ///     given and the ferry fits in them, the coach is there at once; otherwise it is away for whole days.
        /// </summary>
        public static bool Start(FleetCoach f, Station to, out string message, double hoursBeforeTrip = -1)
        {
            GameObject game = GameObject.GetInstance();
            Carrier carrier = f.Coach.Carrier;
            Station from = f.CurrentPosition;
            if (from == null || to == null)
            {
                message = "Its position is unknown.";
                return false;
            }
            if (from == to)
            {
                message = f.Name + " is already at " + to.Profile.IATACode + ".";
                return false;
            }
            var plan = Plan(f, from, to);
            if (plan == null)
            {
                message = "It cannot ferry: " + GetBlocker(f, from, to) + ".";
                return false;
            }
            double cost = PlanCost(f, from, plan);
            if (carrier.Money < cost)
            {
                message = "It cannot ferry: there is not enough money for the fuel (" + cost.ToString("N0") + ").";
                return false;
            }
            double km = PlanKm(from, plan);
            double hours = PlanHours(f, from, plan);
            CarrierHelpers.AddCarrierInvoice(carrier, game.GameTime, Invoice.InvoiceType.TripExpenses, -cost);
            f.Coach.Flown += km;
            string via = plan.Count > 1 ? " via " + string.Join(", ", plan.Take(plan.Count - 1).Select(a => a.Profile.IATACode)) : "";
            string leg = from.Profile.IATACode + " - " + to.Profile.IATACode + via + " (" + DistanceFormatter(km) + ", " + hours.ToString("0.0") + " h, " + cost.ToString("N0") + ")";
            if (hoursBeforeTrip >= 0 && hours <= hoursBeforeTrip)
            {
                f.CurrentPosition = to;
                f.LastTripDate = game.GameTime.Date;
                Post("Ferry trip", f.Name + " ferried " + leg + " before its first trip today.");
                message = "Ferried.";
                return true;
            }
            int days = Math.Max(1, (int) Math.Ceiling(hours/24.0));
            f.FerryTo = to;
            f.FerryUntil = game.GameTime.Date.AddDays(days);
            Post("Ferry trip", f.Name + " is ferrying " + leg + ". It is there on " + f.FerryUntil.ToShortDateString() + ".");
            message = f.Name + " is ferrying " + leg + ". It arrives on " + f.FerryUntil.ToShortDateString() + ".";
            return true;
        }

        /// <summary>Why the coach cannot be ferried by hand now, or null when it can.</summary>
        public static string GetManualBlocker(FleetCoach f)
        {
            GameObject game = GameObject.GetInstance();
            if (f.Coach.BuiltDate > game.GameTime) return "it has not been delivered yet";
            if (f.Status != FleetCoach.CoachStatus.Stopped) return "stop it first";
            if (IsFerrying(f)) return "it is already on a ferry trip";
            if (CharterService.IsOnCharter(f)) return "it is out on a charter";
            if (CoachDealService.IsLeasedOut(f)) return "it is leased out";
            if (MaintenanceService.IsGrounded(f)) return "it is grounded";
            if (f.Drivers.Count < f.Coach.Type.DriverCrew) return "it needs a full driver crew (" + f.Drivers.Count + "/" + f.Coach.Type.DriverCrew + ")";
            return null;
        }

        /// <summary>Daily: ferries that have arrived put the coach at its destination (also for stopped coach).</summary>
        public static void ProcessDaily(Carrier carrier)
        {
            GameObject game = GameObject.GetInstance();
            foreach (FleetCoach f in carrier.Fleet.ToList())
            {
                if (f.FerryTo != null && f.FerryUntil <= game.GameTime)
                {
                    f.CurrentPosition = f.FerryTo;
                    f.FerryTo = null;
                    f.LastTripDate = game.GameTime.Date;
                    if (carrier == game.HumanCarrier)
                    {
                        Post("Ferry trip", f.Name + " has landed at " + f.CurrentPosition.Profile.IATACode + ".");
                    }
                }
            }
        }

        /// <summary>A sentence on where the coach is and, when it is not flying, why.</summary>
        public static string Diagnose(FleetCoach f)
        {
            GameObject game = GameObject.GetInstance();
            string at = f.CurrentPosition == null ? "unknown" : f.CurrentPosition.Profile.IATACode + " " + f.CurrentPosition.Profile.Name;
            if (f.Coach.BuiltDate > game.GameTime) return "Not delivered yet.";
            if (IsFerrying(f)) return "At " + at + ", on a ferry trip to " + f.FerryTo.Profile.IATACode + " until " + f.FerryUntil.ToShortDateString() + ".";
            if (CharterService.IsOnCharter(f)) return "At " + at + ", out on a charter.";
            if (CoachDealService.IsLeasedOut(f)) return "At " + at + ", leased out.";
            if (f.Routes.Count == 0) return "At " + at + ". No routes: schedule it from here, or use Ferry to bring it to one of your stations.";
            if (f.Status == FleetCoach.CoachStatus.Stopped) return "At " + at + ". It is stopped: press Start for it to run its schedule" + (f.Drivers.Count < f.Coach.Type.DriverCrew ? " (it first needs a full driver crew)" : "") + ".";
            if (MaintenanceService.IsGrounded(f)) return "At " + at + ", grounded until " + f.GroundedToDate.ToShortDateString() + ".";
            var entries = f.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == f)).ToList();
            if (entries.Count == 0) return "At " + at + ". It has routes but no trips in a timetable: add trips under its route's timetable.";
            var first = entries.OrderBy(e => e.Day).ThenBy(e => e.Time).First().DepartureStation;
            if (first != null && f.CurrentPosition != null && first != f.CurrentPosition && MathHelpers.GetDistance(first, f.CurrentPosition) >= 50)
            {
                string why = GetBlocker(f, f.CurrentPosition, first);
                return "At " + at + ", its schedule starts at " + first.Profile.IATACode + ". " + (why != null ? "No ferry is possible: " + why + "." : f.Coach.Carrier.AutoFerry ? "It ferries there on its first running day." : "Auto ferry is off, so it starts from there.");
            }
            return "At " + at + ", running its schedule.";
        }
    }
}
