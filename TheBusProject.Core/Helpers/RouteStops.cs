using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Routes;
using TheBusProject.Models.Stations;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     The stops along a route. A coach line isn't a flight: Miami to Los Angeles is one service that calls at
    ///     Little Rock and Phoenix, and anybody can ride between any two of those four. Each leg is a route of its
    ///     own with its own fare, so only a leg has to be within the coach's range, and the stops are where the
    ///     passengers change, the driver changes and the coach fills up.
    /// </summary>
    public static class RouteStops
    {
        /// <summary>As many stops as a service will take before it stops being a service and becomes a tour.</summary>
        public const int Max = 8;

        /// <summary>US hours of service: ten hours at the wheel, then the driver must be relieved.</summary>
        public const double MaxDrivingHours = 10;

        /// <summary>A break every so many hours at the wheel, which is both the law and the bladder.</summary>
        public const double HoursBetweenBreaks = 4.5;

        /// <summary>How long the coach stands at a break: everyone off, twenty-five minutes.</summary>
        public const int BreakMinutes = 25;

        /// <summary>The stops of a route, in the order the coach calls at them, between the two ends.</summary>
        public static List<Station> Of(Route route)
        {
            var stops = new List<Station>();
            if (route == null || !route.HasStopovers) return stops;

            foreach (Route leg in Legs(route))
            {
                if (leg.Destination1 != route.Destination1 && !stops.Contains(leg.Destination1)) stops.Add(leg.Destination1);
                if (leg.Destination2 != route.Destination2 && !stops.Contains(leg.Destination2)) stops.Add(leg.Destination2);
            }

            return stops;
        }

        /// <summary>Every leg of the route in order, or the route itself when it runs non-stop.</summary>
        public static List<Route> Legs(Route route)
        {
            if (route == null) return new List<Route>();
            if (!route.HasStopovers) return new List<Route> { route };

            var legs = new List<Route>();
            foreach (StopoverRoute stop in route.Stopovers)
            {
                foreach (Route leg in stop.Legs)
                {
                    if (!legs.Contains(leg)) legs.Add(leg);
                }
            }

            return legs;
        }

        /// <summary>Every place the service calls, the two ends included, in order.</summary>
        public static List<Station> Calls(Route route)
        {
            var calls = new List<Station> { route.Destination1 };
            calls.AddRange(Of(route));
            calls.Add(route.Destination2);
            return calls;
        }

        /// <summary>The longest single leg, which is the one that has to be within a coach's range.</summary>
        public static double LongestLegKm(Route route)
        {
            return Legs(route).Max(l => l.GetDistance());
        }

        /// <summary>The road a coach covers over the whole service, every leg added up.</summary>
        public static double TotalKm(Route route)
        {
            return Legs(route).Sum(l => l.GetDistance());
        }

        /// <summary>
        ///     The breaks a leg needs: one every four and a half hours at the wheel, so a nine hour run stops once.
        ///     They are the law, and they are also why a coach takes longer than the distance says.
        /// </summary>
        public static int BreaksOn(double drivingHours)
        {
            if (drivingHours <= HoursBetweenBreaks) return 0;
            return (int) Math.Floor((drivingHours - 0.01)/HoursBetweenBreaks);
        }

        /// <summary>How long those breaks add to a leg.</summary>
        public static TimeSpan BreakTime(double drivingHours)
        {
            return TimeSpan.FromMinutes(BreaksOn(drivingHours)*BreakMinutes);
        }

        /// <summary>
        ///     Why this service can't be run as it stands, or null when it can: a leg beyond the coach's range, a
        ///     platform too short for it somewhere along the way, a leg longer than a driver may drive, or a stop
        ///     that isn't a stop at all.
        /// </summary>
        public static string WhyNot(Station from, Station to, IList<Station> stops, Coach coach)
        {
            return WhyNot(from, to, stops, coach, 1);
        }

        /// <summary>
        ///     The same, knowing how many drivers are aboard. One driver may be at the wheel for ten hours; a second
        ///     aboard means they relieve each other and the coach keeps going, which is how the long overnight runs
        ///     are really worked.
        /// </summary>
        public static string WhyNot(Station from, Station to, IList<Station> stops, Coach coach, int driversAboard)
        {
            if (from == null || to == null) return "Pick both ends first.";
            if (stops != null && stops.Count > Max) return "A service calls at " + Max + " stops at most.";

            var calls = new List<Station> { from };
            if (stops != null) calls.AddRange(stops);
            calls.Add(to);

            for (int i = 0; i < calls.Count; i++)
            {
                if (calls[i] == null) return "One of the stops is empty.";
                for (int j = i + 1; j < calls.Count; j++)
                {
                    if (calls[i] == calls[j]) return calls[i].Profile.Name + " is on the route twice.";
                }
            }

            if (coach == null) return null;

            for (int i = 0; i < calls.Count - 1; i++)
            {
                Station a = calls[i], b = calls[i + 1];
                double km = MathHelpers.GetDistance(a, b);
                if (km > coach.Range)
                {
                    return string.Format(
                        "{0} to {1} is {2:#,0} km and {3} can only run {4:#,0}. Put a stop in between.",
                        a.Profile.IATACode, b.Profile.IATACode, km, coach.Type.Name, coach.Range);
                }

                double hours = km/Math.Max(1, coach.Type.CruisingSpeed);
                double allowed = MaxDrivingHours*Math.Max(1, driversAboard);
                if (hours > allowed)
                {
                    return driversAboard > 1
                               ? string.Format(
                                   "{0} to {1} is {2:0.0} hours at the wheel and {3} drivers may do {4:0} between them. Put a stop in between.",
                                   a.Profile.IATACode, b.Profile.IATACode, hours, driversAboard, allowed)
                               : string.Format(
                                   "{0} to {1} is {2:0.0} hours at the wheel and a driver may do {3:0}. Put a stop in between, or crew the coach with a second driver so they can relieve each other.",
                                   a.Profile.IATACode, b.Profile.IATACode, hours, MaxDrivingHours);
                }

                if (coach.MinBayLength > Math.Min(a.GetMaxBayLength(), b.GetMaxBayLength()))
                {
                    Station shortOne = a.GetMaxBayLength() < b.GetMaxBayLength() ? a : b;
                    return string.Format(
                        "{0}'s longest platform is {1} m and {2} needs {3} m.",
                        shortOne.Profile.IATACode, shortOne.GetMaxBayLength(), coach.Type.Name, coach.MinBayLength);
                }
            }

            return null;
        }

        /// <summary>
        ///     Puts the stops on the route, in order, each leg a route of its own with its own fare. Clears them
        ///     again when the list is empty, which puts the service back to running non-stop.
        /// </summary>
        public static void Set(Route route, IList<Station> stops)
        {
            if (route == null) return;

            route.Stopovers.Clear();
            if (stops == null || stops.Count == 0) return;

            for (int i = 0; i < stops.Count; i++)
            {
                Station before = i == 0 ? route.Destination1 : stops[i - 1];
                Station after = i == stops.Count - 1 ? route.Destination2 : stops[i + 1];
                route.AddStopover(FleetCoachHelpers.CreateStopoverRoute(before, stops[i], after, route, i > 0, route.Type));
            }

            Reprice(route);
        }


        /// <summary>
        ///     Fills in the stops a long service needs: while a leg is beyond the coach's range or beyond what a
        ///     driver may do in a day, it puts in the station that best splits the worst leg -- near its middle and
        ///     barely off the road. This is how a real coach line is built, a division point at a time.
        /// </summary>
        public static List<Station> Suggest(Station from, Station to, Coach coach, IList<Station> start, int driversAboard = 1)
        {
            var stops = start == null ? new List<Station>() : new List<Station>(start);
            if (from == null || to == null || coach == null) return stops;

            double maxKm = coach.Range;
            double maxHours = MaxDrivingHours*Math.Max(1, driversAboard);
            double speed = Math.Max(1, coach.Type.CruisingSpeed);

            for (int guard = 0; guard < Max; guard++)
            {
                var calls = new List<Station> { from };
                calls.AddRange(stops);
                calls.Add(to);

                // the worst leg: the one furthest past what the coach or the driver can do
                int worst = -1;
                double over = 0;
                for (int i = 0; i < calls.Count - 1; i++)
                {
                    double km = MathHelpers.GetDistance(calls[i], calls[i + 1]);
                    double excess = Math.Max(km - maxKm, (km/speed - maxHours)*speed);
                    if (excess > over)
                    {
                        over = excess;
                        worst = i;
                    }
                }

                if (worst < 0) return stops;
                if (stops.Count >= Max) return stops;

                Station a = calls[worst], b = calls[worst + 1];
                double legKm = MathHelpers.GetDistance(a, b);
                Station best = null;
                double bestScore = double.MaxValue;
                foreach (Station candidate in Stations.GetAllActiveStations())
                {
                    if (calls.Contains(candidate)) continue;
                    if (coach.MinBayLength > candidate.GetMaxBayLength()) continue;
                    double first = MathHelpers.GetDistance(a, candidate), second = MathHelpers.GetDistance(candidate, b);
                    double detour = first + second - legKm;
                    if (detour > Math.Max(80, legKm*0.18)) continue;

                    // near the middle, and hardly off the road: a town the coach would pass anyway
                    double score = detour + Math.Abs(first - second)*0.35;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best == null) return stops;
                stops.Insert(worst, best);
            }

            return stops;
        }


        /// <summary>
        ///     The passengers riding over this leg whose journey starts before it or ends after it: the Miami to Los
        ///     Angeles riders sitting in the coach while it works Lubbock to Phoenix, and the Little Rock to Phoenix
        ///     ones too. Every pair of places on the line sells tickets, which is what makes a service a service
        ///     rather than a string of separate routes.
        /// </summary>
        public static double ThroughDemand(Route service, Station from, Station to, CoachClass.ClassType type)
        {
            if (service == null || !service.HasStopovers) return 0;

            List<Station> calls = Calls(service);
            int a = calls.IndexOf(from), b = calls.IndexOf(to);
            if (a < 0 || b < 0) return 0;
            if (a > b)
            {
                int swap = a;
                a = b;
                b = swap;
            }

            double sum = 0;
            for (int i = 0; i <= a; i++)
            {
                for (int j = b; j < calls.Count; j++)
                {
                    if (i == a && j == b) continue;   // the leg's own passengers are counted already
                    sum += calls[i].GetDestinationPassengersRate(calls[j], type);
                }
            }

            return sum;
        }

        /// <summary>
        ///     A rest break on a leg: where the coach pulls in, how far into the driving it happens and for how
        ///     long. The town is the one the road passes closest to at that point, and it is null only when the leg
        ///     crosses country empty enough that there is nowhere to name.
        /// </summary>
        public class RestStop
        {
            public Station Town { get; set; }

            /// <summary>Hours at the wheel before the coach stops.</summary>
            public double HoursIn { get; set; }

            public int Minutes { get; set; }

            /// <summary>How far the town sits off the road at that point, in km.</summary>
            public double OffRoadKm { get; set; }

            /// <summary>The town, not the depot: a break is "Tuscaloosa, 25 min", not the name of a terminal.</summary>
            public string Where
            {
                get
                {
                    if (Town == null) return "a rest stop";
                    return Town.Profile.Town != null && !string.IsNullOrEmpty(Town.Profile.Town.Name)
                               ? Town.Profile.Town.Name
                               : Town.Profile.Name;
                }
            }

            public override string ToString()
            {
                return Where + ", " + Minutes + " min";
            }
        }

        /// <summary>How far a town may sit off the road and still be where the coach takes its break.</summary>
        private const double BreakTownKm = 70;

        /// <summary>
        ///     How much of the leg the break may slide along the road to reach a town. A driver does not stop at a
        ///     map reference, they stop where there is a counter and a lavatory, and they will press on or pull in
        ///     early to reach one.
        /// </summary>
        private const double BreakSlide = 0.22;

        /// <summary>
        ///     The rest breaks on a leg, as places rather than as minutes. A driver stops every four and a half
        ///     hours, and stops somewhere: this walks the road the coach actually drives and names the town it
        ///     reaches when the break is due, the way a Greyhound schedule reads "Lordsburg, 25 min".
        /// </summary>
        public static List<RestStop> BreaksAlong(Route leg, CoachType type)
        {
            var stops = new List<RestStop>();
            if (leg == null || type == null) return stops;

            double hours = leg.GetDistance()/Math.Max(1, type.CruisingSpeed);
            int count = BreaksOn(hours);
            if (count == 0) return stops;

            var a = leg.Destination1.Profile.Coordinates.ConvertToGeoCoordinate();
            var b = leg.Destination2.Profile.Coordinates.ConvertToGeoCoordinate();
            List<double[]> path = RoadNetwork.Path(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

            var taken = new List<Station> { leg.Destination1, leg.Destination2 };
            for (int k = 1; k <= count; k++)
            {
                // spread the breaks evenly over the drive rather than stacking them every four and a half hours
                // from the start, which on a nine hour leg put the second one six minutes before arrival. Two
                // breaks on nine hours is a stop at three hours and another at six, which is how it is really done
                double at = hours*k/(count + 1.0);
                var stop = new RestStop { HoursIn = at, Minutes = BreakMinutes };
                double fr = Math.Max(0.05, Math.Min(0.95, at/hours));

                // the best town beside the road: close to the carriageway, and as near as may be to where the
                // break is due. A town a little further on is a better rest stop than one nowhere near the road.
                double best = double.MaxValue;
                foreach (Station town in Stations.GetAllActiveStations())
                {
                    if (taken.Contains(town)) continue;
                    var c = town.Profile.Coordinates.ConvertToGeoCoordinate();

                    double offRoad;
                    double along = Beside(path, a, b, c, out offRoad);
                    if (offRoad > BreakTownKm) continue;
                    double slide = Math.Abs(along - fr);
                    if (slide > BreakSlide) continue;

                    double score = offRoad + slide*leg.GetDistance();
                    if (score < best)
                    {
                        best = score;
                        stop.Town = town;
                        stop.OffRoadKm = offRoad;
                        stop.HoursIn = hours*along;
                    }
                }

                if (stop.Town != null) taken.Add(stop.Town);
                stops.Add(stop);
            }

            return stops;
        }

        /// <summary>
        ///     How far along the road a town sits, as a share of the way, and how far off it is. Without a road to
        ///     follow it falls back to the straight line between the ends.
        /// </summary>
        private static double Beside(List<double[]> path, GeoCoordinate a, GeoCoordinate b, GeoCoordinate town, out double offRoadKm)
        {
            if (path == null || path.Count < 2)
            {
                double whole = MathHelpers.GetDistance(a, b);
                double toA = MathHelpers.GetDistance(a, town);
                offRoadKm = Math.Max(0, toA + MathHelpers.GetDistance(town, b) - whole)/2;
                return whole <= 0 ? 0 : Math.Max(0, Math.Min(1, toA/whole));
            }

            // the nearest point of the road, and how far along the road that point is
            var steps = new double[path.Count];
            double total = 0;
            for (int i = 1; i < path.Count; i++)
            {
                total += MathHelpers.GetDistance(new GeoCoordinate(path[i - 1][0], path[i - 1][1]), new GeoCoordinate(path[i][0], path[i][1]));
                steps[i] = total;
            }

            double near = double.MaxValue, at = 0;
            for (int i = 0; i < path.Count; i++)
            {
                double km = MathHelpers.GetDistance(new GeoCoordinate(path[i][0], path[i][1]), town);
                if (km < near)
                {
                    near = km;
                    at = steps[i];
                }
            }

            offRoadKm = near;
            return total <= 0 ? 0 : at/total;
        }

        /// <summary>The breaks on a leg as a line for a schedule: "Lordsburg, 25 min after 4h30".</summary>
        public static string DescribeBreaks(Route leg, CoachType type)
        {
            var stops = BreaksAlong(leg, type);
            if (stops.Count == 0) return "no break";
            return string.Join(", ", stops.Select(
                                   s => s.Where + " after " + Clock(s.HoursIn) + " (" + s.Minutes + " min)"));
        }

        /// <summary>Hours at the wheel as a driver would say them: 2h59, never 2h60.</summary>
        private static string Clock(double hours)
        {
            var t = TimeSpan.FromMinutes(Math.Round(hours*60));
            return (int) t.TotalHours + "h" + t.Minutes.ToString("00");
        }

        /// <summary>The road from one call to another, leg by leg, the way the coach actually drives it.</summary>
        private static double JourneyKm(IList<Station> calls, int from, int to)
        {
            double km = 0;
            for (int i = from; i < to; i++) km += MathHelpers.GetDistance(calls[i], calls[i + 1]);
            return km;
        }

        /// <summary>
        ///     What a seat on this leg is worth against the leg's own fare, between 0 and 1. Everybody aboard the
        ///     Lubbock to Phoenix leg paid for the journey they are making, not for that leg: the Lubbock passengers
        ///     paid the Lubbock to Phoenix fare and are worth all of it, and the ones who got on in Miami for Los
        ///     Angeles paid one Miami to Los Angeles fare, of which this leg has earned its share of the mileage.
        ///     Because the fare per mile falls so far with distance -- Miami to Los Angeles is $171 of 2007 money
        ///     against $315 for the same road bought as five separate tickets -- a through rider is worth well under
        ///     half of a local one on the same seat. Weighted by how many of each there are.
        /// </summary>
        public static double LegFareFactor(Route service, Route leg, CoachClass.ClassType type)
        {
            if (service == null || leg == null || !service.HasStopovers) return 1;

            List<Station> calls = Calls(service);
            int a = calls.IndexOf(leg.Destination1), b = calls.IndexOf(leg.Destination2);
            if (a < 0 || b < 0) return 1;
            if (a > b) { int swap = a; a = b; b = swap; }

            double legKm = JourneyKm(calls, a, b);
            double legFare = PricingService.MarketFare(legKm);
            if (legKm <= 0 || legFare <= 0) return 1;

            double pax = 0, worth = 0;
            for (int i = 0; i <= a; i++)
            {
                for (int j = b; j < calls.Count; j++)
                {
                    double riders = calls[i].GetDestinationPassengersRate(calls[j], type);
                    if (riders <= 0) continue;
                    double journeyKm = JourneyKm(calls, i, j);
                    if (journeyKm <= 0) continue;
                    pax += riders;
                    worth += riders*PricingService.MarketFare(journeyKm)*(legKm/journeyKm);
                }
            }

            if (pax <= 0) return 1;
            return Math.Max(0.3, Math.Min(1, worth/(pax*legFare)));
        }

        /// <summary>
        ///     Works out what each leg of a service is really earning and remembers it on the leg, so the money at
        ///     the end of a trip can be counted without walking the whole line again. Called whenever the stops
        ///     change. The sums are done on the economy fares, which are the bulk of them, and the other classes
        ///     scale with those, so the same figure holds for all of them.
        /// </summary>
        public static void Reprice(Route service)
        {
            if (service == null) return;
            if (!service.HasStopovers)
            {
                service.ThroughFareFactor = 1;
                return;
            }

            foreach (Route leg in Legs(service))
                leg.ThroughFareFactor = LegFareFactor(service, leg, CoachClass.ClassType.EconomyClass);
        }

        /// <summary>The service as a line of codes, for a screen or a log: MIA - LIT - PHX - LAX.</summary>
        public static string Describe(Route route)
        {
            return string.Join(" - ", Calls(route).Select(a => a.Profile.IATACode));
        }
    }
}
