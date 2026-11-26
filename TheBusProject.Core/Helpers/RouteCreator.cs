using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    public enum OpsType
    {
        Regular,

        Business,

        Whole_Day
    }

    public enum ServiceTier
    {
        Basic,

        Standard,

        Premium
    }

    /// <summary>
    ///     What the player chose when creating a route from the advisory tab.
    /// </summary>
    public class NewRouteRequest
    {
        public Station Origin { get; set; }

        public Station Destination { get; set; }

        public ServiceTier Tier { get; set; }

        //null when the coach is assigned later
        public FleetCoach Coach { get; set; }

        public OpsType Operation { get; set; }

        public int TurnaroundMinutes { get; set; }

        public TimeSpan StartTime { get; set; }

        public int RoundTripsPerDay { get; set; }

        public bool StartCoach { get; set; }

        /// <summary>The places the service calls at on the way, in order. Empty for a non-stop run.</summary>
        public List<Station> Stops { get; set; } = new List<Station>();

        //passenger (the default) or cargo
        public Route.RouteType RouteType { get; set; }

        //the price per unit of cargo (cargo routes only)
        public double CargoPrice { get; set; }
    }

    /// <summary>
    ///     Creates a passenger route, and optionally its daily schedule, in one go.
    /// </summary>
    public static class RouteCreator
    {
        public static readonly List<int> TurnaroundOptions = new List<int> { 30, 45, 60, 75, 90, 105, 120 };

        //the human carrier's passenger coach that can fly between the two stations
        public static List<FleetCoach> GetCandidateCoaches(
            Station origin,
            Station destination,
            Route.RouteType routeType = Route.RouteType.Passenger)
        {
            return GetCandidateCoaches(new List<Station> { origin, destination }, routeType);
        }

        /// <summary>
        ///     The coaches that could work a service calling at these places in order. Only a leg has to be within
        ///     the coach's range, so a service with stops can be run by a coach that could never do it in one go.
        /// </summary>
        public static List<FleetCoach> GetCandidateCoaches(
            IList<Station> calls,
            Route.RouteType routeType = Route.RouteType.Passenger)
        {
            double distance = 0;
            long bay = long.MaxValue;
            for (int i = 0; i < calls.Count; i++)
            {
                bay = Math.Min(bay, calls[i].GetMaxBayLength());
                if (i < calls.Count - 1) distance = Math.Max(distance, MathHelpers.GetDistance(calls[i], calls[i + 1]));
            }

            return GameObject.GetInstance()
                             .HumanCarrier.DeliveredFleet.Where(
                                 f => f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Passenger)
                             .Where(f => f.Routes.Count == 0 && !CoachDealService.IsLeasedOut(f))
                             .Where(f => f.Coach.Range >= distance && f.Coach.MinBayLength <= bay)
                             .OrderBy(f => f.Name)
                             .ToList();
        }

        /// <summary>
        ///     Why the player's idle coach of the route's kind can't fly it: "N010DL (Airbus A350-900) needs 2,810 m of
        ///     bay: DUB has 2,800 m". One line per type and reason.
        /// </summary>
        public static List<string> WhyNot(Station origin, Station destination, Route.RouteType routeType = Route.RouteType.Passenger)
        {
            double distance = MathHelpers.GetDistance(origin, destination);
            var lines = new List<string>();
            var idle = GameObject.GetInstance().HumanCarrier.DeliveredFleet
                                 .Where(f => f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Passenger
                                             && f.Routes.Count == 0 && !CoachDealService.IsLeasedOut(f));
            foreach (var g in idle.GroupBy(f => f.Coach.Type))
            {
                var f = g.OrderBy(x => x.Coach.MinBayLength).First();
                string names = string.Join(", ", g.Select(x => x.Name).Take(3)) + (g.Count() > 3 ? " and " + (g.Count() - 3) + " more" : "");
                var shortEnds = new[] {origin, destination}.Where(a => a.GetMaxBayLength() < f.Coach.MinBayLength).ToList();
                if (shortEnds.Count > 0)
                {
                    lines.Add(names + " (" + g.Key.Name + ") need" + (g.Count() == 1 ? "s" : "") + " " + f.Coach.MinBayLength.ToString("N0") + " m of bay: "
                              + string.Join(", ", shortEnds.Select(BayService.Describe)) + ".");
                }
                else if (g.Max(x => x.Coach.Range) < distance)
                {
                    lines.Add(names + " (" + g.Key.Name + "): range " + g.Max(x => x.Coach.Range).ToString("N0") + " km, the route is " + Math.Round(distance).ToString("N0") + " km.");
                }
            }
            return lines;
        }

        //true when a round trip takes so long that the schedule is made weekly instead of daily
        public static bool IsLongRoute(Station origin, Station destination, CoachType type, int turnaroundMinutes)
        {
            TimeSpan minTripTime = MathHelpers.GetTripTime(origin, destination, type).Add(new TimeSpan(0, turnaroundMinutes, 0));

            return minTripTime.Hours >= 12 || minTripTime.Days >= 1;
        }

        //how many round trips a day fit: 06:00-22:00 for a regular schedule, the whole day otherwise
        public static int GetMaxRoundTrips(Station origin, Station destination, CoachType type, int turnaroundMinutes, OpsType operation)
        {
            TimeSpan minTripTime = MathHelpers.GetTripTime(origin, destination, type).Add(new TimeSpan(0, turnaroundMinutes, 0));

            // a coach cannot be worked round the clock: it has to be fuelled, cleaned, swept out and walked round
            // before it goes again, and the law wants a driver's inspection every day. Five hours of the
            // twenty-four belong to the yard, and a route that only runs in the daytime gets a shorter day still.
            int maxHours = operation == OpsType.Whole_Day ? 19 : 14;

            return Math.Max(1, (int) Math.Floor(maxHours * 60 / (2 * minTripTime.TotalMinutes)));
        }

        //the possible first departures for a number of round trips
        public static List<TimeSpan> GetStartTimes(Station origin, Station destination, CoachType type, int turnaroundMinutes, int roundTrips, OpsType operation)
        {
            TimeSpan minTripTime = MathHelpers.GetTripTime(origin, destination, type).Add(new TimeSpan(0, turnaroundMinutes, 0));

            int firstHour = operation == OpsType.Whole_Day ? 0 : 6;

            // A coach line is not a day job. The old rule was that the whole working cycle had to be finished by
            // ten at night, which on a nine hour run left exactly one departure to choose from, six in the
            // morning, and made the overnight service -- the thing intercity coaches are for -- impossible to
            // schedule at all. What a daily timetable actually needs is that the cycle fits inside the day, so
            // the coach is back where it started in time to go again. Within that it may leave whenever it likes
            // and drive through the night.
            double cycleHours = minTripTime.TotalHours*roundTrips*2;
            int lastHour = cycleHours >= 24 ? firstHour : Math.Max(firstHour, 23);

            var times = new List<TimeSpan>();

            for (int hour = firstHour; hour <= lastHour; hour++)
            {
                times.Add(new TimeSpan(hour, 0, 0));
                times.Add(new TimeSpan(hour, 30, 0));
            }

            return times;
        }

        //creates the route (and its schedule); returns the route, or null with a message that says why not
        public static Route Create(NewRouteRequest request, out string message)
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            Station origin = request.Origin;
            Station destination = request.Destination;

            if (carrier.Routes.Exists(
                r =>
                ((r.Destination1 == origin && r.Destination2 == destination)
                    || (r.Destination1 == destination && r.Destination2 == origin))))
            {
                message = "You already have a route between these stations.";
                return null;
            }

            // every leg has to be one the coach can actually run, and one a driver may legally drive
            message = RouteStops.WhyNot(
                origin, destination, request.Stops, request.Coach?.Coach,
                request.Coach == null ? 1 : Math.Max(1, request.Coach.NumberOfDrivers));
            if (message != null)
            {
                return null;
            }

            try
            {
                var calls = new List<Station> { origin };
                if (request.Stops != null) calls.AddRange(request.Stops);
                calls.Add(destination);
                CarrierHelpers.IsRouteDestinationsOk(carrier, calls, Route.RouteType.Passenger);
            }
            catch (Exception ex)
            {
                message = "The route could not be created: " + DescribeError(ex.Message);
                return null;
            }

            int year = GameObject.GetInstance().GameTime.Year;
            double baseFare = PassengerHelpers.GetPassengerPrice(origin, destination);

            Route route = CreatePassengerRoute(carrier, origin, destination, request.Tier, year, baseFare);

            // the places it calls at on the way, each leg a route of its own with its own fare
            RouteStops.Set(route, request.Stops);
            route.Season = Weather.Season.AllYear;
            carrier.AddRoute(route);
            ContractService.LinkRoute(carrier, route);

            message = string.Format("Route {0} created.", RouteStops.Describe(route));

            return AssignCoach(route, carrier, request, ref message);
        }

        private static Route CreatePassengerRoute(
            Carrier carrier,
            Station origin,
            Station destination,
            ServiceTier tier,
            int year,
            double baseFare)
        {
            var route = new PassengerRoute(Guid.NewGuid().ToString(), origin, destination, GameObject.GetInstance().GameTime, 0);

            foreach (CoachClass.ClassType classType in CoachClass.GetCoachTypes())
            {
                if ((int) classType > year)
                {
                    continue;
                }

                RouteCoachClass routeClass = route.GetRouteCoachClass(classType);

                if (routeClass == null)
                {
                    continue;
                }

                routeClass.FarePrice = baseFare * GeneralHelpers.ClassToPriceFactor(classType);

                foreach (RouteFacility.FacilityType facilityType in Enum.GetValues(typeof (RouteFacility.FacilityType)))
                {
                    if (year < (int) facilityType || RouteFacilities.IsDiscontinued(facilityType, year))
                    {
                        continue;
                    }

                    RouteFacility facility = PickFacility(carrier, facilityType, tier);

                    if (facility != null)
                    {
                        routeClass.AddFacility(facility);
                    }
                }
            }

            return route;
        }

        //puts the requested coach on the new route with a daily schedule
        /// <summary>Puts an coach on an existing route that has no coach yet. Returns false, with the reason in message, when it can't be done.</summary>
        public static bool AssignCoach(Route route, NewRouteRequest request, out string message)
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            FleetCoach coach = request.Coach;
            message = "";

            if (route == null || !carrier.Routes.Contains(route))
            {
                message = "That route is not one of yours.";
                return false;
            }

            if (coach == null)
            {
                message = "Pick a coach first.";
                return false;
            }

            if (route.HasCoach)
            {
                message = "This route already has a coach.";
                return false;
            }

            if (!GetCandidateCoaches(route.Destination1, route.Destination2, route.Type).Contains(coach))
            {
                message = coach.Name + " can't run this route (range, bay, or already on a route).";
                return false;
            }

            request.Origin = route.Destination1;
            request.Destination = route.Destination2;
            AssignCoach(route, carrier, request, ref message);

            if (!coach.Routes.Contains(route))
            {
                message = message.Trim();
                return false;
            }

            message = (coach.Name + " is assigned to " + route.Destination1.Profile.IATACode + " - "
                       + route.Destination2.Profile.IATACode + ". " + message).Trim();
            return true;
        }

        /// <summary>Takes a route off its coach and puts it on another one (a new schedule). The old coach is stopped when it has no other route. Nothing changes when it fails.</summary>
        public static bool MoveRoute(Route route, NewRouteRequest request, out string message)
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            FleetCoach target = request.Coach;
            message = "";

            if (route == null || !carrier.Routes.Contains(route) || !route.HasCoach)
            {
                message = "That route has no coach to move it from.";
                return false;
            }

            if (target == null)
            {
                message = "Pick the coach to move the route to.";
                return false;
            }

            List<FleetCoach> olds = route.GetCoaches().ToList();
            if (olds.Contains(target))
            {
                message = "The route is already on " + target.Name + ".";
                return false;
            }

            if (!GetCandidateCoaches(route.Destination1, route.Destination2, route.Type).Contains(target))
            {
                message = target.Name + " can't run this route (range, bay, or already on a route).";
                return false;
            }

            var backups = olds.ToDictionary(
                o => o,
                o => new Tuple<List<RouteTimeTableEntry>, FleetCoach.CoachStatus>(
                    route.TimeTable.Entries.Where(e => e.Coach == o).ToList(), o.Status));

            foreach (FleetCoach old in olds)
            {
                old.RemoveRoute(route);
            }

            request.Origin = route.Destination1;
            request.Destination = route.Destination2;
            string result = "";
            AssignCoach(route, carrier, request, ref result);

            if (!target.Routes.Contains(route))
            {
                foreach (FleetCoach old in olds)
                {
                    route.TimeTable.Entries.AddRange(backups[old].Item1);
                    old.AddRoute(route);
                    old.Status = backups[old].Item2;
                }

                message = result.Trim() + " The route stays on " + string.Join(", ", olds.Select(o => o.Name)) + ".";
                return false;
            }

            foreach (FleetCoach old in olds)
            {
                if (old.Routes.Count == 0)
                {
                    old.Status = FleetCoach.CoachStatus.Stopped;
                }
            }

            message = ("Route " + route.Destination1.Profile.IATACode + " - " + route.Destination2.Profile.IATACode + " moved from "
                       + string.Join(", ", olds.Select(o => o.Name)) + " to " + target.Name + ". " + result).Trim();
            return true;
        }

        private static Route AssignCoach(Route route, Carrier carrier, NewRouteRequest request, ref string message)
        {
            FleetCoach coach = request.Coach;

            if (coach == null)
            {
                message += " Assign a coach to it in the route planner to start running.";
                return route;
            }

            List<string> codes = carrier.GetTripCodes();
            string tripCode1 = codes[0];
            string tripCode2 = codes[1];

            RouteTimeTable timeTable = AIHelpers.CreateCoachRouteTimeTable(
                route,
                coach,
                request.RoundTripsPerDay,
                true,
                request.TurnaroundMinutes,
                request.StartTime,
                tripCode1,
                tripCode2);

            var existing = new List<RouteTimeTableEntry>(
                coach.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == coach)));

            if (!TimeTableHelpers.IsTimeTableValid(timeTable, coach, existing, false))
            {
                message += " The schedule clashes with the trips " + coach.Name
                           + " already has, so no trips were scheduled. Set the timetable in the route planner.";
                return route;
            }

            foreach (RouteTimeTableEntry entry in timeTable.Entries)
            {
                if (!TimeTableHelpers.IsRouteEntryInOccupied(entry, coach))
                {
                    entry.TimeTable = route.TimeTable;
                    route.TimeTable.AddEntry(entry);
                }
            }

            coach.AddRoute(route);

            if (coach.Status != FleetCoach.CoachStatus.Stopped)
            {
                message += " " + coach.Name + " is scheduled on it.";
            }
            else if (request.StartCoach)
            {
                if (coach.Drivers.Count == coach.Coach.Type.DriverCrew)
                {
                    coach.Status = GameObject.GetInstance().DayRoundEnabled
                        ? FleetCoach.CoachStatus.OnRoute
                        : FleetCoach.CoachStatus.ToRouteStart;
                    message += " " + coach.Name + " is running it.";
                }
                else
                {
                    message += " " + coach.Name + " has no full driver crew yet, so it was not started.";
                }
            }
            else
            {
                message += " " + coach.Name + " is scheduled but stopped.";
            }

            return route;
        }

        //true when the human carrier has no passenger contract at the station, so a contract can fix a missing gate
        public static bool NeedsContract(Station station, Terminal.TerminalType terminalType = Terminal.TerminalType.Passenger)
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;

            return !station.GetCarrierContracts(carrier).Any(c => c.TerminalType == terminalType);
        }

        public static int GetFreePassengerGates(Station station)
        {
            return station.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
        }

        public static int GetFreeGates(Station station, Terminal.TerminalType terminalType)
        {
            return station.Terminals.GetFreeGates(terminalType);
        }

        //signs a passenger gate contract (up to two gates, two years) and takes the gates; same rules as the station page
        public static void SignContract(
            Station station,
            StationContract.ContractType contractType,
            Terminal.TerminalType terminalType = Terminal.TerminalType.Passenger)
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            DateTime now = GameObject.GetInstance().GameTime;
            bool cargo = terminalType == Terminal.TerminalType.Cargo;

            int gates = Math.Min(2, GetFreeGates(station, terminalType));

            // a full contract includes the basic facility of the kind (check-in for passengers, cargo for cargo)
            if (contractType == StationContract.ContractType.Full)
            {
                StationFacility.FacilityType facilityType = cargo
                    ? StationFacility.FacilityType.Cargo
                    : StationFacility.FacilityType.CheckIn;

                if (station.GetStationFacility(carrier, facilityType, cargo).TypeLevel == 0)
                {
                    StationFacility basic = StationFacilities.GetFacilities(facilityType).Find(f => f.TypeLevel == 1);

                    station.AddStationFacility(carrier, basic, now);
                    CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Purchases, -basic.Price);
                }
            }

            double yearlyPayment = StationHelpers.GetYearlyContractPayment(station, contractType, gates, 2);

            var contract = new StationContract(
                carrier, station, contractType, terminalType, now, gates, 2, yearlyPayment, true);

            StationHelpers.AddCarrierContract(contract);

            for (int i = 0; i < gates; i++)
            {
                Gate gate = station.Terminals.GetGates().FirstOrDefault(g => g.Carrier == null);
                if (gate != null)
                {
                    gate.Carrier = carrier;
                }
            }
        }

        public static string DescribeError(string code)
        {
            switch (code)
            {
                case "3000":
                    return "No free gates at one of the stations. You need a station contract with free gates at both ends.";
                case "3001":
                    return "The distance is too short, or longer than any available coach can run.";
                case "3002":
                    return "The route is not of an appropriate type for these stations.";
                case "3003":
                    return "Both stations need a cargo facility of yours (Stations tab > Facilities), or a Full cargo gate contract, which includes one.";
                case "3004":
                    return "Your carrier has no license for this route. Buy a higher one under My Carrier > License.";
                case "3005":
                    return "There are trip restrictions between these countries.";
                default:
                    return code;
            }
        }

        //the facility that fits a service tier: the cheapest for basic, the best for premium, one in the middle for standard
        public static RouteFacility PickFacility(Carrier carrier, RouteFacility.FacilityType type, ServiceTier tier)
        {
            List<RouteFacility> facilities =
                CarrierHelpers.GetRouteFacilities(carrier, type).OrderBy(f => f.ServiceLevel).ToList();

            if (facilities.Count == 0)
            {
                return null;
            }

            switch (tier)
            {
                case ServiceTier.Basic:
                    return facilities.First();
                case ServiceTier.Premium:
                    return facilities.Last();
                default:
                    return facilities[(int) Math.Round((facilities.Count - 1) * 0.5)];
            }
        }
    }
}
