using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the class for some general station helpers
    public class StationHelpers
    {
        #region Static Fields

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        #endregion

        #region Public Methods and Operators

        //returns the fuel price for an station
        public static double GetFuelPrice(Station station)
        {
            return GetFuelPrice(station.Profile.Country.Region);
        }

        public static double GetFuelPrice(Region region)
        {
            return region.FuelIndex*GameObject.GetInstance().FuelPrice;
        }

        //returns the standard landing fee for an station
        public static double GetStandardLandingFee(Station station)
        {
            const double basefee = 0.27;

            return basefee*((int) (station.Profile.Size) + 1);
        }

        //returns the landing fee for an coach at an station
        public static double GetLandingFee(Station station, Coach coach)
        {
            return (coach.Type.Weight/1000)*GeneralHelpers.GetInflationPrice(station.LandingFee);
        }

        public static void AddCarrierContract(StationContract contract)
        {
            contract.Station.AddCarrierContract(contract);

            StationFacility checkinFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).First(f => f.TypeLevel == 1);
            StationFacility ticketFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.TicketOffice).First(f => f.TypeLevel == 1);
            StationFacility serviceFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Service).First(f => f.TypeLevel == 1);
            StationFacility cargoTerminal =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Cargo).Find(f => f.TypeLevel > 0);

            if (contract.Type == StationContract.ContractType.FullService)
            {
                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.Cargo)
                            .Facility.TypeLevel < cargoTerminal.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, cargoTerminal, GameObject.GetInstance().GameTime);
                }

                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.CheckIn)
                            .Facility.TypeLevel < checkinFacility.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, checkinFacility, GameObject.GetInstance().GameTime);
                }

                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.TicketOffice)
                            .Facility.TypeLevel < ticketFacility.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, ticketFacility, GameObject.GetInstance().GameTime);
                }

                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.Service)
                            .Facility.TypeLevel < serviceFacility.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, serviceFacility, GameObject.GetInstance().GameTime);
                }
            }
            if (contract.Type == StationContract.ContractType.MediumService)
            {
                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.CheckIn)
                            .Facility.TypeLevel < checkinFacility.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, checkinFacility, GameObject.GetInstance().GameTime);
                }

                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.TicketOffice)
                            .Facility.TypeLevel < ticketFacility.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, ticketFacility, GameObject.GetInstance().GameTime);
                }
            }
            if (contract.Type == StationContract.ContractType.LowService)
            {
                if (
                    contract.Station.GetCarrierStationFacility(null, StationFacility.FacilityType.CheckIn)
                            .Facility.TypeLevel < checkinFacility.TypeLevel)
                {
                    contract.Station.AddStationFacility(null, checkinFacility, GameObject.GetInstance().GameTime);
                }
            }
        }

        public static bool CanFillRoutesEntries(
            Station station,
            Carrier carrier,
            List<StationContract> contracts,
            Weather.Season season)
        {
            List<Route> routes = GetStationRoutes(station, carrier);

            int numberOfOccupiedSlots =
                GetOccupiedSlotTimes(station, carrier, contracts, season)
                    .GroupBy(s => s.Ticks).Count(x => x.Count() > 1);
            return numberOfOccupiedSlots == 0 && !(routes.Count > 0 && contracts.Count == 0);
        }

        public static void CheckForExtendGates(Station station)
        {
            const int minYearsBetweenExpansions = 5;

            if (station.Terminals.GetOrdereredGates() == 0
                && GameObject.GetInstance().GameTime.AddYears(-minYearsBetweenExpansions) > station.LastExpansionDate)
            {
                Terminal minTerminal = station.Terminals.StationTerminals.OrderBy(t => t.Gates.NumberOfGates).First();

                bool newTerminal = minTerminal.Gates.NumberOfGates > 50;
                //extend existing
                if (!newTerminal)
                {
                    int numberOfGates = Math.Max(5, minTerminal.Gates.NumberOfGates);
                    int daysToBuild = numberOfGates*10 + (newTerminal ? 60 : 0);

                    long price = numberOfGates*station.GetTerminalGatePrice()
                                 + (newTerminal ? station.GetTerminalPrice() : 0);
                    price = price/3*4;

                    if (station.Income > price)
                    {
                        for (int i = 0; i < numberOfGates; i++)
                        {
                            var gate = new Gate(GameObject.GetInstance().GameTime.AddDays(daysToBuild)) {Carrier = minTerminal.Carrier};

                            minTerminal.Gates.AddGate(gate);
                        }

                        station.Income -= price;
                        station.LastExpansionDate = GameObject.GetInstance().GameTime;
                    }
                }
                    //build new terminal
                else
                {
                    int numberOfGates = station.Terminals.GetTerminals()[0].Gates.NumberOfDeliveredGates;

                    int daysToBuild = numberOfGates*10 + (newTerminal ? 60 : 0);

                    long price = numberOfGates*station.GetTerminalGatePrice()
                                 + (newTerminal ? station.GetTerminalPrice() : 0);
                    price = price/3*4;

                    if (station.Income > price)
                    {
                        var terminal = new Terminal(
                            station,
                            null,
                            "Terminal",
                            numberOfGates,
                            GameObject.GetInstance().GameTime.AddDays(daysToBuild),
                            Terminal.TerminalType.Passenger);

                        station.AddTerminal(terminal);
                        station.Income -= price;
                        station.LastExpansionDate = GameObject.GetInstance().GameTime;
                    }
                }
            }
        }

        public static void CheckForExtendBay(Station station)
        {
            bool isOnlyHeliport = !station.Bays.Exists(r => r.Type == Bay.BayType.Regular);

            if (!isOnlyHeliport)
            {
                const int minYearsBetweenExpansions = 5;

                long maxBayLenght = (from r in station.Bays select r.Length).Max();
                long longestRequiredBayLenght =
                    CoachTypes.GetTypes(
                        a =>
                        a.Produced.From <= GameObject.GetInstance().GameTime
                        && a.Produced.To >= GameObject.GetInstance().GameTime).Max(a => a.MinBayLength);

                List<Route> stationRoutes = GetStationRoutes(station);
                IEnumerable<FleetCoach> routeCoaches = stationRoutes.SelectMany(r => r.GetCoaches());

                var fleetCoaches = routeCoaches as FleetCoach[] ?? routeCoaches.ToArray();
                long longestBayInUse = fleetCoaches.Any()
                                              ? fleetCoaches.Max(a => a.Coach.MinBayLength)
                                              : 0;

                if (maxBayLenght < longestRequiredBayLenght/2 && maxBayLenght < longestBayInUse*3/4
                    && GameObject.GetInstance().GameTime.AddYears(-minYearsBetweenExpansions) > station.LastExpansionDate)
                {
                    List<string> bayNames =
                        (from r in Stations.GetAllStations().SelectMany(a => a.Bays) select r.Name).Distinct().ToList();

                    foreach (Bay r in station.Bays)
                    {
                        bayNames.Remove(r.Name);
                    }

                    Bay.SurfaceType surface = station.Bays[0].Surface;
                    long length = Math.Min(longestRequiredBayLenght*3/4, longestBayInUse*2);

                    var bay = new Bay(
                        bayNames[Rnd.Next(bayNames.Count)],
                        length,
                        Bay.BayType.Regular,
                        surface,
                        GameObject.GetInstance().GameTime.AddDays(90),
                        false);
                    station.Bays.Add(bay);

                    station.LastExpansionDate = GameObject.GetInstance().GameTime;
                }
            }
        }

        //returns the longest distance between stations in a match

        public static void ClearStationStatistics()
        {
            foreach (Station station in Stations.GetAllStations())
            {
                station.Statistics.Stats.Clear();
            }
        }

        private static readonly Dictionary<int, double> YearCoeffs =
            new Dictionary<int, double> {{1960, 1.5}, {1970, 1.4}, {1980, 1.2}, {1990, 1.0}, {2000, 1.0658}, {2010, 1.05}, {2020, 1.1}};

        public static GeneralHelpers.Size ConvertStationPaxToSize(double size)
        {
            var yearCoeffs = YearCoeffs;

            int decade = (GameObject.GetInstance().GameTime.Year - 1960)/10*10 + 1960;

            double coeff = 1;

            if (yearCoeffs.ContainsKey(decade))
            {
                coeff = yearCoeffs[decade];
            }

            double coeffPax = coeff*size;

            if (coeffPax > 9000)
            {
                return GeneralHelpers.Size.Largest;
            }
            if (coeffPax > 4000)
            {
                return GeneralHelpers.Size.VeryLarge;
            }
            if (coeffPax > 1800)
            {
                return GeneralHelpers.Size.Large;
            }
            if (coeffPax > 700)
            {
                return GeneralHelpers.Size.Medium;
            }
            if (coeffPax > 250)
            {
                return GeneralHelpers.Size.Small;
            }
            if (coeffPax > 80)
            {
                return GeneralHelpers.Size.VerySmall;
            }

            return GeneralHelpers.Size.Smallest;
        }

        //returns all entries for a specific station with take off in a time span for a day

        //creates the weather for an station
        public static void CreateStationWeather(Station station)
        {
            station.Weather[0] = null;

            WeatherAverage average =
                (WeatherAverages.GetWeatherAverages(
                    w => w.Station != null && w.Station == station && w.Month == GameObject.GetInstance().GameTime.Month)
                                .FirstOrDefault() ?? WeatherAverages.GetWeatherAverages(
                                    w =>
                                    w.Town != null && w.Town == station.Profile.Town
                                    && w.Month == GameObject.GetInstance().GameTime.Month).FirstOrDefault()) ?? WeatherAverages.GetWeatherAverages(
                                        w =>
                                        w.Country != null && w.Country == station.Profile.Town.Country
                                        && w.Month == GameObject.GetInstance().GameTime.Month).FirstOrDefault();

            if (average == null)
            {
                CreateFiveDaysStationWeather(station);
            }
            else
            {
                var lStation = new List<Station> {station};

                CreateStationsWeather(lStation, average);
            }
        }

        //creates the weather (5 days) for a number of station with an average
        public static void CreateStationsWeather(List<Station> stations, WeatherAverage average)
        {
            if (stations.Count > 0)
            {
                const int maxDays = 5;
                var weathers = new Weather[maxDays];

                if (stations[0].Weather[0] == null)
                {
                    for (int i = 0; i < maxDays; i++)
                    {
                        weathers[i] = CreateDayWeather(
                            GameObject.GetInstance().GameTime.AddDays(i),
                            i > 0 ? weathers[i - 1] : null,
                            average);
                    }
                }
                else
                {
                    for (int i = 1; i < maxDays; i++)
                    {
                        weathers[i - 1] = stations[0].Weather[i];
                    }

                    weathers[maxDays - 1] = CreateDayWeather(
                        GameObject.GetInstance().GameTime.AddDays(maxDays - 1),
                        weathers[maxDays - 2],
                        average);
                }

                foreach (Station station in stations)
                {
                    station.Weather = weathers;
                }
            }
        }

        //creates the weather (5 days) for an station
        public static void CreateFiveDaysStationWeather(Station station)
        {
            const int maxDays = 5;
            if (station.Weather[0] == null)
            {
                for (int i = 0; i < maxDays; i++)
                {
                    station.Weather[i] = CreateDayWeather(
                        station,
                        GameObject.GetInstance().GameTime.AddDays(i),
                        i > 0 ? station.Weather[i - 1] : null);
                }
            }
            else
            {
                for (int i = 1; i < maxDays; i++)
                {
                    station.Weather[i - 1] = station.Weather[i];
                }

                station.Weather[maxDays - 1] = CreateDayWeather(
                    station,
                    GameObject.GetInstance().GameTime.AddDays(maxDays - 1),
                    station.Weather[maxDays - 2]);
            }
        }

        //creates a new weather object for a specific date based on the weather for another day

        //returns the price for a contract at an station
        public static double GetStationContractPrice(Station station)
        {
            double paxDemand = station.Profile.MajorDestionations.Sum(d => d.Value) + station.Profile.Pax;

            const double basePrice = 10000;

            return GeneralHelpers.GetInflationPrice(paxDemand*basePrice);

            //(initial amount, in the millions; and a montly amount probably $50,000 or so
        }

        public static List<RouteTimeTableEntry> GetStationLandings(
            Station station,
            DayOfWeek day,
            TimeSpan startTime,
            TimeSpan endTime)
        {
            return
                GetStationRoutes(station)
                    .SelectMany(
                        r =>
                        r.TimeTable.Entries.FindAll(
                            e =>
                            e.Coach != null && e.Destination.Station == station
                            && e.Time.Add(
                                MathHelpers.GetTripTime(
                                    e.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                                    e.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                                    e.Coach.Coach.Type)) >= startTime
                            && e.Time.Add(
                                MathHelpers.GetTripTime(
                                    e.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                                    e.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                                    e.Coach.Coach.Type)) < endTime && e.Day == day))
                    .ToList();
        }

        public static List<Route> GetStationRoutes(Station station, Carrier carrier)
        {
            return carrier.Routes.FindAll(r => r.Destination2 == station || r.Destination1 == station);
        }

        public static List<Route> GetStationRoutes(Station station)
        {
            IEnumerable<Route> routes =
                Carriers.GetAllCarriers()
                        .SelectMany(a => a.Routes)
                        .Where(r => r.Destination1 == station || r.Destination2 == station);

            return routes.ToList();
        }

        public static List<Route> GetStationRoutes(Station station1, Station station2)
        {
            var routes = new List<Route>(Carriers.GetAllCarriers().SelectMany(a => a.Routes));

            return
                routes.Where(
                    r =>
                    (r.Destination1 == station1 && r.Destination2 == station2)
                    || (r.Destination1 == station2 && r.Destination2 == station1)).ToList();
        }

        public static double GetStationBayPrice(Station station, long lenght)
        {
            double pricePerMeter = 0;
            if (station.Profile.Size == GeneralHelpers.Size.VeryLarge
                || station.Profile.Size == GeneralHelpers.Size.Largest)
            {
                pricePerMeter = 30000;
            }
            if (station.Profile.Size == GeneralHelpers.Size.Large || station.Profile.Size == GeneralHelpers.Size.Medium)
            {
                pricePerMeter = 24000;
            }
            if (station.Profile.Size == GeneralHelpers.Size.Small)
            {
                pricePerMeter = 18000;
            }
            if (station.Profile.Size == GeneralHelpers.Size.Smallest
                || station.Profile.Size == GeneralHelpers.Size.VerySmall)
            {
                pricePerMeter = 12000;
            }

            return pricePerMeter*lenght;
        }

        public static List<RouteTimeTableEntry> GetStationTakeoffs(
            Station station,
            DayOfWeek day,
            TimeSpan startTime,
            TimeSpan endTime)
        {
            return
                GetStationRoutes(station)
                    .SelectMany(
                        r =>
                        r.TimeTable.Entries.FindAll(
                            e =>
                            e.Coach != null && e.DepartureStation == station && e.Time >= startTime
                            && e.Time < endTime && e.Day == day))
                    .ToList();
        }

        public static List<Station> GetStationsNearStation(Station station, double distance)
        {
            return station.Statics.GetStationsWithin(distance);
        }

        public static double GetHubPrice(Station station, HubType type)
        {
            double price = type.Price;

            price = price + 25000*((int) station.Profile.Size);
            return Convert.ToInt64(GeneralHelpers.GetInflationPrice(price));
        }

        public static double GetLongestDistance(Predicate<Station> match)
        {
            List<Station> stations1 = Stations.GetAllStations(match);
            List<Station> stations2 = Stations.GetAllStations(match);

            double maxDistance = double.MinValue;

            foreach (Station station1 in stations1)
            {
                double distance = stations2.Where(a => a != station1).Max(a => MathHelpers.GetDistance(a, station1));

                if (distance >= maxDistance)
                {
                    maxDistance = distance;
                }
            }

            return maxDistance;
        }

        public static int GetNumberOfStationsRoutes(Station station1, Station station2)
        {
            var routes = new List<Route>(Carriers.GetAllCarriers().SelectMany(a => a.Routes));

            return
                routes.Count(
                    r =>
                    (r.Destination1 == station1 && r.Destination2 == station2)
                    || (r.Destination1 == station2 && r.Destination2 == station1));
        }

        //checks an station for extending of bay

        //returns all occupied slot times for an carrier at an station (15 minutes slots)
        public static List<TimeSpan> GetOccupiedSlotTimes(
            Station station,
            Carrier carrier,
            List<StationContract> contracts,
            Weather.Season season)
        {
            // (a value tuple: KeyValuePair has no fast equality, and comparing it was the slow part here)
            var occupiedSlots = new List<(Route Route, TimeSpan Time)>();
            var occupiedSet = new HashSet<(Route, TimeSpan)>();

            var gateTimeBefore = new TimeSpan(0, 15, 0);
            var gateTimeAfter = new TimeSpan(0, 15, 0);

            int gates = contracts.Sum(c => c.NumberOfGates);

            // a computer carrier plans its routes in parallel, so the lists can change while they are read:
            // take a copy, and read again if it changed under us
            List<RouteTimeTableEntry> entries = null;
            for (int attempt = 0; entries == null; attempt++)
            {
                try
                {
                    var routes = new List<Route>(GetStationRoutes(station, carrier));
                    entries = new List<RouteTimeTableEntry>(
                        routes.Where(r => season == Weather.Season.AllYear || season == r.Season)
                              .SelectMany(r => r.TimeTable.Entries.ToArray()));
                }
                catch (InvalidOperationException) when (attempt < 20)
                {
                    System.Threading.Thread.Yield();
                }
                catch (ArgumentException) when (attempt < 20)
                {
                    System.Threading.Thread.Yield();
                }
            }
            entries.RemoveAll(e => e?.Coach?.Coach == null || e.TimeTable?.Route == null);

            foreach (RouteTimeTableEntry entry in entries)
            {
                var entryTakeoffTime = new TimeSpan(
                    (int) entry.Day,
                    entry.Time.Hours,
                    entry.Time.Minutes,
                    entry.Time.Seconds);
                TimeSpan entryLandingTime =
                    entryTakeoffTime.Add(entry.TimeTable.Route.GetTripTime(entry.Coach.Coach.Type));

                if (entryLandingTime.Days > 6)
                {
                    entryLandingTime = new TimeSpan(
                        0,
                        entryLandingTime.Hours,
                        entryLandingTime.Minutes,
                        entryLandingTime.Seconds);
                }

                if (entry.DepartureStation == station)
                {
                    TimeSpan entryStartTakeoffTime = entryTakeoffTime.Subtract(gateTimeBefore);
                    TimeSpan entryEndTakeoffTime = entryTakeoffTime.Add(gateTimeAfter);

                    var tTakeoffTime = new TimeSpan(
                        entryStartTakeoffTime.Days,
                        entryStartTakeoffTime.Hours,
                        (entryStartTakeoffTime.Minutes/15)*15,
                        0);

                    while (tTakeoffTime < entryEndTakeoffTime)
                    {
                        var takeoffSlot = (entry.TimeTable.Route, tTakeoffTime);
                        if (occupiedSet.Add(takeoffSlot))
                        {
                            occupiedSlots.Add(takeoffSlot);
                        }
                        tTakeoffTime = tTakeoffTime.Add(new TimeSpan(0, 15, 0));
                    }
                }

                if (entry.DepartureStation != station)
                {
                    TimeSpan entryStartLandingTime = entryLandingTime.Subtract(gateTimeBefore);
                    TimeSpan entryEndLandingTime = entryLandingTime.Add(gateTimeAfter);

                    var tLandingTime = new TimeSpan(
                        entryStartLandingTime.Days,
                        entryStartLandingTime.Hours,
                        (entryStartLandingTime.Minutes/15)*15,
                        0);

                    while (tLandingTime < entryEndLandingTime)
                    {
                        var landingSlot = (entry.TimeTable.Route, tLandingTime);
                        if (occupiedSet.Add(landingSlot))
                        {
                            occupiedSlots.Add(landingSlot);
                        }
                        tLandingTime = tLandingTime.Add(new TimeSpan(0, 15, 0));
                    }
                }
            }

            // the times where all the gates are taken, each listed once per route using it (in the order first seen)
            var counts = new Dictionary<TimeSpan, int>();
            var order = new List<TimeSpan>();
            foreach (var slot in occupiedSlots)
            {
                int c;
                if (counts.TryGetValue(slot.Time, out c)) counts[slot.Time] = c + 1;
                else
                {
                    counts[slot.Time] = 1;
                    order.Add(slot.Time);
                }
            }
            var result = new List<TimeSpan>();
            foreach (TimeSpan time in order)
            {
                int c = counts[time];
                if (c >= gates)
                    for (int i = 0; i < c; i++) result.Add(time);
            }
            return result;
        }

        public static List<TimeSpan> GetOccupiedSlotTimes(Station station, Carrier carrier, Weather.Season season, Terminal.TerminalType type)
        {
            return GetOccupiedSlotTimes(
                station,
                carrier,
                station.CarrierContracts.Where(c => c.Carrier == carrier && c.TerminalType == type).ToList(),
                season);
        }

        public static double GetShortestDistance(Predicate<Station> match)
        {
            List<Station> stations1 = Stations.GetAllStations(match);
            List<Station> stations2 = Stations.GetAllStations(match);

            double minDistance = double.MaxValue;

            foreach (Station station1 in stations1)
            {
                double distance = stations2.Where(a => a != station1).Max(a => MathHelpers.GetDistance(a, station1));

                if (distance <= minDistance)
                {
                    minDistance = distance;
                }
            }

            return minDistance;
        }

        //sets the station expansion to an station
        public static void SetStationExpansion(Station station, StationExpansion expansion, bool onStartUp = false)
        {
            if (expansion.Type == StationExpansion.ExpansionType.Name)
            {
                if (expansion.NotifyOnChange && !onStartUp)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "Station Name Changed",
                                      $"[LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) has changed its name to {expansion.Name}"));
                }

                station.Profile.Name = expansion.Name;
            }
            if (expansion.Type == StationExpansion.ExpansionType.NewBay)
            {
                var bay = new Bay(expansion.Name, expansion.Length, Bay.BayType.Regular, expansion.Surface, expansion.Date, true);
                station.Bays.Add(bay);

                if (expansion.NotifyOnChange && !onStartUp)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "New Bay",
                                      $"[LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) has created a new bay"));
                }
            }
            if (expansion.Type == StationExpansion.ExpansionType.BayLength)
            {
                Bay bay = station.Bays.FirstOrDefault(r => r.Name == expansion.Name);

                if (bay != null)
                {
                    if (expansion.NotifyOnChange && !onStartUp)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.StationNews,
                                          GameObject.GetInstance().GameTime,
                                          "New Terminal",
                                          $"[LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) has changed the length of the bay {expansion.Name} to {new SmallDistanceFormatter().Convert(expansion.Length)}"));
                    }
                }
            }
            if (expansion.Type == StationExpansion.ExpansionType.NewTerminal)
            {
                var terminal = new Terminal(station, expansion.Name, expansion.Gates, expansion.Date, expansion.TerminalType);
                station.AddTerminal(terminal);

                if (expansion.NotifyOnChange && !onStartUp)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "New Terminal",
                                      $"[LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) has created a new terminal with {expansion.Gates} gates"));
                }
            }
            if (expansion.Type == StationExpansion.ExpansionType.ExtraGates)
            {
                Terminal terminal = station.Terminals.StationTerminals.FirstOrDefault(t => t.Name == expansion.Name);

                if (terminal != null)
                {
                    for (int i = 0; i < expansion.Gates; i++)
                        terminal.Gates.AddGate(new Gate(expansion.Date));

                    if (expansion.NotifyOnChange && !onStartUp)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.StationNews,
                                          GameObject.GetInstance().GameTime,
                                          "New Gates at Station",
                                          $"[LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) has created {expansion.Gates} gates in {expansion.Name}"));
                    }
                }
            }
            if (expansion.Type == StationExpansion.ExpansionType.CloseTerminal)
            {
                Terminal terminal = station.Terminals.StationTerminals.FirstOrDefault(t => t.Name == expansion.Name);

                if (terminal != null)
                {
                    station.RemoveTerminal(terminal);

                    if (expansion.NotifyOnChange && !onStartUp)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.StationNews,
                                          GameObject.GetInstance().GameTime,
                                          "Closed Terminal",
                                          $"[LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) has closed its terminal {expansion.Name}"));
                    }
                }
            }
            //close terminal
        }

        //returns if an carrier has enough free slots at an station

        //returns the yearly payment for a number of gates
        public static double GetYearlyContractPayment(
            Station station,
            StationContract.ContractType type,
            int gates,
            int length)
        {
            double basePrice = 0;

            if (type == StationContract.ContractType.Full)
            {
                basePrice = station.GetGatePrice()*12;
            }

            if (type == StationContract.ContractType.LowService)
            {
                basePrice = station.GetGatePrice()*13;
            }

            if (type == StationContract.ContractType.MediumService)
            {
                basePrice = station.GetGatePrice()*17;
            }

            if (type == StationContract.ContractType.FullService)
            {
                basePrice = station.GetGatePrice()*20;
            }

            double lengthFactor = 100 - length;

            return gates*(basePrice*(lengthFactor/100));
        }

        public static bool HasBadWeather(Station station)
        {
            return false;
            //station.Weather[0].WindSpeed == Weather.eWindSpeed.Hurricane || station.Weather[0].WindSpeed == Weather.eWindSpeed.Violent_Storm;
        }

        public static bool HasFreeGates(Station station, Carrier carrier)
        {
            Terminal.TerminalType type = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

            return HasFreeGates(station, carrier, type);
        }

        public static bool HasFreeGates(Station station, Carrier carrier, Terminal.TerminalType type)
        {
            List<StationContract> contracts = station.GetCarrierContracts(carrier).Where(c => c.TerminalType == type).ToList();

            if (contracts.Count == 0)
            {
                return false;
            }

            return station.Terminals.GetFreeSlotsPercent(carrier, type) > 90;
        }

        public static bool HasRoute(Station station1, Station station2)
        {
            return RouteIndex.HasRoute(station1, station2);
        }

        public static void ReallocateStation(Station stationOld, Station stationNew)
        {
            if (stationNew.GetMajorDestinations().Count == 0)
            {
                foreach (DestinationDemand paxDemand in stationOld.GetDestinationsPassengers())
                {
                    stationNew.AddDestinationPassengersRate(paxDemand);
                }
            }
        }

        public static bool RentGates(Station station, Carrier carrier, StationContract.ContractType type)
        {
            Terminal.TerminalType terminaltype = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

            return RentGates(station, carrier, type, terminaltype);
        }

        public static bool RentGates(Station station, Carrier carrier, StationContract.ContractType type, Terminal.TerminalType terminaltype)
        {
            int maxGates = station.Terminals.GetFreeGates(terminaltype);

            int gatesToRent = Math.Min(maxGates, (int) (carrier.Mentality) + 2);

            if (gatesToRent == 0)
            {
                return false;
            }

            RentGates(station, carrier, type, terminaltype, gatesToRent);

            return true;
        }

        public static void RentGates(
            Station station,
            Carrier carrier,
            StationContract.ContractType type,
            Terminal.TerminalType terminaltype,
            int gates,
            int length = 20)
        {
            // extra gates go on the carrier's existing contract of the same kind, with the rent growing with them;
            // when there is none (or only a different kind), the gates get a contract of their own
            StationContract existing = station.CarrierContracts.FirstOrDefault(
                c => c.Carrier == carrier
                     && c.Type == type
                     && c.TerminalType == terminaltype
                     && c.Terminal == null
                     && !c.IsExclusiveDeal);

            if (existing != null)
            {
                existing.NumberOfGates += gates;
                existing.YearlyPayment += GetYearlyContractPayment(station, type, gates, existing.Length);
            }
            else
            {
                AddCarrierContract(
                    new StationContract(
                        carrier,
                        station,
                        type,
                        terminaltype,
                        GameObject.GetInstance().GameTime,
                        gates,
                        length,
                        GetYearlyContractPayment(station, type, gates, length),
                        true));
            }

            for (int i = 0; i < gates; i++)
            {
                Gate gate = station.Terminals.GetGates().FirstOrDefault(g => g.Carrier == null);

                if (gate != null)
                    gate.Carrier = carrier;
            }
        }

        #endregion

        #region Methods

        private static Weather CreateDayWeather(Station station, DateTime date, Weather previousWeather)
        {
            WeatherAverage average = WeatherAverages.GetWeatherAverage(date.Month, station);

            if (average != null)
            {
                return CreateDayWeather(date, previousWeather, average);
            }

            var precipitationValues = (Weather.Precipitation[]) Enum.GetValues(typeof (Weather.Precipitation));
            var coverValues = (Weather.CloudCover[]) Enum.GetValues(typeof (Weather.CloudCover));
            var windDirectionValues = (Weather.WindDirection[]) Enum.GetValues(typeof (Weather.WindDirection));
            var windSpeedValues = (Weather.eWindSpeed[]) Enum.GetValues(typeof (Weather.eWindSpeed));
            Weather.eWindSpeed windSpeed;
            double temperature;

            Weather.WindDirection windDirection = windDirectionValues[Rnd.Next(windDirectionValues.Length)];

            if (previousWeather == null)
            {
                windSpeed = windSpeedValues[Rnd.Next(windSpeedValues.Length)];

                const double maxTemp = 40;
                const double minTemp = -20;

                temperature = MathHelpers.GetRandomDoubleNumber(minTemp, maxTemp);
            }
            else
            {
                int windIndex = windSpeedValues.ToList().IndexOf(previousWeather.WindSpeed);
                windSpeed =
                    windSpeedValues[
                        Rnd.Next(Math.Max(0, windIndex - 2), Math.Min(windIndex + 2, windSpeedValues.Length))];

                double previousTemperature = (previousWeather.TemperatureHigh + previousWeather.TemperatureLow)/2;

                double maxTemp = Math.Min(40, previousTemperature + 5);
                double minTemp = Math.Max(-20, previousTemperature - 5);

                temperature = MathHelpers.GetRandomDoubleNumber(minTemp, maxTemp);
            }

            double temperatureLow = temperature - Rnd.Next(1, 10);
            double temperatureHigh = temperature + Rnd.Next(1, 10);

            double tempDiff = temperatureHigh - temperatureLow;
            double temperatureSunrise = temperatureLow + MathHelpers.GetRandomDoubleNumber(-2, Math.Min(tempDiff, 2));
            double temperatureSunset = temperatureHigh - MathHelpers.GetRandomDoubleNumber(-2, Math.Min(tempDiff, 2));
            double temperatureDayend = temperatureLow + Rnd.Next(-2, 2);

            Weather.CloudCover cover = coverValues[Rnd.Next(coverValues.Length)];
            Weather.Precipitation precip;
            if (cover == Weather.CloudCover.Overcast)
            {
                precip = precipitationValues[Rnd.Next(precipitationValues.Length)];
            }

            var hourlyTemperature = new HourlyWeather[24];

            if (previousWeather == null)
            {
                hourlyTemperature[0] = new HourlyWeather(
                    temperatureLow,
                    cover,
                    cover == Weather.CloudCover.Overcast ? GetPrecipitation(temperatureLow) : Weather.Precipitation.None,
                    windSpeed,
                    windDirection);
            }
            else
            {
                hourlyTemperature[0] = previousWeather.Temperatures[previousWeather.Temperatures.Length - 1];
            }

            double morningSteps = (temperatureSunrise - hourlyTemperature[0].Temperature)/(Weather.Sunrise - 1);

            for (int i = 1; i <= Weather.Sunrise; i++)
            {
                double temp = hourlyTemperature[i - 1].Temperature + morningSteps;
                Weather.CloudCover hourlyCover = Rnd.Next(3) == 0 ? coverValues[Rnd.Next(coverValues.Length)] : cover;

                int windspeedIndex = windSpeedValues.ToList().IndexOf(windSpeed);
                Weather.eWindSpeed[] hourlyWindspeedValues =
                    {
                        windSpeed, windSpeed, windSpeed,
                        hourlyTemperature[i - 1].WindSpeed,
                        windspeedIndex > 0
                            ? (Weather.eWindSpeed) windspeedIndex - 1
                            : (Weather.eWindSpeed) windspeedIndex + 1,
                        windspeedIndex < windSpeedValues.Length - 1
                            ? (Weather.eWindSpeed) windspeedIndex + 1
                            : (Weather.eWindSpeed) windspeedIndex - 1
                    };
                Weather.eWindSpeed hourlyWindspeed = hourlyWindspeedValues[Rnd.Next(hourlyWindspeedValues.Length)];

                hourlyTemperature[i] = new HourlyWeather(
                    temp,
                    hourlyCover,
                    hourlyCover == Weather.CloudCover.Overcast ? GetPrecipitation(temp) : Weather.Precipitation.None,
                    hourlyWindspeed,
                    windDirection);
            }

            double daySteps = (temperatureSunset - temperatureSunrise)/(Weather.Sunset - Weather.Sunrise - 1);

            for (int i = Weather.Sunrise + 1; i < Weather.Sunset; i++)
            {
                Weather.CloudCover hourlyCover = Rnd.Next(3) == 0 ? coverValues[Rnd.Next(coverValues.Length)] : cover;

                double temp = hourlyTemperature[i - 1].Temperature + daySteps;
                if (hourlyCover != hourlyTemperature[i - 1].Cover && hourlyCover == Weather.CloudCover.Overcast)
                {
                    temp -= MathHelpers.GetRandomDoubleNumber(1, 4);
                }
                if (hourlyCover != hourlyTemperature[i - 1].Cover
                    && hourlyTemperature[i - 1].Cover == Weather.CloudCover.Overcast)
                {
                    temp += MathHelpers.GetRandomDoubleNumber(1, 4);
                }

                int windspeedIndex = windSpeedValues.ToList().IndexOf(windSpeed);
                Weather.eWindSpeed[] hourlyWindspeedValues =
                    {
                        windSpeed, windSpeed, windSpeed,
                        hourlyTemperature[i - 1].WindSpeed,
                        windspeedIndex > 0
                            ? (Weather.eWindSpeed) windspeedIndex - 1
                            : (Weather.eWindSpeed) windspeedIndex + 1,
                        windspeedIndex < windSpeedValues.Length - 1
                            ? (Weather.eWindSpeed) windspeedIndex + 1
                            : (Weather.eWindSpeed) windspeedIndex - 1
                    };
                Weather.eWindSpeed hourlyWindspeed = hourlyWindspeedValues[Rnd.Next(hourlyWindspeedValues.Length)];

                hourlyTemperature[i] = new HourlyWeather(
                    temp,
                    hourlyCover,
                    hourlyCover == Weather.CloudCover.Overcast ? GetPrecipitation(temp) : Weather.Precipitation.None,
                    hourlyWindspeed,
                    windDirection);
            }

            double eveningSteps = (temperatureDayend - temperatureSunset)/(hourlyTemperature.Length - Weather.Sunset);

            for (int i = Weather.Sunset; i < hourlyTemperature.Length; i++)
            {
                double temp = hourlyTemperature[i - 1].Temperature + eveningSteps;
                Weather.CloudCover hourlyCover = Rnd.Next(3) == 0 ? coverValues[Rnd.Next(coverValues.Length)] : cover;

                int windspeedIndex = windSpeedValues.ToList().IndexOf(windSpeed);
                Weather.eWindSpeed[] hourlyWindspeedValues =
                    {
                        windSpeed, windSpeed, windSpeed,
                        hourlyTemperature[i - 1].WindSpeed,
                        windspeedIndex > 0
                            ? (Weather.eWindSpeed) windspeedIndex - 1
                            : (Weather.eWindSpeed) windspeedIndex + 1,
                        windspeedIndex < windSpeedValues.Length - 1
                            ? (Weather.eWindSpeed) windspeedIndex + 1
                            : (Weather.eWindSpeed) windspeedIndex - 1
                    };
                Weather.eWindSpeed hourlyWindspeed = hourlyWindspeedValues[Rnd.Next(hourlyWindspeedValues.Length)];

                hourlyTemperature[i] = new HourlyWeather(
                    temp,
                    hourlyCover,
                    hourlyCover == Weather.CloudCover.Overcast ? GetPrecipitation(temp) : Weather.Precipitation.None,
                    hourlyWindspeed,
                    windDirection);
            }

            temperatureLow = hourlyTemperature.Min(t => t.Temperature);
            temperatureHigh = hourlyTemperature.Max(t => t.Temperature);
            cover =
                (from c in hourlyTemperature group c by c.Cover into g select new {Cover = g.Key, Qty = g.Count()})
                    .OrderByDescending(g => g.Qty).First().Cover;
            precip =
                (from c in hourlyTemperature group c by c.Precip into g select new {Precip = g.Key, Qty = g.Count()})
                    .OrderByDescending(g => g.Qty).First().Precip;

            var weather = new Weather(
                date,
                windSpeed,
                windDirection,
                cover,
                precip,
                hourlyTemperature,
                temperatureLow,
                temperatureHigh);

            return weather;
        }

        //creates the weather from an average
        private static Weather CreateDayWeather(DateTime date, Weather previousWeather, WeatherAverage average)
        {
            var windDirectionValues = (Weather.WindDirection[]) Enum.GetValues(typeof (Weather.WindDirection));
            var windSpeedValues = (Weather.eWindSpeed[]) Enum.GetValues(typeof (Weather.eWindSpeed));
            var coverValues = (Weather.CloudCover[]) Enum.GetValues(typeof (Weather.CloudCover));

            Weather.WindDirection windDirection = windDirectionValues[Rnd.Next(windDirectionValues.Length)];
            Weather.CloudCover cover;
            Weather.Precipitation precip;
            Weather.eWindSpeed windSpeed;
            double temperatureLow,
                   temperatureHigh;

            int windIndexMin = windSpeedValues.ToList().IndexOf(average.WindSpeedMin);
            int windIndexMax = windSpeedValues.ToList().IndexOf(average.WindSpeedMax);

            if (previousWeather == null)
            {
                windSpeed = windSpeedValues[Rnd.Next(windIndexMin, windIndexMax)];

                temperatureLow = MathHelpers.GetRandomDoubleNumber(
                    average.TemperatureMin - 5,
                    average.TemperatureMin + 5);
                temperatureHigh =
                    MathHelpers.GetRandomDoubleNumber(
                        Math.Max(average.TemperatureMax - 5, temperatureLow + 1),
                        average.TemperatureMax + 5);
            }
            else
            {
                double previousTemperature = (previousWeather.TemperatureHigh + previousWeather.TemperatureLow)/2;
                int windIndex = windSpeedValues.ToList().IndexOf(previousWeather.WindSpeed);
                windSpeed =
                    windSpeedValues[
                        Rnd.Next(Math.Max(windIndexMin, windIndex - 2), Math.Min(windIndex + 2, windIndexMax))];

                double minTemp = Math.Max(average.TemperatureMin, previousTemperature - 5);
                temperatureLow = MathHelpers.GetRandomDoubleNumber(minTemp - 5, minTemp + 5);

                double maxTemp = Math.Min(average.TemperatureMax, previousTemperature + 5);

                temperatureHigh = MathHelpers.GetRandomDoubleNumber(Math.Max(maxTemp - 5, temperatureLow), maxTemp + 5);
                //rnd.NextDouble() * ((maxTemp + 5) - Math.Max(maxTemp - 5, temperatureLow + 2)) + Math.Max(maxTemp - 5, temperatureLow + 2);
            }

            double tempDiff = temperatureHigh - temperatureLow;
            double temperatureSunrise = temperatureLow + MathHelpers.GetRandomDoubleNumber(-2, Math.Min(tempDiff, 2));
            double temperatureSunset = temperatureHigh - MathHelpers.GetRandomDoubleNumber(-2, Math.Min(tempDiff, 2));
            double temperatureDayend = temperatureLow + Rnd.Next(-2, 2);

            double temperature = (temperatureLow + temperatureHigh)/2;

            bool isOvercast = Rnd.Next(100) < average.Precipitation;
            if (isOvercast)
            {
                cover = Weather.CloudCover.Overcast;
                precip = GetPrecipitation(temperature);
            }
            else
            {
                Weather.CloudCover[] notOvercastCovers =
                    {
                        Weather.CloudCover.Clear, Weather.CloudCover.MostlyCloudy,
                        Weather.CloudCover.PartlyCloudy
                    };
                cover = notOvercastCovers[Rnd.Next(notOvercastCovers.Length)];
            }

            var hourlyTemperature = new HourlyWeather[24];

            if (previousWeather == null)
            {
                hourlyTemperature[0] = new HourlyWeather(
                    temperatureLow,
                    cover,
                    cover == Weather.CloudCover.Overcast ? GetPrecipitation(temperatureLow) : Weather.Precipitation.None,
                    windSpeed,
                    windDirection);
            }
            else
            {
                hourlyTemperature[0] = previousWeather.Temperatures[previousWeather.Temperatures.Length - 1];
            }

            double morningSteps = (temperatureSunrise - hourlyTemperature[0].Temperature)/(Weather.Sunrise - 1);

            for (int i = 1; i <= Weather.Sunrise; i++)
            {
                double temp = hourlyTemperature[i - 1].Temperature + morningSteps;
                Weather.CloudCover hourlyCover = Rnd.Next(3) == 0 ? coverValues[Rnd.Next(coverValues.Length)] : cover;

                int windspeedIndex = windSpeedValues.ToList().IndexOf(windSpeed);
                Weather.eWindSpeed[] hourlyWindspeedValues =
                    {
                        windSpeed, windSpeed, windSpeed,
                        hourlyTemperature[i - 1].WindSpeed,
                        windspeedIndex > 0
                            ? (Weather.eWindSpeed) windspeedIndex - 1
                            : (Weather.eWindSpeed) windspeedIndex + 1,
                        windspeedIndex < windSpeedValues.Length - 1
                            ? (Weather.eWindSpeed) windspeedIndex + 1
                            : (Weather.eWindSpeed) windspeedIndex - 1
                    };
                Weather.eWindSpeed hourlyWindspeed = hourlyWindspeedValues[Rnd.Next(hourlyWindspeedValues.Length)];

                hourlyTemperature[i] = new HourlyWeather(
                    temp,
                    hourlyCover,
                    hourlyCover == Weather.CloudCover.Overcast ? GetPrecipitation(temp) : Weather.Precipitation.None,
                    hourlyWindspeed,
                    windDirection);
            }

            double daySteps = (temperatureSunset - temperatureSunrise)/(Weather.Sunset - Weather.Sunrise - 1);

            for (int i = Weather.Sunrise + 1; i < Weather.Sunset; i++)
            {
                Weather.CloudCover hourlyCover = Rnd.Next(3) == 0 ? coverValues[Rnd.Next(coverValues.Length)] : cover;

                double temp = hourlyTemperature[i - 1].Temperature + daySteps;
                if (hourlyCover != hourlyTemperature[i - 1].Cover && hourlyCover == Weather.CloudCover.Overcast)
                {
                    temp -= MathHelpers.GetRandomDoubleNumber(1, 4);
                }
                if (hourlyCover != hourlyTemperature[i - 1].Cover
                    && hourlyTemperature[i - 1].Cover == Weather.CloudCover.Overcast)
                {
                    temp += MathHelpers.GetRandomDoubleNumber(1, 4);
                }

                int windspeedIndex = windSpeedValues.ToList().IndexOf(windSpeed);
                Weather.eWindSpeed[] hourlyWindspeedValues =
                    {
                        windSpeed, windSpeed, windSpeed,
                        hourlyTemperature[i - 1].WindSpeed,
                        windspeedIndex > 0
                            ? (Weather.eWindSpeed) windspeedIndex - 1
                            : (Weather.eWindSpeed) windspeedIndex + 1,
                        windspeedIndex < windSpeedValues.Length - 1
                            ? (Weather.eWindSpeed) windspeedIndex + 1
                            : (Weather.eWindSpeed) windspeedIndex - 1
                    };
                Weather.eWindSpeed hourlyWindspeed = hourlyWindspeedValues[Rnd.Next(hourlyWindspeedValues.Length)];

                hourlyTemperature[i] = new HourlyWeather(
                    temp,
                    hourlyCover,
                    hourlyCover == Weather.CloudCover.Overcast ? GetPrecipitation(temp) : Weather.Precipitation.None,
                    hourlyWindspeed,
                    windDirection);
            }

            double eveningSteps = (temperatureDayend - temperatureSunset)/(hourlyTemperature.Length - Weather.Sunset);

            for (int i = Weather.Sunset; i < hourlyTemperature.Length; i++)
            {
                double temp = hourlyTemperature[i - 1].Temperature + eveningSteps;
                Weather.CloudCover hourlyCover = Rnd.Next(3) == 0 ? coverValues[Rnd.Next(coverValues.Length)] : cover;

                int windspeedIndex = windSpeedValues.ToList().IndexOf(windSpeed);
                Weather.eWindSpeed[] hourlyWindspeedValues =
                    {
                        windSpeed, windSpeed, windSpeed,
                        hourlyTemperature[i - 1].WindSpeed,
                        windspeedIndex > 0
                            ? (Weather.eWindSpeed) windspeedIndex - 1
                            : (Weather.eWindSpeed) windspeedIndex + 1,
                        windspeedIndex < windSpeedValues.Length - 1
                            ? (Weather.eWindSpeed) windspeedIndex + 1
                            : (Weather.eWindSpeed) windspeedIndex - 1
                    };
                Weather.eWindSpeed hourlyWindspeed = hourlyWindspeedValues[Rnd.Next(hourlyWindspeedValues.Length)];

                hourlyTemperature[i] = new HourlyWeather(
                    temp,
                    hourlyCover,
                    hourlyCover == Weather.CloudCover.Overcast ? GetPrecipitation(temp) : Weather.Precipitation.None,
                    hourlyWindspeed,
                    windDirection);
            }
            temperatureLow = hourlyTemperature.Min(t => t.Temperature);
            temperatureHigh = hourlyTemperature.Max(t => t.Temperature);

            cover =
                (from c in hourlyTemperature group c by c.Cover into g select new {Cover = g.Key, Qty = g.Count()})
                    .OrderByDescending(g => g.Qty).First().Cover;
            precip =
                (from c in hourlyTemperature group c by c.Precip into g select new {Precip = g.Key, Qty = g.Count()})
                    .OrderByDescending(g => g.Qty).First().Precip;

            var weather = new Weather(
                date,
                windSpeed,
                windDirection,
                cover,
                precip,
                hourlyTemperature,
                temperatureLow,
                temperatureHigh);

            return weather;
        }

        //returns the precipitation for a temperature
        private static Weather.Precipitation GetPrecipitation(double temperature)
        {
            if (temperature > 10)
            {
                Weather.Precipitation[] values =
                    {
                        Weather.Precipitation.Thunderstorms, Weather.Precipitation.HeavyRain,
                        Weather.Precipitation.LightRain,
                        Weather.Precipitation.IsolatedThunderstorms
                    };
                return values[Rnd.Next(values.Length)];
            }
            if (temperature <= 10 && temperature >= 5)
            {
                Weather.Precipitation[] values =
                    {
                        Weather.Precipitation.HeavyRain, Weather.Precipitation.LightRain,
                        Weather.Precipitation.IsolatedRain,
                        Weather.Precipitation.IsolatedThunderstorms
                    };
                return values[Rnd.Next(values.Length)];
            }
            if (temperature < 5 && temperature >= -3)
            {
                Weather.Precipitation[] values =
                    {
                        Weather.Precipitation.FreezingRain,
                        Weather.Precipitation.MixedRainAndSnow,
                        Weather.Precipitation.Sleet, Weather.Precipitation.LightSnow,
                        Weather.Precipitation.IsolatedSnow
                    };
                return values[Rnd.Next(values.Length)];
            }
            if (temperature < -3)
            {
                Weather.Precipitation[] values =
                    {
                        Weather.Precipitation.HeavySnow, Weather.Precipitation.LightSnow,
                        Weather.Precipitation.IsolatedSnow
                    };
                return values[Rnd.Next(values.Length)];
            }
            return Weather.Precipitation.LightRain;
        }

        #endregion

        //returns the price for a hub at an station
    }
}