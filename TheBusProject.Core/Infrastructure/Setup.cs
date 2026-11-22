using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.CarrierCooperation;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Countries.Towns;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.HistoricEvents;
using TheBusProject.Models.General.Holidays;
using TheBusProject.Models.General.Scenarios;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Infrastructure
{
    /*! Setup class.
     * This class is used for configuring the games environment.
     * The class needs no instantiation, because all methods are declared static.
     */

    public class Setup
    {
        #region Static Fields

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        // the loaders catch and swallow their own exceptions, so also write them to startup.log where they can be found
        private static void LogError(string message, params object[] args)
        {
            try
            {
                var text = args != null && args.Length > 0 && !(args[0] is Exception)
                    ? string.Format(message, args)
                    : message;
                var ex = args?.OfType<Exception>().FirstOrDefault();
                StartupLog.Write("LOADER ERROR: " + text + (ex != null ? "\n" + ex : ""));
            }
            catch
            {
            }
        }

        private static void LogError(Exception e)
        {
            StartupLog.Write("LOADER ERROR: " + e);
            StartupLog.Write("LOADER ERROR: " + e);
        }

        #endregion

        #region Public Methods and Operators

        public static Carrier LoadCarrier(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            XmlElement root = doc.DocumentElement;

            var profileElement = (XmlElement) root?.SelectSingleNode("profile");

            if (profileElement != null)
            {
                string name = profileElement.Attributes["name"].Value;
                string iata = profileElement.Attributes["iata"].Value;
                string color = profileElement.Attributes["color"].Value;

                string logoImage = profileElement.HasAttribute("logo") ? profileElement.Attributes["logo"].Value : "";

                string sCountries = profileElement.Attributes["country"].Value;

                var countries = sCountries.Split(';').Select(Countries.GetCountry).ToList();

                //Country country = Countries.GetCountry(profileElement.Attributes["country"].Value);
                string ceo = profileElement.Attributes["CEO"].Value;
                var mentality =
                    (Carrier.CarrierMentality)
                        DataParsing.ParseEnum(typeof (Carrier.CarrierMentality), profileElement.Attributes["mentality"].Value);
                var market =
                    (CarrierFocus)
                        DataParsing.ParseEnum(typeof (CarrierFocus), profileElement.Attributes["market"].Value);

                var routeFocus = Route.RouteType.Passenger;

                if (profileElement.HasAttribute("routefocus"))
                {
                    routeFocus =
                        (Route.RouteType) DataParsing.ParseEnum(typeof (Route.RouteType), profileElement.Attributes["routefocus"].Value);
                }

                var narrativeElement = (XmlElement) profileElement.SelectSingleNode("narrative");

                string narrative = "";
                if (narrativeElement != null)
                {
                    narrative = narrativeElement.Attributes["narrative"].Value;
                }

                bool isReal = true;
                int founded = 1950;
                int folded = 2199;

                var infoElement = (XmlElement) root.SelectSingleNode("info");
                if (infoElement != null)
                {
                    isReal = Convert.ToBoolean(infoElement.Attributes["real"].Value);
                    founded = Convert.ToInt16(infoElement.Attributes["from"].Value);
                    folded = Convert.ToInt16(infoElement.Attributes["to"].Value);
                }

                var license = Carrier.CarrierLicense.Regional;

                if (market == CarrierFocus.Global)
                {
                    license = mentality == Carrier.CarrierMentality.Aggressive ? Carrier.CarrierLicense.LongHaul : Carrier.CarrierLicense.Domestic;
                }

                if (market == CarrierFocus.Regional)
                {
                    license = Carrier.CarrierLicense.Regional;
                }

                var carrier = new Carrier(
                    new CarrierProfile(name, iata, color, ceo, isReal, founded, folded),
                    mentality,
                    market,
                    license,
                    routeFocus) {Profile = {Countries = countries}};
                carrier.Profile.Country = carrier.Profile.Countries[0];
                carrier.Profile.LogoName = logoImage;

                var preferedsElement = (XmlElement) root.SelectSingleNode("prefereds");

                if (preferedsElement != null)
                {
                    if (preferedsElement.HasAttribute("coaches"))
                    {
                        string[] preferedCoaches = preferedsElement.Attributes["coaches"].Value.Split(',');

                        foreach (string preferedCoach in preferedCoaches)
                        {
                            CoachType pCoach = CoachTypes.GetType(preferedCoach);
                            carrier.Profile.AddPreferedCoach(pCoach);
                        }
                    }
                    if (preferedsElement.HasAttribute("primarypurchasing"))
                    {
                        var primarypurchasing =
                            (CarrierProfile.PreferedPurchasing)
                                DataParsing.ParseEnum(
                                    typeof (CarrierProfile.PreferedPurchasing),
                                    preferedsElement.Attributes["primarypurchasing"].Value);
                        carrier.Profile.PrimaryPurchasing = primarypurchasing;
                    }
                }

                XmlNodeList logosList = profileElement.SelectNodes("logos/logo");

                if (logosList != null)
                    foreach (XmlElement logoElement in logosList)
                    {
                        int logoFromYear = Convert.ToInt16(logoElement.Attributes["from"].Value);
                        int logoToYear = Convert.ToInt16(logoElement.Attributes["to"].Value);
                        string logoPath = AppSettings.GetDataPath() + "/graphics/carrierlogos/multilogos/"
                                          + logoElement.Attributes["path"].Value + ".png";

                        carrier.Profile.AddLogo(new CarrierLogo(logoFromYear, logoToYear, logoPath));
                    }

                if (profileElement.HasAttribute("preferedstation"))
                {
                    Station preferedStation = Stations.GetStation(profileElement.Attributes["preferedstation"].Value);
                    carrier.Profile.PreferedStation = preferedStation;
                }

                XmlNodeList subsidiariesList = root.SelectNodes("subsidiaries/subsidiary");
                if (subsidiariesList != null)
                {
                    foreach (XmlElement subsidiaryElement in subsidiariesList)
                    {
                        string subName = subsidiaryElement.Attributes["name"].Value;
                        string subIATA = subsidiaryElement.Attributes["IATA"].Value;
                        Station subStation = Stations.GetStation(subsidiaryElement.Attributes["homebase"].Value);
                        var subMentality =
                            (Carrier.CarrierMentality)
                                DataParsing.ParseEnum(
                                    typeof (Carrier.CarrierMentality),
                                    subsidiaryElement.Attributes["mentality"].Value);
                        var subMarket =
                            (CarrierFocus)
                                DataParsing.ParseEnum(typeof (CarrierFocus), subsidiaryElement.Attributes["market"].Value);

                        string subLogo = AppSettings.GetDataPath() + "/graphics/carrierlogos/"
                                         + subsidiaryElement.Attributes["logo"].Value + ".png";

                        var carrierRouteFocus = Route.RouteType.Passenger;

                        if (subsidiaryElement.HasAttribute("routefocus"))
                        {
                            carrierRouteFocus =
                                (Route.RouteType)
                                    DataParsing.ParseEnum(typeof (Route.RouteType), subsidiaryElement.Attributes["routefocus"].Value);
                        }

                        carrier.FutureCarriers.Add(
                            new FutureSubsidiaryCarrier(
                                subName,
                                subIATA,
                                subStation,
                                subMentality,
                                subMarket,
                                carrierRouteFocus,
                                subLogo));
                    }
                }

                var startDataElement = (XmlElement) root.SelectSingleNode("startdata");
                if (startDataElement != null)
                {
                    var startData = new CarrierStartData(carrier);

                    XmlNodeList routesList = startDataElement.SelectNodes("routes/route");
                    if (routesList != null)
                        foreach (XmlElement routeElement in routesList)
                        {
                            string dest1 = routeElement.Attributes["destination1"].Value;
                            string dest2 = routeElement.Attributes["destination2"].Value;
                            int opened = Convert.ToInt16(routeElement.Attributes["opened"].Value);
                            int closed = Convert.ToInt16(routeElement.Attributes["closed"].Value);

                            Route.RouteType routetype = carrier.CarrierRouteFocus;

                            if (routeElement.HasAttribute("routetype"))
                            {
                                routetype =
                                    (Route.RouteType)
                                        DataParsing.ParseEnum(typeof (Route.RouteType), routeElement.Attributes["routetype"].Value);
                            }

                            var sdr = new StartDataRoute(dest1, dest2, opened, closed, routetype);
                            startData.AddRoute(sdr);

                            if (routeElement.HasAttribute("coach"))
                            {
                                CoachType coachType = CoachTypes.GetType(routeElement.Attributes["coach"].Value);
                                sdr.Type = coachType;
                            }
                        }

                    XmlNodeList coachesList = startDataElement.SelectNodes("coaches/coach");

                    foreach (XmlElement coachElement in coachesList)
                    {
                        string type = coachElement.Attributes["type"].Value;
                        int early = Convert.ToInt16(coachElement.Attributes["early"].Value);
                        int late = Convert.ToInt16(coachElement.Attributes["late"].Value);

                        startData.AddCoaches(new StartDataCoaches(type, early, late));
                    }

                    XmlNodeList randomRoutesList = startDataElement.SelectNodes("random_routes/routes");

                    foreach (XmlElement routeElement in randomRoutesList)
                    {
                        string origin = routeElement.Attributes["origin"].Value;
                        int destinations = Convert.ToInt16(routeElement.Attributes["destinations"].Value);
                        var minimumsize =
                            (GeneralHelpers.Size)
                                DataParsing.ParseEnum(typeof (GeneralHelpers.Size), routeElement.Attributes["minimumsize"].Value);

                        Route.RouteType routetype = carrier.CarrierRouteFocus;

                        if (routeElement.HasAttribute("routetype"))
                        {
                            routetype =
                                (Route.RouteType)
                                    DataParsing.ParseEnum(typeof (Route.RouteType), routeElement.Attributes["routetype"].Value);
                        }

                        var routes = new StartDataRoutes(origin, destinations, minimumsize, routetype);

                        XmlNodeList countriesList = routeElement.SelectNodes("countries/country");

                        foreach (XmlElement countryElement in countriesList)
                        {
                            Country routesCountry = Countries.GetCountry(countryElement.Attributes["value"].Value);
                            routes.AddCountry(routesCountry);
                        }
                        startData.AddOriginRoutes(routes);
                    }

                    CarrierStartDatas.AddStartData(startData);
                }

                carrier.Profile.Narrative = narrative;

                Carriers.AddCarrier(carrier);

                return carrier;
            }
            return null;
        }

        /// <summary>The codes of every station in the data files when they were last read (saved with a game).</summary>
        public static HashSet<string> DataStationCodes { get; private set; }

        public static void LoadStations()
        {
            //LoadStations(AppSettings.getDataPath() + "/stations.xml");
            try
            {
                var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/stations");
                if (!dir.Exists) { StartupLog.Write("Data note: no stations folder; that data is skipped."); return; }

                // "north america MX.xml" is an older copy of many of the stations in "north america.xml":
                // it is loaded last so the newer definitions are the ones that are kept
                foreach (FileInfo file in dir.GetFiles("*.xml")
                                             .OrderBy(f => f.Name.EndsWith(" MX.xml", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                                             .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    LoadStations(file.FullName);
                }
            }
            catch (Exception e)
            {
                var file = new StreamWriter(
                    AppSettings.GetCommonApplicationDataPath() + "/thecarrierstartup.log",
                    true);
                file.WriteLine("Station failing");
                file.WriteLine(e.ToString());
                file.WriteLine(e.StackTrace);
                file.Close();
            }
            DataStationCodes = new HashSet<string>(Stations.GetAllStations().Select(a => a.Profile.IATACode));
            Stations.RememberWorld();
            Stations.CountSizes();
            foreach (GeneralHelpers.Size size in Enum.GetValues(typeof (GeneralHelpers.Size)))
            {
                if (!Stations.CargoStationsSizes.ContainsKey(size))
                {
                    GeneralHelpers.Size size1 = size;
                    Stations.CargoStationsSizes.Add(size, Stations.GetStations(a => a.Profile.Cargo == size1).Count);
                }
            }
        }

        public static void LoadConfigurations()
        {
            Configurations.Clear();

            LoadStandardConfigurations();
        }

        public static void SetupAlliances()
        {
            var alliances = new List<Alliance>(Alliances.GetAlliances());
            foreach (Alliance alliance in alliances)
            {
                int activeMembers =
                    alliance.Members.Count(
                        m =>
                        Carriers.ContainsCarrier(m.Carrier) && !m.Carrier.IsHuman
                        && m.JoinedDate <= GameObject.GetInstance().GameTime
                        && m.ExitedDate > GameObject.GetInstance().GameTime);

                if (activeMembers > 1)
                {
                    var members = new List<AllianceMember>(alliance.Members);

                    foreach (
                        AllianceMember member in
                            members.Where(
                                m =>
                                !Carriers.GetAllCarriers().Contains(m.Carrier)
                                || m.JoinedDate > GameObject.GetInstance().GameTime
                                || GameObject.GetInstance().GameTime > m.ExitedDate))
                    {
                        alliance.RemoveMember(member);
                    }
                }
                else
                {
                    while (alliance.Members.Count > 0)
                    {
                        alliance.RemoveMember(alliance.Members[0]);
                    }

                    Alliances.RemoveAlliance(alliance);
                }
            }
        }

        /*! reads the basic xmls for loading of game
         */

        /*! public static method SetupGame().
         * Tries to create game´s environment and base configuration.
         */

        /// <summary>The exception that stopped the last SetupGame() call, or null if it completed.</summary>
        public static Exception SetupError { get; private set; }

        public static void SetupGame()
        {
            SetupError = null;
            try
            {
                ReadSettingsFile();
                UserInterface.PrepareMaps?.Invoke();
                ClearLists();

                CreateTimeZones();
                SetupDifficultyLevels();
                SetupStatisticsTypes();
                CreateHubTypes();

                LoadRegions();
                LoadCountries();
                LoadStates();
                LoadTemporaryCountries();
                LoadUnions();

                CreateContinents();

                LoadStations();
                LoadStationLogos();
                LoadStationMaps();
                LoadStationFacilities();
                LoadCooperations();
                LoadMajorDestinations();

                LoadCarrierFacilities();

                LoadManufacturers();
                LoadManufacturerLogos();
                LoadCoaches();
                LoadCoachImages();
                LoadCoachFacilities();
                LoadEngineTypes();
                LoadTripRestrictions();
                LoadInflationYears();
                LoadHolidays();
                LoadHistoricEvents();
                LoadRandomEvents();
                LoadSpecialContracts();
                LoadWeatherAverages();

                CreateAdvertisementTypes();
                CreateFeeTypes();
                CreateTripFacilities();
                CreateTrainingCoachTypes();
                CreateDriverRatings();

                LoadStandardConfigurations();
                LoadCoachTypeConfigurations();

                LoadCarriers();
                LoadAlliances();

                LoadScenarios();
            }
            catch (Exception e)
            {
                //var file = new StreamWriter(
                //    AppSettings.GetCommonApplicationDataPath() + "/thecarrierstartup.log",
                //    true);
                //file.WriteLine("Game start failing");
                //file.WriteLine(e.ToString());
                //file.WriteLine(e.StackTrace);
                //file.Close();

                LogError("Failed to setup game", e);
                SetupError = e;
                return;
            }


            IEnumerable<Station> noRunways = Stations.GetAllStations().Where(a => a.Runways.Count == 0);

            foreach (Station station in noRunways)
                Console.WriteLine(@"{0}, {1} in {2} has no runways", station.Profile.Name, station.Profile.IATACode, station.Profile.Country.Name);

            Console.WriteLine(@"Stations: " + Stations.GetAllStations().Count);
            Console.WriteLine(@"Carriers: " + Carriers.GetAllCarriers().Count);

            Station lbb = Stations.GetStation("LBB");
            Station dfw = Stations.GetStation("DFW");

            if (lbb != null && dfw != null)
            {
                Console.WriteLine(@"Distance: " + MathHelpers.GetDistance(lbb, dfw));
            }

            /*
            System.IO.StreamWriter aFile = new System.IO.StreamWriter("c:/bbm/stations.csv");

            string lines = "Name;IATA;ICAO;Type;Season;Town;Country;GMT;DST;Latitude;Longitude;Size;Pax;Cargosize;Cargo;Terminals[Name%Gates];Runways[Name%Surface%Lenght]";
            
            aFile.WriteLine(lines);

            foreach (Station station in Stations.GetAllStations())
            {
                string stationLine = station.Profile.Name;
                stationLine += ";" + station.Profile.IATACode;
                stationLine += ";" + station.Profile.ICAOCode;
                stationLine += ";" + station.Profile.Type.ToString();
                stationLine += ";" + station.Profile.Season.ToString(); 
                stationLine += ";" + station.Profile.Town.Name;
                stationLine += ";" + station.Profile.Country.Name;
                stationLine += ";" + station.Profile.OffsetGMT.ToString();
                stationLine += ";" + station.Profile.OffsetDST.ToString();
                stationLine += ";" + station.Profile.Coordinates.Latitude.ToString();
                stationLine += ";" + station.Profile.Coordinates.Longitude.ToString();
                stationLine += ";" + station.Profile.Size.ToString();
                stationLine += ";" + station.Profile.Pax.ToString();
                stationLine += ";" + station.Profile.Cargo.ToString();
                stationLine += ";" + station.Profile.CargoVolume.ToString();

                string runwaysLine="";
                string terminalsLine="";

                foreach (Terminal terminal in station.Terminals.StationTerminals)
                {
                    terminalsLine += "[" + terminal.Name + "%" + terminal.Gates.NumberOfGates.ToString() + "]";
                }

                foreach (Runway runway in station.Runways)
                {
                    runwaysLine +=  "[" + runway.Name + "%" + runway.Surface.ToString() + "%" + runway.Length + "]";
                }

                stationLine += ";" + terminalsLine;
                stationLine += ";" + runwaysLine;

                aFile.WriteLine(stationLine);
            
                    
                    
                    //string.Format("{0};{1};{2};{3};{4};{5};{6};{7};{8};{9};{10};{11};{12};{13};{14};{15};{16}", station.Profile.Name, station.Profile.IATACode, station.Profile.ICAOCode, station.Profile.Type.ToString(), station.Profile.Season.ToString(),station.Profile.Town.Name,station.Profile.Town.Country.Name,station.Profile.OffsetGMT.ToString(),station.Profile.OffsetDST.ToString());

               
            }
            aFile.Close();
        */
            /*
                var townGroups =
                    from a in Stations.GetAllStations() where a.Profile.Town.Name.Trim().Length == 0
                    group a by a.Profile.Country.Region into g
                    select new { Region = g.Key, Stations = g };

                foreach (var g in townGroups)
                {
                    Console.WriteLine(g.Region.Name);
                    foreach (var a in g.Stations)
                    {
                        Console.WriteLine("      {0} ({1}), {2}",a.Profile.Name,a.Profile.IATACode,a.Profile.Country.Name);
                    }
                } */

            /*
            var coachFamilies = CoachTypes.GetAllTypes().Select(a => a.CoachFamily).Distinct();

            foreach (string family in coachFamilies)
                Console.WriteLine(family);

            /*
            var carrierRegionGroups =
                from a in Carriers.GetAllCarriers()
                group a by a.Profile.Country.Region into g
                select new { Region = g.Key, Stations = g };

            foreach (var g in carrierRegionGroups)
            {
                Console.WriteLine("{0} has {1} stations", g.Region.Name,g.Stations.Count());
                
             
            }


            var carriers = Carriers.GetAllCarriers().FindAll(a => a.Profile.PreferedStation == null);
            var carrierLogos = Carriers.GetAllCarriers().FindAll(a => a.Profile.Logos.Count==0);

            foreach (Carrier noLogoCarrier in carrierLogos)
                Console.WriteLine(noLogoCarrier.Profile.Name);

            var noRunwayStations = Stations.GetStations(a => a.Runways.Count == 0);
            var cargoCoaches = CoachTypes.GetAllTypes().FindAll(a => a.TypeCoach == CoachType.TypeOfCoach.Cargo);

            foreach (Station station in noRunwayStations)
                Console.WriteLine(station.Profile.Name);

            */
        }

        public static void SetupLoadedGame()
        {
            Countries.Clear();
            Regions.Clear();
            Unions.Clear();
            Continents.Clear();

            LoadRegions();
            LoadCountries();
            LoadStates();
            LoadTemporaryCountries();
            LoadUnions();

            CreateContinents();
        }

        public static void SetupMainGame(List<Carrier> opponents, int totalOpponents)
        {
            int year = GameObject.GetInstance().GameTime.Year;

            List<Carrier> notAvailableCarriers =
                Carriers.GetCarriers(a => !(a.Profile.Founded <= year && a.Profile.Folded > year));

            foreach (Carrier carrier in notAvailableCarriers)
            {
                Carriers.RemoveCarrier(carrier);
            }

            var carriers =
                new List<Carrier>(
                    Carriers.GetCarriers(
                        a =>
                        !a.IsHuman && !opponents.Contains(a) && a.Profile.Founded <= year && a.Profile.Folded > year));

            carriers = MathHelpers.Shuffle(carriers);

            for (int i = 0; i < carriers.Count + opponents.Count - totalOpponents; i++)
            {
                Carriers.RemoveCarrier(carriers[i]);
            }

            SetupMainGame();
        }

        public static void SetupMainGame(int opponents, bool sameRegion)
        {
            StartupLog.Write("SetupMainGame: removing carriers");
            RemoveCarriers(opponents, sameRegion);
            StartupLog.Write("SetupMainGame: carriers reduced to " + Carriers.GetAllCarriers().Count);

            SetupMainGame();
        }

        public static void SetupMergers()
        {
            CarrierMergers.Clear();
            LoadCarrierMergers();

            var mergers = new List<CarrierMerger>(CarrierMergers.GetCarrierMergers());

            foreach (CarrierMerger merger in mergers)
            {
                if (!Carriers.ContainsCarrier(merger.Carrier1) || !Carriers.ContainsCarrier(merger.Carrier2)
                    || merger.Carrier2.IsHuman || merger.Carrier1.IsHuman)
                {
                    CarrierMergers.RemoveCarrierMerger(merger);
                }
            }
        }

        #endregion

        #region Methods

        public static void ClearLists()
        {
            DemandOverrides.Clear();
            AdvertisementTypes.Clear();
            TimeZones.Clear();
            SpecialContractTypes.Clear();
            TemporaryCountries.Clear();
            Stations.Clear();
            StationFacilities.Clear();
            CarrierFacilities.Clear();
            Manufacturers.Clear();
            Coaches.Clear();
            Carriers.Clear();
            CoachFacilities.Clear();
            RouteFacilities.Clear();
            StatisticsTypes.Clear();
            Continents.Clear();
            Regions.Clear();
            Countries.Clear();
            States.Clear();
            Unions.Clear();
            CoachTypes.Clear();
            EngineTypes.Clear();
            FeeTypes.Clear();
            DriverRatings.Clear();
            Alliances.Clear();
            TripRestrictions.Clear();
            Inflations.Clear();
            Holidays.Clear();
            Configurations.Clear();
            HistoricEvents.Clear();
            RandomEvents.Clear();
            WeatherAverages.Clear();
            DifficultyLevels.Clear();
            Drivers.Clear();
            Instructors.Clear();
            TrainingCoachTypes.Clear();
            Scenarios.Clear();
            // kept the previous game's carriers (and their whole world) in memory
            CarrierStartDatas.Clear();
            HubTypes.Clear();
            CooperationTypes.Clear();
        }

        /*! reads the settings file if existing
         */

        /*! creates the Advertisement types
         */

        public static void CreateAdvertisementTypes()
        {
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Internet, "No Advertisement", 0, 0));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Internet, "Local", 5000, 1));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Internet, "National", 10000, 2));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Internet, "Global", 25000, 3));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Newspaper, "No Advertisement", 0, 0));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Newspaper, "Local", 5000, 1));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Newspaper, "National", 10000, 2));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Newspaper, "Global", 25000, 3));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Radio, "No Advertisement", 0, 0));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Radio, "Local", 5000, 1));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Radio, "National", 10000, 2));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Radio, "Global", 25000, 3));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Tv, "No Advertisement", 0, 0));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Tv, "Local", 5000, 1));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Tv, "National", 10000, 2));
            AdvertisementTypes.AddAdvertisementType(
                new AdvertisementType(AdvertisementType.CarrierAdvertisementType.Tv, "Global", 25000, 3));
        }

        private static void CreateCarrierLogos()
        {
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                string logoName = carrier.Profile.IATACode;
                if (carrier.Profile.LogoName.Length > 0)
                {
                    logoName = carrier.Profile.LogoName;
                }

                carrier.Profile.AddLogo(
                    File.Exists(AppSettings.GetDataPath() + "/graphics/carrierlogos/" + logoName + ".png")
                        ? new CarrierLogo(AppSettings.GetDataPath() + "/graphics/carrierlogos/" + logoName + ".png")
                        : new CarrierLogo(AppSettings.GetDataPath() + "/graphics/carrierlogos/default.png"));
            }
        }

        private static void CreateCarrierStartData(Carrier carrier, CarrierStartData startData)
        {
            StationFacility checkinFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);
            StationFacility cargoTerminal =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Cargo).Find(f => f.TypeLevel > 0);

            //  int difficultyFactor = GameObject.GetInstance().Difficulty.AILevel > 1 ? 2 : 1; //level easy

            int startDataFactor = Convert.ToInt16(GameObject.GetInstance().Difficulty.StartDataLevel);
            List<StartDataRoute> startroutes =
                startData.Routes.FindAll(
                    r =>
                    r.Opened <= GameObject.GetInstance().GameTime.Year
                    && r.Closed >= GameObject.GetInstance().GameTime.Year);

            Terminal.TerminalType terminaltype = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            //creates the routes
            List<StartDataRoute> sRoutes = startroutes.GetRange(0, startroutes.Count/startDataFactor);
            Parallel.ForEach(
                sRoutes,
                new ParallelOptions { MaxDegreeOfParallelism = 1 },
                startRoute =>
                    {
                        Station dest1 = Stations.GetStation(startRoute.Destination1);
                        Station dest2 = Stations.GetStation(startRoute.Destination2);

                        if (dest1 != null && dest2 != null)
                        {
                            if (dest1.GetStationFacility(carrier, StationFacility.FacilityType.Cargo).TypeLevel == 0
                                && dest1.GetStationFacility(null, StationFacility.FacilityType.Cargo).TypeLevel == 0
                                && carrier.CarrierRouteFocus == Route.RouteType.Cargo)
                            {
                                dest1.AddStationFacility(carrier, cargoTerminal, GameObject.GetInstance().GameTime);
                            }

                            if (dest2.GetStationFacility(carrier, StationFacility.FacilityType.Cargo).TypeLevel == 0
                                && dest2.GetStationFacility(null, StationFacility.FacilityType.Cargo).TypeLevel == 0
                                && carrier.CarrierRouteFocus == Route.RouteType.Cargo)
                            {
                                dest2.AddStationFacility(carrier, cargoTerminal, GameObject.GetInstance().GameTime);
                            }

                            if (!StationHelpers.HasFreeGates(dest1, carrier, terminaltype))
                            {
                                StationHelpers.RentGates(dest1, carrier, StationContract.ContractType.LowService, terminaltype);
                            }

                            if (!StationHelpers.HasFreeGates(dest2, carrier, terminaltype))
                            {
                                StationHelpers.RentGates(dest2, carrier, StationContract.ContractType.LowService, terminaltype);
                            }

                            Guid id = Guid.NewGuid();

                            Route route = null;

                            double price = PassengerHelpers.GetPassengerPrice(dest1, dest2);

                            if (startRoute.RouteType == Route.RouteType.Mixed
                                || startRoute.RouteType == Route.RouteType.Passenger)
                            {
                                route = new PassengerRoute(
                                    id.ToString(),
                                    dest1,
                                    dest2,
                                    GameObject.GetInstance().GameTime,
                                    price);
                            }

                            if (startRoute.RouteType == Route.RouteType.Cargo)
                            {
                                route = new CargoRoute(
                                    id.ToString(),
                                    dest1,
                                    dest2,
                                    GameObject.GetInstance().GameTime,
                                    PassengerHelpers.GetCargoPrice(dest1, dest2));
                            }

                            KeyValuePair<Coach, bool>? coach = null;
                            if (startRoute.Type != null)
                            {
                                double distance = MathHelpers.GetDistance(dest1, dest2);

                                if (startRoute.Type.Range > distance)
                                {
                                    coach =
                                        new KeyValuePair<Coach, bool>(
                                            Coaches.GetCoachesForSale(a => a.Type == startRoute.Type).FirstOrDefault(),
                                            true);

                                    if (coach.Value.Key == null)
                                    {
                                        id = Guid.NewGuid();
                                        var nCoach = new Coach(
                                            id.ToString(),
                                            startRoute.Type,
                                            carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier),
                                            GameObject.GetInstance().GameTime);
                                        Coaches.AddCoach(nCoach);

                                        nCoach.ClearCoachClasses();

                                        CoachHelpers.CreateCoachClasses(nCoach);

                                        coach = new KeyValuePair<Coach, bool>(nCoach, true);
                                    }
                                }
                            }

                            bool leaseCoach = carrier.Profile.PrimaryPurchasing
                                                    == CarrierProfile.PreferedPurchasing.Leasing;

                            if (coach == null)
                            {
                                coach = AIHelpers.GetCoachForRoute(
                                    carrier,
                                    dest2,
                                    dest1,
                                    leaseCoach,
                                    startRoute.RouteType,
                                    true);

                                if (coach == null
                                    && carrier.Profile.PrimaryPurchasing == CarrierProfile.PreferedPurchasing.Random)
                                {
                                    AIHelpers.GetCoachForRoute(
                                        carrier,
                                        dest2,
                                        dest1,
                                        true,
                                        startRoute.RouteType,
                                        true);
                                }
                            }

                            if (coach != null)
                            {
                                FleetCoach fCoach = CarrierHelpers.AddCoach(
                                    carrier,
                                    coach.Value.Key,
                                    carrier.Stations[0],
                                    leaseCoach);
                                fCoach.AddRoute(route);
                                fCoach.Status = FleetCoach.CoachStatus.ToRouteStart;
                                CarrierHelpers.HireCoachDrivers(fCoach);

                                if (route != null)
                                {
                                    route.LastUpdated = GameObject.GetInstance().GameTime;

                                    if (startRoute.RouteType == Route.RouteType.Mixed
                                        || startRoute.RouteType == Route.RouteType.Passenger)
                                    {
                                        CoachHelpers.CreateCoachClasses(fCoach.Coach);

                                        RouteClassesConfiguration configuration =
                                            AIHelpers.GetRouteConfiguration((PassengerRoute) route);

                                        foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                                        {
                                            ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type).FarePrice =
                                                price*GeneralHelpers.ClassToPriceFactor(classConfiguration.Type);

                                            foreach (RouteFacility rFacility in classConfiguration.GetFacilities())
                                            {
                                                ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type)
                                                                        .AddFacility(rFacility);
                                            }
                                        }

                                        AIHelpers.CreateRouteTimeTable(route, fCoach);
                                    }
                                    if (startRoute.RouteType == Route.RouteType.Cargo)
                                    {
                                        AIHelpers.CreateCargoRouteTimeTable(route, fCoach);
                                    }
                                }
                            }
                            carrier.AddRoute(route);
                        }
                    });
            //adds the coaches
            Parallel.ForEach(
                startData.Coaches,
                new ParallelOptions { MaxDegreeOfParallelism = 1 },
                coaches =>
                    {
                        CoachType type = CoachTypes.GetType(coaches.Type); //B747-200

                        const int totalSpan = 2010 - 1960;
                        int yearSpan = GameObject.GetInstance().GameTime.Year - 1960;
                        double valueSpan = Convert.ToDouble(coaches.CoachesLate - coaches.CoachesEarly);
                        double span = valueSpan/Convert.ToDouble(totalSpan);

                        int numbers = Math.Max(1, Convert.ToInt16(span*yearSpan)/startDataFactor);

                        if (type == null)
                        {
                            string tCarrier = carrier.Profile.Name;
                            string typeNull = coaches.Type;

                            Console.WriteLine(tCarrier + @" " + typeNull);
                        }
                        if (type != null && type.Produced.From <= GameObject.GetInstance().GameTime)
                        {
                            for (int i = 0; i < Math.Max(numbers, coaches.CoachesEarly); i++)
                            {
                                Guid id = Guid.NewGuid();

                                int countryNumber = Rnd.Next(Countries.GetCountries().Count() - 1);
                                Country country = Countries.GetCountries()[countryNumber];

                                int builtYear = Rnd.Next(
                                    type.Produced.From.Year,
                                    Math.Min(GameObject.GetInstance().GameTime.Year - 1, type.Produced.To.Year));

                                var coach = new Coach(
                                    id.ToString(),
                                    type,
                                    country.TailNumbers.GetNextTailNumber(),
                                    new DateTime(builtYear, 1, 1));

                                int age = MathHelpers.CalculateAge(coach.BuiltDate, GameObject.GetInstance().GameTime);

                                long kmPerYear = Rnd.Next(100000, 1000000);
                                long km = kmPerYear*age;

                                coach.Flown = km;

                                coach.EngineType = EngineTypes.GetStandardEngineType(coach.Type, coach.BuiltDate.Year);

                                Coaches.AddCoach(coach);

                                bool leaseCoach = carrier.Profile.PrimaryPurchasing
                                                        == CarrierProfile.PreferedPurchasing.Leasing;

                                FleetCoach fCoach = CarrierHelpers.AddCoach(
                                    carrier,
                                    coach,
                                    carrier.Stations[0],
                                    leaseCoach);
                                fCoach.Status = FleetCoach.CoachStatus.Stopped;
                                CarrierHelpers.HireCoachDrivers(fCoach);

                                CoachHelpers.CreateCoachClasses(fCoach.Coach);
                            }
                        }
                    });

            //the origin routes
            Parallel.ForEach(
                startData.OriginRoutes,
                new ParallelOptions { MaxDegreeOfParallelism = 1 },
                routes =>
                    {
                        Station origin = Stations.GetStation(routes.Origin);

                        if (origin != null)
                        {
                            Terminal.TerminalType terminalType = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

                            for (int i = 0;
                                 i < Math.Min(routes.Destinations/startDataFactor, origin.Terminals.GetFreeGates(terminalType));
                                 i++)
                            {
                                //if (origin.getStationFacility(carrier, StationFacility.FacilityType.CheckIn).TypeLevel == 0)
                                //origin.addStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);

                                if (!StationHelpers.HasFreeGates(origin, carrier, terminalType))
                                {
                                    StationHelpers.RentGates(origin, carrier, StationContract.ContractType.LowService, terminalType);
                                }

                                Station destination = GetStartDataRoutesDestination(routes);

                                //if (destination.getStationFacility(carrier, StationFacility.FacilityType.CheckIn).TypeLevel == 0)
                                //destination.addStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);

                                if (!StationHelpers.HasFreeGates(destination, carrier, terminalType))
                                {
                                    StationHelpers.RentGates(destination, carrier, StationContract.ContractType.LowService, terminalType);
                                }

                                Guid id = Guid.NewGuid();

                                Route route = null;

                                double price = PassengerHelpers.GetPassengerPrice(origin, destination);

                                if (routes.RouteType == Route.RouteType.Mixed
                                    || routes.RouteType == Route.RouteType.Passenger)
                                {
                                    route = new PassengerRoute(
                                        id.ToString(),
                                        origin,
                                        destination,
                                        GameObject.GetInstance().GameTime,
                                        price);
                                }

                                if (routes.RouteType == Route.RouteType.Cargo)
                                {
                                    route = new CargoRoute(
                                        id.ToString(),
                                        origin,
                                        destination,
                                        GameObject.GetInstance().GameTime,
                                        PassengerHelpers.GetCargoPrice(origin, destination));
                                }

                                bool leaseCoach = carrier.Profile.PrimaryPurchasing
                                                        == CarrierProfile.PreferedPurchasing.Leasing;

                                KeyValuePair<Coach, bool>? coach = AIHelpers.GetCoachForRoute(
                                    carrier,
                                    origin,
                                    destination,
                                    leaseCoach,
                                    routes.RouteType,
                                    true);

                                if (coach == null
                                    && carrier.Profile.PrimaryPurchasing == CarrierProfile.PreferedPurchasing.Random)
                                {
                                    coach = AIHelpers.GetCoachForRoute(
                                        carrier,
                                        origin,
                                        destination,
                                        true,
                                        routes.RouteType,
                                        true);
                                }

                                double distance = MathHelpers.GetDistance(origin, destination);

                                if (coach != null)
                                {
                                    FleetCoach fCoach = CarrierHelpers.AddCoach(
                                        carrier,
                                        coach.Value.Key,
                                        carrier.Stations[0],
                                        leaseCoach);
                                    fCoach.AddRoute(route);
                                    fCoach.Status = FleetCoach.CoachStatus.ToRouteStart;
                                    CarrierHelpers.HireCoachDrivers(fCoach);

                                    if (route != null)
                                    {
                                        route.LastUpdated = GameObject.GetInstance().GameTime;

                                        if (routes.RouteType == Route.RouteType.Passenger
                                            || routes.RouteType == Route.RouteType.Mixed)
                                        {
                                            CoachHelpers.CreateCoachClasses(fCoach.Coach);

                                            RouteClassesConfiguration configuration =
                                                AIHelpers.GetRouteConfiguration((PassengerRoute) route);

                                            foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                                            {
                                                ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type).FarePrice
                                                    = price*GeneralHelpers.ClassToPriceFactor(classConfiguration.Type);

                                                foreach (RouteFacility rFacility in classConfiguration.GetFacilities())
                                                {
                                                    ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type)
                                                                            .AddFacility(rFacility);
                                                }
                                            }

                                            AIHelpers.CreateRouteTimeTable(route, fCoach);
                                        }
                                        if (routes.RouteType == Route.RouteType.Cargo)
                                        {
                                            AIHelpers.CreateCargoRouteTimeTable(route, fCoach);
                                        }

                                        carrier.AddRoute(route);
                                    }
                                }
                            }
                        }
                    });
        }

        private static void CreateComputerRoutes(Carrier carrier)
        {
            bool leaseCoach = carrier.Profile.PrimaryPurchasing == CarrierProfile.PreferedPurchasing.Leasing;

            Station stationHomeBase = FindComputerHomeBase(carrier);

            StationFacility serviceFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Service).Find(f => f.TypeLevel == 1);
            StationFacility checkinFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);
            StationFacility cargoTerminal =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Cargo).Find(f => f.TypeLevel > 0);

            stationHomeBase.AddStationFacility(carrier, serviceFacility, GameObject.GetInstance().GameTime);
            stationHomeBase.AddStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);

            if (carrier.CarrierRouteFocus == Route.RouteType.Cargo)
            {
                stationHomeBase.AddStationFacility(carrier, cargoTerminal, GameObject.GetInstance().GameTime);
            }

            CarrierStartData startData = CarrierStartDatas.GetCarrierStartData(carrier);

            //creates the start data for an carrier
            if (startData != null)
            {
                StationHelpers.RentGates(stationHomeBase, carrier, StationContract.ContractType.Full);

                CreateCarrierStartData(carrier, startData);
            }
            else
            {
                List<Station> stationDestinations = AIHelpers.GetDestinationStations(carrier, stationHomeBase);

                if (stationDestinations.Count == 0)
                {
                    stationDestinations =
                        Stations.GetStations(
                            a =>
                            a.Profile.Country.Region == stationHomeBase.Profile.Country.Region
                            && a != stationHomeBase);
                }

                KeyValuePair<Coach, bool>? coach = null;
                Station stationDestination = null;

                int counter = 0;

                while ((stationDestination == null || coach == null || !coach.HasValue)
                       && stationDestinations.Count > counter)
                {
                    stationDestination = stationDestinations[counter];

                    coach = AIHelpers.GetCoachForRoute(
                        carrier,
                        stationHomeBase,
                        stationDestination,
                        leaseCoach,
                        carrier.CarrierRouteFocus,
                        true);

                    counter++;
                }

                if (stationDestination == null || !coach.HasValue)
                {
                    CreateComputerRoutes(carrier);
                }
                else
                {
                    StationHelpers.AddCarrierContract(
                        new StationContract(
                            carrier,
                            stationHomeBase,
                            StationContract.ContractType.Full,
                            carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger,
                            GameObject.GetInstance().GameTime,
                            2,
                            25,
                            0,
                            true));

                    StationHelpers.RentGates(stationDestination, carrier, StationContract.ContractType.LowService);
                    //stationDestination.addStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);

                    Guid id = Guid.NewGuid();

                    double price = PassengerHelpers.GetPassengerPrice(stationDestination, carrier.Stations[0]);

                    Route route = null;
                    if (carrier.CarrierRouteFocus == Route.RouteType.Passenger)
                    {
                        route = new PassengerRoute(
                            id.ToString(),
                            stationDestination,
                            carrier.Stations[0],
                            GameObject.GetInstance().GameTime,
                            price);

                        RouteClassesConfiguration configuration = AIHelpers.GetRouteConfiguration((PassengerRoute) route);

                        foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                        {
                            ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type).FarePrice = price
                                                                                                                *GeneralHelpers
                                                                                                                     .ClassToPriceFactor
                                                                                                                     (
                                                                                                                         classConfiguration
                                                                                                                             .Type);

                            foreach (RouteFacility rFacility in classConfiguration.GetFacilities())
                            {
                                ((PassengerRoute) route).GetRouteCoachClass(classConfiguration.Type)
                                                        .AddFacility(rFacility);
                            }
                        }
                    }
                    if (carrier.CarrierRouteFocus == Route.RouteType.Helicopter)
                    {
                        route = new HelicopterRoute(
                            id.ToString(),
                            stationDestination,
                            carrier.Stations[0],
                            GameObject.GetInstance().GameTime,
                            price);

                        RouteClassesConfiguration configuration = AIHelpers.GetRouteConfiguration((HelicopterRoute) route);

                        foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                        {
                            ((HelicopterRoute) route).GetRouteCoachClass(classConfiguration.Type).FarePrice = price
                                                                                                                 *GeneralHelpers
                                                                                                                      .ClassToPriceFactor
                                                                                                                      (
                                                                                                                          classConfiguration
                                                                                                                              .Type);

                            foreach (RouteFacility rFacility in classConfiguration.GetFacilities())
                            {
                                ((HelicopterRoute) route).GetRouteCoachClass(classConfiguration.Type)
                                                         .AddFacility(rFacility);
                            }
                        }
                    }
                    if (carrier.CarrierRouteFocus == Route.RouteType.Cargo)
                    {
                        route = new CargoRoute(
                            id.ToString(),
                            stationDestination,
                            carrier.Stations[0],
                            GameObject.GetInstance().GameTime,
                            PassengerHelpers.GetCargoPrice(stationDestination, carrier.Stations[0]));

                        stationDestination.AddStationFacility(carrier, cargoTerminal, GameObject.GetInstance().GameTime);
                    }
                    if (leaseCoach)
                    {
                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Rents,
                            -coach.Value.Key.LeasingPrice*2);
                    }
                    else
                    {
                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Purchases,
                            -coach.Value.Key.GetPrice());
                    }

                    var fCoach =
                        new FleetCoach(
                            leaseCoach ? FleetCoach.PurchasedType.Leased : FleetCoach.PurchasedType.Bought,
                            GameObject.GetInstance().GameTime,
                            carrier,
                            coach.Value.Key,
                            stationHomeBase) {Status = FleetCoach.CoachStatus.ToRouteStart};
                    fCoach.AddRoute(route);
                    CoachHelpers.CreateCoachClasses(fCoach.Coach);
                    CarrierHelpers.HireCoachDrivers(fCoach);

                    carrier.AddCoach(fCoach);

                    carrier.AddRoute(route);
                    if (route != null)
                    {
                        route.LastUpdated = GameObject.GetInstance().GameTime;

                        if (route.Type == Route.RouteType.Passenger || route.Type == Route.RouteType.Mixed)
                        {
                            AIHelpers.CreateRouteTimeTable(route, fCoach);
                        }
                        if (route.Type == Route.RouteType.Cargo)
                        {
                            AIHelpers.CreateCargoRouteTimeTable(route, fCoach);
                        }
                    }
                }
            }
        }

        /*! creates the time zones.
         */

        /*!creates the continents
         */

        private static void CreateContinents()
        {
            var africa = new Continent("101", "Africa");
            africa.AddRegion(Regions.GetRegion("101"));
            africa.AddRegion(Regions.GetRegion("102"));
            africa.AddRegion(Regions.GetRegion("103"));
            africa.AddRegion(Regions.GetRegion("104"));
            africa.AddRegion(Regions.GetRegion("105"));
            Continents.AddContinent(africa);

            var asia = new Continent("102", "Asia");
            asia.AddRegion(Regions.GetRegion("106"));
            asia.AddRegion(Regions.GetRegion("107"));
            asia.AddRegion(Regions.GetRegion("108"));
            asia.AddRegion(Regions.GetRegion("109"));
            asia.AddRegion(Regions.GetRegion("110"));
            Continents.AddContinent(asia);

            var australia = new Continent("103", "Australia and Oceania");
            australia.AddRegion(Regions.GetRegion("111"));
            australia.AddRegion(Regions.GetRegion("112"));
            Continents.AddContinent(australia);

            var europe = new Continent("104", "Europe");
            europe.AddRegion(Regions.GetRegion("113"));
            europe.AddRegion(Regions.GetRegion("114"));
            europe.AddRegion(Regions.GetRegion("115"));
            europe.AddRegion(Regions.GetRegion("116"));
            Continents.AddContinent(europe);

            var northAmerica = new Continent("105", "North America");
            northAmerica.AddRegion(Regions.GetRegion("117"));
            northAmerica.AddRegion(Regions.GetRegion("118"));
            northAmerica.AddRegion(Regions.GetRegion("119"));
            Continents.AddContinent(northAmerica);

            var southAmerica = new Continent("106", "South America");
            southAmerica.AddRegion(Regions.GetRegion("120"));
            Continents.AddContinent(southAmerica);
        }

        private static void CreateFeeTypes()
        {
            //wages
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Wage, "Cab Wage", 4.11, 3.75, 12.75, 100));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Wage, "Maintenance Wage", 3.95, 3.0, 4.25, 100));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Wage, "Support Wage", 2.65, 1, 3, 100));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Wage, "Cabin Wage", 1.9, 1, 4, 100));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Wage, "Instructor Base Salary", 267.00, 200, 300, 100));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Wage, "Driver Base Salary", 80.0, 60, 95, 100));

            //food and drinks
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.FoodDrinks, "Alcholic Drinks", 0.75, 0.5, 1.1, 75));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.FoodDrinks, "Drinks", 0.2, 0.1, 0.8, 75));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.FoodDrinks, "Snacks", 0.35, 0.25, 0.5, 70));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.FoodDrinks, "Meal", 1.40, 1.25, 2, 50));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.FoodDrinks, "WiFi", 1.4, 0, 6.25, 25, 2007));

            //fees
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Fee, "1 Bag", 0, 0, 5, 95));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Fee, "2 Bags", 0, 0, 5.25, 25));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Fee, "3+ Bags", 0, 0, 6, 2));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Fee, "Pets", 0, 0, 18, 1));

            //discounts
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Discount, "Employee Discount", 0, 0, 100, 1));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Discount, "Government Discount", 0, 0, 100, 3));
            FeeTypes.AddType(new FeeType(FeeType.EFeeType.Discount, "Military Discount", 0, 0, 100, 1));
        }

        private static void CreateTripFacilities()
        {
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "100",
                    RouteFacility.FacilityType.Food,
                    "None",
                    -50,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "101",
                    RouteFacility.FacilityType.Food,
                    "Buyable Snacks",
                    10,
                    RouteFacility.ExpenseType.Random,
                    0.10,
                    FeeTypes.GetType("Snacks")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "102",
                    RouteFacility.FacilityType.Food,
                    "Free Snacks",
                    15,
                    RouteFacility.ExpenseType.Fixed,
                    0.10,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "103",
                    RouteFacility.FacilityType.Food,
                    "Buyable Meal",
                    25,
                    RouteFacility.ExpenseType.Random,
                    0.50,
                    FeeTypes.GetType("Meal")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "104",
                    RouteFacility.FacilityType.Food,
                    "Basic Meal",
                    50,
                    RouteFacility.ExpenseType.Fixed,
                    0.50,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "105",
                    RouteFacility.FacilityType.Food,
                    "Full Dinner",
                    100,
                    RouteFacility.ExpenseType.Fixed,
                    2,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "106",
                    RouteFacility.FacilityType.Drinks,
                    "None",
                    -50,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "107",
                    RouteFacility.FacilityType.Drinks,
                    "Buyable",
                    25,
                    RouteFacility.ExpenseType.Random,
                    0.10,
                    FeeTypes.GetType("Drinks")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "108",
                    RouteFacility.FacilityType.Drinks,
                    "Free",
                    80,
                    RouteFacility.ExpenseType.Fixed,
                    0.20,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "109",
                    RouteFacility.FacilityType.AlcoholicDrinks,
                    "None",
                    0,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "110",
                    RouteFacility.FacilityType.AlcoholicDrinks,
                    "Buyable",
                    40,
                    RouteFacility.ExpenseType.Random,
                    0.05,
                    FeeTypes.GetType("Alcholic Drinks")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "111",
                    RouteFacility.FacilityType.AlcoholicDrinks,
                    "Free",
                    100,
                    RouteFacility.ExpenseType.Fixed,
                    0.75,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "112",
                    RouteFacility.FacilityType.WiFi,
                    "None",
                    0,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "113",
                    RouteFacility.FacilityType.WiFi,
                    "Buyable",
                    40,
                    RouteFacility.ExpenseType.Random,
                    0.5,
                    FeeTypes.GetType("WiFi"),
                    CarrierFacilities.GetFacility("107")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "114",
                    RouteFacility.FacilityType.WiFi,
                    "Free",
                    100,
                    RouteFacility.ExpenseType.Fixed,
                    0.5,
                    null,
                    CarrierFacilities.GetFacility("107")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "115",
                    RouteFacility.FacilityType.Magazines,
                    "None",
                    0,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "116",
                    RouteFacility.FacilityType.Magazines,
                    "Available",
                    40,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null,
                    CarrierFacilities.GetFacility("101")));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "117",
                    RouteFacility.FacilityType.Newspapers,
                    "None",
                    0,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null));
            RouteFacilities.AddFacility(
                new RouteFacility(
                    "118",
                    RouteFacility.FacilityType.Newspapers,
                    "Available",
                    35,
                    RouteFacility.ExpenseType.Fixed,
                    0,
                    null,
                    CarrierFacilities.GetFacility("102")));
        }

        private static void CreateHubTypes()
        {
            HubTypes.AddHubType(new HubType("Hub", 50000, HubType.TypeOfHub.Hub));
            HubTypes.AddHubType(new HubType("Regional hub", 40000, HubType.TypeOfHub.RegionalHub));
            HubTypes.AddHubType(new HubType("Focus city", 25000, HubType.TypeOfHub.FocusCity));
            HubTypes.AddHubType(new HubType("Fortress hub", 75000, HubType.TypeOfHub.FortressHub));
        }

        private static void CreateDriverRatings()
        {
            var ratingA = new DriverRating("A", 70, 3);
            ratingA.AddCoach(TrainingCoachTypes.GetCoachType("Cessna 172"));
            ratingA.AddCoach(TrainingCoachTypes.GetCoachType("Beechcraft King Air 350"));
            DriverRatings.AddRating(ratingA);

            var ratingB = new DriverRating("B", 85, 4);
            ratingB.AddCoach(TrainingCoachTypes.GetCoachType("Cessna 172"));
            ratingB.AddCoach(TrainingCoachTypes.GetCoachType("Beechcraft King Air 350"));
            DriverRatings.AddRating(ratingB);

            var ratingC = new DriverRating("C", 95, 5);
            ratingC.AddCoach(TrainingCoachTypes.GetCoachType("Beechcraft King Air 350"));
            DriverRatings.AddRating(ratingC);

            var ratingD = new DriverRating("D", 120, 7);
            ratingD.AddCoach(TrainingCoachTypes.GetCoachType("Beechcraft King Air 350"));
            DriverRatings.AddRating(ratingD);

            var ratingE = new DriverRating("E", 150, 10);
            ratingE.AddCoach(TrainingCoachTypes.GetCoachType("Beechcraft King Air 350"));
            DriverRatings.AddRating(ratingE);
        }

        private static void CreateDrivers()
        {
            int driversPool = 100*Carriers.GetAllCarriers().Count;

            GeneralHelpers.CreateDrivers(driversPool);

            int instructorsPool = 75*Carriers.GetAllCarriers().Count;

            GeneralHelpers.CreateInstructors(instructorsPool);
        }

        private static void CreateTimeZones()
        {
            TimeZones.AddTimeZone(new GameTimeZone("Baker Island Time", "BIT", new TimeSpan(-12, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Samoa Standard Time", "SST", new TimeSpan(-11, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Hawaiian Standard Time", "HAST", new TimeSpan(-10, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Alaskan Standard Time", "MIT", new TimeSpan(-9, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Pacific Standard Time", "PST", new TimeSpan(-8, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Mountain Standard Time", "MST", new TimeSpan(-7, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Central Standard Time", "CST", new TimeSpan(-6, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Eastern Standard Time", "EST", new TimeSpan(-5, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Venezuelan Standard Time", "VET", new TimeSpan(-4, -30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Atlantic Standard Time", "AST", new TimeSpan(-4, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Newfoundland Standard Time", "NST", new TimeSpan(-3, -30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("South America Standard Time", "SAST", new TimeSpan(-3, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Mid-Atlantic Standard Time", "MAST", new TimeSpan(-2, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Cape Verde Time", "CVT", new TimeSpan(-1, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Greenwich Mean Time", "GMT", new TimeSpan(0, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Central European Time", "CET", new TimeSpan(1, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Eastern European Time", "EET", new TimeSpan(2, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("East Africa Time", "EAT", new TimeSpan(3, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Iran Standard Time", "IRST", new TimeSpan(3, 30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Gulf Standard Time", "GST", new TimeSpan(4, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Afghanistan Time", "AFT", new TimeSpan(4, 30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("West Asia Standard Time", "WAST", new TimeSpan(5, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Indian Standard Time", "IST", new TimeSpan(5, 30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Nepal Standard Time", "NPT", new TimeSpan(5, 45, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Asia Standard Time", "ASST", new TimeSpan(6, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Central Asia Standard Time", "CEST", new TimeSpan(7, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("China Standard Time", "CST", new TimeSpan(8, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Japan Standard Time", "JST", new TimeSpan(9, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Australia Standard Time", "AST", new TimeSpan(9, 30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Australian Eastern Standard Time", "AEST", new TimeSpan(10, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Australian Central Standard Time", "ACST", new TimeSpan(10, 30, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Central Pacific Standard Time", "CPST", new TimeSpan(11, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("New Zealand Standard Time", "NZST", new TimeSpan(12, 0, 0)));
            TimeZones.AddTimeZone(new GameTimeZone("Tonga Standard Time", "TST", new TimeSpan(13, 0, 0)));
        }

        private static void CreateTrainingCoachTypes()
        {
            TrainingCoachTypes.AddCoachType(new TrainingCoachType("Cessna 172", 26705, 2, 1));
            TrainingCoachTypes.AddCoachType(new TrainingCoachType("Beechcraft King Air 350", 129520, 12, 2));
        }

        private static Station FindComputerHomeBase(Carrier carrier)
        {
            Terminal.TerminalType terminaltype = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            if (carrier.Profile.PreferedStation != null
                && GeneralHelpers.IsStationActive(carrier.Profile.PreferedStation)
                && carrier.Profile.PreferedStation.Terminals.GetFreeGates(terminaltype) > 1)
            {
                return carrier.Profile.PreferedStation;
            }
            List<Station> stations =
                Stations.GetStations(carrier.Profile.Country).FindAll(a => a.Terminals.GetFreeGates(terminaltype) > 1);

            if (stations.Count < 4)
            {
                stations =
                    Stations.GetStations(carrier.Profile.Country.Region).FindAll(a => a.Terminals.GetFreeGates(terminaltype) > 1);
            }

            var list = new Dictionary<Station, int>();
            stations.ForEach(
                a => list.Add(a, ((int) a.Profile.Size)*(StationHelpers.GetStationsNearStation(a, 1000).Count) + 1));

            return AIHelpers.GetRandomItem(list);
        }

        private static Station GetStartDataRoutesDestination(StartDataRoutes routes)
        {
            double maxRange =
                (CoachTypes.GetTypes(
                    t =>
                    t.Produced.From <= GameObject.GetInstance().GameTime
                    && t.Produced.To > GameObject.GetInstance().GameTime).Max(t => t.Range))*0.8;

            var stations = new List<Station>();

            if (routes.RouteType == Route.RouteType.Cargo)
            {
                stations =
                    Stations.GetStations(
                        a =>
                        routes.Countries.Contains(a.Profile.Country)
                        && MathHelpers.GetDistance(Stations.GetStation(routes.Origin), a) < maxRange
                        && a != Stations.GetStation(routes.Origin)
                        && ((int) a.Profile.Cargo) >= ((int) routes.MinimumSize) && a.Terminals.GetFreeGates(Terminal.TerminalType.Cargo) > 0);
            }

            if (routes.RouteType == Route.RouteType.Passenger || routes.RouteType == Route.RouteType.Mixed)
            {
                stations =
                    Stations.GetStations(
                        a =>
                        routes.Countries.Contains(a.Profile.Country)
                        && MathHelpers.GetDistance(Stations.GetStation(routes.Origin), a) < maxRange
                        && a != Stations.GetStation(routes.Origin)
                        && ((int) a.Profile.Size) >= ((int) routes.MinimumSize) && a.Terminals.GetFreeGates(Terminal.TerminalType.Passenger) > 0);
            }

            return stations[Rnd.Next(stations.Count)];
        }

        /*!loads the different scenarios
         */

        /*! loads the carrier facilities.
         */

        private static void LoadCarrierFacilities()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/carrierfacilities.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList facilitiesList = root?.SelectNodes("//carrierfacility");

            if (facilitiesList != null)
                foreach (XmlElement element in facilitiesList)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    double price = XmlConvert.ToDouble(element.Attributes["price"].Value);
                    double monthlycost = XmlConvert.ToDouble(element.Attributes["monthlycost"].Value);
                    int fromyear = XmlConvert.ToInt16(element.Attributes["fromyear"].Value);

                    var levelElement = (XmlElement) element.SelectSingleNode("level");
                    if (levelElement != null)
                    {
                        int service = Convert.ToInt32(levelElement.Attributes["service"].Value);
                        int luxury = Convert.ToInt32(levelElement.Attributes["luxury"].Value);

                        CarrierFacilities.AddFacility(
                            new CarrierFacility(section, uid, price, monthlycost, fromyear, service, luxury));
                    }

                    if (element.SelectSingleNode("translations") != null)
                    {
                        Translator.GetInstance()
                            .AddTranslation(
                                root.Name,
                                element.Attributes["uid"].Value,
                                element.SelectSingleNode("translations"));
                    }
                }
        }

        private static void LoadCarrierMergers()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/addons/carriers/mergers/mergers.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList mergersList = root?.SelectNodes("//merger");

            if (mergersList != null)
                foreach (XmlElement element in mergersList)
                {
                    string mergerName = element.Attributes["name"].Value;
                    Carrier carrier1 = Carriers.GetCarrier(element.Attributes["carrier1"].Value);
                    Carrier carrier2 = Carriers.GetCarrier(element.Attributes["carrier2"].Value);
                    var mergerType =
                        (CarrierMerger.MergerType)
                            DataParsing.ParseEnum(typeof (CarrierMerger.MergerType), element.Attributes["type"].Value);
                    DateTime mergerDate = DataParsing.ParseDate(element.Attributes["date"].Value, new CultureInfo("en-US", false));

                    var merger = new CarrierMerger(mergerName, carrier1, carrier2, mergerDate, mergerType);

                    if (element.HasAttribute("newname"))
                    {
                        merger.NewName = element.Attributes["newname"].Value;
                    }

                    CarrierMergers.AddCarrierMerger(merger);
                }
        }

        private static readonly System.Collections.Generic.HashSet<string> MissingFacilities = new System.Collections.Generic.HashSet<string>();

        // some configuration files point at a facility id that does not exist for that type (for example Video="100",
        // which is an audio facility); use the basic facility of the type instead
        private static CoachFacility GetFacilityOrBasic(CoachFacility.FacilityType type, string uid)
        {
            var facility = CoachFacilities.GetFacility(type, uid);
            if (facility != null)
            {
                return facility;
            }

            if (MissingFacilities.Add(type + uid))
            {
                StartupLog.Write("Data note: no " + type + " facility with id " + uid + ", using the basic " + type + " facility");
            }
            return CoachFacilities.GetBasicFacility(type);
        }

        private static CoachConfiguration LoadCoachConfiguration(XmlElement element)
        {
            string name = element.Attributes["name"].Value;
            string id = element.Attributes["id"].Value;
            int minimumSeats = Convert.ToInt16(element.Attributes["minimumseats"].Value);

            XmlNodeList classesList = element.SelectNodes("classes/class");

            var configuration = new CoachConfiguration(name, minimumSeats, true) {ID = id};

            if (classesList != null)
                foreach (XmlElement classElement in classesList)
                {
                    int seating = Convert.ToInt16(classElement.Attributes["seating"].Value);
                    int regularseating = Convert.ToInt16(classElement.Attributes["regularseating"].Value);
                    var classType =
                        (CoachClass.ClassType)
                        DataParsing.ParseEnum(typeof (CoachClass.ClassType), classElement.Attributes["type"].Value);

                    var classConf = new CoachClassConfiguration(classType, seating, regularseating);
                    foreach (CoachFacility.FacilityType facType in Enum.GetValues(typeof (CoachFacility.FacilityType)))
                    {
                        string facUid = DataParsing.AttributeValue(classElement, facType.ToString());
                        if (facUid == null)
                        {
                            LogError("Missing coach facility " + facType + " in a class of " + element.Attributes["name"]?.Value);
                            continue;
                        }

                        classConf.AddFacility(GetFacilityOrBasic(facType, facUid));
                    }

                    configuration.AddClassConfiguration(classConf);
                }

            return configuration;
        }

        public static void LoadCoachFacilities()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/coachfacilities.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList facilitiesList = root?.SelectNodes("//coachfacility");
            if (facilitiesList != null)
                foreach (XmlElement element in facilitiesList)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    var type =
                        (CoachFacility.FacilityType)
                            DataParsing.ParseEnum(typeof (CoachFacility.FacilityType), element.Attributes["type"].Value);
                    int fromyear = Convert.ToInt16(element.Attributes["fromyear"].Value);

                    var levelElement = (XmlElement) element.SelectSingleNode("level");
                    if (levelElement != null)
                    {
                        int service = Convert.ToInt32(levelElement.Attributes["service"].Value);

                        var seatsElement = (XmlElement) element.SelectSingleNode("seats");
                        if (seatsElement != null)
                        {
                            double seatsPercent = XmlConvert.ToDouble(seatsElement.Attributes["percent"].Value);
                            double seatsPrice = XmlConvert.ToDouble(seatsElement.Attributes["price"].Value);
                            double seatuse = XmlConvert.ToDouble(seatsElement.Attributes["uses"].Value);

                            CoachFacilities.AddFacility(
                                new CoachFacility(section, uid, type, fromyear, service, seatsPercent, seatsPrice, seatuse));
                        }
                    }

                    if (element.SelectSingleNode("translations") != null)
                    {
                        Translator.GetInstance()
                            .AddTranslation(
                                root.Name,
                                element.Attributes["uid"].Value,
                                element.SelectSingleNode("translations"));
                    }
                }
        }

        private static void LoadCoachImages()
        {
            string file = AppSettings.GetDataPath() + "/graphics/coachimages/images.xml";

            var doc = new XmlDocument();
            doc.Load(file);
            XmlElement root = doc.DocumentElement;

            XmlNodeList imagesList = root?.SelectNodes("//coachimage");

            if (imagesList != null)
                foreach (XmlElement image in imagesList)
                {
                    string imageFile =
                        $"{AppSettings.GetDataPath()}/graphics/coachimages/{image.Attributes["image"].Value}.png";

                    string[] types = image.Attributes["types"].Value.Split(',');

                    foreach (string type in types)
                    {
                        CoachType coachType = CoachTypes.GetType(type);

                        if (coachType != null)
                        {
                            coachType.Image = imageFile;
                        }
                    }
                }
        }

        /*! loads the regions.
         */

        private static void LoadCoachTypeConfiguration(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            XmlElement root = doc.DocumentElement;

            XmlNodeList configurationsList = root?.SelectNodes("//configuration");

            if (configurationsList != null)
                foreach (XmlElement element in configurationsList)
                {
                    string name = element.Attributes["name"].Value;
                    string id = element.Attributes["id"].Value;
                    CoachType type = CoachTypes.GetType(element.Attributes["coach"].Value);
                    int fromYear = Convert.ToInt16(element.Attributes["yearfrom"].Value);
                    int toYear = Convert.ToInt16(element.Attributes["yearto"].Value);
                    /*coach="Boeing 747-400" name="Boeing 747-400 (1998)" yearfrom="1998" yearto="2199" id="301"*/

                    XmlNodeList classesList = element.SelectNodes("classes/class");

                    var configuration = new CoachTypeConfiguration(
                        name,
                        type,
                        new Period<DateTime>(new DateTime(fromYear, 1, 1), new DateTime(toYear, 12, 31)),
                        true) {ID = id};

                    if (classesList != null)
                        foreach (XmlElement classElement in classesList)
                        {
                            int seating = Convert.ToInt16(classElement.Attributes["seating"].Value);
                            var classType =
                                (CoachClass.ClassType)
                                    DataParsing.ParseEnum(typeof (CoachClass.ClassType), classElement.Attributes["type"].Value);

                            var classConf = new CoachClassConfiguration(classType, seating, seating);
                            foreach (
                                CoachFacility.FacilityType facType in Enum.GetValues(typeof (CoachFacility.FacilityType)))
                            {
                                string facUid = DataParsing.AttributeValue(classElement, facType.ToString());
                        if (facUid == null)
                        {
                            LogError("Missing coach facility " + facType + " in a class of " + element.Attributes["name"]?.Value);
                            continue;
                        }

                                classConf.AddFacility(GetFacilityOrBasic(facType, facUid));
                            }

                            configuration.AddClassConfiguration(classConf);
                        }

                    Configurations.AddConfiguration(configuration);
                }
        }

        private static void LoadCoachTypeConfigurations()
        {
            try
            {
                var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/coaches/configurations");
                if (!dir.Exists) { StartupLog.Write("Data note: no configurations folder; that data is skipped."); return; }

                foreach (FileInfo file in dir.GetFiles("*.xml"))
                {
                    LoadCoachTypeConfiguration(file.FullName);
                }
            }
            catch (Exception e)
            {
                LogError("Error loading Coach configurations", e);
            }
        }

        /*!loads the engine types
         */

        /*!loads the coaches.
         */

        private static void LoadCoaches()
        {
            try
            {
                var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/coaches");
                if (!dir.Exists) { StartupLog.Write("Data note: no coaches folder; that data is skipped."); return; }

                foreach (FileInfo file in dir.GetFiles("*.xml"))
                {
                    LoadCoaches(file.FullName);
                }
            }
            catch (Exception e)
            {
                LogError("Error loading coaches", e);
            }
        }

        private static void LoadCoaches(string file)
        {
            string dir = AppSettings.GetDataPath() + "/graphics/coachimages/";
            string id = "";
            try
            {
                var doc = new XmlDocument();
                doc.Load(file);
                XmlElement root = doc.DocumentElement;

                XmlNodeList coachesList = root?.SelectNodes("//coach");

                if (coachesList != null)
                    foreach (XmlElement coach in coachesList)
                    {
                        CoachType.TypeOfCoach coachType = coach.HasAttribute("type")
                            ? (CoachType.TypeOfCoach)
                                DataParsing.ParseEnum(typeof (CoachType.TypeOfCoach), coach.Attributes["type"].Value)
                            : CoachType.TypeOfCoach.Passenger;

                        string manufacturerName = coach.Attributes["manufacturer"].Value;
                        Manufacturer manufacturer = Manufacturers.GetManufacturer(manufacturerName);

                        string name = coach.Attributes["name"].Value;

                        string family;

                        if (coach.HasAttribute("family"))
                        {
                            family = coach.Attributes["family"].Value;
                        }
                        else
                        {
                            family = name.LastIndexOfAny(new[] {' ', '-'}) > 0 ? name.Substring(0, name.LastIndexOfAny(new[] {' ', '-'})) : name;
                        }

                        bool isConvertable;

                        if (coach.HasAttribute("convertable"))
                        {
                            isConvertable = Convert.ToBoolean(coach.Attributes["convertable"].Value);
                        }
                        else
                        {
                            if (coachType == CoachType.TypeOfCoach.Cargo
                                || coachType == CoachType.TypeOfCoach.Mixed
                                || coachType == CoachType.TypeOfCoach.Helicopter)
                            {
                                isConvertable = false;
                            }
                            else
                            {
                                isConvertable = true;
                            }
                        }

                        long price = Convert.ToInt64(coach.Attributes["price"].Value);

                        id = name;

                        var typeElement = (XmlElement) coach.SelectSingleNode("type");
                        var body =
                            (CoachType.BodyType)
                                DataParsing.ParseEnum(typeof (CoachType.BodyType), typeElement.Attributes["body"].Value);


                        var rangeType =
                            (CoachType.TypeRange)
                                DataParsing.ParseEnum(typeof (CoachType.TypeRange), typeElement.Attributes["rangetype"].Value);
                        var engine =
                            (CoachType.TypeOfEngine)
                                DataParsing.ParseEnum(typeof (CoachType.TypeOfEngine), typeElement.Attributes["engine"].Value);

                        var specsElement = (XmlElement) coach.SelectSingleNode("specs");
                        double wingspan = XmlConvert.ToDouble(specsElement.Attributes["wingspan"].Value);
                        double length = XmlConvert.ToDouble(specsElement.Attributes["length"].Value);
                        long range = Convert.ToInt64(specsElement.Attributes["range"].Value);

                        double speed = XmlConvert.ToDouble(specsElement.Attributes["speed"].Value);
                        long fuelcapacity = XmlConvert.ToInt64(specsElement.Attributes["fuelcapacity"].Value);
                        double fuel = XmlConvert.ToDouble(specsElement.Attributes["consumption"].Value);
                        long runwaylenght = XmlConvert.ToInt64(specsElement.Attributes["runwaylengthrequired"].Value);

                        double weight = specsElement.HasAttribute("weight") ? Convert.ToDouble(specsElement.Attributes["weight"].Value) : CoachHelpers.GetCalculatedWeight(wingspan, length, fuelcapacity);

                        var capacityElement = (XmlElement) coach.SelectSingleNode("capacity");

                        var producedElement = (XmlElement) coach.SelectSingleNode("produced");
                        int fromYear = Convert.ToInt16(producedElement.Attributes["from"].Value);
                        int toYear = Convert.ToInt16(producedElement.Attributes["to"].Value);
                        int prodRate = producedElement.HasAttribute("rate")
                            ? Convert.ToInt16(producedElement.Attributes["rate"].Value)
                            : 10;

                        var from = new DateTime(fromYear, 1, 2);
                        var to = new DateTime(toYear, 12, 31);

                        CoachType type = null;

                        if (coachType == CoachType.TypeOfCoach.Helicopter)
                        {
                            int passengers = Convert.ToInt16(capacityElement.Attributes["passengers"].Value);
                            int drivers = Convert.ToInt16(capacityElement.Attributes["drivers"].Value);
                            int attendants = Convert.ToInt16(capacityElement.Attributes["attendants"].Value);
                            int maxClasses = Convert.ToInt16(capacityElement.Attributes["maxclasses"].Value);
                            type = new CoachPassengerType(
                                manufacturer,
                                name,
                                family,
                                passengers,
                                drivers,
                                attendants,
                                speed,
                                range,
                                wingspan,
                                length,
                                weight,
                                fuel,
                                price,
                                maxClasses,
                                runwaylenght,
                                fuelcapacity,
                                body,
                                rangeType,
                                engine,
                                new Period<DateTime>(@from, to),
                                prodRate,
                                isConvertable) {TypeCoach = CoachType.TypeOfCoach.Helicopter};
                        }

                        if (coachType == CoachType.TypeOfCoach.Passenger)
                        {
                            int passengers = Convert.ToInt16(capacityElement.Attributes["passengers"].Value);
                            int drivers = Convert.ToInt16(capacityElement.Attributes["drivers"].Value);
                            int attendants = Convert.ToInt16(capacityElement.Attributes["attendants"].Value);
                            int maxClasses = Convert.ToInt16(capacityElement.Attributes["maxclasses"].Value);
                            type = new CoachPassengerType(
                                manufacturer,
                                name,
                                family,
                                passengers,
                                drivers,
                                attendants,
                                speed,
                                range,
                                wingspan,
                                length,
                                weight,
                                fuel,
                                price,
                                maxClasses,
                                runwaylenght,
                                fuelcapacity,
                                body,
                                rangeType,
                                engine,
                                new Period<DateTime>(@from, to),
                                prodRate,
                                isConvertable);
                        }
                        if (coachType == CoachType.TypeOfCoach.Cargo)
                        {
                            int drivers = Convert.ToInt16(capacityElement.Attributes["drivers"].Value);
                            double cargo = Convert.ToDouble(
                                capacityElement.Attributes["cargo"].Value,
                                CultureInfo.GetCultureInfo("en-US").NumberFormat);
                            type = new CoachCargoType(
                                manufacturer,
                                name,
                                family,
                                drivers,
                                cargo,
                                speed,
                                range,
                                wingspan,
                                length,
                                weight,
                                fuel,
                                price,
                                runwaylenght,
                                fuelcapacity,
                                body,
                                rangeType,
                                engine,
                                new Period<DateTime>(@from, to),
                                prodRate,
                                isConvertable);
                        }
                        if (coachType == CoachType.TypeOfCoach.Mixed)
                        {
                            int passengers = Convert.ToInt16(capacityElement.Attributes["passengers"].Value);
                            int drivers = Convert.ToInt16(capacityElement.Attributes["drivers"].Value);
                            double cargo = Convert.ToDouble(
                                capacityElement.Attributes["cargo"].Value,
                                CultureInfo.GetCultureInfo("en-US").NumberFormat);
                            int attendants = Convert.ToInt16(capacityElement.Attributes["attendants"].Value);
                            int maxClasses = Convert.ToInt16(capacityElement.Attributes["maxclasses"].Value);

                            type = new CoachCombiType(
                                manufacturer,
                                name,
                                family,
                                passengers,
                                drivers,
                                attendants,
                                speed,
                                range,
                                wingspan,
                                length,
                                weight,
                                fuel,
                                price,
                                maxClasses,
                                runwaylenght,
                                fuelcapacity,
                                body,
                                rangeType,
                                engine,
                                new Period<DateTime>(@from, to),
                                prodRate,
                                cargo,
                                isConvertable);
                        }

                        //if (coach.HasAttribute("image") && coach.Attributes["image"].Value.Length > 1)
                        //type.Image = dir + coach.Attributes["image"].Value + ".png";

                        if (type != null)
                        {
                            CoachTypes.AddType(type);
                        }
                    }
            }
            catch (Exception e)
            {
                LogError("Error loading coach {0}", id);
                LogError(e);
            }
        }

        private static void LoadCarriers()
        {
            string f = "";
            try
            {
                var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/carriers");
                if (!dir.Exists) { StartupLog.Write("Data note: no carriers folder; that data is skipped."); return; }

                foreach (FileInfo file in dir.GetFiles("*.xml"))
                {
                    f = file.Name;
                    LoadCarrier(file.FullName);
                }
                GameObject.GetInstance().SetHumanCarrier(Carriers.GetAllCarriers()[0]);
                GameObject.GetInstance().MainCarrier = GameObject.GetInstance().HumanCarrier;

                CreateCarrierLogos();
            }
            catch (Exception e)
            {
                //var file = new StreamWriter(
                //    AppSettings.GetCommonApplicationDataPath() + "/thecarrierstartup.log",
                //    true);
                //file.WriteLine("Carriers failing: " + f);
                //file.WriteLine(e.ToString());
                //file.WriteLine(e.StackTrace);
                //file.Close();
                LogError("Error loading carrier: {0}", f);
                LogError(e);
            }
        }

        private static void LoadStationFacilities()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/stationfacilities.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList facilitiesList = root?.SelectNodes("//stationfacility");

            if (facilitiesList != null)
                foreach (XmlElement element in facilitiesList)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    string shortname = element.Attributes["shortname"].Value;
                    var type =
                        (StationFacility.FacilityType)
                            DataParsing.ParseEnum(typeof (StationFacility.FacilityType), element.Attributes["type"].Value);
                    int typeLevel = Convert.ToInt16(element.Attributes["typelevel"].Value);

                    double price = XmlConvert.ToDouble(element.Attributes["price"].Value);
                    int buildingDays = XmlConvert.ToInt32(element.Attributes["buildingdays"].Value);

                    var levelElement = (XmlElement) element.SelectSingleNode("level");
                    if (levelElement != null)
                    {
                        int service = Convert.ToInt32(levelElement.Attributes["service"].Value);
                        int luxury = Convert.ToInt32(levelElement.Attributes["luxury"].Value);

                        var facility = new StationFacility(
                            section,
                            uid,
                            shortname,
                            type,
                            buildingDays,
                            typeLevel,
                            price,
                            service,
                            luxury);

                        StationFacilities.AddFacility(facility);

                        var employeesElement = (XmlElement) element.SelectSingleNode("employees");

                        if (employeesElement != null)
                        {
                            var employeestype =
                                (StationFacility.EmployeeTypes)
                                    DataParsing.ParseEnum(typeof (StationFacility.EmployeeTypes), employeesElement.Attributes["type"].Value);
                            int numberofemployees = Convert.ToInt16(employeesElement.Attributes["numberofemployees"].Value);

                            facility.EmployeeType = employeestype;
                            facility.NumberOfEmployees = numberofemployees;
                        }
                    }

                    if (element.SelectSingleNode("translations") != null)
                    {
                        Translator.GetInstance()
                            .AddTranslation(
                                root.Name,
                                element.Attributes["uid"].Value,
                                element.SelectSingleNode("translations"));
                    }
                }
        }

        private static void LoadStationLogos()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/graphics/stationlogos");
            if (!dir.Exists) { StartupLog.Write("Data note: no stationlogos folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.png"))
            {
                string code = file.Name.Split('.')[0].ToUpper();
                Station station = Stations.GetStation(code);

                if (station != null)
                {
                    station.Profile.Logo = file.FullName;
                }
                else
                {
                    Console.WriteLine(@"The logo {0} doesn't match any stations", code);
                }
            }
        }

        private static void LoadStationMaps()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/graphics/stationmaps");
            if (!dir.Exists) { StartupLog.Write("Data note: no stationmaps folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.png"))
            {
                string code = file.Name.Split('.')[0].ToUpper();
                Station station = Stations.GetStation(code);

                if (station != null)
                {
                    station.Profile.Map = file.FullName;
                }
                else
                {
                    code = "x";
                }
            }
        }

        /*!loads the coach images
         */

        private static void LoadStations(string filename)
        {
            string id = "";
            try
            {
                var doc = new XmlDocument();
                doc.Load(filename);
                XmlElement root = doc.DocumentElement;

                XmlNodeList stationsList = root?.SelectNodes("//station");

                if (stationsList != null)
                    foreach (XmlElement stationElement in stationsList)
                    {
                        string name = stationElement.Attributes["name"].Value;
                        string icao = stationElement.Attributes["icao"].Value;
                        string iata = stationElement.Attributes["iata"].Value;

                        id = name + " iata: " + iata;

                        var type =
                            (StationProfile.StationType)
                                DataParsing.ParseEnum(typeof (StationProfile.StationType), stationElement.Attributes["type"].Value);
                        var season =
                            (Weather.Season) DataParsing.ParseEnum(typeof (Weather.Season), stationElement.Attributes["season"].Value);

                        var periodElement = (XmlElement) stationElement.SelectSingleNode("period");

                        Period<DateTime> stationPeriod;
                        if (periodElement != null)
                        {
                            DateTime stationFrom = DataParsing.ParseDate(
                                periodElement.Attributes["from"].Value,
                                new CultureInfo("en-US", false));
                            DateTime stationTo = DataParsing.ParseDate(
                                periodElement.Attributes["to"].Value,
                                new CultureInfo("en-US", false));

                            stationPeriod = new Period<DateTime>(stationFrom, stationTo);
                        }
                        else
                        {
                            stationPeriod = new Period<DateTime>(new DateTime(1959, 12, 31), new DateTime(2199, 12, 31));
                        }

                        var townElement = (XmlElement) stationElement.SelectSingleNode("town");
                        string town = townElement.Attributes["town"].Value;
                        string country = townElement.Attributes["country"].Value;
                        TimeSpan gmt = TimeSpan.Parse(townElement.Attributes["GMT"].Value);
                        TimeSpan dst = TimeSpan.Parse(townElement.Attributes["DST"].Value);

                        var latitudeElement = (XmlElement) stationElement.SelectSingleNode("coordinates/latitude");
                        var longitudeElement = (XmlElement) stationElement.SelectSingleNode("coordinates/longitude");
                        string[] latitude = latitudeElement.Attributes["value"].Value.Split(
                            new[] {'°', '\''},
                            StringSplitOptions.RemoveEmptyEntries);
                        string[] longitude =
                            longitudeElement.Attributes["value"].Value.Split(
                                new[] {'°', '\''},
                                StringSplitOptions.RemoveEmptyEntries);
                        var coords = new int[6];

                        //latitude
                        coords[0] = DataParsing.ParseInt(latitude[0]);
                        coords[1] = DataParsing.ParseInt(latitude[1]);
                        coords[2] = DataParsing.ParseInt(latitude[2]);

                        if (latitude[3] == "S")
                        {
                            coords[0] = -coords[0];
                        }

                        //longitude
                        coords[3] = DataParsing.ParseInt(longitude[0]);
                        coords[4] = DataParsing.ParseInt(longitude[1]);
                        coords[5] = DataParsing.ParseInt(longitude[2]);

                        if (longitude[3] == "W")
                        {
                            coords[3] = -coords[3];
                        }

                        /*
                    foreach(string l in latitude ) 
                    {
                        //int.TryParse(l, out coords[c]);
                        coords[c] = int.Parse(l);
                        c++;
                    }
                    c = 3;
					
                    foreach (string l in longitude)
                    {
                        //int.TryParse(l, out coords[c]);
                        coords[c] = int.Parse(l);
					
                        c++;
                    }*/

                        //cleaning up

                        //GeoCoordinate pos = new GeoCoordinate(MathHelpers.DMStoDeg(coords[0], coords[1], coords[2]),MathHelpers.DMStoDeg(coords[3],coords[4],coords[5]));
                        var pos = new Coordinates(
                            new Coordinate(coords[0], coords[1], coords[2]),
                            new Coordinate(coords[3], coords[4], coords[5]));

                        //double longitude = Coordinate.Parse(longitudeElement.Attributes["value"].Value);

                        var sizeElement = (XmlElement) stationElement.SelectSingleNode("size");

                        var paxValues = new List<PaxValue>();

                        if (!sizeElement.HasChildNodes)
                        {
                            var size =
                                (GeneralHelpers.Size)
                                    DataParsing.ParseEnum(typeof (GeneralHelpers.Size), sizeElement.Attributes["value"].Value);
                            int pax = sizeElement.HasAttribute("pax")
                                ? Convert.ToInt32(sizeElement.Attributes["pax"].Value)
                                : 0;

                            paxValues.Add(new PaxValue(stationPeriod.From.Year, stationPeriod.To.Year, size, pax));
                        }
                        else
                        {
                            XmlNodeList yearsList = sizeElement.SelectNodes("yearvalues/yearvalue");

                            foreach (XmlElement yearElement in yearsList)
                            {
                                int fromYear = Convert.ToInt16(yearElement.Attributes["from"].Value);
                                int toYear = Convert.ToInt16(yearElement.Attributes["to"].Value);
                                var size =
                                    (GeneralHelpers.Size)
                                        DataParsing.ParseEnum(typeof (GeneralHelpers.Size), yearElement.Attributes["value"].Value);
                                int pax = Convert.ToInt32(yearElement.Attributes["pax"].Value);

                                var paxValue = new PaxValue(fromYear, toYear, size, pax);

                                if (yearElement.HasAttribute("inflationafter"))
                                {
                                    paxValue.InflationAfterYear =
                                        Convert.ToDouble(
                                            yearElement.Attributes["inflationafter"].Value,
                                            CultureInfo.GetCultureInfo("en-US").NumberFormat);
                                }
                                if (yearElement.HasAttribute("inflationbefore"))
                                {
                                    paxValue.InflationBeforeYear =
                                        Convert.ToDouble(
                                            yearElement.Attributes["inflationbefore"].Value,
                                            CultureInfo.GetCultureInfo("en-US").NumberFormat);
                                }

                                paxValues.Add(paxValue);
                            }
                        }

                        GeneralHelpers.Size cargoSize;
                        double cargovolume = sizeElement.HasAttribute("cargovolume")
                            ? Convert.ToDouble(
                                sizeElement.Attributes["cargovolume"].Value,
                                CultureInfo.GetCultureInfo("en-US").NumberFormat)
                            : 0;

                        if (sizeElement.HasAttribute("cargo"))
                        {
                            cargoSize =
                                (GeneralHelpers.Size)
                                    DataParsing.ParseEnum(typeof (GeneralHelpers.Size), sizeElement.Attributes["cargo"].Value);
                        }
                        else
                        {
                            //calculates the cargo size
                            var cargoSizes = (GeneralHelpers.Size[]) Enum.GetValues(typeof (GeneralHelpers.Size));

                            int i = 0;

                            var list = new Dictionary<GeneralHelpers.Size, int>();

                            while (i < cargoSizes.Length && cargoSizes[i] <= paxValues.First().Size)
                            {
                                list.Add(cargoSizes[i], 10 - i);
                                i++;
                            }

                            cargoSize = AIHelpers.GetRandomItem(list);
                        }

                        Town eTown;
                        if (town.Contains(","))
                        {
                            State state = States.GetState(Countries.GetCountry(country), town.Split(',')[1].Trim());

                            eTown = state == null ? new Town(town.Split(',')[0], Countries.GetCountry(country)) : new Town(town.Split(',')[0], Countries.GetCountry(country), state);
                        }
                        else
                        {
                            eTown = new Town(town, Countries.GetCountry(country));
                        }

                        var profile = new StationProfile(
                            name,
                            iata,
                            icao,
                            type,
                            stationPeriod,
                            eTown,
                            gmt,
                            dst,
                            pos,
                            cargoSize,
                            cargovolume,
                            season) {PaxValues = paxValues};

                        var station = new Station(profile);

                        var destinationsElement = (XmlElement) stationElement.SelectSingleNode("destinations");

                        if (destinationsElement != null)
                        {
                            XmlNodeList majorDestinationsList = destinationsElement.SelectNodes("destination");

                            var majorDestinations = new Dictionary<string, int>();

                            foreach (XmlElement majorDestinationNode in majorDestinationsList)
                            {
                                string majorDestination = majorDestinationNode.Attributes["station"].Value;
                                int majorDestinationPax = Convert.ToInt32(majorDestinationNode.Attributes["pax"].Value);

                                majorDestinations.Add(majorDestination, majorDestinationPax);
                            }

                            station.Profile.MajorDestionations = majorDestinations;
                        }

                        XmlNodeList terminalList = stationElement.SelectNodes("terminals/terminal");

                        foreach (XmlElement terminalNode in terminalList)
                        {
                            string terminalName = terminalNode.Attributes["name"].Value;
                            int terminalGates = XmlConvert.ToInt32(terminalNode.Attributes["gates"].Value);

                            Terminal.TerminalType terminalType;

                            if (terminalNode.HasAttribute("type"))
                                terminalType =
                                    (Terminal.TerminalType)
                                        DataParsing.ParseEnum(typeof (Terminal.TerminalType), terminalNode.Attributes["type"].Value);
                            else
                                terminalType = Terminal.TerminalType.Passenger;

                            station.Terminals.AddTerminal(
                                new Terminal(station, null, terminalName, terminalGates, new DateTime(1950, 1, 1), terminalType));
                        }

                        XmlNodeList runwaysList = stationElement.SelectNodes("runways/runway");

                        foreach (XmlElement runwayNode in runwaysList)
                        {
                            string runwayName = runwayNode.Attributes["name"].Value;
                            long runwayLength = XmlConvert.ToInt32(runwayNode.Attributes["length"].Value);
                            var surface =
                                (Runway.SurfaceType)
                                    DataParsing.ParseEnum(typeof (Runway.SurfaceType), runwayNode.Attributes["surface"].Value);

                            var runwayType = Runway.RunwayType.Regular;

                            if (runwayNode.HasAttribute("type"))
                            {
                                runwayType =
                                    (Runway.RunwayType)
                                        DataParsing.ParseEnum(typeof (Runway.RunwayType), runwayNode.Attributes["type"].Value);
                            }

                            station.Runways.Add(
                                new Runway(runwayName, runwayLength, runwayType, surface, new DateTime(1900, 1, 1), true));
                        }

                        XmlNodeList expansionsList = stationElement.SelectNodes("expansions/expansion");

                        foreach (XmlElement expansionNode in expansionsList)
                        {
                            var expansionType =
                                (StationExpansion.ExpansionType)
                                    DataParsing.ParseEnum(typeof (StationExpansion.ExpansionType), expansionNode.Attributes["type"].Value);

                            DateTime expansionDate = DataParsing.ParseDate(
                                expansionNode.Attributes["date"].Value,
                                new CultureInfo("en-US", false));

                            bool expansionNotify = Convert.ToBoolean(expansionNode.Attributes["notify"].Value);

                            var expansion = new StationExpansion(expansionType, expansionDate, expansionNotify);

                            if (expansionType == StationExpansion.ExpansionType.Name)
                            {
                                string expansionName = expansionNode.Attributes["name"].Value;

                                expansion.Name = expansionName;
                            }
                            if (expansionType == StationExpansion.ExpansionType.RunwayLength)
                            {
                                string expansionName = expansionNode.Attributes["name"].Value;
                                long length = Convert.ToInt64(expansionNode.Attributes["length"].Value);

                                expansion.Name = expansionName;
                                expansion.Length = length;
                            }
                            if (expansionType == StationExpansion.ExpansionType.NewRunway)
                            {
                                string expansionName = expansionNode.Attributes["name"].Value;
                                long length = Convert.ToInt64(expansionNode.Attributes["length"].Value);

                                var surface =
                                    (Runway.SurfaceType)
                                        DataParsing.ParseEnum(typeof (Runway.SurfaceType), expansionNode.Attributes["surface"].Value);

                                expansion.Name = expansionName;
                                expansion.Length = length;
                                expansion.Surface = surface;
                            }

                            if (expansionType == StationExpansion.ExpansionType.NewTerminal)
                            {
                                string expansionName = expansionNode.Attributes["name"].Value;
                                int gates = Convert.ToInt16(expansionNode.Attributes["gates"].Value);

                                Terminal.TerminalType terminalType;

                                if (expansionNode.HasAttribute("terminaltype"))
                                    terminalType =
                                        (Terminal.TerminalType)
                                            DataParsing.ParseEnum(typeof (Terminal.TerminalType), expansionNode.Attributes["terminaltype"].Value);
                                else
                                    terminalType = Terminal.TerminalType.Passenger;

                                expansion.Name = expansionName;
                                expansion.Gates = gates;
                                expansion.TerminalType = terminalType;
                            }
                            if (expansionType == StationExpansion.ExpansionType.ExtraGates)
                            {
                                string expansionName = expansionNode.Attributes["name"].Value;
                                int gates = Convert.ToInt16(expansionNode.Attributes["gates"].Value);

                                expansion.Name = expansionName;
                                expansion.Gates = gates;
                            }
                            if (expansionType == StationExpansion.ExpansionType.CloseTerminal)
                            {
                                string expansionName = expansionNode.Attributes["name"].Value;
                                int gates = Convert.ToInt16(expansionNode.Attributes["gates"].Value);

                                expansion.Name = expansionName;
                                expansion.Gates = gates;
                            }
                            station.Profile.AddExpansion(expansion);
                        }

                        //30.06.14: Added for loading of landing fees

                        double landingFee = stationElement.HasAttribute("landingfee") ? Convert.ToDouble(stationElement.Attributes["landingfee"].Value, new CultureInfo("en-US", false)) : StationHelpers.GetStandardLandingFee(station);

                        station.LandingFee = landingFee;

                        // an station that is defined twice (same code, same ICAO and the same period) is only added once;
                        // an station that replaced an older one with the same code (other period) is kept
                        bool alreadyLoaded = Stations.GetStation(
                            a => a.Profile.ID == station.Profile.ID
                                 || (a.Profile.IATACode == station.Profile.IATACode
                                     && a.Profile.ICAOCode == station.Profile.ICAOCode
                                     && a.Profile.Period.From == station.Profile.Period.From
                                     && a.Profile.Period.To == station.Profile.Period.To)) != null;

                        if (!alreadyLoaded)
                        {
                            Stations.AddStation(station);
                        }
                    }
            }
            catch (Exception e)
            {
                /*
                System.IO.StreamWriter file = new System.IO.StreamWriter(AppSettings.getCommonApplicationDataPath() + "/thecarrierstartup.log", true);
                file.WriteLine("Station failing: " + id);
                file.WriteLine(e.ToString());
                file.WriteLine(e.StackTrace);
                file.Close();
                 * */

                LogError("Station {0} failing to load", id);
                LogError(e);
            }
        }

        private static void LoadAlliance(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            XmlElement root = doc.DocumentElement;

            try
            {
                if (root != null)
                {
                    string allianceName = root.Attributes["name"].Value;
                    string logo = AppSettings.GetDataPath() + "/graphics/alliancelogos/" + root.Attributes["logo"].Value
                                  + ".png";
                    DateTime formationDate = DataParsing.ParseDate(
                        root.Attributes["formation"].Value,
                        new CultureInfo("en-US", false));

                    Station headquarter = Stations.GetStation(root.Attributes["headquarter"].Value);

                    var alliance = new Alliance(formationDate, allianceName, headquarter) {Logo = logo};

                    XmlNodeList membersList = root.SelectNodes("members/member");

                    if (membersList != null)
                        foreach (XmlElement memberNode in membersList)
                        {
                            Carrier memberCarrier = Carriers.GetCarrier(memberNode.Attributes["carrier"].Value);
                            DateTime joinedDate = DataParsing.ParseDate(
                                memberNode.Attributes["joined"].Value,
                                new CultureInfo("en-US", false));

                            var member = new AllianceMember(memberCarrier, joinedDate);
                            if (memberNode.HasAttribute("exited"))
                            {
                                member.ExitedDate = DataParsing.ParseDate(
                                    memberNode.Attributes["exited"].Value,
                                    new CultureInfo("en-US", false));
                            }

                            alliance.AddMember(member);
                        }

                    Alliances.AddAlliance(alliance);
                }
            }
            catch (Exception e)
            {
                /*
                System.IO.StreamWriter file = new System.IO.StreamWriter(AppSettings.getCommonApplicationDataPath() + "/thecarrierstartup.log", true);
                file.WriteLine("Alliance failing: " + path);
                file.WriteLine(e.ToString());
                file.WriteLine(e.StackTrace);
			   
                file.Close();
                 * */

                LogError("Alliance {0} failing to load", path);
                LogError(e);
            }
        }

        private static void LoadAlliances()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/alliances");
            if (!dir.Exists) { StartupLog.Write("Data note: no alliances folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                LoadAlliance(file.FullName);
            }
        }

        private static void LoadCooperations()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/carriercooperations.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList cooperationsList = root?.SelectNodes("//cooperation");

            if (cooperationsList != null)
                foreach (XmlElement element in cooperationsList)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    double price = Convert.ToDouble(
                        element.Attributes["price"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);
                    int fromyear = Convert.ToInt16(element.Attributes["fromyear"].Value);
                    double monthlyprice = Convert.ToDouble(
                        element.Attributes["monthlyprice"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);
                    int servicelevel = Convert.ToInt16(element.Attributes["servicelevel"].Value);
                    double incomeperpax = Convert.ToDouble(
                        element.Attributes["incomeperpax"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);
                    var minsize =
                        (GeneralHelpers.Size) DataParsing.ParseEnum(typeof (GeneralHelpers.Size), element.Attributes["minsize"].Value);

                    var type = new CooperationType(
                        section,
                        uid,
                        minsize,
                        fromyear,
                        price,
                        monthlyprice,
                        servicelevel,
                        incomeperpax);
                    CooperationTypes.AddCooperationType(type);
                    /*uid="101" price="250000" fromyear="1980" monthlyprice="10000" servicelevel="50" incomepercent="3"*/

                    if (element.SelectSingleNode("translations") != null)
                    {
                        Translator.GetInstance()
                            .AddTranslation(
                                root.Name,
                                element.Attributes["uid"].Value,
                                element.SelectSingleNode("translations"));
                    }
                }
        }

        private static void LoadCountries()
        {
            var territoryElements = new List<XmlElement>();

            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/countries.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList countriesList = root?.SelectNodes("//country");
            if (countriesList != null)
                foreach (XmlElement element in countriesList)
                {
                    var territoryElement = (XmlElement) element.SelectSingleNode("territoryof");

                    if (territoryElement != null)
                    {
                        territoryElements.Add(element);
                    }
                    else
                    {
                        string section = root.Name;
                        string uid = element.Attributes["uid"].Value;
                        string shortname = element.Attributes["shortname"].Value;
                        string flag = element.Attributes["flag"].Value;
                        Region region = Regions.GetRegion(element.Attributes["region"].Value);
                        string tailformat = element.Attributes["tailformat"].Value;

                        var country = new Country(section, uid, shortname, region, tailformat) {Flag = AppSettings.GetDataPath() + "/graphics/flags/" + flag + ".png"};

                        Countries.AddCountry(country);

                        XmlNodeList currenciesList = element.SelectNodes("currency");

                        if (currenciesList != null)
                            foreach (XmlElement currencyElement in currenciesList)
                            {
                                string currencySymbol = currencyElement.Attributes["symbol"].Value;
                                double currencyRate = Convert.ToDouble(
                                    currencyElement.Attributes["rate"].Value,
                                    CultureInfo.GetCultureInfo("en-US").NumberFormat);
                                var currencyPosition =
                                    (CountryCurrency.CurrencyPosition)
                                        DataParsing.ParseEnum(
                                            typeof (CountryCurrency.CurrencyPosition),
                                            currencyElement.Attributes["position"].Value);

                                var currencyFromDate = new DateTime(1900, 1, 1);
                                var currencyToDate = new DateTime(2199, 12, 31);

                                if (currencyElement.HasAttribute("from"))
                                {
                                    currencyFromDate = DataParsing.ParseDate(currencyElement.Attributes["from"].Value);
                                }

                                if (currencyElement.HasAttribute("to"))
                                {
                                    currencyToDate = DataParsing.ParseDate(currencyElement.Attributes["to"].Value);
                                }

                                country.AddCurrency(
                                    new Currency
                                    {
                                        Symbol = currencySymbol,
                                        Exchange = currencyRate,
                                        From = currencyFromDate,
                                        To = currencyToDate,
                                        SymbolOnRight = currencyPosition == CountryCurrency.CurrencyPosition.Right
                                    });
                            }

                        if (element.SelectSingleNode("translations") != null)
                        {
                            Translator.GetInstance()
                                .AddTranslation(
                                    root.Name,
                                    element.Attributes["uid"].Value,
                                    element.SelectSingleNode("translations"));
                        }
                    }
                }
            //reads all countries which is a territory for another
            foreach (XmlElement element in territoryElements)
            {
                if (root != null)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    string shortname = element.Attributes["shortname"].Value;
                    string flag = element.Attributes["flag"].Value;
                    Region region = Regions.GetRegion(element.Attributes["region"].Value);
                    string tailformat = element.Attributes["tailformat"].Value;

                    var territoryElement = (XmlElement) element.SelectSingleNode("territoryof");

                    if (territoryElement != null)
                    {
                        Country territoryOf = Countries.GetCountry(territoryElement.Attributes["uid"].Value);

                        Country country = new TerritoryCountry(section, uid, shortname, region, tailformat, territoryOf);

                        country.Flag = AppSettings.GetDataPath() + "/graphics/flags/" + flag + ".png";
                        Countries.AddCountry(country);
                    }
                }

                    if (element.SelectSingleNode("translations") != null)
                    {
                        if (root != null)
                            Translator.GetInstance()
                                      .AddTranslation(
                                          root.Name,
                                          element.Attributes["uid"].Value,
                                          element.SelectSingleNode("translations"));
                    }
            }
        }

        private static void LoadEngineTypes()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/engines");
            if (!dir.Exists) { StartupLog.Write("Data note: no engines folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                LoadEngineTypes(file.FullName);
            }
        }

        private static void LoadEngineTypes(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);

            XmlElement root = doc.DocumentElement;

            XmlNodeList enginesList = root?.SelectNodes("//engine");

            if (enginesList != null)
                foreach (XmlElement engineElement in enginesList)
                {
                    string manufacturerName = engineElement.Attributes["manufacturer"].Value;
                    Manufacturer manufacturer = Manufacturers.GetManufacturer(manufacturerName);

                    string name = engineElement.Attributes["model"].Value;

                    var specsElement = (XmlElement) engineElement.SelectSingleNode("specs");
                    if (specsElement != null)
                    {
                        var engineType =
                            (EngineType.TypeOfEngine)
                                DataParsing.ParseEnum(typeof (EngineType.TypeOfEngine), specsElement.Attributes["type"].Value);
                        var noiseLevel =
                            (EngineType.NoiseLevel)
                                DataParsing.ParseEnum(typeof (EngineType.NoiseLevel), specsElement.Attributes["noise"].Value);
                        double consumption = Convert.ToDouble(
                            specsElement.Attributes["consumptionModifier"].Value,
                            CultureInfo.GetCultureInfo("en-US").NumberFormat);
                        long price = Convert.ToInt64(specsElement.Attributes["price"].Value);

                        var perfElement = (XmlElement) engineElement.SelectSingleNode("performance");
                        if (perfElement != null)
                        {
                            int speed = Convert.ToInt32(perfElement.Attributes["maxspeed"].Value);
                            int ceiling = Convert.ToInt32(perfElement.Attributes["ceiling"].Value);
                            double runway = Convert.ToDouble(
                                perfElement.Attributes["runwaylengthrequiredModifier"].Value,
                                CultureInfo.GetCultureInfo("en-US").NumberFormat);
                            double range = Convert.ToDouble(
                                perfElement.Attributes["rangeModifier"].Value,
                                CultureInfo.GetCultureInfo("en-US").NumberFormat);

                            var producedElement = (XmlElement) engineElement.SelectSingleNode("produced");
                            if (producedElement != null)
                            {
                                int from = Convert.ToInt16(producedElement.Attributes["from"].Value);
                                int to = Convert.ToInt16(producedElement.Attributes["to"].Value);

                                var coachElement = (XmlElement) engineElement.SelectSingleNode("coach");
                                if (coachElement != null)
                                {
                                    string modelsElement = coachElement.Attributes["models"].Value;

                                    var engine = new EngineType(
                                        name,
                                        manufacturer,
                                        engineType,
                                        noiseLevel,
                                        consumption,
                                        price,
                                        speed,
                                        ceiling,
                                        runway,
                                        range,
                                        new Period<int>(@from, to));

                                    string[] models = modelsElement.Split(',');

                                    foreach (string model in models)
                                    {
                                        CoachType coachType = CoachTypes.GetAllTypes().FirstOrDefault(a => a.Name == model.Trim());

                                        if (coachType != null)
                                        {
                                            engine.AddCoachType(coachType);
                                        }
                                        else
                                            Console.WriteLine(@"Missing coach type for engine: " + model);
                                    }

                                    EngineTypes.AddEngineType(engine);
                                }
                            }
                        }
                    }
                }
        }

        private static void LoadTripRestrictions()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/triprestrictions.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList restrictionsList = root?.SelectNodes("//restriction");
            if (restrictionsList != null)
                foreach (XmlElement element in restrictionsList)
                {
                    var type =
                        (TripRestriction.RestrictionType)
                            DataParsing.ParseEnum(typeof (TripRestriction.RestrictionType), element.Attributes["type"].Value);

                    DateTime startDate = DataParsing.ParseDate(
                        element.Attributes["start"].Value,
                        new CultureInfo("en-US", false));
                    DateTime endDate = DataParsing.ParseDate(element.Attributes["end"].Value, new CultureInfo("en-US", false));

                    var countriesElement = (XmlElement) element.SelectSingleNode("countries");

                    BaseUnit from = null, to = null;

                    if (countriesElement != null && countriesElement.Attributes["fromtype"].Value == "C")
                    {
                        @from = Countries.GetCountry(countriesElement.Attributes["from"].Value);
                    }
                    else
                    {
                        if (countriesElement != null) @from = Unions.GetUnion(countriesElement.Attributes["from"].Value);
                    }

                    if (countriesElement != null && countriesElement.Attributes["totype"].Value == "C")
                    {
                        to = Countries.GetCountry(countriesElement.Attributes["to"].Value);
                    }
                    else
                    {
                        if (countriesElement != null) to = Unions.GetUnion(countriesElement.Attributes["to"].Value);
                    }

                    TripRestrictions.AddRestriction(new TripRestriction(type, startDate, endDate, @from, to));
                }
        }

        private static void LoadHistoricEvent(string filename)
        {
            var doc = new XmlDocument();
            doc.Load(filename);
            XmlElement root = doc.DocumentElement;

            if (root != null)
            {
                string name = root.Attributes["name"].Value;
                string text = root.Attributes["text"].Value;
                DateTime eventDate = DataParsing.ParseDate(root.Attributes["date"].Value, new CultureInfo("en-US", false));

                var historicEvent = new HistoricEvent(name, text, eventDate);

                XmlNodeList influencesList = root.SelectNodes("influences/influence");

                if (influencesList != null)
                    foreach (XmlElement influenceElement in influencesList)
                    {
                        var type =
                            (HistoricEventInfluence.InfluenceType)
                            DataParsing.ParseEnum(
                                typeof (HistoricEventInfluence.InfluenceType),
                                influenceElement.Attributes["type"].Value);
                        double value = Convert.ToDouble(
                            influenceElement.Attributes["value"].Value,
                            CultureInfo.GetCultureInfo("en-US").NumberFormat);
                        DateTime endDate = DataParsing.ParseDate(
                            influenceElement.Attributes["enddate"].Value,
                            new CultureInfo("en-US", false));

                        historicEvent.AddInfluence(new HistoricEventInfluence(type, value, endDate));
                    }

                HistoricEvents.AddHistoricEvent(historicEvent);
            }
        }

        private static void LoadHistoricEvents()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/historicevents");
            if (!dir.Exists) { StartupLog.Write("Data note: no historicevents folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                LoadHistoricEvent(file.FullName);
            }
        }

        private static void LoadHolidays()
        {
            string id = "";
            try
            {
                var doc = new XmlDocument();
                doc.Load(AppSettings.GetDataPath() + "/holidays.xml");
                XmlElement root = doc.DocumentElement;

                XmlNodeList holidaysList = root?.SelectNodes("//holiday");

                if (holidaysList != null)
                    foreach (XmlElement element in holidaysList)
                    {
                        string uid = element.Attributes["uid"].Value;
                        string name = element.Attributes["name"].Value;

                        id = name;

                        var type =
                            (Holiday.HolidayType) DataParsing.ParseEnum(typeof (Holiday.HolidayType), element.Attributes["type"].Value);
                        var traveltype =
                            (Holiday.TravelType)
                                DataParsing.ParseEnum(typeof (Holiday.TravelType), element.Attributes["holidaytype"].Value);

                        var countries = new List<Country>();

                        XmlNodeList countriesList = element.SelectNodes("observers/observer");

                        if (countriesList != null)
                            countries.AddRange(from XmlElement eCountry in countriesList select Countries.GetCountry(eCountry.Attributes["country"].Value));

                        var dateElement = (XmlElement) element.SelectSingleNode("observationdate");

                        if (type == Holiday.HolidayType.FixedDate)
                        {
                            if (dateElement != null)
                            {
                                int month = Convert.ToInt16(dateElement.Attributes["month"].Value);
                                int day = Convert.ToInt16(dateElement.Attributes["day"].Value);

                                var date = new DateTime(1900, month, day);

                                foreach (Country country in countries)
                                {
                                    var holiday = new Holiday(root.Name, uid, type, name, traveltype, country) {Date = date};

                                    Holidays.AddHoliday(holiday);
                                }
                            }
                        }
                        if (type == Holiday.HolidayType.FixedMonth)
                        {
                            if (dateElement != null)
                            {
                                int month = Convert.ToInt16(dateElement.Attributes["month"].Value);

                                foreach (Country country in countries)
                                {
                                    var holiday = new Holiday(root.Name, uid, type, name, traveltype, country) {Month = month};

                                    Holidays.AddHoliday(holiday);
                                }
                            }
                        }
                        if (type == Holiday.HolidayType.FixedWeek)
                        {
                            if (dateElement != null)
                            {
                                int week = Convert.ToInt16(dateElement.Attributes["week"].Value);

                                foreach (Country country in countries)
                                {
                                    var holiday = new Holiday(root.Name, uid, type, name, traveltype, country) {Week = week};

                                    Holidays.AddHoliday(holiday);
                                }
                            }
                        }
                        if (type == Holiday.HolidayType.NonFixedDate)
                        {
                            if (dateElement != null)
                            {
                                int month = Convert.ToInt16(dateElement.Attributes["month"].Value);
                                int week = Convert.ToInt16(dateElement.Attributes["week"].Value);
                                var day = (DayOfWeek) DataParsing.ParseEnum(typeof (DayOfWeek), dateElement.Attributes["dayofweek"].Value);

                                foreach (Country country in countries)
                                {
                                    var holiday = new Holiday(root.Name, uid, type, name, traveltype, country) {Month = month, Week = week, Day = day};

                                    Holidays.AddHoliday(holiday);
                                }
                            }
                        }

                        if (element.SelectSingleNode("translations") != null)
                        {
                            Translator.GetInstance()
                                .AddTranslation(
                                    root.Name,
                                    element.Attributes["uid"].Value,
                                    element.SelectSingleNode("translations"));
                        }
                    }
            }
            catch (Exception e)
            {
                LogError("Error loading holiday {0}", id);
                LogError(e);
            }
        }

        /*! loads the inflation years
         */

        public static void LoadInflationYears()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/inflations.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList inflationsList = root?.SelectNodes("//inflation");

            if (inflationsList != null)
                foreach (XmlElement element in inflationsList)
                {
                    int year = Convert.ToInt16(element.Attributes["year"].Value);
                    double fuelprice = Convert.ToDouble(
                        element.Attributes["fuelprice"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);
                    double inflation = Convert.ToDouble(
                        element.Attributes["inflation"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);
                    double modifier = Convert.ToDouble(
                        element.Attributes["pricemodifier"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);

                    Inflations.AddInflationYear(new Inflation(year, fuelprice, inflation, modifier));
                }
        }

        /*!loads the major destinations
         */

        private static void LoadMajorDestinations()
        {
            try
            {
                var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/stations/majordestinations");
                if (!dir.Exists) { StartupLog.Write("Data note: no majordestinations folder; that data is skipped."); return; }

                foreach (FileInfo file in dir.GetFiles("*.xml"))
                {
                    LoadMajorDestinations(file.FullName);
                }
            }
            catch (Exception e)
            {
                LogError("Error loading destinations", e);
            }

            foreach (Station station in Stations.GetAllStations())
            {
                int majorPax = station.Profile.MajorDestionations.Sum(d => d.Value);

                if (majorPax > 0)
                {
                    station.Profile.SetPaxValue(Math.Max(station.Profile.Pax, majorPax));
                }
            }
        }

        private static void LoadMajorDestinations(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            XmlElement root = doc.DocumentElement;

            if (root != null)
            {
                XmlNodeList stationsList = root.SelectNodes("//majordestination");
                string id = "";
                try
                {
                    if (stationsList != null)
                        foreach (XmlElement stationElement in stationsList)
                        {
                            Station station = Stations.GetStation(stationElement.Attributes["station"].Value);

                            if (station == null)
                            {
                                continue; // the station failed to load
                            }

                            id = station.Profile.IATACode;

                            XmlNodeList destinationsList = stationElement.SelectNodes("destinations/destination");

                            if (destinationsList != null)
                                foreach (XmlElement destinationElement in destinationsList)
                                {
                                    string destination = destinationElement.Attributes["station"].Value;
                                    int pax = Convert.ToInt32(destinationElement.Attributes["pax"].Value);

                                    station.AddMajorDestination(destination, pax);
                                }
                        }
                }
                catch (Exception e)
                {
                    LogError("Error loading destination {0}", id);
                    LogError(e);
                }
            }
        }

        private static void LoadManufacturerLogos()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/graphics/manufacturerlogos");
            if (!dir.Exists) { StartupLog.Write("Data note: no manufacturerlogos folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.png"))
            {
                string name = file.Name.Split('.')[0];
                Manufacturer manufacturer = Manufacturers.GetManufacturer(name);

                if (manufacturer != null)
                {
                    manufacturer.Logo = file.FullName;
                }
                else
                {
                    name = "x";
                }
            }
        }

        private static void LoadManufacturers()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/manufacturers.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList manufacturersList = root?.SelectNodes("//manufacturer");
            if (manufacturersList != null)
                foreach (XmlElement manufacturer in manufacturersList)
                {
                    string name = manufacturer.Attributes["name"].Value;
                    string shortname = manufacturer.Attributes["shortname"].Value;

                    Country country = Countries.GetCountry(manufacturer.Attributes["country"].Value);

                    bool isReal = !manufacturer.HasAttribute("isreal") || Convert.ToBoolean(manufacturer.Attributes["isreal"].Value);

                    Manufacturers.AddManufacturer(new Manufacturer(name, shortname, country, isReal));
                }
        }

        /*loads the special contracts*/

        public static void LoadSpecialContracts()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/contracts");
            if (!dir.Exists) { StartupLog.Write("Data note: no contracts folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                LoadSpecialContract(file.FullName);
            }
        }

        /*loads a special contract*/

        public static void LoadSpecialContract(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            XmlElement root = doc.DocumentElement;
            XmlNodeList contractsList = root?.SelectNodes("//contract");

            if (contractsList != null)
                foreach (XmlElement element in contractsList)
                {
                    string name = element.Attributes["name"].Value;
                    string text = element.Attributes["text"].Value;

                    var infoElement = (XmlElement) element.SelectSingleNode("information");

                    if (infoElement != null)
                    {
                        long payment = Convert.ToInt64(infoElement.Attributes["payment"].Value);
                        bool asbonus = Convert.ToBoolean(infoElement.Attributes["bonus"].Value);
                        long penalty = Convert.ToInt64(infoElement.Attributes["penalty"].Value);
                        bool isfixeddate = !infoElement.HasAttribute("frequency");

                        var scType = new SpecialContractType(name, text, payment, asbonus, penalty, isfixeddate);

                        if (isfixeddate)
                        {
                            DateTime fromdate = DataParsing.ParseDate(
                                infoElement.Attributes["from"].Value,
                                new CultureInfo("en-US", false));

                            DateTime todate = DataParsing.ParseDate(
                                infoElement.Attributes["to"].Value,
                                new CultureInfo("en-US", false));

                            scType.Period = new Period<DateTime>(fromdate, todate);
                        }
                        else
                        {
                            int frequency = Convert.ToInt32(infoElement.Attributes["frequency"].Value);

                            scType.Frequency = frequency;
                        }

                        XmlNodeList routesList = element.SelectNodes("routes/route");

                        if (routesList != null)
                            foreach (XmlElement routeElement in routesList)
                            {
                                // public SpecialContractRoute(Station destination1, Station destination2, long passengers,Boolean bothways)
                                Station departure = Stations.GetStation(routeElement.Attributes["departure"].Value);
                                Station destination = Stations.GetStation(routeElement.Attributes["destination"].Value);
                                bool bothways = Convert.ToBoolean(routeElement.Attributes["bothways"].Value);
                                long passengers = Convert.ToInt64(routeElement.Attributes["passengers"].Value);

                                var routetype = Route.RouteType.Passenger;

                                if (routeElement.HasAttribute("type"))
                                    routetype = (Route.RouteType)
                                        DataParsing.ParseEnum(typeof (Route.RouteType), routeElement.Attributes["type"].Value);

                                var scRoute = new SpecialContractRoute(departure, destination, passengers, routetype, bothways);
                                scType.Routes.Add(scRoute);
                            }

                        XmlNodeList parametersList = element.SelectNodes("parameters/parameter");

                        if (parametersList != null)
                            foreach (XmlElement parameterElement in parametersList)
                            {
                                if (parameterElement.HasAttribute("departure"))
                                {
                                    var parameter = new ContractRequirement(ContractRequirement.RequirementType.Destination);

                                    Station departure = Stations.GetStation(parameterElement.Attributes["departure"].Value);

                                    parameter.Departure = departure;

                                    string destinationText = parameterElement.Attributes["destination"].Value;

                                    if (destinationText == "any")
                                    {
                                        foreach (SpecialContractRoute scroute in scType.Routes.Where(r => r.Departure == departure))
                                        {
                                            var tparameter = new ContractRequirement(ContractRequirement.RequirementType.Destination) {Departure = departure, Destination = scroute.Destination};

                                            scType.Requirements.Add(tparameter);
                                        }
                                    }
                                    else
                                    {
                                        parameter.Destination = Stations.GetStation(destinationText);

                                        scType.Requirements.Add(parameter);
                                    }
                                }
                                else
                                {
                                    var parameter = new ContractRequirement(ContractRequirement.RequirementType.ClassType);

                                    var type =
                                        (CoachClass.ClassType)
                                            DataParsing.ParseEnum(typeof (CoachClass.ClassType), parameterElement.Attributes["classtype"].Value);

                                    int seats = Convert.ToInt32(parameterElement.Attributes["minseats"].Value);

                                    parameter.ClassType = type;
                                    parameter.MinSeats = seats;

                                    scType.Requirements.Add(parameter);
                                }
                            }

                        SpecialContractTypes.AddType(scType);
                    }
                }
        }

        //loads the random events
        private static void LoadRandomEvents()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/addons/events/RandomEvents.xml");
            XmlElement root = doc.DocumentElement;
            XmlNodeList eventsList = root?.SelectNodes("//event");

            if (eventsList != null)
                foreach (XmlElement element in eventsList)
                {
                    string section = root.Name;
                    var effects = (XmlElement) element.SelectSingleNode("effects");
                    var demand = (XmlElement) element.SelectSingleNode("demand");
                    var valid = (XmlElement) element.SelectSingleNode("valid");
                    string uid = element.Attributes["uid"].Value;
                    var eventType = new RandomEvent.EventType();
                    string type = element.Attributes["type"].Value;
                    string focus = element.Attributes["type"].Value;
                    var eventFocus = new RandomEvent.Focus();
                    switch (type)
                    {
                        case "Safety":
                            eventType = RandomEvent.EventType.Safety;
                            break;
                        case "Security":
                            eventType = RandomEvent.EventType.Security;
                            break;
                        case "Customer":
                            eventType = RandomEvent.EventType.Customer;
                            break;
                        case "Employee":
                            eventType = RandomEvent.EventType.Employee;
                            break;
                        case "Maintenance":
                            eventType = RandomEvent.EventType.Maintenance;
                            break;
                        case "Political":
                            eventType = RandomEvent.EventType.Political;
                            break;
                    }

                    switch (focus)
                    {
                        case "Carrier":
                            eventFocus = RandomEvent.Focus.Carrier;
                            break;

                        case "Coach":
                            eventFocus = RandomEvent.Focus.Coach;
                            break;

                        case "Station":
                            eventFocus = RandomEvent.Focus.Station;
                            break;
                    }

                    string name = element.Attributes["name"].Value;
                    string message = element.Attributes["text"].Value;
                    int frequency = int.Parse(element.Attributes["frequency"].Value)/3;
                    DateTime start = valid != null && valid.HasAttribute("from")
                        ? DataParsing.ParseDate(valid.Attributes["from"].Value)
                        : DateTime.Now.AddYears(100);
                    DateTime end = valid != null && valid.HasAttribute("to")
                        ? DataParsing.ParseDate(valid.Attributes["to"].Value)
                        : DateTime.Now.AddYears(100);

                    bool critical = Convert.ToBoolean(element.Attributes["important"].Value);
                    // if (int.Parse(effects.Attributes["important"].Value) == 1) critical = true; else critical = false;<

                    if (effects != null)
                    {
                        int effectLength = int.Parse(effects.Attributes["duration"].Value);

                        int chEffect = effects.HasAttribute("customerHappiness")
                            ? int.Parse(effects.Attributes["customerHappiness"].Value)
                            : 0;
                        int ehEffect = effects.HasAttribute("employeeHappiness")
                            ? int.Parse(effects.Attributes["employeeHappiness"].Value)
                            : 0;
                        int aSecurityEffect = effects.HasAttribute("carrierSecurity")
                            ? int.Parse(effects.Attributes["carrierSecurity"].Value)
                            : 0;
                        int aSafetyEffect = effects.HasAttribute("carrierSafety")
                            ? int.Parse(effects.Attributes["carrierSafety"].Value)
                            : 0;
                        int damageEffect = effects.HasAttribute("coachDamage")
                            ? int.Parse(effects.Attributes["coachDamage"].Value)
                            : 0;
                        int financial = effects.HasAttribute("financial") ? int.Parse(effects.Attributes["financial"].Value) : 0;
                        double paxDemand = demand != null && demand.HasAttribute("passenger")
                            ? double.Parse(demand.Attributes["passenger"].Value)
                            : 0;
                        double cargoDemand = demand != null && demand.HasAttribute("cargo") ? double.Parse(demand.Attributes["cargo"].Value) : 0;

                        var rEvent = new RandomEvent(
                            eventType,
                            eventFocus,
                            aSafetyEffect,
                            financial,
                            uid,
                            frequency,
                            start,
                            end,
                            name,
                            message,
                            critical,
                            chEffect,
                            damageEffect,
                            aSecurityEffect,
                            ehEffect,
                            paxDemand,
                            cargoDemand,
                            effectLength);

                        RandomEvents.AddEvent(rEvent);
                    }
                }
        }

        private static void LoadRegions()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/regions.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList regionsList = root?.SelectNodes("//region");
            if (regionsList != null)
                foreach (XmlElement element in regionsList)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    double fuelindex = Convert.ToDouble(
                        element.Attributes["fuel"].Value,
                        CultureInfo.GetCultureInfo("en-US").NumberFormat);

                    Regions.AddRegion(new Region(section, uid, fuelindex));

                    if (element.SelectSingleNode("translations") != null)
                    {
                        Translator.GetInstance()
                            .AddTranslation(
                                root.Name,
                                element.Attributes["uid"].Value,
                                element.SelectSingleNode("translations"));
                    }
                }
        }

        private static RouteClassesConfiguration LoadRouteClassesConfiguration(XmlElement element)
        {
            string name = element.Attributes["name"].Value;
            string id = element.Attributes["id"].Value;

            XmlNodeList classesList = element.SelectNodes("classes/class");

            var configuration = new RouteClassesConfiguration(name, true) {ID = id};

            if (classesList != null)
                foreach (XmlElement classElement in classesList)
                {
                    var type =
                        (CoachClass.ClassType)
                        DataParsing.ParseEnum(typeof (CoachClass.ClassType), classElement.Attributes["type"].Value);

                    var classConfiguration = new RouteClassConfiguration(type);

                    foreach (RouteFacility.FacilityType facilityType in Enum.GetValues(typeof (RouteFacility.FacilityType)))
                    {
                        string value = classElement.Attributes[facilityType.ToString()].Value;
                        RouteFacility facility = RouteFacilities.GetFacility(value);

                        classConfiguration.AddFacility(facility);
                    }
                    configuration.AddClass(classConfiguration);
                }

            return configuration;
        }

        private static void LoadScenario(string filename)
        {
            var doc = new XmlDocument();
            doc.Load(filename);
            XmlElement element = doc.DocumentElement;

            try
            {
                if (element != null)
                {
                    string scenarioName = element.Attributes["name"].Value;
                    int startYear = Convert.ToInt32(element.Attributes["startYear"].Value);
                    long startCash = Convert.ToInt64(element.Attributes["startCash"].Value);
                    int endYear = Convert.ToInt32(element.Attributes["endYear"].Value);
                    DifficultyLevel difficulty = DifficultyLevels.GetDifficultyLevel(element.Attributes["difficulty"].Value);

                    var selectSingleNode = element.SelectSingleNode("intro");
                    if (selectSingleNode?.Attributes != null)
                    {
                        string description = selectSingleNode.Attributes["text"].Value;
                        var singleNode = element.SelectSingleNode("success");
                        string successText = null;
                        if (singleNode?.Attributes != null)
                        {
                            successText = singleNode.Attributes["text"].Value;
                        }

                        var startElement = (XmlElement) element.SelectSingleNode("start");

                        if (startElement != null)
                        {
                            Carrier startCarrier = Carriers.GetCarrier(startElement.Attributes["carrier"].Value);
                            Station homebase = Stations.GetStation(startElement.Attributes["homeBase"].Value);

                            if (startElement.HasAttribute("license"))
                            {
                                startCarrier.License =
                                    (Carrier.CarrierLicense)
                                        DataParsing.ParseEnum(typeof (Carrier.CarrierLicense), startElement.Attributes["license"].Value);
                            }

                            var scenario = new Scenario(
                                scenarioName,
                                description,
                                startCarrier,
                                homebase,
                                startYear,
                                endYear,
                                startCash,
                                difficulty);
                            scenario.SuccessText = successText;
                            if (startElement.HasAttribute("debt"))
                            {
                                scenario.StartDebt = Convert.ToInt64(startElement.Attributes["debt"].Value, CultureInfo.InvariantCulture);
                            }
                            Scenarios.AddScenario(scenario);

                            XmlNodeList humanRoutesList = startElement.SelectNodes("routes/route");

                            if (humanRoutesList != null)
                                foreach (XmlElement humanRouteElement in humanRoutesList)
                                {
                                    Station routeDestination1 = Stations.GetStation(humanRouteElement.Attributes["departure"].Value);
                                    Station routeDestination2 = Stations.GetStation(humanRouteElement.Attributes["destination"].Value);
                                    CoachType routeCoachType =
                                        CoachTypes.GetType(humanRouteElement.Attributes["coach"].Value);
                                    int routeQuantity = Convert.ToInt32(humanRouteElement.Attributes["quantity"].Value);

                                    scenario.AddRoute(
                                        new ScenarioCarrierRoute(routeDestination1, routeDestination2, routeCoachType, routeQuantity));
                                }

                            XmlNodeList destinationsList = startElement.SelectNodes("destinations/destination");

                            if (destinationsList != null)
                                foreach (XmlElement destinationElement in destinationsList)
                                {
                                    scenario.AddDestination(Stations.GetStation(destinationElement.Attributes["station"].Value));
                                }

                            XmlNodeList fleetList = startElement.SelectNodes("fleet/coach");

                            if (fleetList != null)
                                foreach (XmlElement fleetElement in fleetList)
                                {
                                    CoachType fleetCoachType = CoachTypes.GetType(fleetElement.Attributes["name"].Value);

                                    int fleetQuantity = Convert.ToInt32(fleetElement.Attributes["quantity"].Value);

                                    scenario.AddFleet(fleetCoachType, fleetQuantity);
                                }

                            XmlNodeList aiNodeList = startElement.SelectNodes("AI/carrier");

                            if (aiNodeList != null)
                                foreach (XmlElement aiElement in aiNodeList)
                                {
                                    Carrier aiCarrier = Carriers.GetCarrier(aiElement.Attributes["name"].Value, startYear);
                                    Station aiHomebase = Stations.GetStation(aiElement.Attributes["homeBase"].Value);

                                    var scenarioCarrier = new ScenarioCarrier(aiCarrier, aiHomebase);

                                    XmlNodeList aiRoutesList = aiElement.SelectNodes("route");

                                    if (aiRoutesList != null)
                                        foreach (XmlElement aiRouteElement in aiRoutesList)
                                        {
                                            Station aiRouteDestination1 = Stations.GetStation(aiRouteElement.Attributes["departure"].Value);
                                            Station aiRouteDestination2 = Stations.GetStation(
                                                aiRouteElement.Attributes["destination"].Value);
                                            CoachType routeCoachType =
                                                CoachTypes.GetType(aiRouteElement.Attributes["coach"].Value);
                                            int routeQuantity = Convert.ToInt32(aiRouteElement.Attributes["quantity"].Value);

                                            scenarioCarrier.AddRoute(
                                                new ScenarioCarrierRoute(
                                                    aiRouteDestination1,
                                                    aiRouteDestination2,
                                                    routeCoachType,
                                                    routeQuantity));
                                        }

                                    scenario.AddOpponentCarrier(scenarioCarrier);
                                }

                            XmlNodeList modifiersList = element.SelectNodes("modifiers/paxDemand");

                            if (modifiersList != null)
                                foreach (XmlElement paxElement in modifiersList)
                                {
                                    Country country = null;
                                    Station station = null;

                                    if (paxElement.HasAttribute("country"))
                                    {
                                        country = Countries.GetCountry(paxElement.Attributes["country"].Value);
                                    }

                                    if (paxElement.HasAttribute("station"))
                                    {
                                        station = Stations.GetStation(paxElement.Attributes["station"].Value);
                                    }

                                    double factor = Convert.ToDouble(
                                        paxElement.Attributes["change"].Value,
                                        CultureInfo.GetCultureInfo("en-US").NumberFormat);

                                    var enddate =
                                        new DateTime(scenario.StartYear + Convert.ToInt32(paxElement.Attributes["length"].Value), 1, 1);

                                    scenario.AddPassengerDemand(new ScenarioPassengerDemand(factor, enddate, country, station));
                                }

                            XmlNodeList parametersList = element.SelectNodes("parameters/failure");

                            foreach (XmlElement parameterElement in parametersList)
                            {
                                string id = parameterElement.Attributes["id"].Value;
                                var failureType =
                                    (ScenarioFailure.FailureType)
                                        DataParsing.ParseEnum(typeof (ScenarioFailure.FailureType), parameterElement.Attributes["type"].Value);
                                object failureValue = parameterElement.Attributes["value"].Value;
                                double checkMonths = parameterElement.HasAttribute("at")
                                    ? 12
                                      *Convert.ToDouble(
                                          parameterElement.Attributes["at"].Value,
                                          CultureInfo.GetCultureInfo("en-US").NumberFormat)
                                    : 1;
                                string failureText = parameterElement.Attributes["text"].Value;
                                double monthsOfFailure = parameterElement.HasAttribute("for")
                                    ? 12
                                      *Convert.ToDouble(
                                          parameterElement.Attributes["for"].Value,
                                          CultureInfo.GetCultureInfo("en-US").NumberFormat)
                                    : 1;

                                var failure = new ScenarioFailure(
                                    id,
                                    failureType,
                                    (int) checkMonths,
                                    failureValue,
                                    failureText,
                                    (int) monthsOfFailure);

                                scenario.AddScenarioFailure(failure);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError("Error loading scenario", e);
            }
        }

        private static void LoadScenarios()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/scenarios");
            if (!dir.Exists) { StartupLog.Write("Data note: no scenarios folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                LoadScenario(file.FullName);
            }
        }

        private static void LoadStandardConfigurations()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/standardconfigurations.xml");
            XmlElement root = doc.DocumentElement;

            try
            {
                XmlNodeList configurationsList = root?.SelectNodes("//configuration");

                if (configurationsList != null)
                    foreach (XmlElement element in configurationsList)
                    {
                        string type = element.Attributes["type"].Value;

                        if (type == "Route")
                        {
                            Configurations.AddConfiguration(LoadRouteClassesConfiguration(element));
                        }
                        if (type == "Coach")
                        {
                            Configurations.AddConfiguration(LoadCoachConfiguration(element));
                        }
                    }
            }
            catch (Exception e)
            {
                LogError("Error loading configurations", e);
            }
        }

        /*!loads the states
         */

        private static void LoadStates()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/states.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList statesList = root?.SelectNodes("//state");
            if (statesList != null)
                foreach (XmlElement element in statesList)
                {
                    Country country = Countries.GetCountry(element.Attributes["country"].Value);
                    string name = element.Attributes["name"].Value;
                    string shortname = element.Attributes["shortname"].Value;
                    bool overseas = false;

                    if (element.HasAttribute("overseas"))
                    {
                        overseas = Convert.ToBoolean(element.Attributes["overseas"].Value);
                    }

                    var state = new State(country, name, shortname, overseas)
                    {
                        Flag = AppSettings.GetDataPath() + "/graphics/flags/states/"
                               + element.Attributes["flag"].Value + ".png"
                    };

                    States.AddState(state);

                    if (!File.Exists(state.Flag))
                    {
                        name = "";
                    }
                }
        }

        /*!loads the countries.
         */

        /*! loads the temporary countries
         */

        private static void LoadTemporaryCountries()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/temporary countries.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList countriesList = root?.SelectNodes("//country");
            if (countriesList != null)
                foreach (XmlElement element in countriesList)
                {
                    string section = root.Name;
                    string uid = element.Attributes["uid"].Value;
                    string shortname = element.Attributes["shortname"].Value;
                    string flag = element.Attributes["flag"].Value;
                    Region region = Regions.GetRegion(element.Attributes["region"].Value);
                    string tailformat = element.Attributes["tailformat"].Value;
                    var tempType =
                        (TemporaryCountry.TemporaryType)
                            DataParsing.ParseEnum(typeof (TemporaryCountry.TemporaryType), element.Attributes["type"].Value);

                    var periodElement = (XmlElement) element.SelectSingleNode("period");
                    if (periodElement != null)
                    {
                        DateTime startDate = DataParsing.ParseDate(
                            periodElement.Attributes["start"].Value,
                            new CultureInfo("en-US", false));
                        DateTime endDate = DataParsing.ParseDate(
                            periodElement.Attributes["end"].Value,
                            new CultureInfo("en-US", false));

                        var country = new Country(section, uid, shortname, region, tailformat);

                        if (element.SelectSingleNode("translations") != null)
                        {
                            Translator.GetInstance()
                                .AddTranslation(
                                    root.Name,
                                    element.Attributes["uid"].Value,
                                    element.SelectSingleNode("translations"));
                        }

                        var historyElement = (XmlElement) element.SelectSingleNode("history");

                        var tCountry = new TemporaryCountry(tempType, country, startDate, endDate);

                        if (tempType == TemporaryCountry.TemporaryType.ManyToOne)
                        {
                            if (historyElement != null)
                            {
                                Country before = Countries.GetCountry(historyElement.Attributes["before"].Value);
                                Country after = Countries.GetCountry(historyElement.Attributes["after"].Value);

                                tCountry.CountryBefore = before;
                                tCountry.CountryAfter = after;
                            }
                        }
                        if (tempType == TemporaryCountry.TemporaryType.OneToMany)
                        {
                            XmlNodeList tempCountriesList = historyElement?.SelectNodes("tempcountries/tempcountry");

                            if (tempCountriesList != null)
                                foreach (XmlElement tempCountryElement in tempCountriesList)
                                {
                                    Country tempCountry = Countries.GetCountry(tempCountryElement.Attributes["id"].Value);
                                    DateTime cStartDate = DataParsing.ParseDate(
                                        tempCountryElement.Attributes["start"].Value,
                                        new CultureInfo("en-US", false));
                                    DateTime cEndDate = DataParsing.ParseDate(
                                        tempCountryElement.Attributes["end"].Value,
                                        new CultureInfo("en-US", false));

                                    tCountry.Countries.Add(new OneToManyCountry(tempCountry, cStartDate, cEndDate));
                                }
                        }

                        tCountry.Flag = AppSettings.GetDataPath() + "/graphics/flags/" + flag + ".png";

                        TemporaryCountries.AddCountry(tCountry);
                    }
                }
        }

        /*! load the unions
         */

        private static void LoadUnions()
        {
            var doc = new XmlDocument();
            doc.Load(AppSettings.GetDataPath() + "/unions.xml");
            XmlElement root = doc.DocumentElement;

            XmlNodeList unionsList = root?.SelectNodes("//union");
            if (unionsList != null)
                foreach (XmlElement element in unionsList)
                {
                    try
                    {
                        string section = root.Name;
                        string uid = element.Attributes["uid"].Value;
                        string shortname = element.Attributes["shortname"].Value;
                        string flag = element.Attributes["flag"].Value;

                        var periodElement = (XmlElement) element.SelectSingleNode("period");
                        if (periodElement != null)
                        {
                            DateTime creationDate = DataParsing.ParseDate(
                                periodElement.Attributes["creation"].Value,
                                new CultureInfo("en-US", false));
                            DateTime obsoleteDate = DataParsing.ParseDate(
                                periodElement.Attributes["obsolete"].Value,
                                new CultureInfo("en-US", false));

                            var union = new Union(section, uid, shortname, creationDate, obsoleteDate);
                            XmlNodeList membersList = element.SelectNodes("members/member");

                            if (membersList != null)
                                foreach (XmlElement memberNode in membersList)
                                {
                                    Country country = Countries.GetCountry(memberNode.Attributes["country"].Value);
                                    DateTime fromDate = DataParsing.ParseDate(
                                        memberNode.Attributes["memberfrom"].Value,
                                        new CultureInfo("en-US", false));
                                    DateTime toDate = DataParsing.ParseDate(
                                        memberNode.Attributes["memberto"].Value,
                                        new CultureInfo("en-US", false));

                                    if (country == null)
                                    {
                                        uid = "";
                                    }

                                    union.AddMember(new UnionMember(country, fromDate, toDate));
                                }

                            union.Flag = AppSettings.GetDataPath() + "/graphics/flags/" + flag + ".png";
                            Unions.AddUnion(union);
                        }

                        if (element.SelectSingleNode("translations") != null)
                        {
                            Translator.GetInstance()
                                .AddTranslation(
                                    root.Name,
                                    element.Attributes["uid"].Value,
                                    element.SelectSingleNode("translations"));
                        }
                    }
                    catch (Exception e)
                    {
                        LogError("Error loading unions", e);
                    }
                }
        }

        /*! load the coach facilities.
         */

        /*! loads all weather averages
         */

        private static void LoadWeatherAverages()
        {
            var dir = new DirectoryInfo(AppSettings.GetDataPath() + "/addons/weather");
            if (!dir.Exists) { StartupLog.Write("Data note: no weather folder; that data is skipped."); return; }

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                LoadWeatherAverages(file.FullName);
            }
        }

        /*! loads a weather averages
       */

        private static void LoadWeatherAverages(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            XmlElement root = doc.DocumentElement;

            Country country = null;
            Station station = null;
            Town town = null;

            if (root != null)
            {
                string type = root.Attributes["type"].Value;
                string value = root.Attributes["value"].Value;

                if (type == "country")
                {
                    country = Countries.GetCountry(value);
                }

                if (type == "town")
                {
                    town = Towns.GetTown(value);
                }

                if (type == "station")
                {
                    station = Stations.GetStation(value);
                }
            }

            XmlNodeList monthsList = root?.SelectNodes("months/month");

            if (monthsList != null)
                foreach (XmlElement monthElement in monthsList)
                {
                    int month = Convert.ToInt16(monthElement.Attributes["month"].Value);
                    int precipitation = Convert.ToInt16(monthElement.Attributes["precipitation"].Value);

                    var tempElement = (XmlElement) monthElement.SelectSingleNode("temp");
                    if (tempElement != null)
                    {
                        double minTemp = Convert.ToDouble(
                            tempElement.Attributes["min"].Value,
                            CultureInfo.GetCultureInfo("en-US").NumberFormat);
                        double maxTemp = Convert.ToDouble(
                            tempElement.Attributes["max"].Value,
                            CultureInfo.GetCultureInfo("en-US").NumberFormat);

                        var windElement = (XmlElement) monthElement.SelectSingleNode("wind");
                        if (windElement != null)
                        {
                            var minWind =
                                (Weather.eWindSpeed) DataParsing.ParseEnum(typeof (Weather.eWindSpeed), windElement.Attributes["min"].Value);
                            var maxWind =
                                (Weather.eWindSpeed) DataParsing.ParseEnum(typeof (Weather.eWindSpeed), windElement.Attributes["max"].Value);

                            if (country != null)
                            {
                                WeatherAverages.AddWeatherAverage(
                                    new WeatherAverage(month, minTemp, maxTemp, precipitation, minWind, maxWind, country));
                            }

                            if (town != null)
                            {
                                WeatherAverages.AddWeatherAverage(
                                    new WeatherAverage(month, minTemp, maxTemp, precipitation, minWind, maxWind, town));
                            }

                            if (station != null)
                            {
                                WeatherAverages.AddWeatherAverage(
                                    new WeatherAverage(month, minTemp, maxTemp, precipitation, minWind, maxWind, station));
                            }
                        }
                    }
                }
        }

        private static void ReadSettingsFile()
        {
            if (File.Exists(AppSettings.GetCommonApplicationDataPath() + "/game.settings"))
            {
                var file = new StreamReader(AppSettings.GetCommonApplicationDataPath() + "/game.settings");

                string language = file.ReadLine();
                string screenMode = file.ReadLine();

                AppSettings.GetInstance().SetLanguage(Languages.GetLanguage(language));
                if (screenMode != null)
                    Settings.GetInstance().Mode =
                        (ScreenMode) DataParsing.ParseEnum(typeof (ScreenMode), screenMode, true);
            }
        }

        private static void RemoveCarriers(int opponnents, bool sameRegion)
        {
            Station humanStation = GameObject.GetInstance().HumanCarrier.Stations[0];
            int year = GameObject.GetInstance().GameTime.Year;

            List<Carrier> notAvailableCarriers =
                Carriers.GetCarriers(a => !(a.Profile.Founded <= year && a.Profile.Folded > year));

            foreach (Carrier carrier in notAvailableCarriers)
            {
                Carriers.RemoveCarrier(carrier);
            }

            int count =
                Carriers.GetCarriers(a => !a.IsHuman && a.Profile.Founded <= year && a.Profile.Folded > year).Count;

            var carriers =
                new List<Carrier>(
                    Carriers.GetCarriers(a => !a.IsHuman && a.Profile.Founded <= year && a.Profile.Folded > year));

            if (sameRegion)
            {
                carriers =
                    carriers.OrderByDescending(
                        a =>
                        a.Profile.PreferedStation == null
                            ? double.MaxValue
                            : MathHelpers.GetDistance(a.Profile.PreferedStation, humanStation)).ToList();
            }
            else
            {
                carriers = MathHelpers.Shuffle(carriers);
            }

            for (int i = 0; i < count - opponnents; i++)
            {
                Carriers.RemoveCarrier(carriers[i]);
            }
        }

        /*loads the alliances
         */

        /*! sets up the difficulty levels
         */

        private static void SetupDifficultyLevels()
        {
            DifficultyLevels.AddDifficultyLevel(new DifficultyLevel("Easy", 1.5, 0.75, 1.5, 1, 1.25, 5));
            DifficultyLevels.AddDifficultyLevel(new DifficultyLevel("Normal", 1, 1, 1.2, 1.1, 1, 2));
            DifficultyLevels.AddDifficultyLevel(new DifficultyLevel("Hard", 0.5, 1.25, 1, 1.2, 0.75, 1));
        }

        /*! sets up the statistics types.
         */

        private static void SetupMainGame()
        {
            StartupLog.Write("SetupMainGame: creating drivers");
            CreateDrivers();
            StartupLog.Write("SetupMainGame: drivers done");

            //creates the facilities for driver training
            IEnumerable<string> coachFamilies =
                CoachTypes.GetTypes(
                    t =>
                    t.Produced.From.Year < GameObject.GetInstance().GameTime.Year
                    && t.Produced.To > GameObject.GetInstance().GameTime.AddYears(-30))
                             .Select(a => a.CoachFamily)
                             .Distinct();

            foreach (string family in coachFamilies)
            {
                CarrierFacilities.AddFacility(
                    new DriverTrainingFacility(
                        "carrierfacilities",
                        family,
                        9000,
                        1000,
                        GameObject.GetInstance().GameTime.Year,
                        0,
                        0,
                        family));
            }

            StartupLog.Write("SetupMainGame: driver training facilities done, setting station facilities for " + Stations.GetAllStations().Count + " stations and " + Carriers.GetAllCarriers().Count + " carriers");
            //sets all the facilities at an station to none for all carriers
            Parallel.ForEach(
                Stations.GetAllStations(),
                station =>
                    {
                        foreach (Carrier carrier in Carriers.GetAllCarriers())
                        {
                            foreach (
                                StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)))
                            {
                                StationFacility noneFacility =
                                    StationFacilities.GetFacilities(type)
                                                     .Find((facility => facility.TypeLevel == 0));

                                station.AddStationFacility(carrier, noneFacility, GameObject.GetInstance().GameTime);
                            }
                        }
                        if (station.Profile.Size == GeneralHelpers.Size.Largest
                            || station.Profile.Size == GeneralHelpers.Size.VeryLarge
                            || station.Profile.Size == GeneralHelpers.Size.Large)
                        {
                            if (!station.Runways.Exists(r => r.Type == Runway.RunwayType.Helipad))
                            {
                                var helipad = new Runway("H1", Rnd.Next(17, 25), Runway.RunwayType.Helipad, Runway.SurfaceType.Concrete, GameObject.GetInstance().GameTime, true);

                                station.Runways.Add(helipad);
                            }
                        }
                        if (station.Profile.Cargo == GeneralHelpers.Size.VeryLarge
                            || station.Profile.Cargo == GeneralHelpers.Size.Largest
                            || station.Profile.Size == GeneralHelpers.Size.Largest
                            || station.Profile.Size == GeneralHelpers.Size.VeryLarge)
                        {
                            StationFacility cargoTerminal =
                                StationFacilities.GetFacilities(StationFacility.FacilityType.Cargo)
                                                 .Find(f => f.TypeLevel > 0);

                            station.AddStationFacility(null, cargoTerminal, GameObject.GetInstance().GameTime);

                            if (!station.Terminals.StationTerminals.Exists(t => t.Type == Terminal.TerminalType.Cargo))
                            {
                                station.Terminals.AddTerminal(new Terminal(station, "Cargo Terminal", ((int) station.Profile.Cargo) + 5, GameObject.GetInstance().GameTime, Terminal.TerminalType.Cargo));
                            }
                        }

                        StationHelpers.CreateStationWeather(station);

                        //creates the already existing expansions
                        IEnumerable<StationExpansion> expansions = station.Profile.Expansions.Where(e => GameObject.GetInstance().GameTime > e.Date);

                        foreach (StationExpansion expansion in expansions)
                            StationHelpers.SetStationExpansion(station, expansion, true);
                    });

            StartupLog.Write("SetupMainGame: station facilities done");
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                StartupLog.Write("SetupMainGame: setting up carrier " + carrier.Profile.IATACode);
                carrier.Money = GameObject.GetInstance().StartMoney;

                if (carrier.IsHuman)
                {
                    GameObject.GetInstance().HumanMoney = carrier.Money;
                }

                carrier.StartMoney = carrier.Money;
                carrier.Fees = new CarrierFees();
                carrier.AddCarrierPolicy(new CarrierPolicy("Cancellation Minutes", 150));

                // (the old 10,000 share objects aren't made any more: SharesService's Holdings say who owns the carrier)

                if (!carrier.IsHuman)
                {
                    carrier.Profile.Country = carrier.Profile.Countries[Rnd.Next(carrier.Profile.Countries.Count)];

                    CreateComputerRoutes(carrier);
                }
            }

            /*
            Stations.GetStation("BOS").Terminals.rentGate(GameObject.GetInstance().HumanCarrier);
            Stations.GetStation("AAR").Terminals.rentGate(GameObject.GetInstance().HumanCarrier);
            Stations.GetStation("CPH").Terminals.rentGate(GameObject.GetInstance().HumanCarrier);
            Stations.GetStation("CPH").Terminals.rentGate(GameObject.GetInstance().HumanCarrier);
            Stations.GetStation("SBY").Terminals.rentGate(GameObject.GetInstance().HumanCarrier);

            Coach coach = Coaches.GetCoachesForSale(a => a.Type.Name == "Boeing 737-900ER").First();
            CarrierHelpers.BuyCoach(GameObject.GetInstance().HumanCarrier, coach, GameObject.GetInstance().HumanCarrier.Stations[0]);
            */
            StartupLog.Write("SetupMainGame: carriers done, setting up alliances");
            SetupAlliances();
            SetupMergers();
            StartupLog.Write("SetupMainGame: finished");
        }

        private static void SetupStatisticsTypes()
        {
            StatisticsTypes.AddStatisticsType(new StatisticsType("Arrivals", "Arrivals"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Departures", "Departures"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Cargo", "Cargo"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Cargo per trip", "Cargo%"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Passengers", "Passengers"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Passengers per trip", "Passengers%"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Passenger Capacity", "Capacity"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Coach Income", "Coach_Income"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("On-Time trips", "On-Time"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Trips On-Time", "On-Time%"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Cancellations", "Cancellations"));
            StatisticsTypes.AddStatisticsType(new StatisticsType("Cancellation Percent", "Cancellation%"));
        }

        #endregion

        /*! private static variable rnd.
         * holds a random number.
         */

        /*! private static method ClearLists().
         * Resets game´s environment.
         */

        //sets up the carrier mergers
    }
}