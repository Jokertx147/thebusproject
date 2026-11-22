using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helper class for fleet coaches
    public class FleetCoachHelpers
    {
        public enum DelayType
        {
            None,
            CoachProblems,
            BadWeather,
            StationTraffic
        }

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        //returns the number of delay minutes (0 if not delayed) for an coach
        public static KeyValuePair<DelayType, int> GetDelayedMinutes(FleetCoach coach)
        {
            //has already been delayed
            if (!coach.CurrentTrip.IsOnTime)
                return new KeyValuePair<DelayType, int>(DelayType.None, 0);

            var delays = new Dictionary<DelayType, int> {{DelayType.CoachProblems, GetCoachAgeDelay(coach)}, {DelayType.BadWeather, 0}};

            //delays.Add(DelayType.Bad_weather, GetCoachWeatherDelay(coach));

            var delay = new KeyValuePair<DelayType, int>(DelayType.None, 0);
            foreach (var d in delays)
            {
                if (d.Value > delay.Value)
                    delay = d;
            }

            return delay;
        }

        //returns the delay time because of the age of an coach
        public static int GetCoachAgeDelay(FleetCoach coach)
        {
            int age = coach.Coach.Age;

            int tAge = 100 - (age*2);

            bool delayed = Rnd.Next(100) > tAge;

            if (delayed)
                return Rnd.Next(0, age)*2;
            return 0;
        }

        //returns the delay time because of the weather for an coach
        public static int GetCoachWeatherDelay(FleetCoach coach)
        {
            Station departureStation = coach.CurrentTrip.GetDepartureStation();

            int windFactor = 0;

            if (departureStation.Weather[0] == null)
                return 0;

            switch (departureStation.Weather[0].WindSpeed)
            {
                case Weather.eWindSpeed.StrongBreeze:
                    windFactor = 2;
                    break;
                case Weather.eWindSpeed.NearGale:
                    windFactor = 4;
                    break;
                case Weather.eWindSpeed.Gale:
                    windFactor = 6;
                    break;
                case Weather.eWindSpeed.StrongGale:
                    windFactor = 8;
                    break;
                case Weather.eWindSpeed.Storm:
                    windFactor = 10;
                    break;
                case Weather.eWindSpeed.ViolentStorm:
                    windFactor = 12;
                    break;
                case Weather.eWindSpeed.Hurricane:
                    windFactor = 14;
                    break;
            }

            if ((departureStation.Weather[0].Temperatures[GameObject.GetInstance().GameTime.Hour].Precip == Weather.Precipitation.None ||
                 departureStation.Weather[0].Temperatures[GameObject.GetInstance().GameTime.Hour].Precip == Weather.Precipitation.LightRain) && windFactor == 0)
                return 0;

            int weatherFactor = 0;

            switch (departureStation.Weather[0].Temperatures[GameObject.GetInstance().GameTime.Hour].Precip)
            {
                case Weather.Precipitation.IsolatedSnow:
                    weatherFactor = 2;
                    break;
                case Weather.Precipitation.LightSnow:
                    weatherFactor = 4;
                    break;
                case Weather.Precipitation.HeavySnow:
                    weatherFactor = 8;
                    break;
                case Weather.Precipitation.Fog:
                    weatherFactor = 4;
                    break;
                case Weather.Precipitation.Sleet:
                    weatherFactor = 4;
                    break;
                case Weather.Precipitation.FreezingRain:
                    weatherFactor = 6;
                    break;
                case Weather.Precipitation.MixedRainAndSnow:
                    weatherFactor = 6;
                    break;
                case Weather.Precipitation.IsolatedRain:
                    weatherFactor = 1;
                    break;
                case Weather.Precipitation.LightRain:
                    weatherFactor = 2;
                    break;
                case Weather.Precipitation.HeavyRain:
                    weatherFactor = 4;
                    break;
                case Weather.Precipitation.IsolatedThunderstorms:
                    weatherFactor = 5;
                    break;
                case Weather.Precipitation.Thunderstorms:
                    weatherFactor = 8;
                    break;
            }

            int delayTime = Rnd.Next((weatherFactor + windFactor), (weatherFactor + windFactor)*12);

            return delayTime;
        }

        /* clears the statistics for all fleet coaches
        */

        public static void ClearCoachStatistics()
        {
            foreach (Carrier carrier in Carriers.GetAllCarriers())
                foreach (FleetCoach coach in carrier.Fleet)
                    coach.Statistics.Clear();
        }

        //creates the stop over route based on the main route
        public static StopoverRoute CreateStopoverRoute(Station dest1, Station stopover, Station dest2, Route mainroute, bool oneLegged, Route.RouteType type)
        {
            var stopoverRoute = new StopoverRoute(stopover);

            Guid id = Guid.NewGuid();

            if (!oneLegged)
            {
                if (mainroute.Type == Route.RouteType.Passenger || mainroute.Type == Route.RouteType.Mixed)
                {
                    var routeLegTwo = new PassengerRoute(id.ToString(), dest1, stopover, GameObject.GetInstance().GameTime, 0);

                    foreach (RouteCoachClass aClass in ((PassengerRoute) mainroute).Classes)
                    {
                        //routeLegTwo.getRouteCoachClass(aClass.Type).FarePrice = aClass.FarePrice;
                        routeLegTwo.GetRouteCoachClass(aClass.Type).FarePrice = PassengerHelpers.GetPassengerPrice(dest1, stopover)*GeneralHelpers.ClassToPriceFactor(aClass.Type);

                        foreach (RouteFacility facility in aClass.GetFacilities())
                            routeLegTwo.GetRouteCoachClass(aClass.Type).AddFacility(facility);

                        routeLegTwo.GetRouteCoachClass(aClass.Type).Seating = aClass.Seating;
                    }


                    stopoverRoute.AddLeg(routeLegTwo);
                }
                if (mainroute.Type == Route.RouteType.Cargo || mainroute.Type == Route.RouteType.Mixed)
                {
                    var routeLegTwo = new CargoRoute(id.ToString(), dest1, stopover, GameObject.GetInstance().GameTime, ((CargoRoute) mainroute).PricePerUnit);

                    stopoverRoute.AddLeg(routeLegTwo);
                }
            }

            if (mainroute.Type == Route.RouteType.Mixed || mainroute.Type == Route.RouteType.Passenger)
            {
                id = Guid.NewGuid();

                var routeLegOne = new PassengerRoute(id.ToString(), stopover, dest2, GameObject.GetInstance().GameTime, 0);

                foreach (RouteCoachClass aClass in ((PassengerRoute) mainroute).Classes)
                {
                    //routeLegOne.getRouteCoachClass(aClass.Type).FarePrice = aClass.FarePrice;

                    routeLegOne.GetRouteCoachClass(aClass.Type).FarePrice = PassengerHelpers.GetPassengerPrice(stopover, dest2)*GeneralHelpers.ClassToPriceFactor(aClass.Type);


                    foreach (RouteFacility facility in aClass.GetFacilities())
                        routeLegOne.GetRouteCoachClass(aClass.Type).AddFacility(facility);

                    routeLegOne.GetRouteCoachClass(aClass.Type).Seating = aClass.Seating;
                }

                stopoverRoute.AddLeg(routeLegOne);
            }
            if (mainroute.Type == Route.RouteType.Cargo || mainroute.Type == Route.RouteType.Mixed)
            {
                var routeLegOne = new CargoRoute(id.ToString(), stopover, dest2, GameObject.GetInstance().GameTime, ((CargoRoute) mainroute).PricePerUnit);

                stopoverRoute.AddLeg(routeLegOne);
            }


            return stopoverRoute;
        }

        public static void CreateStopoverRoute(Route route, Station stopover1, Station stopover2 = null)
        {
            if (stopover1 != null)
            {
                if (stopover2 != null)
                {
                    route.AddStopover(CreateStopoverRoute(route.Destination1, stopover1, stopover2, route, false, route.Type));
                    route.AddStopover(CreateStopoverRoute(stopover1, stopover2, route.Destination2, route, true, route.Type));
                }
                else
                    route.AddStopover(CreateStopoverRoute(route.Destination1, stopover1, route.Destination2, route, false, route.Type));
            }

            /*
            if (stopover1 != null)
            {
                if (stopover2 != null)
                    route.addStopover(FleetCoachHelpers.CreateStopoverRoute(route.Destination1, stopover1, stopover2, route, false, route.Type));
                else
                    route.addStopover(FleetCoachHelpers.CreateStopoverRoute(route.Destination1, stopover1, route.Destination2, route, false, route.Type));
            }

            if (stopover2 != null && stopover1!=null)
            {
               route.addStopover(FleetCoachHelpers.CreateStopoverRoute(stopover1, stopover2, route.Destination2, route, true, route.Type));
              
            }
             * */
        }

        //returns the minimum time between trips for an coach
        public static TimeSpan GetMinTimeBetweenTrips(FleetCoach coach)
        {
            return GetMinTimeBetweenTrips(coach.Coach.GetTotalSeatCapacity());
        }

        public static TimeSpan GetMinTimeBetweenTrips(Coach coach)
        {
            return GetMinTimeBetweenTrips(coach.GetTotalSeatCapacity());
        }

        public static TimeSpan GetMinTimeBetweenTrips(CoachType type)
        {
            if (type == null || type is CoachCargoType)
                return GetMinTimeBetweenTrips(0);

            return GetMinTimeBetweenTrips(((CoachPassengerType) type).MaxSeatingCapacity);
        }

        /// <summary>
        ///     The least time an coach needs on the ground between two trips: unloading, cleaning, loading and
        ///     boarding again. It used to be half an hour for anything up to 200 seats and an hour above that, which
        ///     left a 37 seat turboprop needing as long as a full narrowbody, far longer than the quarter of an hour
        ///     such an coach really turns round in, and so allowed it only a fraction of the short hops it flies in
        ///     life. The steps below follow roughly what each size really takes: 15 to 20 minutes for a regional
        ///     turboprop or regional jet, 20 to 30 for a small jet, 25 to 40 for a narrowbody, 35 to 45 for the larger
        ///     narrowbodies, and an hour for a widebody.
        ///
        ///     Every step is the same as before or shorter, never longer, so a timetable that was legal stays legal.
        ///     The widest coach are if anything given too little here: a 747 or an A380 really takes an hour and a
        ///     half or more, but lengthening it would make existing timetables illegal, so it is left alone.
        /// </summary>
        private static TimeSpan GetMinTimeBetweenTrips(int passengers)
        {
            if (passengers <= 50) return new TimeSpan(0, 20, 0);
            if (passengers <= 100) return new TimeSpan(0, 25, 0);
            if (passengers <= 200) return new TimeSpan(0, 30, 0);
            if (passengers <= 250) return new TimeSpan(0, 40, 0);
            return new TimeSpan(0, 60, 0);
        }

        //sets the trips stats for an coach
        public static void SetTripStats(FleetCoach coach)
        {
            TimeSpan tripTime = MathHelpers.GetTripTime(coach.CurrentTrip.Entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                                                            coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(), coach.Coach.Type);
            DateTime landingTime = coach.CurrentTrip.TripTime.Add(tripTime);

            coach.Coach.FlownHours = coach.Coach.FlownHours.Add(tripTime);

            Station dest = coach.CurrentTrip.Entry.Destination.Station;
            Station dept = coach.CurrentTrip.Entry.DepartureStation;

            //canellation and ontime-percent
            double cancellationPercent = coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cancellations"))/
                                         (coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals")) +
                                          coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cancellations")));
            coach.Coach.Carrier.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cancellation%"), cancellationPercent*100);

            // (the whole delay: .Minutes alone counted a trip an hour and five minutes late as on time)
            bool isOnTime = landingTime.Subtract(coach.CurrentTrip.GetScheduledLandingTime()).TotalMinutes <= TripStatistics.OnTimeMinutes;

            if (isOnTime)
                coach.Coach.Carrier.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("On-Time"), 1);

            coach.Coach.Carrier.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"), 1);
            dest.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Arrivals"), 1);
            // the route's arrivals (each landing used to be added to its cargo instead): by cabin class for passengers, below
            if (coach.CurrentTrip.IsCargoTrip())
                coach.CurrentTrip.Entry.TimeTable.Route.Statistics.AddStatisticsValue(StatisticsTypes.GetStatisticsType("Arrivals"), 1);
            coach.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"), 1);

            double onTimePercent = coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("On-Time"))/
                                   coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"));
            coach.Coach.Carrier.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("On-Time%"), onTimePercent*100);


            if (coach.CurrentTrip.IsCargoTrip())
            {
                coach.CurrentTrip.Entry.TimeTable.Route.Statistics.AddStatisticsValue(StatisticsTypes.GetStatisticsType("Cargo"), (int) coach.CurrentTrip.Cargo);
                double routePassengers = coach.CurrentTrip.Entry.TimeTable.Route.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("Cargo"));
                double routeDepartures = coach.CurrentTrip.Entry.TimeTable.Route.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("Arrivals"));
                coach.CurrentTrip.Entry.TimeTable.Route.Statistics.SetStatisticsValue(StatisticsTypes.GetStatisticsType("Cargo%"), (int) (routePassengers/routeDepartures));

                double cargocapacity = 0;

                var type = coach.Coach.Type as CoachCargoType;
                if (type != null)
                    cargocapacity = type.CargoSize;

                var combiType = coach.Coach.Type as CoachCombiType;
                if (combiType != null)
                    cargocapacity = combiType.CargoSize;

                coach.CurrentTrip.Entry.TimeTable.Route.Statistics.AddStatisticsValue(StatisticsTypes.GetStatisticsType("Capacity"), (int) cargocapacity);

                double coachCargo = coach.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo"));
                double coachDepartures = coach.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"));
                coach.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo%"), (int) (coachCargo/coachDepartures));

                coach.Coach.Carrier.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo"), coach.CurrentTrip.Cargo);
                coach.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo"), coach.CurrentTrip.Cargo);
                // the coach's cargo per trip with this trip in it
                coach.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo%"),
                                                       (int) (coach.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo"))/coachDepartures));
                dest.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Cargo"), (int) coach.CurrentTrip.Cargo);

                double destCargo = dest.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Cargo"));
                double destDepartures = dest.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Arrivals"));
                dest.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Cargo%"), (int) (destCargo/destDepartures));

                double carrierCargo = coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo"));
                double carrierDepartures = coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"));
                coach.Coach.Carrier.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Cargo%"), (int) (carrierCargo/carrierDepartures));

                dept.AddCargoDestinationStatistics(dest, coach.CurrentTrip.Cargo);
            }
            if (coach.CurrentTrip.IsPassengerTrip())
            {
                foreach (TripCoachClass fac in coach.CurrentTrip.Classes)
                {
                    RouteCoachClass raClass = ((PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route).GetRouteCoachClass(fac.CoachClass.Type);
                    coach.CurrentTrip.Entry.TimeTable.Route.Statistics.AddStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Arrivals"), 1);

                    coach.CurrentTrip.Entry.TimeTable.Route.Statistics.AddStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Passengers"),
                                                                                               coach.CurrentTrip.GetTripCoachClass(raClass.Type).Passengers);
                    double routePassengers = coach.CurrentTrip.Entry.TimeTable.Route.Statistics.GetStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Passengers"));
                    double routeDepartures = coach.CurrentTrip.Entry.TimeTable.Route.Statistics.GetStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Arrivals"));
                    coach.CurrentTrip.Entry.TimeTable.Route.Statistics.SetStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Passengers%"), (int) (routePassengers/routeDepartures));

                    coach.CurrentTrip.Entry.TimeTable.Route.Statistics.AddStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Capacity"),
                                                                                               coach.Coach.GetCoachClass(raClass.Type).SeatingCapacity);
                }

                double coachPassengers = coach.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"));
                double coachDepartures = coach.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"));
                coach.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers%"), (int) (coachPassengers/coachDepartures));


                coach.Coach.Carrier.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"),
                                                                        coach.CurrentTrip.GetTotalPassengers());
                coach.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"), coach.CurrentTrip.GetTotalPassengers());
                // the coach's passengers per trip with this trip in it
                coach.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers%"),
                                                       (int) (coach.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"))/coachDepartures));
                // the seats flown, for the share of them filled
                coach.Coach.Carrier.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Capacity"), coach.Coach.GetTotalSeatCapacity());
                dest.Statistics.AddStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Passengers"),
                                                   coach.CurrentTrip.GetTotalPassengers());


                double destPassengers = dest.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Passengers"));
                double destDepartures = dest.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Arrivals"));
                dest.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Passengers%"),
                                                   (int) (destPassengers/destDepartures));

                double carrierPassengers = coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"));
                double carrierDepartures = coach.Coach.Carrier.Statistics.GetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Arrivals"));
                coach.Coach.Carrier.Statistics.SetStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers%"),
                                                                        (int) (carrierPassengers/carrierDepartures));

                //the statistics for destination station
                dept.AddPassengerDestinationStatistics(dest, coach.CurrentTrip.GetTotalPassengers());
            }
        }

        //returns the trip crusing speed based on the wind
        public static int GetCruisingSpeed(FleetCoach coach)
        {
            Station dest = coach.CurrentTrip.Entry.Destination.Station;
            Station dept = coach.CurrentTrip.GetDepartureStation();

            double windFirstHalf = ((int) dept.Weather[0].WindSpeed)*GetWindInfluence(coach, dept.Weather[0]);

            double windSecondHalf = ((int) dest.Weather[0].WindSpeed)*GetWindInfluence(coach, dest.Weather[0]);

            int speed = Convert.ToInt32(((coach.Coach.CruisingSpeed + windFirstHalf) + (coach.Coach.CruisingSpeed + windSecondHalf))/2);

            return speed;
        }

        //returns if the wind is tail (1), head (-1), or from side (0)
        private static int GetWindInfluence(FleetCoach coach, Weather currentWeather)
        {
            double direction = MathHelpers.GetDirection(coach.CurrentTrip.GetDepartureStation().Profile.Coordinates.ConvertToGeoCoordinate(),
                                                        coach.CurrentTrip.GetNextDestination().Profile.Coordinates.ConvertToGeoCoordinate());

            Weather.WindDirection windDirection = MathHelpers.GetWindDirectionFromDirection(direction);

            int windDirectionLenght = Enum.GetValues(typeof (Weather.WindDirection)).Length;
            int indexCurrentPosition = Array.IndexOf(Enum.GetValues(typeof (Weather.WindDirection)), windDirection);
            //int indexWeather = Array.IndexOf(Enum.GetValues(typeof(Weather.WindDirection)),currentWeather.WindSpeed);

            //check for tail wind
            Weather.WindDirection windTailLeft = indexCurrentPosition > 0 ? (Weather.WindDirection) indexCurrentPosition - 1 : (Weather.WindDirection) windDirectionLenght - 1;
            Weather.WindDirection windTailRight = indexCurrentPosition < windDirectionLenght - 1 ? (Weather.WindDirection) indexCurrentPosition + 1 : 0;

            if (windTailLeft == currentWeather.Direction || windTailRight == currentWeather.Direction || windDirection == currentWeather.Direction)
                return 1;

            Weather.WindDirection windOpposite = indexCurrentPosition - (windDirectionLenght/2) > 0
                                                     ? (Weather.WindDirection) indexCurrentPosition - (windDirectionLenght/2)
                                                     : (Weather.WindDirection) windDirectionLenght - 1 - indexCurrentPosition - (windDirectionLenght/2);
            int indexOpposite = Array.IndexOf(Enum.GetValues(typeof (Weather.WindDirection)), windOpposite);

            Weather.WindDirection windHeadLeft = indexOpposite > 0 ? (Weather.WindDirection) indexOpposite - 1 : (Weather.WindDirection) windDirectionLenght - 1;
            Weather.WindDirection windHeadRight = indexOpposite < windDirectionLenght - 1 ? (Weather.WindDirection) indexOpposite + 1 : 0;

            if (windHeadLeft == currentWeather.Direction || windHeadRight == currentWeather.Direction || windOpposite == currentWeather.Direction)
                return -1;

            return 0;
        }

        //returns the fuel expenses for an coach
        public static double GetFuelExpenses(FleetCoach coach, double distance)
        {
            Station departureStation = coach.CurrentTrip.GetDepartureStation();

            double fuelPrice = StationHelpers.GetFuelPrice(departureStation);

            if (coach.CurrentTrip.IsPassengerTrip())
            {
                int pax = coach.CurrentTrip.GetTotalPassengers();

                double basePrice = fuelPrice*distance*((CoachPassengerType) coach.Coach.Type).MaxSeatingCapacity*coach.Coach.FuelConsumption*0.55;
                double paxPrice = fuelPrice*distance*coach.Coach.FuelConsumption*coach.CurrentTrip.GetTotalPassengers()*0.45;
                double seatsPrice =
                    coach.CurrentTrip.Classes.Sum(c => (coach.Coach.GetCoachClass(c.CoachClass.Type).GetFacility(CoachFacility.FacilityType.Seat).SeatUses - 1)*c.Passengers);
                return basePrice + paxPrice + seatsPrice;
            }
            else
            {
                double basePrice = fuelPrice*distance*((CoachCargoType) coach.Coach.Type).CargoSize*coach.Coach.FuelConsumption*0.55;
                double cargoPrice = fuelPrice*coach.Coach.FuelConsumption*coach.CurrentTrip.Cargo*distance*0.45;

                return basePrice + cargoPrice;
            }
        }

        //converts a passenger coach to a cargo coach
        public static void ConvertPassengerToCargoCoach(FleetCoach coach)
        {
            var oldType = (CoachPassengerType) coach.Coach.Type;

            double cargoSize = CoachHelpers.GetPassengerCargoSize(oldType);
            DateTime builtDate = GameObject.GetInstance().GameTime.AddDays(CoachHelpers.GetCargoConvertingDays(oldType));

            var newType = new CoachCargoType(oldType.Manufacturer, oldType.Name + "F", oldType.CoachFamily, oldType.DriverCrew, cargoSize, oldType.CruisingSpeed, oldType.Range, oldType.Wingspan,
                                                oldType.Length, oldType.Weight, oldType.FuelConsumption, oldType.Price, oldType.MinRunwaylength, oldType.FuelCapacity, oldType.Body, oldType.RangeType,
                                                oldType.Engine, oldType.Produced, oldType.ProductionRate, false, false);

            coach.Coach.Type = newType;
            coach.Coach.BuiltDate = builtDate;
        }

        //does the maintenance of a given type, sends the invoice, updates the last/next maintenance, and improves the coach's damage
        //make sure you pass this function a string value of either "A" "B" "C" or "D" or it will throw an error!
        public static void DoMaintenance(FleetCoach coach)
        {
            if (coach.SchedAMaintenance == GameObject.GetInstance().GameTime.Date)
            {
                double expense = (coach.Coach.GetValue()*0.01) + 2000;
                var maintCheck = new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                CarrierHelpers.AddCarrierInvoice(coach.Coach.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                coach.Coach.Condition += Rnd.Next(3, 10);
                if (coach.Coach.Condition > 100) coach.Coach.Condition = 100;
                coach.LastAMaintenance = GameObject.GetInstance().GameTime;
                coach.SchedAMaintenance = coach.SchedAMaintenance.AddDays(coach.AMaintenanceInterval);
                coach.MaintenanceHistory.Add(maintCheck, "A");
            }

            if (coach.SchedBMaintenance == GameObject.GetInstance().GameTime.Date)
            {
                double expense = (coach.Coach.GetValue()*0.02) + 4500;
                var maintCheck = new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                CarrierHelpers.AddCarrierInvoice(coach.Coach.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                coach.Coach.Condition += Rnd.Next(12, 20);
                if (coach.Coach.Condition > 100) coach.Coach.Condition = 100;
                coach.LastBMaintenance = GameObject.GetInstance().GameTime;
                coach.SchedBMaintenance = coach.SchedBMaintenance.AddDays(coach.BMaintenanceInterval);
                coach.MaintenanceHistory.Add(maintCheck, "B");
            }

            if (coach.SchedCMaintenance == GameObject.GetInstance().GameTime.Date)
            {
                double expense = (coach.Coach.GetValue()*0.025) + 156000;
                coach.OOSDate = coach.SchedCMaintenance.AddDays(coach.Coach.Condition + 20);
                var maintCheck = new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                CarrierHelpers.AddCarrierInvoice(coach.Coach.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                coach.Coach.Condition += Rnd.Next(20, 30);
                if (coach.Coach.Condition > 100) coach.Coach.Condition = 100;
                coach.LastCMaintenance = GameObject.GetInstance().GameTime;
                coach.SchedCMaintenance = coach.CMaintenanceInterval > -1
                                                 ? coach.SchedCMaintenance.AddMonths(coach.CMaintenanceInterval)
                                                 : coach.DueCMaintenance = GameObject.GetInstance().GameTime.AddMonths(18);
                coach.MaintenanceHistory.Add(maintCheck, "C");
                foreach (Route r in coach.Routes.ToList())
                {
                    coach.MaintRoutes.Add(r);
                    coach.Routes.Remove(r);
                }
            }

            if (coach.SchedDMaintenance == GameObject.GetInstance().GameTime.Date)
            {
                double expense = (coach.Coach.GetValue()*0.03) + 1200000;
                coach.OOSDate = coach.SchedDMaintenance.AddDays(coach.Coach.Condition + 50);
                var maintCheck = new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                CarrierHelpers.AddCarrierInvoice(coach.Coach.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -expense);
                coach.Coach.Condition += Rnd.Next(35, 50);
                if (coach.Coach.Condition > 100) coach.Coach.Condition = 100;
                coach.LastDMaintenance = GameObject.GetInstance().GameTime;
                coach.SchedDMaintenance = coach.DMaintenanceInterval > -1
                                                 ? coach.SchedDMaintenance.AddMonths(coach.DMaintenanceInterval)
                                                 : coach.DueDMaintenance = GameObject.GetInstance().GameTime.AddMonths(60);
                coach.DueDMaintenance = GameObject.GetInstance().GameTime.AddMonths(60);
                coach.MaintenanceHistory.Add(maintCheck, "D");
                foreach (Route r in coach.Routes.ToList())
                {
                    coach.MaintRoutes.Add(r);
                    coach.Routes.Remove(r);
                }
            }
        }

        //restores routes removed for maintenance
        public static void RestoreMaintRoutes(FleetCoach coach)
        {
            if (coach.OOSDate <= GameObject.GetInstance().GameTime)
            {
                foreach (Route r in coach.MaintRoutes)
                {
                    coach.Routes.Add(r);
                }

                coach.MaintRoutes.Clear();
            }
        }

        //sets A and B check intervals
        public static void SetMaintenanceIntervals(FleetCoach coach, int a, int b)
        {
            coach.AMaintenanceInterval = a;
            coach.BMaintenanceInterval = b;
            coach.CMaintenanceInterval = -1;
            coach.DMaintenanceInterval = -1;
        }

        public static void SetMaintenanceIntervals(FleetCoach coach, int a, int b, int c)
        {
            if (coach.CMaintenanceInterval == -1)
            {
                coach.AMaintenanceInterval = a;
                coach.BMaintenanceInterval = b;
                coach.DMaintenanceInterval = c;
            }

            else if (coach.DMaintenanceInterval == -1)
            {
                coach.AMaintenanceInterval = a;
                coach.BMaintenanceInterval = b;
                coach.CMaintenanceInterval = c;
            }
        }

        public static void SetMaintenanceIntervals(FleetCoach coach, int a, int b, int c, int d)
        {
            coach.AMaintenanceInterval = a;
            coach.BMaintenanceInterval = b;
            coach.CMaintenanceInterval = c;
            coach.DMaintenanceInterval = d;
        }
    }

    //the helpers class for fleet coach insurance
    public class FleetCoachInsurancesHelpers
    {
        //add insurance policy
        public static void CreatePolicy(Carrier carrier, FleetCoach coach, CoachInsurance.InsuranceType type, CoachInsurance.InsuranceScope scope, CoachInsurance.PaymentTerms terms,
                                        double length, int amount)
        {
            #region Method Setup

            var rnd = new Random();
            double hub = carrier.GetHubs().Count()*0.1;
            var policy = new CoachInsurance(type, scope, terms, amount)
                {
                    InsuranceEffective = GameObject.GetInstance().GameTime,
                    InsuranceExpires = GameObject.GetInstance().GameTime.AddYears((int) length),
                    PolicyIndex = GameObject.GetInstance().GameTime.ToString(CultureInfo.InvariantCulture) + carrier
                };
            switch (policy.InsTerms)
            {
                case CoachInsurance.PaymentTerms.Monthly:
                    policy.RemainingPayments = length*12;
                    break;
                case CoachInsurance.PaymentTerms.Quarterly:
                    policy.RemainingPayments = length*4;
                    break;
                case CoachInsurance.PaymentTerms.Biannual:
                    policy.RemainingPayments = length*2;
                    break;
                case CoachInsurance.PaymentTerms.Annual:
                    policy.RemainingPayments = length;
                    break;
            }
            //sets up multipliers based on the type and scope of insurance policy
            var typeMultipliers = new Dictionary<CoachInsurance.InsuranceType, double>();
            var scopeMultipliers = new Dictionary<CoachInsurance.InsuranceScope, double>();
            const double typeMLiability = 1;
            const double typeMGroundParked = 1.2;
            const double typeMGroundTaxi = 1.5;
            const double typeMGroundCombined = 1.8;
            const double typeMInTrip = 2.2;
            const double typeMFullCoverage = 2.7;

            const double scMStation = 1;
            const double scMDomestic = 1.5;
            double scMHub = 1.5 + hub;
            double scMGlobal = 2.0 + hub;

            #endregion

            #region Domestic/Int'l Station Counter

            int i = 0;
            int j = 0;
            foreach (Station station in GameObject.GetInstance().HumanCarrier.Stations)
            {
                if (station.Profile.Country != GameObject.GetInstance().HumanCarrier.Profile.Country)
                {
                    i++;
                }
                else j++;
            }

            #endregion

            // all the decision making for monthly payment amounts and deductibles
            switch (type)
            {
                    #region Liability

                case CoachInsurance.InsuranceType.Liability:
                    switch (scope)
                    {
                        case CoachInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMLiability*scMStation;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;

                            break;

                        case CoachInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMLiability*scMDomestic;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMLiability*scMHub;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMLiability*scMGlobal;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region Ground Parked

                case CoachInsurance.InsuranceType.GroundParked:
                    switch (scope)
                    {
                        case CoachInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundParked*scMStation;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundParked*scMDomestic;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundParked*scMHub;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundParked*scMGlobal;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region Ground Taxi

                case CoachInsurance.InsuranceType.GroundTaxi:
                    switch (scope)
                    {
                        case CoachInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundTaxi*scMStation;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundTaxi*scMDomestic;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundTaxi*scMHub;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundTaxi*scMGlobal;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region Ground Combined

                case CoachInsurance.InsuranceType.CombinedGround:
                    switch (scope)
                    {
                        case CoachInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundCombined*scMStation;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundCombined*scMDomestic;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundCombined*scMHub;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMGroundCombined*scMGlobal;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region In Trip

                case CoachInsurance.InsuranceType.InTrip:
                    switch (scope)
                    {
                        case CoachInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMInTrip*scMStation;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMInTrip*scMDomestic;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMInTrip*scMHub;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMInTrip*scMGlobal;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }

                    break;

                    #endregion

                    #region Full Coverage

                case CoachInsurance.InsuranceType.FullCoverage:
                    switch (scope)
                    {
                        case CoachInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFullCoverage*scMStation;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFullCoverage*scMDomestic;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFullCoverage*scMHub;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CoachInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFullCoverage*scMGlobal;
                            if (terms == CoachInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CoachInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CoachInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CoachInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion
            }
        }

        public static void AddPolicy(FleetCoach coach, CoachInsurance insurance)
        {
            coach.InsurancePolicies.Add(insurance);
        }


        //extend or modify policy
        public static void ModifyPolicy(FleetCoach coach, string index, CoachInsurance newPolicy)
        {
            //CoachInsurance oldPolicy = coach.InsurancePolicies[index];
            //use the index to compare the new policy passed in to the existing one and make changes
        }

        public static void CheckExpiredInsurance(Carrier carrier)
        {
            DateTime date = GameObject.GetInstance().GameTime;
            foreach (FleetCoach coach in carrier.Fleet)
            {
                foreach (CoachInsurance policy in coach.InsurancePolicies)
                {
                    if (policy.InsuranceExpires < GameObject.GetInstance().GameTime)
                        coach.InsurancePolicies.Remove(policy);
                }
            }
        }

        public static void MakeInsurancePayment(FleetCoach coach, Carrier carrier)
        {
            foreach (CoachInsurance policy in coach.InsurancePolicies)
            {
                if (policy.RemainingPayments > 0)
                {
                    if (policy.NextPaymentDue.Month == GameObject.GetInstance().GameTime.Month)
                    {
                        //Invoice payment = new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, policy.PaymentAmount);
                        CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -policy.PaymentAmount);
                        policy.RemainingPayments--;
                        switch (policy.InsTerms)
                        {
                            case CoachInsurance.PaymentTerms.Monthly:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(1);
                                break;
                            case CoachInsurance.PaymentTerms.Quarterly:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(3);
                                break;
                            case CoachInsurance.PaymentTerms.Biannual:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(6);
                                break;
                            case CoachInsurance.PaymentTerms.Annual:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(12);
                                break;
                        }
                    }
                }
            }
        }

        public static void FileInsuranceClaim(Carrier carrier, Station station, StationFacilities facility)
        {
        }

        public static void ReceiveInsurancePayout(Carrier carrier, Station station)
        {
        }
    }
}