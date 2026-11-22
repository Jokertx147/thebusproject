using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helpers class for the AI
    public class AIHelpers
    {
        #region Static Fields

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        /// <summary>
        ///     True one time in oneIn (always when oneIn is 1 or less). Works with any size: the products the carrier
        ///     checks use (style x fleet x idle coach) overflowed whole numbers and crashed the game for big carriers.
        /// </summary>
        private static bool Chance(double oneIn)
        {
            return oneIn <= 1 || Rnd.NextDouble()*oneIn < 1;
        }

        #endregion

        #region Public Methods and Operators

        public static bool WillTakeSpecialContract(Carrier carrier, SpecialContractType contract)
        {
            return false;
        }

        public static bool CanJoinAlliance(Carrier carrier, Alliance alliance)
        {
            IEnumerable<Country> sameCountries =
                alliance.Members.SelectMany(m => m.Carrier.Stations)
                        .Select(a => a.Profile.Country)
                        .Distinct()
                        .Intersect(carrier.Stations.Select(a => a.Profile.Country).Distinct());
            IEnumerable<Station> sameDestinations =
                alliance.Members.SelectMany(m => m.Carrier.Stations).Distinct().Intersect(carrier.Stations);

            double carrierDestinations = carrier.Stations.Count;
            double carrierRoutes = carrier.Routes.Count;
            double carrierCountries = carrier.Stations.Select(a => a.Profile.Country).Distinct().Count();
            double carrierAlliances = carrier.Alliances.Count;

            double allianceRoutes = alliance.Members.SelectMany(m => m.Carrier.Routes).Count();

            //declines if carrier is much smaller than alliance
            if (carrierRoutes*5 < allianceRoutes)
            {
                return false;
            }

            //declines if there is a match for 75% of the carrier and alliance destinations
            if (sameDestinations.Count() >= carrierDestinations*0.75)
            {
                return false;
            }

            //declines if there is a match for 75% of the carrier and alliance countries
            if (sameCountries.Count() >= carrierCountries*0.75)
            {
                return false;
            }

            return true;
        }

        public static bool CanRemoveFromAlliance(Carrier remover, Carrier toremove, Alliance alliance)
        {
            return remover.GetValue() > toremove.GetValue()*0.9;
        }

        public static RouteTimeTable CreateCoachRouteTimeTable(
            Route route,
            FleetCoach coach,
            int tripsPerDay,
            string tripCode1,
            string tripCode2)
        {
            const int startHour = 6;
            const int endHour = 22;

            TimeSpan routeTripTime = route.GetTripTime(coach.Coach.Type);

            TimeSpan minTripTime = routeTripTime.Add(FleetCoachHelpers.GetMinTimeBetweenTrips(coach));

            var minDelayMinutes = (int) FleetCoachHelpers.GetMinTimeBetweenTrips(coach).TotalMinutes;

            int startMinutes =
                Convert.ToInt16(((endHour - startHour)*60) - (minTripTime.TotalMinutes*tripsPerDay*2));

            if (startMinutes < 0)
            {
                startMinutes = 0;
            }

            TimeSpan tripTime = new TimeSpan(startHour, 0, 0).Add(new TimeSpan(0, startMinutes/2, 0));

            return CreateCoachRouteTimeTable(
                route,
                coach,
                tripsPerDay,
                true,
                minDelayMinutes,
                tripTime,
                tripCode1,
                tripCode2);
        }

        public static RouteTimeTable CreateCoachRouteTimeTable(
            Route route,
            FleetCoach coach,
            int numberOfTrips,
            Boolean isDaily,
            int delayMinutes,
            TimeSpan startTime,
            string tripCode1,
            string tripCode2)
        {
            var delayTime = new TimeSpan(0, delayMinutes, 0);
            var timeTable = new RouteTimeTable(route);

            TimeSpan routeTripTime = route.GetTripTime(coach.Coach.Type);

            TimeSpan minTripTime = routeTripTime.Add(delayTime);

            if (minTripTime.Hours < 12 && minTripTime.Days < 1 && isDaily)
            {
                var tripTime = new TimeSpan(startTime.Hours, startTime.Minutes, startTime.Seconds);
                //new TimeSpan(startHour, 0, 0).Add(new TimeSpan(0, startMinutes / 2, 0));

                for (int i = 0; i < numberOfTrips; i++)
                {
                    timeTable.AddDailyEntries(new RouteEntryDestination(route.Destination2, tripCode1), tripTime);

                    tripTime = tripTime.Add(minTripTime);

                    timeTable.AddDailyEntries(new RouteEntryDestination(route.Destination1, tripCode2), tripTime);

                    tripTime = tripTime.Add(minTripTime);
                }
            }
            else
            {
                if (isDaily || minTripTime.Hours >= 12 || minTripTime.Days >= 1)
                {
                    DayOfWeek day = 0;

                    int outTime = 15*Rnd.Next(-12, 12);
                    int homeTime = 15*Rnd.Next(-12, 12);

                    for (int i = 0; i < 3; i++)
                    {
                        Gate outboundGate = GetFreeCarrierGate(
                            coach.Coach.Carrier,
                            route.Destination1,
                            day,
                            new TimeSpan(12, 0, 0).Add(new TimeSpan(0, outTime, 0)));
                        timeTable.AddEntry(
                            new RouteTimeTableEntry(
                                timeTable,
                                day,
                                new TimeSpan(12, 0, 0).Add(new TimeSpan(0, outTime, 0)),
                                new RouteEntryDestination(route.Destination2, tripCode1),
                                outboundGate));

                        day += 2;
                    }

                    day = (DayOfWeek) 1;

                    for (int i = 0; i < 3; i++)
                    {
                        Gate outboundGate = GetFreeCarrierGate(
                            coach.Coach.Carrier,
                            route.Destination2,
                            day,
                            new TimeSpan(12, 0, 0).Add(new TimeSpan(0, homeTime, 0)));

                        timeTable.AddEntry(
                            new RouteTimeTableEntry(
                                timeTable,
                                day,
                                new TimeSpan(12, 0, 0).Add(new TimeSpan(0, homeTime, 0)),
                                new RouteEntryDestination(route.Destination1, tripCode2),
                                outboundGate));

                        day += 2;
                    }
                }
                else
                {
                    var day = (DayOfWeek) (7 - numberOfTrips/2);

                    for (int i = 0; i < numberOfTrips; i++)
                    {
                        var tripTime = new TimeSpan(startTime.Hours, startTime.Minutes, startTime.Seconds);

                        timeTable.AddEntry(
                            new RouteTimeTableEntry(
                                timeTable,
                                day,
                                tripTime,
                                new RouteEntryDestination(route.Destination2, tripCode1)));

                        tripTime = tripTime.Add(minTripTime);

                        timeTable.AddEntry(
                            new RouteTimeTableEntry(
                                timeTable,
                                day,
                                tripTime,
                                new RouteEntryDestination(route.Destination1, tripCode2)));

                        day++;

                        if (((int) day) > 6)
                        {
                            day = 0;
                        }
                    }
                }
            }

            foreach (RouteTimeTableEntry e in timeTable.Entries)
            {
                e.Coach = coach;
            }

            return timeTable;
        }

        public static RouteTimeTable CreateBusinessRouteTimeTable(
            Route route,
            FleetCoach coach,
            int tripsPerDay,
            string tripCode1,
            string tripCode2)
        {
            var timeTable = new RouteTimeTable(route);

            TimeSpan minTripTime =
                MathHelpers.GetTripTime(
                    route.Destination1.Profile.Coordinates.ConvertToGeoCoordinate(),
                    route.Destination2.Profile.Coordinates.ConvertToGeoCoordinate(),
                    coach.Coach.Type)
                           .Add(new TimeSpan(FleetCoachHelpers.GetMinTimeBetweenTrips(coach).Ticks));

            int startHour = 6;
            const int endHour = 10;

            int maxHours = endHour - startHour; //entries.Coaches == null

            int startMinutes = Convert.ToInt16((maxHours*60) - (minTripTime.TotalMinutes*tripsPerDay*2));

            if (startMinutes < 0)
            {
                startMinutes = 0;
            }

            //morning
            TimeSpan tripTime = new TimeSpan(startHour, 0, 0).Add(new TimeSpan(0, startMinutes/2, 0));

            for (int i = 0; i < tripsPerDay; i++)
            {
                timeTable.AddWeekDailyEntries(new RouteEntryDestination(route.Destination2, tripCode1), tripTime);

                tripTime = tripTime.Add(minTripTime);

                timeTable.AddWeekDailyEntries(new RouteEntryDestination(route.Destination1, tripCode2), tripTime);

                tripTime = tripTime.Add(minTripTime);
            }
            //evening
            startHour = 18;
            tripTime = new TimeSpan(startHour, 0, 0).Add(new TimeSpan(0, startMinutes/2, 0));
            for (int i = 0; i < tripsPerDay; i++)
            {
                timeTable.AddWeekDailyEntries(new RouteEntryDestination(route.Destination2, tripCode1), tripTime);

                tripTime = tripTime.Add(minTripTime);

                timeTable.AddWeekDailyEntries(new RouteEntryDestination(route.Destination1, tripCode2), tripTime);

                tripTime = tripTime.Add(minTripTime);
            }

            if (timeTable.Entries.Count == 0)
            {
                tripCode1 = "TT";
            }

            foreach (RouteTimeTableEntry e in timeTable.Entries)
            {
                e.Coach = coach;
            }

            return timeTable;
        }

        public static void CreateCargoRouteTimeTable(Route route, FleetCoach coach)
        {
            TimeSpan routeTripTime =
                MathHelpers.GetTripTime(
                    route.Destination1.Profile.Coordinates.ConvertToGeoCoordinate(),
                    route.Destination2.Profile.Coordinates.ConvertToGeoCoordinate(),
                    coach.Coach.Type);
            TimeSpan minTripTime =
                routeTripTime.Add(new TimeSpan(FleetCoachHelpers.GetMinTimeBetweenTrips(coach).Ticks));

            const int maxHours = 20 - 8; //from 08.00 to 20.00

            int tripsPerDay = Convert.ToInt16(maxHours*60/(2*minTripTime.TotalMinutes));

            string tripCode1 = coach.Coach.Carrier.GetNextTripCode(0);
            string tripCode2 = coach.Coach.Carrier.GetNextTripCode(1);

            route.TimeTable = CreateCoachRouteTimeTable(route, coach, tripsPerDay, tripCode1, tripCode2);
        }

        public static bool CreateRouteTimeTable(Route route, List<FleetCoach> coaches)
        {
            var totalTripTime = new TimeSpan(coaches.Sum(a => route.GetTripTime(a.Coach.Type).Ticks));
            var maxTripTime = new TimeSpan(coaches.Max(a => route.GetTripTime(a.Coach.Type)).Ticks);

            int maxHours = 22 - 6 - (int) Math.Ceiling(maxTripTime.TotalMinutes); //from 06.00 to 22.00

            if (totalTripTime.TotalMinutes > maxHours)
            {
                return false;
            }

            var startTime = new TimeSpan(6, 0, 0);

            foreach (FleetCoach coach in coaches)
            {
                string tripCode1 = coach.Coach.Carrier.GetNextTripCode(0);
                string tripCode2 = coach.Coach.Carrier.GetNextTripCode(1);

                CreateCoachRouteTimeTable(
                    route,
                    coach,
                    1,
                    true,
                    (int) FleetCoachHelpers.GetMinTimeBetweenTrips(coach).TotalMinutes,
                    startTime,
                    tripCode1,
                    tripCode2);

                startTime = startTime.Add(route.GetTripTime(coach.Coach.Type));
            }

            return true;
        }

        public static void CreateRouteTimeTable(Route route, FleetCoach coach)
        {
            TimeSpan routeTripTime =
                MathHelpers.GetTripTime(
                    route.Destination1.Profile.Coordinates.ConvertToGeoCoordinate(),
                    route.Destination2.Profile.Coordinates.ConvertToGeoCoordinate(),
                    coach.Coach.Type);
            TimeSpan minTripTime =
                routeTripTime.Add(new TimeSpan(FleetCoachHelpers.GetMinTimeBetweenTrips(coach).Ticks));

            const int maxHours = 22 - 6; //from 06.00 to 22.00

            int fits = Convert.ToInt16(maxHours*60/(2*minTripTime.TotalMinutes));
            int tripsPerDay = fits;

            // Flying the number of round trips the forecast chose, rather than as many as the day holds, was tried
            // and measured over four games of five years each way. It did what it was meant to: the loss-making
            // routes the review keeps fell from 89 to 20 and their losses from 979,000 a day to 83,000. But the
            // computer carriers then flew a third less (39 trips a week an coach to 26) and the industry earned
            // 874 million a month instead of 713, ranges that do not overlap, so it destroyed about five times the
            // profit it saved in losses. The forecast picks the trips that earn most for a route nobody knows yet,
            // and a route's demand grows as the carrier becomes known there; setting the timetable once by that
            // figure leaves a matured route under-served for good. Adjusting the trips as a route matures, rather
            // than fixing them at creation, is the version worth trying.

            if (AIRouteChoice.RecordCreation != null && route is PassengerRoute && coach.Coach.Carrier != null && !coach.Coach.Carrier.IsHuman)
            {
                try { AIRouteChoice.RecordCreation(route, coach, tripsPerDay); } catch (Exception) { }
            }

            string tripCode1 = coach.Coach.Carrier.GetNextTripCode(0);
            string tripCode2 = coach.Coach.Carrier.GetNextTripCode(1);

            route.TimeTable = CreateCoachRouteTimeTable(route, coach, tripsPerDay, tripCode1, tripCode2);
        }

        public static bool DoAcceptAllianceInvitation(Carrier carrier, Alliance alliance)
        {
            //a subsidiary of a human carrier will always accept an invitation to an alliance where the parent is a member
            if (alliance.IsHumanAlliance && carrier.IsHuman)
            {
                return true;
            }

            IEnumerable<Country> sameCountries =
                alliance.Members.SelectMany(m => m.Carrier.Stations)
                        .Select(a => a.Profile.Country)
                        .Distinct()
                        .Intersect(carrier.Stations.Select(a => a.Profile.Country).Distinct());
            IEnumerable<Station> sameDestinations =
                alliance.Members.SelectMany(m => m.Carrier.Stations).Distinct().Intersect(carrier.Stations);

            double carrierDestinations = carrier.Stations.Count;
            double carrierRoutes = carrier.Routes.Count;
            double carrierCountries = carrier.Stations.Select(a => a.Profile.Country).Distinct().Count();
            double carrierAlliances = carrier.Alliances.Count;

            double allianceRoutes = alliance.Members.SelectMany(m => m.Carrier.Routes).Count();

            //declines if invited carrier is much larger than alliance
            if (carrierRoutes > 2*allianceRoutes)
            {
                return false;
            }

            //declines if there is a match for 50% of the carrier and alliance destinations
            if (sameDestinations.Count() >= carrierDestinations*0.50)
            {
                return false;
            }

            //declines if there is a match for 75% of the carrier and alliance countries
            if (sameCountries.Count() >= carrierCountries*0.75)
            {
                return false;
            }

            //declines if the carrier already are in "many" alliances - many == 2
            if (carrierAlliances > 2)
            {
                return false;
            }

            return true;
        }

        public static KeyValuePair<Coach, bool>? GetCoachForRoute(
            Carrier carrier,
            Station destination1,
            Station destination2,
            bool doLeasing,
            Route.RouteType focus,
            bool forStartdata = false)
        {
            List<CoachType> carrierCoaches = carrier.Profile.PreferedCoaches;

            const double maxLoanTotal = 100000000;
            double distance = MathHelpers.GetDistance(
                destination1.Profile.Coordinates.ConvertToGeoCoordinate(),
                destination2.Profile.Coordinates.ConvertToGeoCoordinate());

            CoachType.TypeRange rangeType = GeneralHelpers.ConvertDistanceToRangeType(distance);

            List<Coach> coaches;

            if (carrierCoaches.Count > 0)
            {
                if (focus == Route.RouteType.Cargo)
                {
                    if (doLeasing)
                    {
                        coaches = Coaches.GetCoachesForLeasing().FindAll(a => a.Type is CoachCargoType
                                                                                    && a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.LeasingPrice*2 < carrier.Money);

                        if (coaches.Count == 0)
                        {
                            coaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachCargoType)
                                         .FindAll(
                                             a =>
                                             a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.LeasingPrice*2 < carrier.Money);
                        }
                    }
                    else
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachCargoType)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.GetPrice() < carrier.Money - 1000000);
                    }
                }
                else if (focus == Route.RouteType.Helicopter)
                {
                    if (doLeasing)
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType && a.Type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.LeasingPrice*2 < carrier.Money);
                    }
                    else
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType && a.Type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.GetPrice() < carrier.Money - 1000000);
                    }
                }
                else
                {
                    if (doLeasing)
                    {
                        coaches =
                            Coaches.GetCoachesForLeasing()
                                     .FindAll(
                                         a => a.Type is CoachPassengerType
                                              && a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.LeasingPrice*2 < carrier.Money);

                        if (coaches.Count == 0)
                        {
                            coaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType)
                                         .FindAll(
                                             a =>
                                             a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.LeasingPrice*2 < carrier.Money);
                        }
                    }
                    else
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && carrierCoaches.Contains(a.Type) && a.GetPrice() < carrier.Money - 1000000);
                    }
                }
            }
            else
            {
                if (focus == Route.RouteType.Cargo)
                {
                    if (doLeasing)
                    {
                        coaches = Coaches.GetCoachesForLeasing()
                                             .FindAll(
                                                 a => a.Type is CoachCargoType
                                                      && a.GetAge() < 10 && distance < a.Range && a.LeasingPrice*2 < carrier.Money);

                        if (coaches.Count == 0)
                        {
                            coaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachCargoType)
                                         .FindAll(
                                             a =>
                                             a.GetAge() < 10 && distance < a.Range && a.LeasingPrice*2 < carrier.Money);
                        }
                    }
                    else
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachCargoType)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && a.GetPrice() < carrier.Money - 1000000);
                    }
                }
                else if (focus == Route.RouteType.Helicopter)
                {
                    if (doLeasing)
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType && a.Type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && a.LeasingPrice*2 < carrier.Money);
                    }
                    else
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType && a.Type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && a.GetPrice() < carrier.Money - 1000000);
                    }
                }
                else
                {
                    if (doLeasing)
                    {
                        coaches =
                            Coaches.GetCoachesForSale()
                                     .FindAll(
                                         a => a.Type is CoachPassengerType
                                              && a.GetAge() < 10 && distance < a.Range && a.LeasingPrice*2 < carrier.Money);

                        if (coaches.Count == 0)
                        {
                            coaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType)
                                         .FindAll(
                                             a =>
                                             a.GetAge() < 10 && distance < a.Range && a.LeasingPrice*2 < carrier.Money);
                        }
                    }
                    else
                    {
                        coaches =
                            Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType)
                                     .FindAll(
                                         a =>
                                         a.GetAge() < 10 && distance < a.Range && a.GetPrice() < carrier.Money - 1000000);
                    }
                }
            }
            if (coaches.Count > 0)
            {
                return new KeyValuePair<Coach, bool>(coaches.OrderBy(a => a.Price).First(), false);
            }
            if (carrier.Mentality == Carrier.CarrierMentality.Aggressive || carrier.Fleet.Count == 0 || forStartdata)
            {
                double carrierLoanTotal = carrier.Loans.Sum(l => l.Balance);

                if (carrierLoanTotal < maxLoanTotal)
                {
                    List<Coach> loanCoaches;

                    if (carrierCoaches.Count > 0)
                    {
                        if (focus == Route.RouteType.Cargo)
                        {
                            loanCoaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachCargoType)
                                         .FindAll(
                                             a =>
                                             distance < a.Range && carrierCoaches.Contains(a.Type) && a.GetPrice() < carrier.Money + maxLoanTotal - carrierLoanTotal);
                        }
                        else if (focus == Route.RouteType.Helicopter)
                        {
                            loanCoaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType && a.Type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
                                         .FindAll(
                                             a =>
                                             distance < a.Range && carrierCoaches.Contains(a.Type) && a.GetPrice() < carrier.Money + maxLoanTotal - carrierLoanTotal);
                        }
                        else
                        {
                            loanCoaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType)
                                         .FindAll(
                                             a =>
                                             distance < a.Range && carrierCoaches.Contains(a.Type) && a.GetPrice() < carrier.Money + maxLoanTotal - carrierLoanTotal);
                        }
                    }
                    else
                    {
                        if (focus == Route.RouteType.Cargo)
                        {
                            loanCoaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachCargoType)
                                         .FindAll(
                                             a =>
                                             distance < a.Range && a.GetPrice() < carrier.Money + maxLoanTotal - carrierLoanTotal);
                        }
                        else if (focus == Route.RouteType.Helicopter)
                        {
                            loanCoaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType && a.Type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
                                         .FindAll(
                                             a =>
                                             distance < a.Range && a.GetPrice() < carrier.Money + maxLoanTotal - carrierLoanTotal);
                        }
                        else
                        {
                            loanCoaches =
                                Coaches.GetCoachesForSale(a => a.Type is CoachPassengerType)
                                         .FindAll(
                                             a =>
                                             distance < a.Range && a.GetPrice() < carrier.Money + maxLoanTotal - carrierLoanTotal);
                        }
                    }
                    if (loanCoaches.Count > 0)
                    {
                        Coach coach = loanCoaches.OrderBy(a => a.Price).First();

                        if (coach == null)
                        {
                            return null;
                        }

                        return new KeyValuePair<Coach, bool>(coach, true);
                    }
                    return null;
                }
                return null;
            }
            return null;
        }

        public static Station GetDestinationStation(Carrier carrier, Station station)
        {
            List<Station> stations = GetDestinationStations(carrier, station);
            if (stations.Count == 0)
            {
                return null;
            }
            return stations[0];
        }

        // the distance to the nearest other station, worked out once a day per station (stations open and close by year)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Station, Tuple<DateTime, double>> NearestCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Station, Tuple<DateTime, double>>();

        private static double NearestStationKm(Station station)
        {
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (NearestCache.TryGetValue(station, out cached) && cached.Item1 == today) return cached.Item2;
            double km = (from a in Stations.GetStations(a => a != station)
                         select MathHelpers.GetDistance(
                             a.Profile.Coordinates.ConvertToGeoCoordinate(),
                             station.Profile.Coordinates.ConvertToGeoCoordinate())).Min();
            NearestCache.AddOrUpdate(station, Tuple.Create(today, km));
            return km;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Dictionary<Station, Tuple<DateTime, List<Station>>>> AllowedCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Carrier, Dictionary<Station, Tuple<DateTime, List<Station>>>>();

        public static List<Station> GetDestinationStations(Carrier carrier, Station station)
        {
            long pfR = Profiler.Start();
            IEnumerable<long> coaches = from a in Coaches.GetCoachesForSale() select a.Range;
            var enumerable = coaches as long[] ?? coaches.ToArray();
            double maxDistance = !enumerable.Any() ? 5000 : enumerable.Max();

            double minDistance = NearestStationKm(station);

            // the stations the carrier may fly to from here (licences and trip restrictions) change at most daily:
            // worked out once a day per station, then the towns it already serves are left out
            DateTime today = GameObject.GetInstance().GameTime.Date;
            Dictionary<Station, Tuple<DateTime, List<Station>>> perStation = AllowedCache.GetValue(carrier, x => new Dictionary<Station, Tuple<DateTime, List<Station>>>());
            List<Station> allowed;
            lock (perStation)
            {
                Tuple<DateTime, List<Station>> cached;
                allowed = perStation.TryGetValue(station, out cached) && cached.Item1 == today ? cached.Item2 : null;
            }
            if (allowed == null)
            {
                allowed = Stations.GetStations(
                    a =>
                    CarrierHelpers.HasCarrierLicens(carrier, station, a)
                    && !TripRestrictions.HasRestriction(
                        a.Profile.Country,
                        station.Profile.Country,
                        GameObject.GetInstance().GameTime,
                        TripRestriction.RestrictionType.Trips)
                    && !TripRestrictions.HasRestriction(
                        station.Profile.Country,
                        a.Profile.Country,
                        GameObject.GetInstance().GameTime,
                        TripRestriction.RestrictionType.Trips)
                    && !TripRestrictions.HasRestriction(
                        carrier,
                        a.Profile.Country,
                        station.Profile.Country,
                        GameObject.GetInstance().GameTime));
                lock (perStation) perStation[station] = Tuple.Create(today, allowed);
            }
            // (towns match by name and state, as Town's == does)
            var servedTowns = new HashSet<(string, object)>(carrier.Stations.ToList().Select(ar => (ar.Profile.Town?.Name, (object) ar.Profile.Town?.State)));
            List<Station> stations = allowed.Where(a => !servedTowns.Contains((a.Profile.Town?.Name, (object) a.Profile.Town?.State))).ToList();
            List<Route> routes = carrier.Routes.FindAll(r => r.Destination1 == station || r.Destination2 == station);
            Profiler.Stop("route search: allowed stations", pfR);
            pfR = Profiler.Start();

            CarrierFocus marketFocus = carrier.MarketFocus;

            Terminal.TerminalType terminaltype = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

            if (carrier.Stations.Count < 4)
            {
                var focuses = new List<CarrierFocus> {CarrierFocus.Local, CarrierFocus.Local, CarrierFocus.Local, marketFocus};

                marketFocus = focuses[Rnd.Next(focuses.Count)];
            }

            switch (marketFocus)
            {
                case CarrierFocus.Domestic:
                    stations = stations.FindAll(a => a.Profile.Country == station.Profile.Country);
                    break;
                case CarrierFocus.Global:
                    stations =
                        stations.FindAll(
                            a =>
                            IsRouteInCorrectArea(station, a)
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) > 100
                            && station.Profile.Town != a.Profile.Town
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) < maxDistance
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) > 100);
                    break;
                case CarrierFocus.Local:
                    stations =
                        stations.FindAll(
                            a =>
                            IsRouteInCorrectArea(station, a)
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) < Math.Max(minDistance, 1000)
                            && station.Profile.Town != a.Profile.Town
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) >= Route.MinRouteDistance);
                    break;
                case CarrierFocus.Regional:
                    stations =
                        stations.FindAll(
                            a =>
                            a.Profile.Country.Region == station.Profile.Country.Region
                            && IsRouteInCorrectArea(station, a)
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) < maxDistance
                            && station.Profile.Town != a.Profile.Town
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station.Profile.Coordinates.ConvertToGeoCoordinate()) > 100);
                    break;
            }

            if (stations.Count == 0)
            {
                stations =
                    (from a in
                         Stations.GetStations(
                             a =>
                             IsRouteInCorrectArea(station, a)
                             && MathHelpers.GetDistance(
                                 a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                 station.Profile.Coordinates.ConvertToGeoCoordinate()) < 5000
                             && MathHelpers.GetDistance(
                                 a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                 station.Profile.Coordinates.ConvertToGeoCoordinate()) >= Route.MinRouteDistance)
                     orderby a.Profile.Size descending
                     select a).ToList();
            }

            Profiler.Stop("route search: market focus", pfR);
            using (Profiler.Measure("route search: rank by demand"))
            return (from a in stations
                    where
                        routes.Find(r => r.Destination1 == a || r.Destination2 == a) == null
                        && (a.Terminals.GetFreeGates(terminaltype) > 0 || StationHelpers.HasFreeGates(a, carrier, terminaltype))
                    // with realistic costs, a market others already serve well is worth less than an empty or a crowded-out one
                    orderby
                         (station.GetDestinationPassengersRate(a, CoachClass.ClassType.EconomyClass)
                        + a.GetDestinationPassengersRate(station, CoachClass.ClassType.EconomyClass))
                        *AIPricingService.EntryFactor(carrier, station, a) descending
                    select a).ToList();
        }

        public static T GetRandomItem<T>(Dictionary<T, int> list)
        {
            var tList = new List<T>();

            foreach (T item in list.Keys)
            {
                for (int i = 0; i < list[item]; i++)
                {
                    tList.Add(item);
                }
            }

            return tList[Rnd.Next(tList.Count)];
        }

        public static RouteClassesConfiguration GetRouteConfiguration(PassengerRoute route)
        {
            double distance = MathHelpers.GetDistance(route.Destination1, route.Destination2);

            if (distance < 500)
            {
                return (RouteClassesConfiguration) Configurations.GetStandardConfiguration("100");
            }
            if (distance < 2000)
            {
                return (RouteClassesConfiguration) Configurations.GetStandardConfiguration("101");
            }
            if (route.Destination1.Profile.Country == route.Destination2.Profile.Country)
            {
                return (RouteClassesConfiguration) Configurations.GetStandardConfiguration("102");
            }
            if (route.Destination1.Profile.Country != route.Destination2.Profile.Country)
            {
                return (RouteClassesConfiguration) Configurations.GetStandardConfiguration("103");
            }

            return null;
        }

        public static bool IsCargoRouteDestinationsCorrect(Station dest1, Station dest2, Carrier carrier)
        {
            return dest1.GetStationFacility(carrier, StationFacility.FacilityType.Cargo, true).TypeLevel > 0
                   && dest2.GetStationFacility(carrier, StationFacility.FacilityType.Cargo, true).TypeLevel > 0;
        }

        public static bool IsRouteInCorrectArea(Station dest1, Station dest2)
        {
            double distance = MathHelpers.GetDistance(
                dest1.Profile.Coordinates.ConvertToGeoCoordinate(),
                dest2.Profile.Coordinates.ConvertToGeoCoordinate());

            bool isOk = (dest1.Profile.Country == dest2.Profile.Country || distance < 1000
                            || (dest1.Profile.Country.Region == dest2.Profile.Country.Region
                                && (dest1.Profile.Type == StationProfile.StationType.ShortHaulInternational
                                    || dest1.Profile.Type == StationProfile.StationType.LongHaulInternational)
                                && (dest2.Profile.Type == StationProfile.StationType.ShortHaulInternational
                                    || dest2.Profile.Type == StationProfile.StationType.LongHaulInternational))
                            || (dest1.Profile.Type == StationProfile.StationType.LongHaulInternational
                                && dest2.Profile.Type == StationProfile.StationType.LongHaulInternational));

            return isOk;
        }

        public static void SetCoachHomebase(FleetCoach coach)
        {
            Station homebase = GetServiceStation(coach.Coach.Carrier) ?? GetDestinationStation(coach.Coach.Carrier, coach.Homebase);

            if (homebase.Terminals.GetNumberOfGates(coach.Coach.Carrier) == 0)
            {
                StationHelpers.RentGates(homebase, coach.Coach.Carrier, StationContract.ContractType.Full,
                                         coach.Coach.Carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger);
                StationFacility checkinFacility =
                    StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);

                if (
                    homebase.GetStationFacility(coach.Coach.Carrier, StationFacility.FacilityType.CheckIn)
                            .TypeLevel == 0)
                {
                    homebase.AddStationFacility(
                        coach.Coach.Carrier,
                        checkinFacility,
                        GameObject.GetInstance().GameTime);
                    CarrierHelpers.AddCarrierInvoice(
                        coach.Coach.Carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Purchases,
                        -checkinFacility.Price);
                }
            }

            coach.Homebase = homebase;
        }

        //updates a cpu carrier
        // one of a computer carrier's daily decisions; an error in it (another carrier changing the same station or
        // coach at the same moment) is logged and the decision skipped for the day, instead of failing the whole day
        private static void Safe(Carrier carrier, string name, Action decision)
        {
            using (Profiler.Measure("ai: " + name))
            {
                try
                {
                    decision();
                }
                catch (Exception ex)
                {
                    Infrastructure.StartupLog.Write("Computer carrier " + carrier.Profile?.Name + ": " + name + " failed and was skipped today: " + ex);
                }
            }
        }

        public static void UpdateCPUCarrier(Carrier carrier)
        {
            Parallel.Invoke(
                () => Safe(carrier, "CheckForNewRoute", () => CheckForNewRoute(carrier)),
                () => Safe(carrier, "CheckForNewHub", () => CheckForNewHub(carrier)),
                () => Safe(carrier, "CheckForUpdateRoute", () => CheckForUpdateRoute(carrier)),
                () => Safe(carrier, "CheckForCoachesWithoutRoutes", () => CheckForCoachesWithoutRoutes(carrier)),
                () => Safe(carrier, "CheckForOrderOfCoaches", () => CheckForOrderOfCoaches(carrier)),
                () => Safe(carrier, "CheckForCarrierAlliance", () => CheckForCarrierAlliance(carrier)),
                () => Safe(carrier, "CheckForCarrierCodesharing", () => CheckForCarrierCodesharing(carrier)),
                () => Safe(carrier, "CheckForSubsidiaryCarrier", () => CheckForSubsidiaryCarrier(carrier)),
                () => Safe(carrier, "CheckForCarrierStationFacilities", () => CheckForCarrierStationFacilities(carrier)),
                () => Safe(carrier, "CheckForOrderOfCoaches", () => CheckForOrderOfCoaches(carrier))); //close parallel.invoke
        }

        #endregion

        #region Methods

        private static void ChangeRouteServiceLevel(PassengerRoute route)
        {
            var oRoutes = new List<Route>(Carriers.GetCarriers(a => a != route.Carrier).SelectMany(a => a.Routes));

            IEnumerable<Route> sameRoutes =
                oRoutes.Where(
                    r =>
                    (r.Type == Route.RouteType.Mixed || r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Helicopter)
                    && ((r.Destination1 == route.Destination1 && r.Destination2 == route.Destination2)
                        || (r.Destination2 == route.Destination1 && r.Destination1 == route.Destination2)));

            var enumerable = sameRoutes as Route[] ?? sameRoutes.ToArray();
            if (enumerable.Any())
            {
                double avgServiceLevel =
                    enumerable.Where(r => r is PassengerRoute)
                              .Average(r => ((PassengerRoute) r).GetServiceLevel(CoachClass.ClassType.EconomyClass));

                RouteClassesConfiguration configuration = GetRouteConfiguration(route);

                Array types = Enum.GetValues(typeof (RouteFacility.FacilityType));

                int ct = 0;
                while (avgServiceLevel > route.GetServiceLevel(CoachClass.ClassType.EconomyClass)
                       && ct < types.Length)
                {
                    var type = (RouteFacility.FacilityType) types.GetValue(ct);

                    RouteFacility currentFacility =
                        route.GetRouteCoachClass(CoachClass.ClassType.EconomyClass).GetFacility(type);

                    List<RouteFacility> facilities =
                        RouteFacilities.GetFacilities(type).OrderBy(f => f.ServiceLevel).ToList();

                    int index = facilities.IndexOf(currentFacility);

                    if (index + 1 < facilities.Count)
                    {
                        route.GetRouteCoachClass(CoachClass.ClassType.EconomyClass)
                             .AddFacility(facilities[index + 1]);
                    }

                    ct++;
                }
            }
            else
            {
                Array types = Enum.GetValues(typeof (RouteFacility.FacilityType));
                double currentServiceLevel = route.GetServiceLevel(CoachClass.ClassType.EconomyClass);

                int ct = 0;
                while (currentServiceLevel + 50 > route.GetServiceLevel(CoachClass.ClassType.EconomyClass)
                       && ct < types.Length)
                {
                    var type = (RouteFacility.FacilityType) types.GetValue(ct);

                    RouteFacility currentFacility =
                        route.GetRouteCoachClass(CoachClass.ClassType.EconomyClass).GetFacility(type);

                    List<RouteFacility> facilities =
                        RouteFacilities.GetFacilities(type).OrderBy(f => f.ServiceLevel).ToList();

                    int index = facilities.IndexOf(currentFacility);

                    if (index + 1 < facilities.Count)
                    {
                        route.GetRouteCoachClass(CoachClass.ClassType.EconomyClass)
                             .AddFacility(facilities[index + 1]);
                    }

                    ct++;
                }
            }
        }

        //checks for building station facilities for the carrier
        private static void CheckForCarrierStationFacilities(Carrier carrier)
        {
            int minRoutesForTicketOffice = 3 + (int) carrier.Mentality;
            List<Station> stations =
                carrier.Stations.FindAll(
                    a => CarrierHelpers.GetStationOutboundRoutes(carrier, a) >= minRoutesForTicketOffice);

            foreach (Station station in stations)
            {
                Boolean allianceHasTicketOffice = carrier.Alliances != null && carrier.Alliances.SelectMany(a => a.Members)
                                                                                      .Any(
                                                                                          m =>
                                                                                          station.GetCarrierStationFacility(m.Carrier, StationFacility.FacilityType.TicketOffice)
                                                                                                 .Facility.TypeLevel > 0);

                if (
                    station.GetCarrierStationFacility(carrier, StationFacility.FacilityType.TicketOffice)
                           .Facility.TypeLevel == 0 && !allianceHasTicketOffice
                    && !station.HasContractType(carrier, StationContract.ContractType.FullService)
                    && !station.HasContractType(carrier, StationContract.ContractType.MediumService))
                {
                    StationFacility facility =
                        StationFacilities.GetFacilities(StationFacility.FacilityType.TicketOffice)
                                         .Find(f => f.TypeLevel == 1);
                    double price = facility.Price;

                    if (!station.IsBuildingFacility(carrier, StationFacility.FacilityType.TicketOffice))
                    {
                        if (station.Profile.Country != carrier.Profile.Country)
                        {
                            price = price*1.25;
                        }

                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Purchases,
                            -price);

                        station.AddStationFacility(
                            carrier,
                            facility,
                            GameObject.GetInstance().GameTime.AddDays(facility.BuildingDays));
                    }
                }
            }
        }

        private static void CheckForCarrierAlliance(Carrier carrier)
        {
            int carrierAlliances = carrier.Alliances.Count;

            if (carrierAlliances == 0)
            {
                const int newAllianceInterval = 10000;
                Boolean newAlliance = Rnd.Next(newAllianceInterval) == 0;

                if (newAlliance)
                {
                    Alliance alliance = GetCarrierAlliance(carrier);

                    if (alliance == null)
                    {
                        //creates a new alliance for the carrier
                        CreateNewAlliance(carrier);
                    }
                        //joins an existing alliance
                    else
                    {
                        if (alliance.IsHumanAlliance)
                        {
                            alliance.AddPendingMember(
                                new PendingAllianceMember(
                                    GameObject.GetInstance().GameTime,
                                    alliance,
                                    carrier,
                                    PendingAllianceMember.AcceptType.Request));
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.AllianceNews,
                                              GameObject.GetInstance().GameTime,
                                              "Request to join alliance",
                                              $"[LI carrier={carrier.Profile.IATACode}] has requested to joined {alliance.Name}. The request can be accepted or declined on the alliance page"));
                        }
                        else
                        {
                            if (CanJoinAlliance(carrier, alliance))
                            {
                                alliance.AddMember(new AllianceMember(carrier, GameObject.GetInstance().GameTime));
                                GameObject.GetInstance()
                                          .NewsBox.AddNews(
                                              new News(
                                                  News.NewsType.AllianceNews,
                                                  GameObject.GetInstance().GameTime,
                                                  "Joined alliance",
                                                  $"[LI carrier={carrier.Profile.IATACode}] has joined {alliance.Name}"));
                            }
                        }
                    }
                }
            }
            else
            {
                CheckForInviteToAlliance(carrier);
            }
        }

        private static void CheckForCarrierCodesharing(Carrier carrier)
        {
            int carrierCodesharings = carrier.Codeshares.Count;

            double newCodesharingInterval = 0;
            switch (carrier.Mentality)
            {
                case Carrier.CarrierMentality.Aggressive:
                    newCodesharingInterval = 85000;
                    break;
                case Carrier.CarrierMentality.Moderate:
                    newCodesharingInterval = 850000;
                    break;
                case Carrier.CarrierMentality.Safe:
                    newCodesharingInterval = 8500000;
                    break;
            }
            newCodesharingInterval *= GameObject.GetInstance().Difficulty.AILevel;

            Boolean newCodesharing = !carrier.IsSubsidiary
                                     && Chance(newCodesharingInterval*(carrierCodesharings + 1));

            if (newCodesharing)
            {
                InviteToCodesharing(carrier);
            }
        }

        //checks for any coaches without routes
        private static readonly WeakDates<Carrier> IdleRouteRetry = new WeakDates<Carrier>();

        /// <summary>How many routes a day a computer carrier looks for its idle coach: one, and one more for every 40 idle (at most 4).</summary>
        public static int IdleSearchesPerDay(int idle)
        {
            return idle <= 0 ? 0 : Math.Min(4, 1 + idle/40);
        }

        private static void CheckForCoachesWithoutRoutes(Carrier carrier)
        {
            lock (carrier.Fleet)
            {
                // coach put up for lease that another carrier has taken: theirs now (the owner is paid the rent)
                carrier.Fleet.RemoveAll(f => f.Coach.Carrier != null && f.Coach.Carrier != carrier && f.Purchased != FleetCoach.PurchasedType.Leased);

                var fleet =
                    new List<FleetCoach>(
                        carrier.Fleet.FindAll(
                            a => a.Coach.BuiltDate <= GameObject.GetInstance().GameTime && !a.HasRoute));
                int max = fleet.Count;

                Boolean outlease = max > 0 && Rnd.Next(1000/max) == 0;

                if (outlease)
                {
                    IOrderedEnumerable<FleetCoach> sFleet = fleet.OrderBy(f => f.Coach.BuiltDate.Year);
                    sFleet.ToList()[0].Coach.Status = Coach.StatusTypes.Leasing;
                }

                fleet =
                    new List<FleetCoach>(
                        carrier.Fleet.FindAll(
                            a => a.Coach.BuiltDate <= GameObject.GetInstance().GameTime && !a.HasRoute && a.UsableBy(carrier)));

                // looking for a route takes long (every station in range is weighed); when nothing was found, wait a few
                // days before looking again instead of searching every day for the idle reserve coach
                DateTime retry;
                bool waiting = IdleRouteRetry.TryGetValue(carrier, out retry) && GameObject.GetInstance().GameTime < retry;
                if (fleet.Count > 0 && !waiting)
                {
                    // an carrier with many idle coach (a big fleet from the start data, or routes given up) opens
                    // several routes a day, so its coach don't wait months for a route
                    int searches = IdleSearchesPerDay(fleet.Count);
                    for (int i = 0; i < searches; i++)
                    {
                        int routesBefore = carrier.Routes.Count;
                        using (Profiler.Measure("ai: idle coach, new route search"))
                            CreateNewRoute(carrier);
                        if (carrier.Routes.Count == routesBefore)
                        {
                            IdleRouteRetry[carrier] = GameObject.GetInstance().GameTime.AddDays(3);
                            break;
                        }
                        System.Threading.Interlocked.Increment(ref AIRouteChoice.IdleRoutes);
                        if (!carrier.Fleet.Exists(a => a.Coach.BuiltDate <= GameObject.GetInstance().GameTime && !a.HasRoute && a.UsableBy(carrier))) break;
                    }
                }
            }

            // idle owned coach cost money: sell what the carrier has no use for
            using (Profiler.Measure("ai: idle coach, sell surplus"))
                OwnershipService.SellSurplus(carrier);
        }

        // Giving the computer carriers the player's fleet renewal was tried and taken out again. It worked: it
        // chose sensible replacements once told to (a Fokker 50 for a DC-3, an A318 for a 737-200) and replaced
        // every coach of a type rather than skipping those whose stations it could not serve. But buying
        // second-hand cannot modernise a fleet, because a used coach is itself old: over ten years with twenty
        // opponents it replaced 673 coach out of 1,077 and the fleet came out 21.3 years old against 21.1
        // without it, with 64% of types no longer built against 64-68%. It also crashed the day's flying now and
        // then, a null in FleetCoachHelpers.SetTripStats as an coach landed, which is what happens when a
        // fleet is rearranged under coach that are in the air.
        //
        // Worth doing properly rather than tuning: let an carrier order new coach, which needs the deliveries
        // the player's renewal processes and the computer carriers have not got, and leave an coach alone until
        // it is on the ground. vm fleetage measures the fleet's age and FleetRenewalService already takes the
        // carrier it works for, so both halves of the measurement are still here.

        private static void CheckForInviteToAlliance(Carrier carrier)
        {
            Alliance alliance = carrier.Alliances[0];

            int members = alliance.Members.Count;
            const int inviteToAllianceInterval = 100000;
            bool inviteToAlliance = Chance((double) inviteToAllianceInterval*members);

            if (inviteToAlliance)
            {
                InviteToAlliance(carrier, alliance);
            }
        }

        //checks for ordering new coaches

        //checks for etablishing a new hub
        private static void CheckForNewHub(Carrier carrier)
        {
            int hubs = carrier.GetHubs().Count;

            int newHubInterval = 0;
            switch (carrier.Mentality)
            {
                case Carrier.CarrierMentality.Aggressive:
                    newHubInterval = 100000;
                    break;
                case Carrier.CarrierMentality.Moderate:
                    newHubInterval = 1000000;
                    break;
                case Carrier.CarrierMentality.Safe:
                    newHubInterval = 10000000;
                    break;
            }

            bool newHub = Chance((double) newHubInterval*hubs);

            if (newHub)
            {
                //creates a new hub for the carrier
                CreateNewHub(carrier);
            }
        }

        private static void CheckForNewRoute(Carrier carrier)
        {
            int coachesInOrder;
            lock (carrier.Fleet)
            {
                var fleet = new List<FleetCoach>(carrier.Fleet);
                coachesInOrder = fleet.Count(a => a.Coach.BuiltDate > GameObject.GetInstance().GameTime);
            }

            int newRouteInterval = 0;
            switch (carrier.Mentality)
            {
                case Carrier.CarrierMentality.Aggressive:
                    newRouteInterval = 10000;
                    break;
                case Carrier.CarrierMentality.Moderate:
                    newRouteInterval = 100000;
                    break;
                case Carrier.CarrierMentality.Safe:
                    newRouteInterval = 1000000;
                    break;
            }

            // an carrier with no routes at all (a new subsidiary) gets going quickly whatever its style
            if (carrier.Routes.Count == 0)
            {
                newRouteInterval = Math.Min(newRouteInterval, 10000);
            }

            // with realistic costs, an carrier sitting on cash puts it to work: new routes come faster
            if (PricingService.Realistic && newRouteInterval > 10000)
            {
                double start = Math.Max(1, GameObject.GetInstance().StartMoney);
                if (carrier.Money > 4*start) newRouteInterval /= 4;
                else if (carrier.Money > 2*start) newRouteInterval /= 2;
            }

            bool newRoute = Chance((double) newRouteInterval*(coachesInOrder + 1)/1100);

            if (newRoute)
            {
                //creates a new route for the carrier
                CreateNewRoute(carrier);
            }
        }

        private static void CheckForOrderOfCoaches(Carrier carrier)
        {
            int newCoachesInterval = 0;

            var fleet = new List<FleetCoach>(carrier.Fleet);

            int coaches = fleet.Count + 1;

            int coachesWithoutRoute = fleet.Count(a => !a.HasRoute) + 1;

            switch (carrier.Mentality)
            {
                case Carrier.CarrierMentality.Aggressive:
                    newCoachesInterval = 10000;
                    break;
                case Carrier.CarrierMentality.Moderate:
                    newCoachesInterval = 100000;
                    break;
                case Carrier.CarrierMentality.Safe:
                    newCoachesInterval = 1000000;
                    break;
            }
            bool newCoaches = Chance((double) newCoachesInterval*(coaches/2)*coachesWithoutRoute);

            if (newCoaches && carrier.Profile.PrimaryPurchasing != CarrierProfile.PreferedPurchasing.Leasing)
            {
                //order new coaches for the carrier
                OrderCoaches(carrier);
            }
        }

        //creates a new hub for an carrier

        //checks for the creation of a subsidiary carrier for an carrier
        private static void CheckForSubsidiaryCarrier(Carrier carrier)
        {
            int subCarriers = carrier.Subsidiaries.Count;

            double newSubInterval = 0;
            switch (carrier.Mentality)
            {
                case Carrier.CarrierMentality.Aggressive:
                    newSubInterval = 100000;
                    break;
                case Carrier.CarrierMentality.Moderate:
                    newSubInterval = 1000000;
                    break;
                case Carrier.CarrierMentality.Safe:
                    newSubInterval = 10000000;
                    break;
            }
            newSubInterval *= GameObject.GetInstance().Difficulty.AILevel;

            //newSubInterval = 0;

            bool newSub = !carrier.IsSubsidiary && Chance(newSubInterval*(subCarriers + 1))
                             && carrier.FutureCarriers.Count > 0 && carrier.Money > carrier.StartMoney/5;

            if (newSub)
            {
                //creates a new subsidiary carrier for the carrier
                CreateSubsidiaryCarrier(carrier);
            }
        }

        private static void CheckForUpdateRoute(Carrier carrier)
        {
            int totalHours = Rnd.Next(24*7, 24*13);
            foreach (
                Route route in
                    carrier.Routes.FindAll(
                        r => GameObject.GetInstance().GameTime.Subtract(r.LastUpdated).TotalHours > totalHours))
            {
                if (route.HasCoach)
                {
                    double balance = route.GetBalance(route.LastUpdated, GameObject.GetInstance().GameTime);

                    Route.RouteType routeType = route.Type;

                    // with realistic costs the carrier prices by load and competition (see AIPricingService)
                    if (PricingService.Realistic && route is PassengerRoute pricedRoute)
                    {
                        if (AIPricingService.Review(carrier, pricedRoute) != null)
                        {
                            route.LastUpdated = GameObject.GetInstance().GameTime;
                        }
                    }
                    else if (PricingService.Realistic && route is CargoRoute cargoPriced)
                    {
                        if (AIPricingService.ReviewCargo(carrier, cargoPriced) != null)
                        {
                            route.LastUpdated = GameObject.GetInstance().GameTime;
                        }
                    }

                    if (routeType == Route.RouteType.Mixed || routeType == Route.RouteType.Passenger || routeType == Route.RouteType.Helicopter)
                    {
                        // a route that loses money with half its seats empty is given up when the route forecast says
                        // it won't pay for its own trips either (its coach is then free for a better one)
                        bool drop = false;
                        using (Profiler.Measure("ai: review route by forecast"))
                        {
                            try
                            {
                                drop = AIRouteChoice.ShouldDrop(carrier, route, balance);
                            }
                            catch (Exception ex)
                            {
                                StartupLog.Write("Route review failed for " + carrier.Profile.Name + ": " + ex.Message);
                            }
                        }
                        if (drop)
                        {
                            System.Threading.Interlocked.Increment(ref AIRouteChoice.Dropped);
                            DropRoute(carrier, route);
                            continue;
                        }

                        if (balance < -1000)
                        {
                            if (!PricingService.Realistic && route.FillingDegree > 0.50 && (((PassengerRoute) route).IncomePerPassenger < 0.50))
                            {
                                foreach (RouteCoachClass rac in ((PassengerRoute) route).Classes)
                                {
                                    rac.FarePrice += 10;
                                }
                                route.LastUpdated = GameObject.GetInstance().GameTime;
                            }
                            if (route.FillingDegree >= 0.2 && route.FillingDegree <= 0.50)
                            {
                                ChangeRouteServiceLevel((PassengerRoute) route);
                            }
                            // (a new route gets a month to find its passengers)
                            if (route.FillingDegree < 0.2 && (GameObject.GetInstance().GameTime - route.StartDate).TotalDays >= 30)
                            {
                                System.Threading.Interlocked.Increment(ref AIRouteChoice.EmptyDropped);
                                carrier.RemoveRoute(route);
                    CompetitorService.RecordDropped(carrier, route);

                                if (route.HasCoach)
                                {
                                    route.GetCoaches().ForEach(a => a.RemoveRoute(route));
                                }

                                if (carrier.Routes.Count == 0)
                                {
                                    CreateNewRoute(carrier);
                                }

                                NewsFeeds.AddNewsFeed(
                                    new NewsFeed(
                                        GameObject.GetInstance().GameTime,
                                        string.Format(
                                            Translator.GetInstance().GetString("NewsFeed", "1002"),
                                            carrier.Profile.Name,
                                            new StationCodeFormatter().Convert(route.Destination1),
                                            new StationCodeFormatter().Convert(route.Destination2))));
                            }
                        }
                    }
                    else
                    {
                        if (balance < -1000)
                        {
                            if (route.FillingDegree > 0.45)
                            {
                                ((CargoRoute) route).PricePerUnit += 10;
                            }
                            if (route.FillingDegree <= 0.45)
                            {
                                carrier.RemoveRoute(route);
                    CompetitorService.RecordDropped(carrier, route);

                                if (route.HasCoach)
                                {
                                    route.GetCoaches().ForEach(a => a.RemoveRoute(route));
                                }

                                if (carrier.Routes.Count == 0)
                                {
                                    CreateNewRoute(carrier);
                                }

                                NewsFeeds.AddNewsFeed(
                                    new NewsFeed(
                                        GameObject.GetInstance().GameTime,
                                        string.Format(
                                            Translator.GetInstance().GetString("NewsFeed", "1002"),
                                            carrier.Profile.Name,
                                            new StationCodeFormatter().Convert(route.Destination1),
                                            new StationCodeFormatter().Convert(route.Destination2))));
                            }
                        }
                    }
                }
                if (route.Banned)
                {
                    carrier.RemoveRoute(route);
                    CompetitorService.RecordDropped(carrier, route);

                    if (route.HasCoach)
                    {
                        route.GetCoaches().ForEach(a => a.RemoveRoute(route));
                    }

                    if (carrier.Routes.Count == 0)
                    {
                        CreateNewRoute(carrier);
                    }

                    NewsFeeds.AddNewsFeed(
                        new NewsFeed(
                            GameObject.GetInstance().GameTime,
                            string.Format(
                                Translator.GetInstance().GetString("NewsFeed", "1002"),
                                carrier.Profile.Name,
                                new StationCodeFormatter().Convert(route.Destination1),
                                new StationCodeFormatter().Convert(route.Destination2))));
                }
            }
        }

        // gives up a computer carrier's route; its coach is free for another
        private static void DropRoute(Carrier carrier, Route route)
        {
            carrier.RemoveRoute(route);
            CompetitorService.RecordDropped(carrier, route);

            if (route.HasCoach)
            {
                route.GetCoaches().ForEach(a => a.RemoveRoute(route));
            }

            if (carrier.Routes.Count == 0)
            {
                CreateNewRoute(carrier);
            }

            NewsFeeds.AddNewsFeed(
                new NewsFeed(
                    GameObject.GetInstance().GameTime,
                    string.Format(
                        Translator.GetInstance().GetString("NewsFeed", "1002"),
                        carrier.Profile.Name,
                        new StationCodeFormatter().Convert(route.Destination1),
                        new StationCodeFormatter().Convert(route.Destination2))));
        }

        private static void CreateBusinessRouteTimeTable(Route route, FleetCoach coach)
        {
            TimeSpan minTripTime =
                route.GetTripTime(coach.Coach.Type)
                     .Add(new TimeSpan(FleetCoachHelpers.GetMinTimeBetweenTrips(coach).Ticks));

            const int maxHours = 10 - 6; //from 06:00 to 10:00 and from 18:00 to 22:00

            int tripsPerDay = Convert.ToInt16(maxHours*60/(2*minTripTime.TotalMinutes));

            string tripCode1 = coach.Coach.Carrier.GetNextTripCode(0);
            string tripCode2 = coach.Coach.Carrier.GetNextTripCode(1);

            route.TimeTable = CreateBusinessRouteTimeTable(route, coach, tripsPerDay, tripCode1, tripCode2);
        }

        private static void CreateNewAlliance(Carrier carrier)
        {
            string name = Alliance.GenerateAllianceName();
            Station headquarter =
                carrier.Stations.FirstOrDefault(
                    a => a.GetCurrentStationFacility(carrier, StationFacility.FacilityType.Service).TypeLevel > 0);


            if (headquarter != null)
            {
                var alliance = new Alliance(GameObject.GetInstance().GameTime, name, headquarter);
                alliance.AddMember(new AllianceMember(carrier, GameObject.GetInstance().GameTime));

                Alliances.AddAlliance(alliance);

                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.StandardNews,
                                  GameObject.GetInstance().GameTime,
                                  "New alliance",
                                  $"A new alliance: {name} has been created by [LI carrier={carrier.Profile.IATACode}]"));

                InviteToAlliance(carrier, alliance);
            }
        }

        private static void CreateNewHub(Carrier carrier)
        {
            var type = HubType.TypeOfHub.FocusCity;

            if (carrier.MarketFocus == CarrierFocus.Domestic
                || carrier.MarketFocus == CarrierFocus.Local)
            {
                type = HubType.TypeOfHub.FocusCity;
            }

            if (carrier.MarketFocus == CarrierFocus.Global)
            {
                type = HubType.TypeOfHub.Hub;
            }

            if (carrier.MarketFocus == CarrierFocus.Regional)
            {
                type = HubType.TypeOfHub.RegionalHub;
            }

            List<Station> stations = carrier.Stations.FindAll(a => CarrierHelpers.CanCreateHub(carrier, a, HubTypes.GetHubType(type)));

            if (stations.Count > 0)
            {
                HubType hubtype = HubTypes.GetHubType(type);

                Station station = (from a in stations orderby a.Profile.Size descending select a).First();

                station.AddHub(new Hub(carrier, hubtype));

                CarrierHelpers.AddCarrierInvoice(
                    carrier,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.Purchases,
                    -StationHelpers.GetHubPrice(station, hubtype));

                // NewsFeeds.AddNewsFeed(new NewsFeed(GameObject.GetInstance().GameTime, string.Format(Translator.GetInstance().GetString("NewsFeed", "1003"), carrier.Profile.Name, new StationCodeFormatter().Convert(station), station.Profile.Town.Name, station.Profile.Town.Country.ShortName)));
            }
        }

        private static void CreateNewRoute(Carrier carrier)
        {
            long pfC = Profiler.Start();
            Station station = GetRouteStartDestination(carrier);
            Profiler.Stop("route search: start station", pfC);
            if (station == null) System.Threading.Interlocked.Increment(ref AIRouteChoice.NoStart);

            if (station != null)
            {
                pfC = Profiler.Start();
                // a passenger carrier forecasts the destinations with the most demand and takes the one that earns the
                // most (as the route planner forecasts a route for the player)
                Station destination = carrier.CarrierRouteFocus == Route.RouteType.Passenger && !carrier.IsHuman
                                          ? AIRouteChoice.PickDestination(carrier, station, GetDestinationStations(carrier, station))
                                          : GetDestinationStation(carrier, station);
                Profiler.Stop("route search: destination", pfC);
                if (destination == null) System.Threading.Interlocked.Increment(ref AIRouteChoice.NoDestination);

                if (destination != null)
                {
                    bool doLeasing = carrier.Profile.PrimaryPurchasing == CarrierProfile.PreferedPurchasing.Leasing
                                        || (carrier.Profile.PrimaryPurchasing
                                            == CarrierProfile.PreferedPurchasing.Random
                                            && (Rnd.Next(5) > 1 || carrier.Money < 10000000));

                    pfC = Profiler.Start();
                    FleetCoach fCoach = GetFleetCoach(carrier, station, destination);
                    Profiler.Stop("route search: own coach", pfC);

                    // the market is only searched when no coach of the fleet can fly the route
                    KeyValuePair<Coach, bool>? coach = null;
                    if (fCoach == null)
                    {
                        pfC = Profiler.Start();
                        coach = GetCoachForRoute(
                            carrier,
                            station,
                            destination,
                            doLeasing,
                            carrier.CarrierRouteFocus);
                        Profiler.Stop("route search: coach to buy", pfC);
                    }

                    if (!coach.HasValue && fCoach == null) System.Threading.Interlocked.Increment(ref AIRouteChoice.NoCoach);
                    if (fCoach != null) System.Threading.Interlocked.Increment(ref AIRouteChoice.UsedIdle);
                    else if (coach.HasValue) System.Threading.Interlocked.Increment(ref AIRouteChoice.GotCoach);
                    if (coach.HasValue || fCoach != null)
                    {
                        if (!StationHelpers.HasFreeGates(destination, carrier))
                        {
                            StationHelpers.RentGates(destination, carrier, StationContract.ContractType.LowService);
                        }

                        if (!carrier.Stations.Contains(destination))
                        {
                            carrier.AddStation(destination);
                        }

                        Guid id = Guid.NewGuid();

                        Route route = null;

                        if (carrier.CarrierRouteFocus == Route.RouteType.Passenger)
                        {
                            double price = PassengerHelpers.GetPassengerPrice(station, destination);

                            route = new PassengerRoute(
                                id.ToString(),
                                station,
                                destination,
                                GameObject.GetInstance().GameTime,
                                price);

                            RouteClassesConfiguration configuration = GetRouteConfiguration((PassengerRoute) route);

                            foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                            {
                                ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type).FarePrice = price
                                                                                                                    *GeneralHelpers
                                                                                                                         .ClassToPriceFactor
                                                                                                                         (
                                                                                                                             classConfiguration
                                                                                                                                 .Type);

                                foreach (RouteFacility facility in classConfiguration.GetFacilities())
                                {
                                    ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type)
                                                            .AddFacility(facility);
                                }
                            }
                        }
                        if (carrier.CarrierRouteFocus == Route.RouteType.Helicopter)
                        {
                            double price = PassengerHelpers.GetPassengerPrice(station, destination);

                            route = new HelicopterRoute(
                                id.ToString(),
                                station,
                                destination,
                                GameObject.GetInstance().GameTime,
                                price);

                            RouteClassesConfiguration configuration = GetRouteConfiguration((PassengerRoute) route);

                            foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                            {
                                ((HelicopterRoute) route).GetRouteCoachClass(classConfiguration.Type).FarePrice = price
                                                                                                                     *GeneralHelpers
                                                                                                                          .ClassToPriceFactor
                                                                                                                          (
                                                                                                                              classConfiguration
                                                                                                                                  .Type);

                                foreach (RouteFacility facility in classConfiguration.GetFacilities())
                                {
                                    ((HelicopterRoute) route).GetRouteCoachClass(classConfiguration.Type)
                                                             .AddFacility(facility);
                                }
                            }
                        }
                        if (carrier.CarrierRouteFocus == Route.RouteType.Cargo)
                        {
                            route = new CargoRoute(
                                id.ToString(),
                                station,
                                destination,
                                GameObject.GetInstance().GameTime,
                                PassengerHelpers.GetCargoPrice(station, destination));
                        }

                        bool isDeptOk = true;
                        bool isDestOk = true;

                        if (!StationHelpers.HasFreeGates(station, carrier))
                        {
                            isDeptOk = StationHelpers.RentGates(
                                station,
                                carrier,
                                StationContract.ContractType.LowService);
                        }

                        if (!StationHelpers.HasFreeGates(destination, carrier))
                        {
                            isDestOk = StationHelpers.RentGates(
                                station,
                                carrier,
                                StationContract.ContractType.LowService);
                        }

                        if (isDestOk && isDeptOk)
                        {
                            bool humanHasRoute =
                                Carriers.GetHumanCarriers()
                                        .SelectMany(a => a.Routes)
                                        .ToList()
                                        .Exists(
                                            r =>
                                            route != null && ((r.Destination1 == route.Destination1
                                                               && r.Destination2 == route.Destination2)
                                                              || (r.Destination1 == route.Destination2
                                                                  && r.Destination2 == route.Destination1)));

                            if (humanHasRoute && Infrastructure.Settings.GetInstance().MailsOnCarrierRoutes)
                            {
                                if (route != null)
                                    GameObject.GetInstance()
                                              .NewsBox.AddNews(
                                                  new News(
                                                      News.NewsType.CarrierNews,
                                                      GameObject.GetInstance().GameTime,
                                                      Translator.GetInstance().GetString("News", "1013"),
                                                      string.Format(
                                                          Translator.GetInstance().GetString("News", "1013", "message"),
                                                          carrier.Profile.IATACode,
                                                          route.Destination1.Profile.IATACode,
                                                          route.Destination2.Profile.IATACode)));
                            }

                            Country newDestination =
                                carrier.Routes.Count(
                                    r =>
                                    r.Destination1.Profile.Country == station.Profile.Country
                                    || r.Destination2.Profile.Country == station.Profile.Country) == 0
                                    ? station.Profile.Country
                                    : null;

                            newDestination =
                                carrier.Routes.Count(
                                    r =>
                                    r.Destination1.Profile.Country == destination.Profile.Country
                                    || r.Destination2.Profile.Country == destination.Profile.Country) == 0
                                    ? destination.Profile.Country
                                    : newDestination;

                            if (newDestination != null && Infrastructure.Settings.GetInstance().MailsOnCarrierRoutes)
                            {
                                GameObject.GetInstance()
                                          .NewsBox.AddNews(
                                              new News(
                                                  News.NewsType.CarrierNews,
                                                  GameObject.GetInstance().GameTime,
                                                  Translator.GetInstance().GetString("News", "1014"),
                                                  string.Format(
                                                      Translator.GetInstance().GetString("News", "1014", "message"),
                                                      carrier.Profile.IATACode,
                                                      ((Country) new CountryCurrentCountryResolver().Convert(newDestination))
                                                          .Name)));
                            }

                            if (!StationHelpers.HasFreeGates(station, carrier))
                            {
                                StationHelpers.RentGates(station, carrier, StationContract.ContractType.LowService);
                            }

                            if (!StationHelpers.HasFreeGates(destination, carrier))
                            {
                                StationHelpers.RentGates(station, carrier, StationContract.ContractType.LowService);
                            }

                            //Console.WriteLine("{3}: {0} has created a route between {1} and {2}", carrier.Profile.Name, route.Destination1.Profile.Name, route.Destination2.Profile.Name,GameObject.GetInstance().GameTime.ToShortDateString());

                            if (fCoach == null)
                            {
                                if (CarrierHelpers.NeedsNewRegistration(carrier, coach.Value.Key.TailNumber))
                                {
                                    coach.Value.Key.TailNumber =
                                        carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier);
                                }

                                if (coach.Value.Value) //loan
                                {
                                    double amount = coach.Value.Key.GetPrice() - carrier.Money + 20000000;

                                    var loan = new Loan(
                                        GameObject.GetInstance().GameTime,
                                        amount,
                                        120,
                                        GeneralHelpers.GetCarrierLoanRate(carrier));

                                    double payment = loan.GetMonthlyPayment();

                                    carrier.AddLoan(loan);
                                    CarrierHelpers.AddCarrierInvoice(
                                        carrier,
                                        loan.Date,
                                        Invoice.InvoiceType.Loans,
                                        loan.Amount);
                                    // the loan pays for the coach: it isn't free
                                    CarrierHelpers.AddCarrierInvoice(
                                        carrier,
                                        GameObject.GetInstance().GameTime,
                                        Invoice.InvoiceType.Purchases,
                                        -coach.Value.Key.GetPrice());
                                }
                                else
                                {
                                    if (doLeasing)
                                    {
                                        CarrierHelpers.AddCarrierInvoice(
                                            carrier,
                                            GameObject.GetInstance().GameTime,
                                            Invoice.InvoiceType.Rents,
                                            -coach.Value.Key.LeasingPrice*2);

                                        if (coach.Value.Key.Owner != null && coach.Value.Key.Owner.IsHuman)
                                            NewsFeeds.AddNewsFeed(new NewsFeed(GameObject.GetInstance().GameTime,
                                                                               string.Format(Translator.GetInstance().GetString("NewsFeed", "1004"), carrier.Profile.Name, coach.Value.Key.TailNumber)));
                                    }
                                    else
                                    {
                                        CarrierHelpers.AddCarrierInvoice(
                                            carrier,
                                            GameObject.GetInstance().GameTime,
                                            Invoice.InvoiceType.Purchases,
                                            -coach.Value.Key.GetPrice());
                                    }
                                }

                                fCoach =
                                    new FleetCoach(
                                        doLeasing
                                            ? FleetCoach.PurchasedType.Leased
                                            : FleetCoach.PurchasedType.Bought,
                                        GameObject.GetInstance().GameTime,
                                        carrier,
                                        coach.Value.Key,
                                        station);
                                carrier.Fleet.Add(fCoach);

                                CoachHelpers.CreateCoachClasses(fCoach.Coach);
                            }

                            //NewsFeeds.AddNewsFeed(new NewsFeed(GameObject.GetInstance().GameTime, string.Format(Translator.GetInstance().GetString("NewsFeed", "1001"), carrier.Profile.Name, new StationCodeFormatter().Convert(route.Destination1), new StationCodeFormatter().Convert(route.Destination2))));

                            if (route.Type == Route.RouteType.Passenger || route.Type == Route.RouteType.Mixed || route.Type == Route.RouteType.Helicopter)
                            {
                                //creates a business route
                                if (IsBusinessRoute(route, fCoach))
                                {
                                    CreateBusinessRouteTimeTable(route, fCoach);
                                }
                                else
                                {
                                    CreateRouteTimeTable(route, fCoach);
                                }
                            }
                            if (route.Type == Route.RouteType.Cargo)
                            {
                                CreateCargoRouteTimeTable(route, fCoach);
                            }

                            fCoach.Status = FleetCoach.CoachStatus.ToRouteStart;
                            CarrierHelpers.HireCoachPilots(fCoach);

                            route.LastUpdated = GameObject.GetInstance().GameTime;
                        }
                        carrier.AddRoute(route);

                        fCoach?.AddRoute(route);
                        CompetitorService.RecordOpened(route);

                        StationFacility checkinFacility =
                            StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn)
                                             .Find(f => f.TypeLevel == 1);
                        StationFacility cargoTerminal =
                            StationFacilities.GetFacilities(StationFacility.FacilityType.Cargo)
                                             .Find(f => f.TypeLevel > 0);

                        /*
                        if (destination.getStationFacility(carrier, StationFacility.FacilityType.CheckIn).TypeLevel == 0)
                        {
                            destination.addStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);
                            CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -checkinFacility.Price);

                        }
                        if (station.getStationFacility(carrier, StationFacility.FacilityType.CheckIn).TypeLevel == 0)
                        {
                            station.addStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);
                            CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -checkinFacility.Price);

                        }
                        */

                        if (destination.GetStationFacility(carrier, StationFacility.FacilityType.Cargo).TypeLevel == 0
                            && destination.GetStationFacility(null, StationFacility.FacilityType.Cargo).TypeLevel == 0
                            && route.Type == Route.RouteType.Cargo)
                        {
                            destination.AddStationFacility(carrier, cargoTerminal, GameObject.GetInstance().GameTime);
                            CarrierHelpers.AddCarrierInvoice(
                                carrier,
                                GameObject.GetInstance().GameTime,
                                Invoice.InvoiceType.Purchases,
                                -cargoTerminal.Price);
                        }

                        if (station.GetStationFacility(carrier, StationFacility.FacilityType.Cargo).TypeLevel == 0
                            && station.GetStationFacility(null, StationFacility.FacilityType.Cargo).TypeLevel == 0
                            && route.Type == Route.RouteType.Cargo)
                        {
                            station.AddStationFacility(carrier, cargoTerminal, GameObject.GetInstance().GameTime);
                            CarrierHelpers.AddCarrierInvoice(
                                carrier,
                                GameObject.GetInstance().GameTime,
                                Invoice.InvoiceType.Purchases,
                                -cargoTerminal.Price);
                        }
                    }
                }
            }
        }

        //creates a new subsidiary carrier for the carrier
        private static void CreateSubsidiaryCarrier(Carrier carrier)
        {
            FutureSubsidiaryCarrier futureCarrier = carrier.FutureCarriers[Rnd.Next(carrier.FutureCarriers.Count)];

            carrier.FutureCarriers.Remove(futureCarrier);

            SubsidiaryCarrier sCarrier = CarrierHelpers.CreateSubsidiaryCarrier(
                carrier,
                carrier.Money/5,
                futureCarrier.Name,
                futureCarrier.IATA,
                futureCarrier.Mentality,
                futureCarrier.Market,
                futureCarrier.CarrierRouteFocus,
                futureCarrier.PreferedStation);
            sCarrier.Profile.AddLogo(new CarrierLogo(futureCarrier.Logo));
            sCarrier.Profile.Color = carrier.Profile.Color;

            CreateNewRoute(sCarrier);

            GameObject.GetInstance()
                      .NewsBox.AddNews(
                          new News(
                              News.NewsType.CarrierNews,
                              GameObject.GetInstance().GameTime,
                              "Created subsidiary",
                              $"[LI carrier={carrier.Profile.IATACode}] has created a new subsidiary carrier [LI carrier={sCarrier.Profile.IATACode}]"));
        }

        //checks for the creation of code sharing for an carrier

        //returns a "good" alliance for an carrier to join
        private static Alliance GetCarrierAlliance(Carrier carrier)
        {
            Alliance bestAlliance = (from a in Alliances.GetAlliances()
                                     where !a.Members.ToList().Exists(m => m.Carrier == carrier)
                                     orderby GetCarrierAllianceScore(carrier, a, true) descending
                                     select a).FirstOrDefault();

            if (bestAlliance != null && GetCarrierAllianceScore(carrier, bestAlliance, true) > 50)
            {
                return bestAlliance;
            }
            return null;
        }

        //returns the "score" for an carrier compared to an alliance
        private static double GetCarrierAllianceScore(Carrier carrier, Alliance alliance, Boolean forAlliance)
        {
            IEnumerable<Country> sameCountries =
                alliance.Members.SelectMany(m => m.Carrier.Stations)
                        .Select(a => a.Profile.Country)
                        .Distinct()
                        .Intersect(carrier.Stations.Select(a => a.Profile.Country).Distinct());
            IEnumerable<Station> sameDestinations =
                alliance.Members.SelectMany(m => m.Carrier.Stations).Distinct().Intersect(carrier.Stations);

            double carrierRoutes = carrier.Routes.Count;
            double allianceRoutes = alliance.Members.SelectMany(m => m.Carrier.Routes).Count();

            double coeff = forAlliance ? allianceRoutes*10 : carrierRoutes*10;

            double score = coeff + (5 - sameCountries.Count())*5 + (5 - sameDestinations.Count())*5;

            return score;
        }

        //returns the best fit carrier for an alliance
        private static Carrier GetAllianceCarrier(Alliance alliance)
        {
            Carrier bestCarrier = (from a in Carriers.GetAllCarriers()
                                   where !alliance.Members.ToList().Exists(m => m.Carrier == a) && a.Alliances.Count == 0
                                   orderby GetCarrierAllianceScore(a, alliance, false) descending
                                   select a).FirstOrDefault();

            if (GetCarrierAllianceScore(bestCarrier, alliance, false) > 50)
            {
                return bestCarrier;
            }
            return null;
        }

        private static int GetCodesharingScore(Carrier asker, Carrier carrier)
        {
            int diffCountries =
                asker.Stations.Select(a => a.Profile.Country)
                     .Intersect(carrier.Stations.Select(a => a.Profile.Country))
                     .Distinct()
                     .Count();
            int diffRoutes = asker.Routes.Count - carrier.Routes.Count;
            int coeff = asker.Stations.Select(a => a.Profile.Country).Distinct().Count()
                        > carrier.Stations.Select(a => a.Profile.Country).Distinct().Count()
                            ? 1
                            : -1;
            int askerRoutes = carrier.Routes.Count;

            return (diffRoutes*7) + (diffCountries*coeff*5) + (askerRoutes*3);
        }

        //creates a new alliance for an carrier

        //returns an coach from the fleet which fits a route
        private static FleetCoach GetFleetCoach(Carrier carrier, Station destination1, Station destination2)
        {
            //Order new coach
            List<FleetCoach> fleet =
                carrier.Fleet.FindAll(
                    f =>
                    !f.HasRoute && f.Coach.BuiltDate <= GameObject.GetInstance().GameTime && f.UsableBy(carrier)
                    && CarrierTypeRules.CanUseType(carrier, f.Coach.Type)
                    && f.Coach.Range
                    > MathHelpers.GetDistance(
                        destination1.Profile.Coordinates.ConvertToGeoCoordinate(),
                        destination2.Profile.Coordinates.ConvertToGeoCoordinate()));

            if (fleet.Count > 0)
            {
                return (from f in fleet orderby f.Coach.Range select f).First();
            }
            return null;
        }

        //returns the best fit for an coach for sale for a route true for loan

        //returns a free gate for an carrier
        private static Gate GetFreeCarrierGate(Carrier carrier, Station station, DayOfWeek day, TimeSpan time)
        {
            List<Gate> carrierGates = station.Terminals.GetGates(carrier);

            return carrierGates.FirstOrDefault();
        }

        private static Station GetRouteStartDestination(Carrier carrier)
        {
            List<Station> homeStations;

            lock (carrier.Stations)
            {
                homeStations = CarrierHelpers.GetHomebases(carrier);
            }
            homeStations.AddRange(carrier.GetHubs());

            Terminal.TerminalType terminaltype = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

            Station station = homeStations.Find(a => StationHelpers.HasFreeGates(a, carrier, terminaltype));

            if (station == null)
            {
                station = homeStations.Find(a => a.Terminals.GetFreeGates(terminaltype) > 0);
                if (station != null)
                {
                    StationHelpers.RentGates(station, carrier, StationContract.ContractType.LowService, terminaltype);
                }
                else
                {
                    station = GetServiceStation(carrier);
                    if (station != null)
                    {
                        StationHelpers.RentGates(station, carrier, StationContract.ContractType.LowService, terminaltype);
                    }
                }
            }

            return station;
        }

        private static Station GetServiceStation(Carrier carrier)
        {
            StationFacility facility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Service).Find(f => f.TypeLevel == 1);

            IOrderedEnumerable<Station> stations =
                from a in
                    carrier.Stations.FindAll(aa => aa.Terminals.GetFreeGates(carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger) > 0)
                orderby a.Profile.Size descending
                select a;

            if (stations.Any())
            {
                Station station = stations.First();

                if (station.GetCarrierStationFacility(carrier, StationFacility.FacilityType.Service).Facility.TypeLevel
                    == 0 && !station.HasContractType(carrier, StationContract.ContractType.FullService))

                    if (!station.IsBuildingFacility(carrier, StationFacility.FacilityType.Service))
                    {
                        station.AddStationFacility(
                            carrier,
                            facility,
                            GameObject.GetInstance().GameTime.AddDays(facility.BuildingDays));

                        double price = facility.Price;

                        if (station.Profile.Country != carrier.Profile.Country)
                        {
                            price = price*1.25;
                        }

                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Purchases,
                            -price);
                    }
                return station;
            }


            return null;
        }

        private static void InviteToAlliance(Carrier carrier, Alliance alliance)
        {
            Carrier bestFitCarrier = GetAllianceCarrier(alliance);

            if (bestFitCarrier != null)
            {
                if (bestFitCarrier == GameObject.GetInstance().HumanCarrier)
                {
                    alliance.AddPendingMember(
                        new PendingAllianceMember(
                            GameObject.GetInstance().GameTime,
                            alliance,
                            bestFitCarrier,
                            PendingAllianceMember.AcceptType.Invitation));
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.AllianceNews,
                                      GameObject.GetInstance().GameTime,
                                      "Invitation to join alliance",
                                      $"[LI carrier={carrier.Profile.IATACode}] has invited you to join {alliance.Name}. The invitation can be accepted or declined on the alliance page"));
                }
                else
                {
                    if (DoAcceptAllianceInvitation(bestFitCarrier, alliance))
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.AllianceNews,
                                          GameObject.GetInstance().GameTime,
                                          "Joined alliance",
                                          $"[LI carrier={bestFitCarrier.Profile.IATACode}] has joined {alliance.Name}"));
                        alliance.AddMember(new AllianceMember(bestFitCarrier, GameObject.GetInstance().GameTime));
                    }
                }
            }
        }

        private static void InviteToCodesharing(Carrier carrier)
        {
            //find the best carrier for codesharing
            IEnumerable<Carrier> carriers =
                Carriers.GetAllCarriers()
                        .Where(a => a != carrier && (!a.IsSubsidiary || ((SubsidiaryCarrier) a).Carrier != carrier)
                                    && !carrier.Codeshares.Any(c => c.Carrier1 == a || c.Carrier2 == a));

            int bestscore = 0;
            Carrier bestCarrier = null;

            foreach (Carrier tCarrier in carriers)
            {
                int score = GetCodesharingScore(carrier, tCarrier);

                if (score > bestscore)
                {
                    bestscore = score;
                    bestCarrier = tCarrier;
                }
            }

            const int minValue = 50;

            if (bestscore > minValue)
            {
                bool acceptInvitation = CarrierHelpers.AcceptCodesharing(
                    bestCarrier,
                    carrier,
                    CodeshareAgreement.CodeshareType.BothWays);

                if (acceptInvitation)
                {
                    if (bestCarrier.IsHuman)
                    {
                        // kept as an offer the player answers on the Codeshares page (a news action is lost when saving)
                        CodeshareService.AddOffer(bestCarrier, carrier);
                    }
                    else
                    {
                        var agreement = new CodeshareAgreement(
                            bestCarrier,
                            carrier,
                            CodeshareAgreement.CodeshareType.BothWays);
                        bestCarrier.AddCodeshareAgreement(agreement);
                        carrier.AddCodeshareAgreement(agreement);

                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.AllianceNews,
                                          GameObject.GetInstance().GameTime,
                                          "Codeshare Agreement",
                                          $"[LI carrier={carrier.Profile.IATACode}] and [LI carrier={bestCarrier.Profile.IATACode}] have made a codeshare agreement"));
                    }
                }
            }
        }

        private static bool IsBusinessRoute(Route route, FleetCoach coach)
        {
            double maxBusinessRouteTime = new TimeSpan(2, 0, 0).TotalMinutes;

            TimeSpan minTripTime =
                MathHelpers.GetTripTime(
                    route.Destination1.Profile.Coordinates.ConvertToGeoCoordinate(),
                    route.Destination2.Profile.Coordinates.ConvertToGeoCoordinate(),
                    coach.Coach.Type)
                           .Add(new TimeSpan(FleetCoachHelpers.GetMinTimeBetweenTrips(coach).Ticks));

            return minTripTime.TotalMinutes <= maxBusinessRouteTime;
        }

        private static void OrderCoaches(Carrier carrier)
        {
            List<CoachType> carrierCoaches = carrier.Profile.PreferedCoaches;

            int coaches = carrier.Fleet.Count;
            int coachesWithoutRoute = carrier.Fleet.Count(a => !a.HasRoute);

            int numberToOrder = Rnd.Next(1, 3 - (int) carrier.Mentality);

            List<Station> homeStations = CarrierHelpers.GetHomebases(carrier);

            var stationsList = homeStations.ToDictionary(a => a, a => (int) a.Profile.Size);
            //Parallel.ForEach(homeStations, a => { stationsList.Add(a, (int)a.Profile.Size); });

            if (stationsList.Count > 0)
            {
                Station homeStation = GetRandomItem(stationsList);

                List<CoachType> types =
                    CoachTypes.GetTypes(
                        t =>
                        t.Produced.From <= GameObject.GetInstance().GameTime
                        && t.Produced.To >= GameObject.GetInstance().GameTime && t.Price*numberToOrder < carrier.Money);

                if (carrier.CarrierRouteFocus == Route.RouteType.Cargo)
                {
                    types.RemoveAll(a => a.TypeCoach == CoachType.TypeOfCoach.Passenger);
                    types.RemoveAll(a => a.TypeCoach == CoachType.TypeOfCoach.Helicopter);
                }

                if (carrier.CarrierRouteFocus == Route.RouteType.Passenger)
                {
                    types.RemoveAll(a => a.TypeCoach == CoachType.TypeOfCoach.Cargo);
                    types.RemoveAll(a => a.TypeCoach == CoachType.TypeOfCoach.Helicopter);
                }
                if (carrier.CarrierRouteFocus == Route.RouteType.Helicopter)
                {
                    types.RemoveAll(a => a.TypeCoach == CoachType.TypeOfCoach.Cargo);
                    types.RemoveAll(a => a.TypeCoach == CoachType.TypeOfCoach.Passenger);
                }
                types = types.OrderBy(t => t.Price).ToList();

                var list = types.ToDictionary(type => type, type => (int) ((type.Range/(Convert.ToDouble(type.Price)/100000))) + (carrierCoaches.Contains(type) ? 10 : 0));

                /*
                Parallel.ForEach(types, t =>
                    {
                        list.Add(t, (int)((t.Range / (t.Price / 100000))));
                    });*/

                if (list.Keys.Count > 0)
                {
                    CoachType type = GetRandomItem(list);

                    var orders = new List<CoachOrder> {new CoachOrder(type, CoachHelpers.GetCoachClasses(type), numberToOrder, false)};

                    DateTime deliveryDate = CoachHelpers.GetOrderDeliveryDate(orders);
                    CarrierHelpers.OrderCoaches(carrier, orders, homeStation, deliveryDate);
                }
            }
        }

        private static void news_Action(object o)
        {
            var agreement = (CodeshareAgreement) o;

            agreement.Carrier1.AddCodeshareAgreement(agreement);
            agreement.Carrier2.AddCodeshareAgreement(agreement);
        }

        #endregion

        //check if an carrier can join an alliance
    }
}