using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Countries.Towns;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.Scenarios;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    public class LoadSaveHelpers
    {
        #region Public Methods and Operators

        public static void DeleteGame(string name)
        {
            File.Delete(AppSettings.GetCommonApplicationDataPath() + "/saves/" + name + ".sav");
        }

        //loads a game

        /*
        //append a file to the list of saved files
        public static void AppendSavedFile(string name, string file)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(AppSettings.getCommonApplicationDataPath() + "/saves/saves.xml");
            XmlElement root = doc.DocumentElement;

            XmlNode node = root.SelectSingleNode("//saves");

            XmlElement e = doc.CreateElement("save");
            e.SetAttribute("name", name);
            e.SetAttribute("file", file);

            node.AppendChild(e);

            doc.Save(AppSettings.getCommonApplicationDataPath() + "/saves/saves.xml");

        }*/
        //returns the names of the saved games
        public static List<string> GetSavedGames()
        {
            var dir = new DirectoryInfo(AppSettings.GetCommonApplicationDataPath() + "/saves/");

            return dir.GetFiles("*.sav").Select(file => file.Name.Split('.')[0]).ToList();
        }

        //returns the saved games with the time each file was written, newest first
        public static List<SaveFileInfo> GetSavedGameInfos()
        {
            var dir = new DirectoryInfo(AppSettings.GetCommonApplicationDataPath() + "/saves/");

            if (!dir.Exists)
            {
                return new List<SaveFileInfo>();
            }

            return dir.GetFiles("*.sav")
                      .Select(file => new SaveFileInfo(file.Name.Split('.')[0], file.LastWriteTime))
                      .OrderByDescending(s => s.SavedAt)
                      .ToList();
        }

        public static void LoadGame(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                UserInterface.ShowMessage?.Invoke(
                    Translator.GetInstance().GetString("MessageBox", "101"),
                    Translator.GetInstance().GetString("MessageBox", "101", "message"));
                return;
            }
            string fileName = AppSettings.GetCommonApplicationDataPath() + "/saves/" + name + ".sav";

            var doc = new XmlDocument();

            using (var fs = new FileStream(fileName, FileMode.Open))
            {
                Stream s = new GZipStream(fs, CompressionMode.Decompress);
                doc.Load(s);
                s.Close();
            }
            //doc.Load(AppSettings.getDataPath() + "/saves/" + name + ".xml");
            XmlElement root = doc.DocumentElement;

            if (root != null)
            {
                DateTime gameTime = DateTime.Parse(root.Attributes["time"].Value, new CultureInfo("de-DE"));
                GameObject.GetInstance().GameTime = gameTime;
            }

            if (root != null)
            {
                XmlNodeList tailnumbersList = root.SelectNodes("//tailnumbers/tailnumber");

                if (tailnumbersList != null)
                    foreach (XmlElement tailnumberNode in tailnumbersList)
                    {
                        Country country = Countries.GetCountry(tailnumberNode.Attributes["country"].Value);

                        if (country != null)
                        {
                            country.TailNumbers.LastTailNumber = tailnumberNode.Attributes["value"].Value;
                        }
                    }
            }

            if (root != null)
            {
                XmlNodeList coachTypesList = root.SelectNodes("//coachtypes/coachtype");

                if (coachTypesList != null)
                    foreach (XmlElement coachTypeNode in coachTypesList)
                    {
                        var coachType =
                            (CoachType.TypeOfCoach)
                            TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CoachType.TypeOfCoach), coachTypeNode.Attributes["type"].Value);
                        CoachType baseType = CoachTypes.GetType(coachTypeNode.Attributes["basetype"].Value);
                        string coachTypeName = coachTypeNode.Attributes["name"].Value;

                        CoachType type = null;

                        if (coachType == CoachType.TypeOfCoach.Passenger)
                        {
                            int attendants = Convert.ToInt16(coachTypeNode.Attributes["attendants"].Value);
                            int passengers = Convert.ToInt16(coachTypeNode.Attributes["passengers"].Value);
                            int maxclasses = Convert.ToInt16(coachTypeNode.Attributes["maxclasses"].Value);

                            type = new CoachPassengerType(
                                baseType.Manufacturer,
                                coachTypeName,
                                "",
                                passengers,
                                baseType.DriverCrew,
                                attendants,
                                0,
                                0,
                                baseType.Wingspan,
                                baseType.Length,
                                baseType.Weight,
                                0,
                                baseType.Price,
                                maxclasses,
                                0,
                                baseType.FuelCapacity,
                                baseType.Body,
                                baseType.RangeType,
                                baseType.Engine,
                                baseType.Produced,
                                baseType.ProductionRate,
                                false,
                                false);
                        }
                        if (type != null)
                        {
                            type.BaseType = baseType;

                            CoachTypes.AddType(type);
                        }
                    }
            }

            Coaches.Clear();

            if (root != null)
            {
                XmlNodeList coachesList = root.SelectNodes("//coaches/coach");

                if (coachesList != null)
                    Parallel.For(
                        0,
                        coachesList.Count,
                        i => //foreach (XmlElement coachNode in coachesList)
                            {
                                var coachNode = (XmlElement) coachesList[i];
                                CoachType type = CoachTypes.GetType(coachNode.Attributes["type"].Value);

                                if (type != null)
                                {
                                    string tailnumber = coachNode.Attributes["tailnumber"].Value;
                                    string id = coachNode.HasAttribute("id") ? coachNode.Attributes["id"].Value : tailnumber;

                                    string last_service = coachNode.Attributes["last_service"].Value;
                                    DateTime built = DateTime.Parse(
                                        coachNode.Attributes["built"].Value,
                                        new CultureInfo("de-DE", false));
                                    double flown = Convert.ToDouble(
                                        coachNode.Attributes["run"].Value,
                                        new CultureInfo("de-DE", false));
                                    double damaged = Convert.ToDouble(
                                        coachNode.Attributes["damaged"].Value,
                                        new CultureInfo("de-DE", false));

                                    var coach = new Coach(id, type, tailnumber, built) {Condition = damaged, Flown = flown};
                                    coach.ClearCoachClasses();

                                    XmlNodeList coachClassList = coachNode.SelectNodes("classes/class");

                                    if (coachClassList != null)
                                        foreach (XmlElement coachClassNode in coachClassList)
                                        {
                                            var coachClassType =
                                                (CoachClass.ClassType)
                                                TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                                    typeof (CoachClass.ClassType),
                                                    coachClassNode.Attributes["type"].Value);
                                            int coachClassSeating = Convert.ToInt16(coachClassNode.Attributes["seating"].Value);

                                            var aClass = new CoachClass(coachClassType, coachClassSeating);
                                            // chs, 2011-13-10 added for loading of coach facilities
                                            XmlNodeList coachClassFacilitiesList =
                                                coachClassNode.SelectNodes("facilities/facility");
                                            if (coachClassFacilitiesList != null)
                                                foreach (XmlElement coachClassFacilityNode in coachClassFacilitiesList)
                                                {
                                                    var coachFacilityType =
                                                        (CoachFacility.FacilityType)
                                                        TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                                            typeof (CoachFacility.FacilityType),
                                                            coachClassFacilityNode.Attributes["type"].Value);

                                                    CoachFacility aFacility = CoachFacilities.GetFacility(
                                                        coachFacilityType,
                                                        coachClassFacilityNode.Attributes["uid"].Value);
                                                    aClass.ForceSetFacility(aFacility);
                                                }

                                            coach.AddCoachClass(aClass);
                                        }

                                    Coaches.AddCoach(coach);
                                }
                            });
            }

            Carriers.Clear();

            if (root != null)
            {
                XmlNodeList carriersList = root.SelectNodes("//carriers/carrier[@subsidiary='False']");

                if (carriersList != null)
                    foreach (XmlElement carrierNode in carriersList)
                    {
                        LoadCarrier(carrierNode);
                    }
            }

            if (root != null)
            {
                XmlNodeList subsidiaryList = root.SelectNodes("//carriers/carrier[@subsidiary='True']");

                if (subsidiaryList != null)
                    foreach (XmlElement carrierNode in subsidiaryList)
                    {
                        LoadCarrier(carrierNode);
                    }
            }

            if (root != null)
            {
                XmlNodeList stationsList = root.SelectNodes("//stations/station");

                var stationsToKeep = new List<Station>();

                if (stationsList != null)
                    foreach (XmlElement stationNode in stationsList)
                    {
                        Station station = Stations.GetStationFromID(stationNode.Attributes["id"].Value);
                        stationsToKeep.Add(station);

                        /*
                  *   XmlElement stationPaxNode = xmlDoc.CreateElement("paxvalue");
                    stationPaxNode.SetAttribute("from", paxValue.FromYear.ToString());
                    stationPaxNode.SetAttribute("to", paxValue.ToYear.ToString());
                    stationPaxNode.SetAttribute("size", paxValue.Size.ToString());
                    stationPaxNode.SetAttribute("pax", paxValue.Pax.ToString());
                    stationPaxNode.SetAttribute("inflationbefore", paxValue.InflationBeforeYear.ToString());
                    stationPaxNode.SetAttribute("inflationafter", paxValue.InflationAfterYear.ToString());
*/

                        station.Income = Convert.ToInt64(stationNode.Attributes["income"].Value);

                        station.Profile.PaxValues.Clear();

                        XmlNodeList paxvaluesList = stationNode.SelectNodes("paxvalues/paxvalue");

                        foreach (XmlElement paxElement in paxvaluesList)
                        {
                            int fromYear = Convert.ToInt16(paxElement.Attributes["from"].Value);
                            int toYear = Convert.ToInt16(paxElement.Attributes["to"].Value);
                            var stationSize =
                                (GeneralHelpers.Size)
                                TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (GeneralHelpers.Size), paxElement.Attributes["size"].Value);
                            double pax = Convert.ToDouble(paxElement.Attributes["pax"].Value);
                            double inflationBefore = Convert.ToDouble(paxElement.Attributes["inflationbefore"].Value);
                            double inflationAfter = Convert.ToDouble(paxElement.Attributes["inflationafter"].Value);

                            var paxValue = new PaxValue(fromYear, toYear, stationSize, pax) {InflationAfterYear = inflationAfter, InflationBeforeYear = inflationBefore};

                            station.Profile.PaxValues.Add(paxValue);
                        }

                        XmlNodeList baysList = stationNode.SelectNodes("bays/bay");

                        foreach (XmlElement bayElement in baysList)
                        {
                            string bayName = bayElement.Attributes["name"].Value;
                            long bayLenght = Convert.ToInt64(bayElement.Attributes["lenght"].Value);
                            var baySurface =
                                (Bay.SurfaceType)
                                TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Bay.SurfaceType), bayElement.Attributes["surface"].Value);
                            DateTime bayDate = DateTime.Parse(
                                bayElement.Attributes["date"].Value,
                                new CultureInfo("de-DE", false));

                            station.Bays.Add(new Bay(bayName, bayLenght, Bay.BayType.Regular, baySurface, bayDate, false));
                        }

                        XmlNodeList stationHubsList = stationNode.SelectNodes("hubs/hub");
                        // station.Hubs.Clear();

                        foreach (XmlElement stationHubElement in stationHubsList)
                        {
                            Carrier carrier = Carriers.GetCarrier(stationHubElement.Attributes["carrier"].Value);
                            // station.Hubs.Add(new Hub(carrier,null));
                        }

                        XmlNodeList stationWeatherList = stationNode.SelectNodes("weathers/weather");

                        for (int i = 0; i < stationWeatherList.Count; i++)
                        {
                            var stationWeatherElement = stationWeatherList[i] as XmlElement;

                            DateTime weatherDate = DateTime.Parse(
                                stationWeatherElement.Attributes["date"].Value,
                                new CultureInfo("de-DE", false));
                            var windDirection =
                                (Weather.WindDirection)
                                TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                    typeof (Weather.WindDirection),
                                    stationWeatherElement.Attributes["direction"].Value);
                            var windSpeed =
                                (Weather.eWindSpeed)
                                TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Weather.eWindSpeed), stationWeatherElement.Attributes["windspeed"].Value);
                            Weather.CloudCover cover = stationWeatherElement.HasAttribute("cover")
                                                           ? (Weather.CloudCover)
                                                             TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Weather.CloudCover), stationWeatherElement.Attributes["cover"].Value)
                                                           : Weather.CloudCover.Clear;
                            Weather.Precipitation precip = stationWeatherElement.HasAttribute("precip")
                                                               ? (Weather.Precipitation)
                                                                 TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Weather.Precipitation), stationWeatherElement.Attributes["precip"].Value)
                                                               : Weather.Precipitation.None;
                            double temperatureLow = stationWeatherElement.HasAttribute("temperatureLow")
                                                        ? Convert.ToDouble(
                                                            stationWeatherElement.Attributes["temperaturelow"].Value,
                                                            new CultureInfo("de-DE", false))
                                                        : 0;
                            double temperatureHigh = stationWeatherElement.HasAttribute("temperatureHigh")
                                                         ? Convert.ToDouble(
                                                             stationWeatherElement.Attributes["temperaturehigh"].Value,
                                                             new CultureInfo("de-DE", false))
                                                         : 20;

                            XmlNodeList stationTemperatureList = stationWeatherElement.SelectNodes("temperatures/temperature");
                            var temperatures = new HourlyWeather[stationTemperatureList.Count];

                            int t = 0;
                            foreach (XmlElement stationTemperatureNode in stationTemperatureList)
                            {
                                double hourlyTemperature = Convert.ToDouble(
                                    stationTemperatureNode.Attributes["temp"].Value,
                                    new CultureInfo("de-DE", false));
                                var hourlyCover =
                                    (Weather.CloudCover)
                                    TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Weather.CloudCover), stationTemperatureNode.Attributes["cover"].Value);
                                var hourlyPrecip =
                                    (Weather.Precipitation)
                                    TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                        typeof (Weather.Precipitation),
                                        stationTemperatureNode.Attributes["precip"].Value);
                                var hourlyWindspeed =
                                    (Weather.eWindSpeed)
                                    TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                        typeof (Weather.eWindSpeed),
                                        stationTemperatureNode.Attributes["windspeed"].Value);
                                var hourlyDirection =
                                    (Weather.WindDirection)
                                    TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                        typeof (Weather.WindDirection),
                                        stationTemperatureNode.Attributes["direction"].Value);

                                temperatures[t] = new HourlyWeather(
                                    hourlyTemperature,
                                    hourlyCover,
                                    hourlyPrecip,
                                    hourlyWindspeed,
                                    hourlyDirection);
                                t++;
                            }

                            station.Weather[i] = new Weather(
                                weatherDate,
                                windSpeed,
                                windDirection,
                                cover,
                                precip,
                                temperatures,
                                temperatureLow,
                                temperatureHigh);
                        }

                        XmlNodeList stationStatList = stationNode.SelectNodes("stats/stat");

                        foreach (XmlElement stationStatNode in stationStatList)
                        {
                            int year = Convert.ToInt32(stationStatNode.Attributes["year"].Value);
                            Carrier carrier = Carriers.GetCarrier(stationStatNode.Attributes["carrier"].Value);
                            string statType = stationStatNode.Attributes["type"].Value;
                            int statValue = Convert.ToInt32(stationStatNode.Attributes["value"].Value);
                            station.Statistics.SetStatisticsValue(
                                year,
                                carrier,
                                StatisticsTypes.GetStatisticsType(statType),
                                statValue);
                        }

                        XmlNodeList stationFacilitiesList = stationNode.SelectNodes("facilities/facility");
                        station.ClearFacilities();

                        foreach (XmlElement stationFacilityNode in stationFacilitiesList)
                        {
                            Carrier carrier = Carriers.GetCarrier(stationFacilityNode.Attributes["carrier"].Value);
                            StationFacility stationFacility =
                                StationFacilities.GetFacility(stationFacilityNode.Attributes["name"].Value);
                            DateTime finishedDate = DateTime.Parse(
                                stationFacilityNode.Attributes["finished"].Value,
                                new CultureInfo("de-DE", false));

                            station.AddStationFacility(carrier, stationFacility, finishedDate);
                        }
                        station.Terminals.Clear();

                        XmlNodeList terminalsList = stationNode.SelectNodes("terminals/terminal");

                        foreach (XmlElement terminalNode in terminalsList)
                        {
                            DateTime deliveryDate = DateTime.Parse(
                                terminalNode.Attributes["delivery"].Value,
                                new CultureInfo("de-DE", false));
                            Carrier owner = Carriers.GetCarrier(terminalNode.Attributes["owner"].Value);
                            string terminalName = terminalNode.Attributes["name"].Value;
                            int gates = Convert.ToInt32(terminalNode.Attributes["totalgates"].Value);

                            var terminal = new Terminal(station, owner, terminalName, gates, deliveryDate, Terminal.TerminalType.Passenger);
                            terminal.Gates.Clear();

                            XmlNodeList stationGatesList = terminalNode.SelectNodes("gates/gate");

                            foreach (XmlElement stationGateNode in stationGatesList)
                            {
                                DateTime gateDeliveryDate = DateTime.Parse(
                                    stationGateNode.Attributes["delivery"].Value,
                                    new CultureInfo("de-DE", false));
                                var gate = new Gate(gateDeliveryDate);

                                terminal.Gates.AddGate(gate);
                            }

                            station.AddTerminal(terminal);
                        }
                        station.ClearCarrierContracts();

                        XmlNodeList contractsList = stationNode.SelectNodes("contracts/contract");

                        foreach (XmlElement contractNode in contractsList)
                        {
                            Carrier contractCarrier = Carriers.GetCarrier(contractNode.Attributes["carrier"].Value);
                            int contractLength = Convert.ToInt16(contractNode.Attributes["length"].Value);
                            DateTime contractDate = DateTime.Parse(
                                contractNode.Attributes["date"].Value,
                                new CultureInfo("de-DE", false));
                            int contractGates = Convert.ToInt16(contractNode.Attributes["gates"].Value);
                            double contractPayment = Convert.ToDouble(
                                contractNode.Attributes["payment"].Value,
                                new CultureInfo("de-DE", false));
                            Boolean contractExclusive = Convert.ToBoolean(contractNode.Attributes["exclusive"].Value);
                            Terminal contractTerminal = contractNode.HasAttribute("terminal")
                                                            ? station.Terminals.StationTerminals.Find(
                                                                t => t.Name == contractNode.Attributes["terminal"].Value)
                                                            : null;

                            var contract = new StationContract(
                                contractCarrier,
                                station,
                                StationContract.ContractType.Full,
                                Terminal.TerminalType.Passenger,
                                contractDate,
                                contractGates,
                                contractLength,
                                contractPayment,
                                true,
                                false,
                                contractExclusive,
                                contractTerminal);
                            StationHelpers.AddCarrierContract(contract);
                        }
                    }

                Stations.RemoveStations(a => !stationsToKeep.Contains(a));
            }

            XmlNodeList stationDestinationsList = root.SelectNodes("//stationdestinations/stationdestination");

            foreach (XmlElement stationDestinationElement in stationDestinationsList)
            {
                Station targetStation = Stations.GetStation(stationDestinationElement.Attributes["id"].Value);

                if (targetStation != null)
                {
                    targetStation.ClearDestinationPassengers();

                    XmlNodeList destinationsList = stationDestinationElement.SelectNodes("destinations/destination");

                    Parallel.For(
                        0,
                        destinationsList.Count,
                        i => //foreach (XmlElement destinationElement in destinationsList)
                            {
                                var destinationElement = (XmlElement) destinationsList[i];
                                Station destStation = Stations.GetStation(destinationElement.Attributes["id"].Value);

                                if (destStation != null)
                                {
                                    ushort rate = ushort.Parse(destinationElement.Attributes["rate"].Value);
                                    long destPassengers = Convert.ToInt64(destinationElement.Attributes["passengers"].Value);

                                    targetStation.AddPassengerDestinationStatistics(destStation, destPassengers);
                                    targetStation.AddDestinationPassengersRate(
                                        new DestinationDemand(destStation.Profile.IATACode, rate));

                                    if (destinationElement.HasAttribute("cargo"))
                                    {
                                        targetStation.AddDestinationCargoRate(
                                            new DestinationDemand(
                                                destStation.Profile.IATACode,
                                                ushort.Parse(destinationElement.Attributes["cargo"].Value)));
                                        targetStation.AddCargoDestinationStatistics(
                                            destStation,
                                            Convert.ToDouble(
                                                destinationElement.Attributes["cargostats"].Value,
                                                new CultureInfo("de-DE", false)));
                                    }
                                }
                            });
                }
            }
            Instructors.Clear();

            XmlNodeList instructorsList = root.SelectNodes("//instructors/instructor");

            foreach (XmlElement instructorNode in instructorsList)
            {
                string firstname = instructorNode.Attributes["firstname"].Value;
                string lastname = instructorNode.Attributes["lastname"].Value;
                DateTime birthdate = DateTime.Parse(
                    instructorNode.Attributes["birthdate"].Value,
                    new CultureInfo("de-DE", false));
                Town town = Towns.GetTown(instructorNode.Attributes["town"].Value);
                //Driver.DriverRating rating = (Driver.DriverRating)TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof(Driver.DriverRating), instructorNode.Attributes["rating"].Value);
                string id = instructorNode.Attributes["id"].Value;

                var instructor = new Instructor(
                    new DriverProfile(firstname, lastname, birthdate, town),
                    DriverRatings.GetRating("A"));

                if (id != "-")
                {
                    TripSchool fs =
                        Carriers.GetAllCarriers()
                                .SelectMany(a => a.TripSchools).FirstOrDefault(f => f.ID == id);
                    instructor.TripSchool = fs;
                    fs.AddInstructor(instructor);
                }

                XmlNodeList studentsList = instructorNode.SelectNodes("students/student");

                foreach (XmlElement studentNode in studentsList)
                {
                    DriverStudent student =
                        instructor.TripSchool.Students.Find(
                            s => s.Profile.Name == studentNode.Attributes["name"].Value);
                    student.Instructor = instructor;
                    instructor.AddStudent(student);
                }

                Instructors.AddInstructor(instructor);
            }

            if (Instructors.GetInstructors().Count == 0)
            {
                GeneralHelpers.CreateInstructors(75*Carriers.GetAllCarriers().Count);
            }

            Drivers.Clear();

            XmlNodeList driversList = root.SelectNodes("//drivers/driver");

            foreach (XmlElement driverNode in driversList)
            {
                string firstname = driverNode.Attributes["firstname"].Value;
                string lastname = driverNode.Attributes["lastname"].Value;
                DateTime birthdate = DateTime.Parse(
                    driverNode.Attributes["birthdate"].Value,
                    new CultureInfo("de-DE", false));
                Town town = Towns.GetTown(driverNode.Attributes["town"].Value);
                DateTime educationdate = DateTime.Parse(
                    driverNode.Attributes["education"].Value,
                    new CultureInfo("de-DE", false));

                //Driver.DriverRating rating = (Driver.DriverRating)TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof(Driver.DriverRating), driverNode.Attributes["rating"].Value);

                var driver = new Driver(
                    new DriverProfile(firstname, lastname, birthdate, town),
                    educationdate,
                    DriverRatings.GetRating("B"));

                if (driverNode.Attributes["carrier"].Value != "-")
                {
                    Carrier driverCarrier = Carriers.GetCarrier(driverNode.Attributes["carrier"].Value);
                    DateTime carriersigneddate = DateTime.Parse(
                        driverNode.Attributes["carriersigned"].Value,
                        new CultureInfo("de-DE", false));

                    driverCarrier.AddDriver(driver);
                    driver.CarrierSignedDate = carriersigneddate;

                    if (driverNode.Attributes["coach"].Value != "-")
                    {
                        FleetCoach coach =
                            driverCarrier.Fleet.Find(f => f.Coach.ID == driverNode.Attributes["coach"].Value);

                        if (coach != null)
                        {
                            driver.Coach = coach;
                            coach.AddDriver(driver);
                        }
                    }
                }

                Drivers.AddDriver(driver);
            }

            if (Drivers.GetNumberOfDrivers() == 0)
            {
                var rnd = new Random();

                GeneralHelpers.CreateDrivers(100*Carriers.GetAllCarriers().Count);

                foreach (FleetCoach coach in Carriers.GetAllCarriers().SelectMany(a => a.Fleet))
                {
                    Driver driver = Drivers.GetDrivers()[rnd.Next(Drivers.GetNumberOfDrivers())];
                    coach.Coach.Carrier.AddDriver(driver);
                    driver.Coach = coach;
                    coach.AddDriver(driver);
                }
            }

            Alliances.Clear();

            XmlNodeList alliancesList = root.SelectNodes("//alliances/alliance");

            foreach (XmlElement allianceNode in alliancesList)
            {
                string allianceName = allianceNode.Attributes["name"].Value;
                DateTime formationDate = DateTime.Parse(
                    allianceNode.Attributes["formation"].Value,
                    new CultureInfo("de-DE"));
                Station allianceHeadquarter = Stations.GetStation(allianceNode.Attributes["headquarter"].Value);

                var alliance = new Alliance(formationDate, allianceName, allianceHeadquarter);

                XmlNodeList membersList = allianceNode.SelectNodes("members/member");

                foreach (XmlElement memberNode in membersList)
                {
                    Carrier allianceMember = Carriers.GetCarrier(memberNode.Attributes["carrier"].Value);
                    DateTime joinedDate = DateTime.Parse(
                        memberNode.Attributes["joined"].Value,
                        new CultureInfo("de-DE"));

                    if (allianceMember != null)
                    {
                        alliance.AddMember(new AllianceMember(allianceMember, joinedDate));
                    }
                }

                XmlNodeList pendingsList = allianceNode.SelectNodes("pendings/pending");

                foreach (XmlElement pendingNode in pendingsList)
                {
                    Carrier pendingCarrier = Carriers.GetCarrier(pendingNode.Attributes["carrier"].Value);
                    DateTime pendingDate = DateTime.Parse(
                        pendingNode.Attributes["date"].Value,
                        new CultureInfo("de-DE"));
                    var pendingType =
                        (PendingAllianceMember.AcceptType)
                        TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (PendingAllianceMember.AcceptType), pendingNode.Attributes["type"].Value);

                    alliance.AddPendingMember(
                        new PendingAllianceMember(pendingDate, alliance, pendingCarrier, pendingType));
                }

                Alliances.AddAlliance(alliance);
            }
            Configurations.Clear();

            XmlNodeList configurationsList = root.SelectNodes("//configurations/configuration");

            foreach (XmlElement confElement in configurationsList)
            {
                string confName = confElement.Attributes["name"].Value;
                string confid = confElement.Attributes["id"].Value;
                Boolean standard = Convert.ToBoolean(confElement.Attributes["standard"].Value);

                int minimumSeats = Convert.ToInt16(confElement.Attributes["minimumseats"].Value);

                var configuration = new CoachConfiguration(confName, minimumSeats, standard) {ID = confid};

                XmlNodeList classesList = confElement.SelectNodes("classes/class");

                foreach (XmlElement classElement in classesList)
                {
                    int seating = Convert.ToInt16(classElement.Attributes["seating"].Value);
                    int regularseating = Convert.ToInt16(classElement.Attributes["regularseating"].Value);
                    var classType =
                        (CoachClass.ClassType)
                        TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CoachClass.ClassType), classElement.Attributes["type"].Value);

                    var classConf = new CoachClassConfiguration(classType, seating, regularseating);
                    foreach (
                        CoachFacility.FacilityType facType in Enum.GetValues(typeof (CoachFacility.FacilityType)))
                    {
                        string facUid = classElement.Attributes[facType.ToString()].Value;

                        classConf.AddFacility(CoachFacilities.GetFacility(facType, facUid));
                    }

                    configuration.AddClassConfiguration(classConf);
                }
                Configurations.AddConfiguration(configuration);
            }

            XmlNodeList routeConfigurationsList =
                root.SelectNodes("//routeclassesconfigurations/routeclassesconfiguration");

            foreach (XmlElement confElement in routeConfigurationsList)
            {
                string routeConfName = confElement.Attributes["name"].Value;
                string confid = confElement.Attributes["id"].Value;
                Boolean standard = Convert.ToBoolean(confElement.Attributes["standard"].Value);

                XmlNodeList classesList = confElement.SelectNodes("classes/class");

                var classesConfiguration = new RouteClassesConfiguration(routeConfName, standard) {ID = confid};

                foreach (XmlElement classElement in classesList)
                {
                    var classType =
                        (CoachClass.ClassType)
                        TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CoachClass.ClassType), classElement.Attributes["type"].Value);

                    var classConf = new RouteClassConfiguration(classType);
                    foreach (RouteFacility.FacilityType facType in Enum.GetValues(typeof (RouteFacility.FacilityType)))
                    {
                        if (classElement.HasAttribute(facType.ToString()))
                        {
                            string facilityName = classElement.Attributes[facType.ToString()].Value;

                            classConf.AddFacility(
                                RouteFacilities.GetFacilities(facType).Find(f => f.Name == facilityName));
                        }
                    }

                    classesConfiguration.AddClass(classConf);
                }

                Configurations.AddConfiguration(classesConfiguration);
            }

            var difficultyNode = (XmlElement) root.SelectSingleNode("//difficulty");
            string difficultyName = difficultyNode.Attributes["name"].Value;
            double moneyLevel = Convert.ToDouble(
                difficultyNode.Attributes["money"].Value,
                new CultureInfo("de-DE", false));
            double priceLevel = Convert.ToDouble(
                difficultyNode.Attributes["price"].Value,
                new CultureInfo("de-DE", false));
            double loanLevel = Convert.ToDouble(
                difficultyNode.Attributes["loan"].Value,
                new CultureInfo("de-DE", false));
            double passengersLevel = Convert.ToDouble(
                difficultyNode.Attributes["passengers"].Value,
                new CultureInfo("de-DE", false));
            double aiLevel = Convert.ToDouble(difficultyNode.Attributes["ai"].Value, new CultureInfo("de-DE", false));

            GameObject.GetInstance().Difficulty = new DifficultyLevel(
                difficultyName,
                moneyLevel,
                loanLevel,
                passengersLevel,
                priceLevel,
                aiLevel,
                1);

            var scenarioNode = (XmlElement) root.SelectSingleNode("//scenario");

            if (scenarioNode != null)
            {
                Scenario scenario = Scenarios.GetScenario(scenarioNode.Attributes["name"].Value);

                var so = new ScenarioObject(scenario) {IsSuccess = Convert.ToBoolean(scenarioNode.Attributes["success"].Value)};

                if (scenarioNode.HasAttribute("failed"))
                {
                    so.ScenarioFailed = scenario.Failures.Find(f => f.ID == scenarioNode.Attributes["failed"].Value);
                }

                XmlNodeList failuresList = scenarioNode.SelectNodes("failures/failure");

                foreach (XmlElement failureNode in failuresList)
                {
                    ScenarioFailure failure = scenario.Failures.Find(f => f.ID == failureNode.Attributes["id"].Value);
                    int failureCount = Convert.ToInt16(failureNode.Attributes["count"].Value);
                    DateTime lastFailureTime = DateTime.Parse(
                        failureNode.Attributes["lastfailuretime"].Value,
                        new CultureInfo("de-DE", false));

                    so.GetScenarioFailure(failure).LastFailureTime = lastFailureTime;
                    so.GetScenarioFailure(failure).Failures = failureCount;
                }

                GameObject.GetInstance().Scenario = so;
            }

            var gameSettingsNode = (XmlElement) root.SelectSingleNode("//gamesettings");

            GameObject.GetInstance().Name = gameSettingsNode.Attributes["name"].Value;

            Carrier humanCarrier = Carriers.GetCarrier(gameSettingsNode.Attributes["human"].Value);
            GameObject.GetInstance().SetHumanCarrier(humanCarrier);

            Carrier mainCarrier = Carriers.GetCarrier(gameSettingsNode.Attributes["maincarrier"].Value);
            GameObject.GetInstance().MainCarrier = mainCarrier;

            double fuelPrice = Convert.ToDouble(
                gameSettingsNode.Attributes["fuelprice"].Value,
                new CultureInfo("de-DE", false));

            GameTimeZone timezone =
                TimeZones.GetTimeZones()
                         .Find(
                             gtz => gtz.UTCOffset == TimeSpan.Parse(gameSettingsNode.Attributes["timezone"].Value));
            GameObject.GetInstance().TimeZone = timezone;

            Infrastructure.Settings.GetInstance().MailsOnLandings =
                Convert.ToBoolean(gameSettingsNode.Attributes["mailonlandings"].Value);
            Infrastructure.Settings.GetInstance().MailsOnBadWeather =
                Convert.ToBoolean(gameSettingsNode.Attributes["mailonbadweather"].Value);

            Infrastructure.Settings.GetInstance().StationCodeDisplay =
                (StationCode)
                TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (StationCode), gameSettingsNode.Attributes["stationcode"].Value);
            if (gameSettingsNode.HasAttribute("minutesperturn"))
            {
                Infrastructure.Settings.GetInstance().MinutesPerTurn =
                    Convert.ToInt16(gameSettingsNode.Attributes["minutesperturn"].Value);
            }
            AppSettings.GetInstance().SetLanguage(Languages.GetLanguage(gameSettingsNode.Attributes["language"].Value));
            GameObject.GetInstance().DayRoundEnabled = Convert.ToBoolean(gameSettingsNode.Attributes["dayround"].Value);

            XmlNodeList itemsList = gameSettingsNode.SelectNodes("calendaritems/calendaritem");

            CalendarItems.Clear();

            foreach (XmlElement itemNode in itemsList)
            {
                var itemType =
                    (CalendarItem.ItemType) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CalendarItem.ItemType), itemNode.Attributes["type"].Value);
                DateTime itemDate = DateTime.Parse(itemNode.Attributes["date"].Value, new CultureInfo("de-DE", false));
                string itemHeader = itemNode.Attributes["header"].Value;
                string itemSubject = itemNode.Attributes["subject"].Value;

                CalendarItems.AddCalendarItem(new CalendarItem(itemType, itemDate, itemHeader, itemSubject));
            }

            XmlNodeList newsList = gameSettingsNode.SelectNodes("news/new");
            GameObject.GetInstance().NewsBox.Clear();

            foreach (XmlElement newsNode in newsList)
            {
                DateTime newsDate = DateTime.Parse(newsNode.Attributes["date"].Value, new CultureInfo("de-DE", false));
                var newsType = (News.NewsType) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (News.NewsType), newsNode.Attributes["type"].Value);
                string newsSubject = newsNode.Attributes["subject"].Value;
                string newsBody = newsNode.Attributes["body"].Value;
                Boolean newsIsRead = Convert.ToBoolean(newsNode.Attributes["isread"].Value);

                var news = new News(newsType, newsDate, newsSubject, newsBody) {IsRead = newsIsRead};

                GameObject.GetInstance().NewsBox.AddNews(news);
            }
            /*
               foreach (Carrier carrier in Carriers.GetAllCarriers())
               {
                   foreach (Route route in carrier.Routes)
                   {
                       Gate gate1 = route.Destination1.Terminals.getEmptyGate(carrier);
                       Gate gate2 = route.Destination2.Terminals.getEmptyGate(carrier);

                       if (gate1!=null) gate1.Route = route;
                       if (gate2!=null) gate2.Route = route;

                   }
               }
            
               */
        }

        //saves the game
        public static void SaveGame(string file)
        {
            var sw = new Stopwatch();
            sw.Start();

            string path = AppSettings.GetCommonApplicationDataPath() + "/saves/" + file + ".sav";

            var xmlDoc = new XmlDocument();

            XmlDeclaration xmlDeclaration = xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", null);
            xmlDoc.AppendChild(xmlDeclaration);

            XmlElement root = xmlDoc.CreateElement("game");
            xmlDoc.AppendChild(root);
            /*
            XmlTextWriter xmlWriter = new XmlTextWriter(path, System.Text.Encoding.UTF8);
            xmlWriter.Formatting = Formatting.Indented;
            xmlWriter.WriteProcessingInstruction("xml", "version='1.0' encoding='UTF-8'");
            xmlWriter.WriteStartElement("game");
            xmlWriter.Close();
            xmlDoc.Load(path);
            */

            root.SetAttribute("time", GameObject.GetInstance().GameTime.ToString(new CultureInfo("de-DE")));

            XmlElement tailnumbersNode = xmlDoc.CreateElement("tailnumbers");

            foreach (Country country in Countries.GetAllCountries())
            {
                XmlElement tailnumberNode = xmlDoc.CreateElement("tailnumber");
                tailnumberNode.SetAttribute("country", country.Uid);
                tailnumberNode.SetAttribute(
                    "value",
                    country.TailNumbers.LastTailNumber ?? country.TailNumbers.GetNextTailNumber());

                tailnumbersNode.AppendChild(tailnumberNode);
            }
            root.AppendChild(tailnumbersNode);

            XmlElement coachTypesNode = xmlDoc.CreateElement("coachtypes");

            foreach (CoachType coachType in CoachTypes.GetNonStandardTypes())
            {
                XmlElement coachTypeNode = xmlDoc.CreateElement("coachtype");
                coachTypeNode.SetAttribute("type", coachType.TypeCoach.ToString());
                coachTypeNode.SetAttribute("basetype", coachType.BaseType.Name);
                coachTypeNode.SetAttribute("name", coachType.Name);

                if (coachType.TypeCoach == CoachType.TypeOfCoach.Passenger)
                {
                    coachTypeNode.SetAttribute(
                        "attendants",
                        ((CoachPassengerType) coachType).Attendants.ToString(CultureInfo.InvariantCulture));
                    coachTypeNode.SetAttribute(
                        "passengers",
                        ((CoachPassengerType) coachType).MaxSeatingCapacity.ToString(CultureInfo.InvariantCulture));
                    coachTypeNode.SetAttribute(
                        "maxclasses",
                        ((CoachPassengerType) coachType).MaxCoachClasses.ToString(CultureInfo.InvariantCulture));
                }

                coachTypesNode.AppendChild(coachTypeNode);
            }

            root.AppendChild(coachTypesNode);

            XmlElement coachesNode = xmlDoc.CreateElement("coaches");

            foreach (Coach coach in Coaches.GetAllCoaches()) //Parallel.ForEach(Coaches.GetAllCoaches(),coach=>
            {
                XmlElement coachNode = xmlDoc.CreateElement("coach");
                coachNode.SetAttribute("type", coach.Type.Name);
                coachNode.SetAttribute("tailnumber", coach.TailNumber);
                coachNode.SetAttribute("id", coach.ID);

                coachNode.SetAttribute("last_service", coach.LastServiceCheck.ToString(CultureInfo.InvariantCulture));
                coachNode.SetAttribute("built", coach.BuiltDate.ToString(new CultureInfo("de-DE")));
                coachNode.SetAttribute("run", coach.Flown.ToString(new CultureInfo("de-DE", false)));
                coachNode.SetAttribute("damaged", coach.Condition.ToString(CultureInfo.InvariantCulture));

                XmlElement coachClassesNode = xmlDoc.CreateElement("classes");
                foreach (CoachClass aClass in coach.Classes)
                {
                    XmlElement coachClassNode = xmlDoc.CreateElement("class");
                    coachClassNode.SetAttribute("type", aClass.Type.ToString());
                    coachClassNode.SetAttribute("seating", aClass.SeatingCapacity.ToString(CultureInfo.InvariantCulture));

                    XmlElement coachClassFacilitiesNode = xmlDoc.CreateElement("facilities");
                    foreach (
                        CoachFacility.FacilityType facilityType in
                            Enum.GetValues(typeof (CoachFacility.FacilityType)))
                    {
                        CoachFacility acFacility = aClass.GetFacility(facilityType);

                        XmlElement coachClassFacilityNode = xmlDoc.CreateElement("facility");
                        coachClassFacilityNode.SetAttribute("type", acFacility.Type.ToString());
                        coachClassFacilityNode.SetAttribute("uid", acFacility.Uid);

                        coachClassFacilitiesNode.AppendChild(coachClassFacilityNode);
                    }

                    coachClassNode.AppendChild(coachClassFacilitiesNode);

                    coachClassesNode.AppendChild(coachClassNode);
                }
                coachNode.AppendChild(coachClassesNode);

                coachesNode.AppendChild(coachNode);
            }
            ;

            root.AppendChild(coachesNode);

            // XmlElement passengersNode = xmlDoc.CreateElement("passengers");

            XmlElement carriersNode = xmlDoc.CreateElement("carriers");
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                // chs, 2011-21-10 changed for the possibility of creating a new carrier
                XmlElement carrierNode = xmlDoc.CreateElement("carrier");
                carrierNode.SetAttribute("name", carrier.Profile.Name);
                carrierNode.SetAttribute("code", carrier.Profile.IATACode);
                carrierNode.SetAttribute("subsidiary", carrier.IsSubsidiary.ToString());
                if (carrier.IsSubsidiary)
                {
                    carrierNode.SetAttribute("parentcarrier", ((SubsidiaryCarrier) carrier).Carrier.Profile.IATACode);
                }

                carrierNode.SetAttribute("country", carrier.Profile.Country.Uid);
                carrierNode.SetAttribute("color", carrier.Profile.Color);
                //carrierNode.SetAttribute("logo", carrier.Profile.Logo.Substring(carrier.Profile.Logo.LastIndexOf('\\') + 1));
                carrierNode.SetAttribute("CEO", carrier.Profile.CEO);
                carrierNode.SetAttribute("money", string.Format("{0:0}", carrier.Money));
                carrierNode.SetAttribute("reputation", carrier.Reputation.ToString(CultureInfo.InvariantCulture));
                carrierNode.SetAttribute("mentality", carrier.Mentality.ToString());
                carrierNode.SetAttribute("market", carrier.MarketFocus.ToString());
                carrierNode.SetAttribute("license", carrier.License.ToString());
                carrierNode.SetAttribute("routefocus", carrier.CarrierRouteFocus.ToString());
                carrierNode.SetAttribute("isreal", carrier.Profile.IsReal.ToString());
                carrierNode.SetAttribute("founded", carrier.Profile.Founded.ToString(CultureInfo.InvariantCulture));
                carrierNode.SetAttribute("folded", carrier.Profile.Folded.ToString(CultureInfo.InvariantCulture));

                XmlElement carrierLogosList = xmlDoc.CreateElement("logos");

                foreach (CarrierLogo logo in carrier.Profile.Logos)
                {
                    XmlElement logoElement = xmlDoc.CreateElement("logo");

                    logoElement.SetAttribute("from", logo.FromYear.ToString(CultureInfo.InvariantCulture));
                    logoElement.SetAttribute("to", logo.ToYear.ToString(CultureInfo.InvariantCulture));
                    logoElement.SetAttribute("path", logo.Path);

                    carrierLogosList.AppendChild(logoElement);
                }
                carrierNode.AppendChild(carrierLogosList);

                if (carrier.Contract != null)
                {
                    XmlElement carrierContractNode = xmlDoc.CreateElement("contract");
                    carrierContractNode.SetAttribute("manufacturer", carrier.Contract.Manufacturer.ShortName);
                    carrierContractNode.SetAttribute(
                        "signingdate",
                        carrier.Contract.SigningDate.ToString(new CultureInfo("de-DE")));
                    carrierContractNode.SetAttribute("length", carrier.Contract.Length.ToString(CultureInfo.InvariantCulture));
                    carrierContractNode.SetAttribute(
                        "discount",
                        carrier.Contract.Discount.ToString(new CultureInfo("de-DE", false)));
                    carrierContractNode.SetAttribute("coaches", carrier.Contract.PurchasedCoaches.ToString(CultureInfo.InvariantCulture));

                    carrierNode.AppendChild(carrierContractNode);
                }

                // chs, 2011-13-10 added for saving of passenger happiness
                XmlElement carrierHappinessNode = xmlDoc.CreateElement("passengerhappiness");
                carrierHappinessNode.SetAttribute("value", "");
                // PassengerHelpers.GetHappinessValue(carrier).ToString(new CultureInfo("de-DE", false)));

                carrierNode.AppendChild(carrierHappinessNode);

                XmlElement carrierFacilitiesNode = xmlDoc.CreateElement("facilities");

                foreach (CarrierFacility facility in carrier.Facilities)
                {
                    XmlElement carrierFacilityNode = xmlDoc.CreateElement("facility");
                    carrierFacilityNode.SetAttribute("uid", facility.Uid);

                    carrierFacilitiesNode.AppendChild(carrierFacilityNode);
                }
                carrierNode.AppendChild(carrierFacilitiesNode);

                XmlElement carrierPoliciesNode = xmlDoc.CreateElement("policies");

                foreach (CarrierPolicy policy in carrier.Policies)
                {
                    XmlElement carrierPolicyNode = xmlDoc.CreateElement("policy");

                    carrierPolicyNode.SetAttribute("name", policy.Name);
                    carrierPolicyNode.SetAttribute("value", policy.PolicyValue.ToString());

                    carrierPoliciesNode.AppendChild(carrierPolicyNode);
                }

                carrierNode.AppendChild(carrierPoliciesNode);

                XmlElement loansNode = xmlDoc.CreateElement("loans");
                foreach (Loan loan in carrier.Loans)
                {
                    XmlElement loanNode = xmlDoc.CreateElement("loan");
                    loanNode.SetAttribute("date", loan.Date.ToString(new CultureInfo("de-DE")));
                    loanNode.SetAttribute("rate", loan.Rate.ToString(new CultureInfo("de-DE", false)));
                    loanNode.SetAttribute("amount", loan.Amount.ToString(new CultureInfo("de-DE", false)));
                    loanNode.SetAttribute("length", loan.Length.ToString(CultureInfo.InvariantCulture));
                    loanNode.SetAttribute("payment", loan.PaymentLeft.ToString(new CultureInfo("de-DE", false)));

                    loansNode.AppendChild(loanNode);
                }
                carrierNode.AppendChild(loansNode);

                XmlElement carrierStatsNode = xmlDoc.CreateElement("stats");
                foreach (StatisticsType type in StatisticsTypes.GetStatisticsTypes())
                {
                    foreach (int year in carrier.Statistics.GetYears())
                    {
                        XmlElement carrierStatNode = xmlDoc.CreateElement("stat");

                        double value = carrier.Statistics.GetStatisticsValue(year, type);

                        carrierStatNode.SetAttribute("year", year.ToString(CultureInfo.InvariantCulture));
                        carrierStatNode.SetAttribute("type", type.Shortname);
                        carrierStatNode.SetAttribute("value", value.ToString(new CultureInfo("de-DE", false)));

                        carrierStatsNode.AppendChild(carrierStatNode);
                    }
                }
                carrierNode.AppendChild(carrierStatsNode);

                XmlElement invoicesNode = xmlDoc.CreateElement("invoices");
                foreach (MonthlyInvoice invoice in carrier.GetInvoices().MonthlyInvoices)
                {
                    XmlElement invoiceNode = xmlDoc.CreateElement("invoice");
                    invoiceNode.SetAttribute("type", invoice.Type.ToString());
                    invoiceNode.SetAttribute("year", invoice.Year.ToString(CultureInfo.InvariantCulture));
                    invoiceNode.SetAttribute("month", invoice.Month.ToString(CultureInfo.InvariantCulture));
                    invoiceNode.SetAttribute("amount", invoice.Amount.ToString(new CultureInfo("de-DE", false)));

                    invoicesNode.AppendChild(invoiceNode);
                }
                carrierNode.AppendChild(invoicesNode);

                // chs, 2011-14-10 added for saving of carrier advertisement
                XmlElement advertisementsNodes = xmlDoc.CreateElement("advertisements");
                foreach (
                    AdvertisementType.CarrierAdvertisementType type in
                        Enum.GetValues(typeof (AdvertisementType.CarrierAdvertisementType)))
                {
                    XmlElement advertisementNode = xmlDoc.CreateElement("advertisement");
                    advertisementNode.SetAttribute("type", type.ToString());
                    advertisementNode.SetAttribute("name", carrier.GetCarrierAdvertisement(type).Name);

                    advertisementsNodes.AppendChild(advertisementNode);
                }
                carrierNode.AppendChild(advertisementsNodes);

                // chs, 2011-17-10 added for saving of carrier fees
                XmlElement feesNode = xmlDoc.CreateElement("fees");
                foreach (FeeType feetype in FeeTypes.GetTypes())
                {
                    XmlElement feeNode = xmlDoc.CreateElement("fee");
                    feeNode.SetAttribute("type", feetype.Name);
                    feeNode.SetAttribute(
                        "value",
                        carrier.Fees.GetValue(feetype).ToString(new CultureInfo("de-DE", false)));

                    feesNode.AppendChild(feeNode);
                }

                carrierNode.AppendChild(feesNode);

                XmlElement tripSchoolsNode = xmlDoc.CreateElement("tripschools");
                foreach (TripSchool fs in carrier.TripSchools)
                {
                    XmlElement tripSchoolNode = xmlDoc.CreateElement("tripschool");
                    tripSchoolNode.SetAttribute("id", fs.ID);
                    tripSchoolNode.SetAttribute("station", fs.Station.Profile.ID);

                    XmlElement trainingCoachesNode = xmlDoc.CreateElement("trainingcoaches");
                    foreach (TrainingCoach coach in fs.TrainingCoaches)
                    {
                        XmlElement trainingCoachNode = xmlDoc.CreateElement("trainingcoach");
                        trainingCoachNode.SetAttribute("type", coach.Type.Name);
                        trainingCoachNode.SetAttribute(
                            "date",
                            coach.BoughtDate.ToString(new CultureInfo("de-DE")));

                        trainingCoachesNode.AppendChild(trainingCoachNode);
                    }

                    tripSchoolNode.AppendChild(trainingCoachesNode);

                    XmlElement studentsNode = xmlDoc.CreateElement("students");
                    foreach (DriverStudent student in fs.Students)
                    {
                        XmlElement studentNode = xmlDoc.CreateElement("student");

                        studentNode.SetAttribute("firstname", student.Profile.Firstname);
                        studentNode.SetAttribute("lastname", student.Profile.Lastname);
                        studentNode.SetAttribute(
                            "birthdate",
                            student.Profile.Birthdate.ToString(new CultureInfo("de-DE")));
                        studentNode.SetAttribute("town", student.Profile.Town.Name);
                        studentNode.SetAttribute("startdate", student.StartDate.ToString(new CultureInfo("de-DE")));

                        studentsNode.AppendChild(studentNode);
                    }

                    tripSchoolNode.AppendChild(studentsNode);
                    tripSchoolsNode.AppendChild(tripSchoolNode);
                }

                carrierNode.AppendChild(tripSchoolsNode);

                XmlElement fleetNode = xmlDoc.CreateElement("fleet");
                foreach (FleetCoach coach in carrier.Fleet)
                {
                    XmlElement fleetCoachNode = xmlDoc.CreateElement("coach");
                    fleetCoachNode.SetAttribute("coach", coach.Coach.TailNumber);
                    fleetCoachNode.SetAttribute("name", coach.Coach.ID);
                    fleetCoachNode.SetAttribute("homebase", coach.Homebase.Profile.IATACode);
                    fleetCoachNode.SetAttribute("purchased", coach.Purchased.ToString());
                    fleetCoachNode.SetAttribute("date", coach.PurchasedDate.ToString(new CultureInfo("de-DE")));
                    fleetCoachNode.SetAttribute(
                        "groundeddate",
                        coach.GroundedToDate.ToString(new CultureInfo("de-DE")));

                    fleetCoachNode.SetAttribute("status", coach.Status.ToString());
                    // fleetCoachNode.SetAttribute("latitude", coach.CurrentPosition.Latitude.ToString());
                    // fleetCoachNode.SetAttribute("longitude", coach.CurrentPosition.Longitude.ToString());

                    XmlElement coachStatsNode = xmlDoc.CreateElement("stats");
                    foreach (StatisticsType type in StatisticsTypes.GetStatisticsTypes())
                    {
                        foreach (int year in coach.Statistics.GetYears())
                        {
                            XmlElement coachStatNode = xmlDoc.CreateElement("stat");

                            double value = coach.Statistics.GetStatisticsValue(year, type);
                            coachStatNode.SetAttribute("year", year.ToString(CultureInfo.InvariantCulture));
                            coachStatNode.SetAttribute("type", type.Shortname);
                            coachStatNode.SetAttribute("value", value.ToString(new CultureInfo("de-DE", false)));

                            coachStatsNode.AppendChild(coachStatNode);
                        }
                    }

                    fleetCoachNode.AppendChild(coachStatsNode);

                    fleetNode.AppendChild(fleetCoachNode);
                }
                carrierNode.AppendChild(fleetNode);

                XmlElement routesNode = xmlDoc.CreateElement("routes");
                foreach (Route route in carrier.Routes)
                {
                    routesNode.AppendChild(SaveRoute(xmlDoc, route));
                }
                carrierNode.AppendChild(routesNode);

                XmlElement tripsNode = xmlDoc.CreateElement("trips");
                foreach (FleetCoach coach in carrier.Fleet)
                {
                    if (coach.CurrentTrip != null && coach.CurrentTrip.Entry != null)
                    {
                        XmlElement tripNode = xmlDoc.CreateElement("trip");

                        tripNode.SetAttribute("coach", coach.Coach.ID);
                        tripNode.SetAttribute("route", coach.CurrentTrip.Entry.TimeTable.Route.Id);
                        tripNode.SetAttribute("destination", coach.CurrentTrip.Entry.Destination.TripCode);
                        tripNode.SetAttribute("day", coach.CurrentTrip.Entry.Day.ToString());
                        tripNode.SetAttribute("time", coach.CurrentTrip.Entry.Time.ToString());
                        tripNode.SetAttribute(
                            "triptime",
                            coach.CurrentTrip.TripTime.ToString(new CultureInfo("de-DE")));

                        var trip = coach.CurrentTrip as StopoverTrip;
                        if (trip != null)
                        {
                            XmlElement stopoverNode = xmlDoc.CreateElement("stopover");
                            stopoverNode.SetAttribute(
                                "currenttrip",
                                trip.CurrentTrip.ToString(CultureInfo.InvariantCulture));

                            if (trip.IsPassengerTrip())
                            {
                                XmlElement stopoverClassesNode = xmlDoc.CreateElement("classes");

                                foreach (
                                    RouteTimeTableEntry stopEntry in
                                        trip.AllClasses.Keys)
                                {
                                    XmlElement stopoverClassNode = xmlDoc.CreateElement("class");
                                    stopoverClassNode.SetAttribute("id", stopEntry.ID);

                                    XmlElement stopoverTripClassesNode = xmlDoc.CreateElement("tripclasses");
                                    foreach (
                                        TripCoachClass faClass in
                                            trip.AllClasses[stopEntry])
                                    {
                                        XmlElement stopoverTripClassNode = xmlDoc.CreateElement("tripclass");
                                        stopoverTripClassNode.SetAttribute(
                                            "type",
                                            faClass.CoachClass.Type.ToString());
                                        stopoverTripClassNode.SetAttribute(
                                            "passengers",
                                            faClass.Passengers.ToString(CultureInfo.InvariantCulture));

                                        stopoverTripClassesNode.AppendChild(stopoverTripClassNode);
                                    }
                                    stopoverClassNode.AppendChild(stopoverTripClassesNode);
                                    stopoverClassesNode.AppendChild(stopoverClassNode);
                                }

                                stopoverNode.AppendChild(stopoverClassesNode);
                            }
                            tripNode.AppendChild(stopoverNode);
                        }

                        if (coach.CurrentTrip.IsPassengerTrip())
                        {
                            XmlElement tripClassesNode = xmlDoc.CreateElement("tripclasses");
                            foreach (TripCoachClass aClass in coach.CurrentTrip.Classes)
                            {
                                XmlElement tripClassNode = xmlDoc.CreateElement("tripclass");
                                tripClassNode.SetAttribute("type", aClass.CoachClass.Type.ToString());
                                tripClassNode.SetAttribute("passengers", aClass.Passengers.ToString(CultureInfo.InvariantCulture));

                                tripClassesNode.AppendChild(tripClassNode);
                            }
                            tripNode.AppendChild(tripClassesNode);
                        }


                        tripsNode.AppendChild(tripNode);
                    }
                }

                carrierNode.AppendChild(tripsNode);

                carriersNode.AppendChild(carrierNode);
            }
            root.AppendChild(carriersNode);

            XmlElement stationsNode = xmlDoc.CreateElement("stations");
            foreach (Station station in Stations.GetAllStations())
            {
                XmlElement stationNode = xmlDoc.CreateElement("station");
                stationNode.SetAttribute("id", station.Profile.ID);
                stationNode.SetAttribute("income", station.Income.ToString(CultureInfo.InvariantCulture));

                XmlElement stationPaxsNode = xmlDoc.CreateElement("paxvalues");
                foreach (PaxValue paxValue in station.Profile.PaxValues)
                {
                    XmlElement stationPaxNode = xmlDoc.CreateElement("paxvalue");
                    stationPaxNode.SetAttribute("from", paxValue.FromYear.ToString(CultureInfo.InvariantCulture));
                    stationPaxNode.SetAttribute("to", paxValue.ToYear.ToString(CultureInfo.InvariantCulture));
                    stationPaxNode.SetAttribute("size", paxValue.Size.ToString());
                    stationPaxNode.SetAttribute("pax", paxValue.Pax.ToString(CultureInfo.InvariantCulture));
                    stationPaxNode.SetAttribute("inflationbefore", paxValue.InflationBeforeYear.ToString(CultureInfo.InvariantCulture));
                    stationPaxNode.SetAttribute("inflationafter", paxValue.InflationAfterYear.ToString(CultureInfo.InvariantCulture));

                    stationPaxsNode.AppendChild(stationPaxNode);
                }
                stationNode.AppendChild(stationPaxsNode);

                XmlElement stationBaysNode = xmlDoc.CreateElement("bays");

                foreach (Bay bay in station.Bays.FindAll(r => !r.Standard))
                {
                    XmlElement stationBayNode = xmlDoc.CreateElement("bay");
                    stationBayNode.SetAttribute("name", bay.Name);
                    stationBayNode.SetAttribute("lenght", bay.Length.ToString(CultureInfo.InvariantCulture));
                    stationBayNode.SetAttribute("surface", bay.Surface.ToString());
                    stationBayNode.SetAttribute("date", bay.BuiltDate.ToString(new CultureInfo("de-DE")));

                    stationBaysNode.AppendChild(stationBayNode);
                }

                stationNode.AppendChild(stationBaysNode);

                XmlElement stationHubsNode = xmlDoc.CreateElement("hubs");
                foreach (Hub hub in station.GetHubs(HubType.TypeOfHub.Hub))
                {
                    XmlElement stationHubNode = xmlDoc.CreateElement("hub");
                    stationHubNode.SetAttribute("carrier", hub.Carrier.Profile.IATACode);

                    stationHubsNode.AppendChild(stationHubNode);
                }

                stationNode.AppendChild(stationHubsNode);

                XmlElement stationWeathersNode = xmlDoc.CreateElement("weathers");
                foreach (Weather weather in station.Weather)
                {
                    if (weather != null)
                    {
                        XmlElement stationWeatherNode = xmlDoc.CreateElement("weather");
                        stationWeatherNode.SetAttribute("date", weather.Date.ToString(new CultureInfo("de-DE")));
                        stationWeatherNode.SetAttribute("direction", weather.Direction.ToString());
                        stationWeatherNode.SetAttribute("windspeed", weather.WindSpeed.ToString());
                        stationWeatherNode.SetAttribute("cover", weather.Cover.ToString());
                        stationWeatherNode.SetAttribute("precip", weather.Precip.ToString());
                        stationWeatherNode.SetAttribute(
                            "temperaturelow",
                            weather.TemperatureLow.ToString(new CultureInfo("de-DE", false)));
                        stationWeatherNode.SetAttribute(
                            "temperaturehigh",
                            weather.TemperatureHigh.ToString(new CultureInfo("de-DE", false)));

                        XmlElement temperaturesNode = xmlDoc.CreateElement("temperatures");
                        foreach (HourlyWeather t in weather.Temperatures)
                        {
                            XmlElement temperatureNode = xmlDoc.CreateElement("temperature");
                            temperatureNode.SetAttribute(
                                "temp",
                                t.Temperature.ToString(new CultureInfo("de-DE", false)));
                            temperatureNode.SetAttribute("cover", t.Cover.ToString());
                            temperatureNode.SetAttribute("precip", t.Precip.ToString());
                            temperatureNode.SetAttribute("windspeed", t.WindSpeed.ToString());
                            temperatureNode.SetAttribute("direction", t.Direction.ToString());

                            temperaturesNode.AppendChild(temperatureNode);
                        }
                        stationWeatherNode.AppendChild(temperaturesNode);

                        stationWeathersNode.AppendChild(stationWeatherNode);
                    }
                }
                stationNode.AppendChild(stationWeathersNode);

                XmlElement stationStatsNode = xmlDoc.CreateElement("stats");
                foreach (Carrier carrier in Carriers.GetAllCarriers())
                {
                    foreach (StatisticsType type in StatisticsTypes.GetStatisticsTypes())
                    {
                        foreach (int year in station.Statistics.GetYears())
                        {
                            double value = station.Statistics.GetStatisticsValue(year, carrier, type);

                            if (value > 0)
                            {
                                XmlElement stationStatNode = xmlDoc.CreateElement("stat");

                                stationStatNode.SetAttribute("year", year.ToString(CultureInfo.InvariantCulture));
                                stationStatNode.SetAttribute("carrier", carrier.Profile.IATACode);
                                stationStatNode.SetAttribute("type", type.Shortname);
                                stationStatNode.SetAttribute("value", value.ToString(new CultureInfo("de-DE", false)));

                                stationStatsNode.AppendChild(stationStatNode);
                            }
                        }
                    }
                }

                stationNode.AppendChild(stationStatsNode);

                XmlElement stationFacilitiesNode = xmlDoc.CreateElement("facilities");
                foreach (Carrier carrier in Carriers.GetAllCarriers())
                {
                    foreach (CarrierStationFacility facility in station.GetStationFacilities(carrier))
                    {
                        XmlElement stationFacilityNode = xmlDoc.CreateElement("facility");
                        stationFacilityNode.SetAttribute("carrier", carrier.Profile.IATACode);
                        stationFacilityNode.SetAttribute("name", facility.Facility.Shortname);
                        stationFacilityNode.SetAttribute(
                            "finished",
                            facility.FinishedDate.ToString(new CultureInfo("de-DE")));

                        stationFacilitiesNode.AppendChild(stationFacilityNode);
                    }
                }

                foreach (CarrierStationFacility facility in station.GetStationFacilities().Where(f => f.Carrier == null)
                    )
                {
                    XmlElement stationFacilityNode = xmlDoc.CreateElement("facility");
                    stationFacilityNode.SetAttribute("carrier", "-");
                    stationFacilityNode.SetAttribute("name", facility.Facility.Shortname);
                    stationFacilityNode.SetAttribute(
                        "finished",
                        facility.FinishedDate.ToString(new CultureInfo("de-DE")));

                    stationFacilitiesNode.AppendChild(stationFacilityNode);
                }
                stationNode.AppendChild(stationFacilitiesNode);

                // chs, 2011-02-11 added for saving the terminals
                XmlElement terminalsNode = xmlDoc.CreateElement("terminals");
                foreach (Terminal terminal in station.Terminals.GetTerminals())
                {
                    XmlElement terminalNode = xmlDoc.CreateElement("terminal");
                    terminalNode.SetAttribute("delivery", terminal.DeliveryDate.ToString(new CultureInfo("de-DE")));
                    terminalNode.SetAttribute(
                        "owner",
                        terminal.Carrier == null ? "station" : terminal.Carrier.Profile.IATACode);
                    terminalNode.SetAttribute("name", terminal.Name);
                    terminalNode.SetAttribute("totalgates", terminal.Gates.GetGates().Count.ToString(CultureInfo.InvariantCulture));
                    XmlElement gatesNode = xmlDoc.CreateElement("gates");
                    foreach (Gate gate in terminal.Gates.GetGates())
                    {
                        XmlElement gateNode = xmlDoc.CreateElement("gate");
                        gateNode.SetAttribute("delivery", gate.DeliveryDate.ToString(new CultureInfo("de-DE")));

                        gatesNode.AppendChild(gateNode);
                    }
                    terminalNode.AppendChild(gatesNode);

                    terminalsNode.AppendChild(terminalNode);
                }
                stationNode.AppendChild(terminalsNode);

                XmlElement contractsNode = xmlDoc.CreateElement("contracts");

                foreach (StationContract contract in station.CarrierContracts)
                {
                    XmlElement contractNode = xmlDoc.CreateElement("contract");

                    contractNode.SetAttribute("carrier", contract.Carrier.Profile.IATACode);
                    contractNode.SetAttribute("length", contract.Length.ToString(CultureInfo.InvariantCulture));
                    contractNode.SetAttribute("date", contract.ContractDate.ToString(new CultureInfo("de-DE")));
                    contractNode.SetAttribute("gates", contract.NumberOfGates.ToString(CultureInfo.InvariantCulture));
                    contractNode.SetAttribute(
                        "payment",
                        contract.YearlyPayment.ToString(new CultureInfo("de-DE", false)));
                    contractNode.SetAttribute("exclusive", contract.IsExclusiveDeal.ToString());

                    if (contract.Terminal != null)
                    {
                        contractNode.SetAttribute("terminal", contract.Terminal.Name);
                    }

                    contractsNode.AppendChild(contractNode);
                }
                stationNode.AppendChild(contractsNode);

                stationsNode.AppendChild(stationNode);
            }
            root.AppendChild(stationsNode);

            sw.Restart();

            XmlElement stationDestinationsNode = xmlDoc.CreateElement("stationdestinations");

            List<Station> destStations = Stations.GetAllStations();

            foreach (Station station in Stations.GetAllStations())
            {
                XmlElement stationDestinationNode = xmlDoc.CreateElement("stationdestination");
                stationDestinationNode.SetAttribute("id", station.Profile.IATACode);

                XmlElement destinationsNode = xmlDoc.CreateElement("destinations");
                //var dests = Stations.GetStations(a => a != station && (station.hasDestinationPassengersRate(a) || station.hasDestinationStatistics(a)));
                //foreach (Station dest in Stations.GetStations(a => a != station && (station.hasDestinationPassengersRate(a) || station.hasDestinationPassengerStatistics(a) || station.hasDestinationCargStatistics(a))))
                //Parallel.ForEach(dests, dest =>
                foreach (Station dest in destStations)
                {
                    if (dest != station
                        && (station.HasDestinationPassengersRate(dest)
                            || station.HasDestinationPassengerStatistics(dest)
                            || station.HasDestinationCargStatistics(dest)))
                    {
                        XmlElement destinationNode = xmlDoc.CreateElement("destination");
                        destinationNode.SetAttribute("id", dest.Profile.IATACode);
                        destinationNode.SetAttribute(
                            "rate",
                            station.GetDestinationPassengersRate(dest, CoachClass.ClassType.EconomyClass).ToString(CultureInfo.InvariantCulture));
                        destinationNode.SetAttribute(
                            "passengers",
                            station.GetDestinationPassengerStatistics(dest).ToString(CultureInfo.InvariantCulture));

                        destinationNode.SetAttribute("cargo", station.GetDestinationCargoRate(dest).ToString(CultureInfo.InvariantCulture));
                        destinationNode.SetAttribute(
                            "cargostats",
                            station.GetDestinationCargoStatistics(dest).ToString(CultureInfo.InvariantCulture));

                        destinationsNode.AppendChild(destinationNode);
                    }
                } //);
                stationDestinationNode.AppendChild(destinationsNode);
                stationDestinationsNode.AppendChild(stationDestinationNode);
            }

            root.AppendChild(stationDestinationsNode);

            Console.WriteLine(@"Saving of destinations: {0} ms.", sw.ElapsedMilliseconds);

            XmlElement instructorsNode = xmlDoc.CreateElement("instructors");

            foreach (Instructor instructor in Instructors.GetInstructors())
            {
                XmlElement instructorNode = xmlDoc.CreateElement("instructor");
                instructorNode.SetAttribute("firstname", instructor.Profile.Firstname);
                instructorNode.SetAttribute("lastname", instructor.Profile.Lastname);
                instructorNode.SetAttribute(
                    "birthdate",
                    instructor.Profile.Birthdate.ToString(new CultureInfo("de-DE")));
                instructorNode.SetAttribute("town", instructor.Profile.Town.Name);
                instructorNode.SetAttribute("rating", instructor.Rating.ToString());
                instructorNode.SetAttribute("id", instructor.TripSchool == null ? "-" : instructor.TripSchool.ID);

                XmlElement studentsNode = xmlDoc.CreateElement("students");

                foreach (DriverStudent student in instructor.Students)
                {
                    XmlElement studentNode = xmlDoc.CreateElement("student");
                    studentNode.SetAttribute("name", student.Profile.Name);

                    studentsNode.AppendChild(studentNode);
                }

                instructorNode.AppendChild(studentsNode);

                instructorsNode.AppendChild(instructorNode);
            }

            root.AppendChild(instructorsNode);

            XmlElement driversNode = xmlDoc.CreateElement("drivers");

            foreach (Driver driver in Drivers.GetDrivers())
            {
                XmlElement driverNode = xmlDoc.CreateElement("driver");
                driverNode.SetAttribute("firstname", driver.Profile.Firstname);
                driverNode.SetAttribute("lastname", driver.Profile.Lastname);
                driverNode.SetAttribute("birthdate", driver.Profile.Birthdate.ToString(new CultureInfo("de-DE")));
                driverNode.SetAttribute("town", driver.Profile.Town.Name);
                driverNode.SetAttribute("education", driver.EducationTime.ToString(new CultureInfo("de-DE")));
                driverNode.SetAttribute("rating", driver.Rating.ToString());
                driverNode.SetAttribute("carrier", driver.Carrier == null ? "-" : driver.Carrier.Profile.IATACode);
                driverNode.SetAttribute("carriersigned", driver.CarrierSignedDate.ToString(new CultureInfo("de-DE")));
                driverNode.SetAttribute("coach", driver.Coach == null ? "-" : driver.Coach.Coach.ID);

                driversNode.AppendChild(driverNode);
            }

            root.AppendChild(driversNode);

            XmlElement alliancesNode = xmlDoc.CreateElement("alliances");
            foreach (Alliance alliance in Alliances.GetAlliances())
            {
                XmlElement allianceNode = xmlDoc.CreateElement("alliance");
                allianceNode.SetAttribute("name", alliance.Name);
                allianceNode.SetAttribute("formation", alliance.FormationDate.ToString(new CultureInfo("de-DE")));
                allianceNode.SetAttribute("headquarter", alliance.Headquarter.Profile.IATACode);

                XmlElement membersNode = xmlDoc.CreateElement("members");

                foreach (AllianceMember carrier in alliance.Members)
                {
                    XmlElement memberNode = xmlDoc.CreateElement("member");
                    memberNode.SetAttribute("carrier", carrier.Carrier.Profile.IATACode);
                    memberNode.SetAttribute("joined", carrier.JoinedDate.ToString(new CultureInfo("de-DE")));

                    membersNode.AppendChild(memberNode);
                }
                allianceNode.AppendChild(membersNode);

                XmlElement pendingsNode = xmlDoc.CreateElement("pendings");

                foreach (PendingAllianceMember pending in alliance.PendingMembers)
                {
                    XmlElement pendingNode = xmlDoc.CreateElement("pending");
                    pendingNode.SetAttribute("carrier", pending.Carrier.Profile.IATACode);
                    pendingNode.SetAttribute("date", pending.Date.ToString(new CultureInfo("de-DE")));
                    pendingNode.SetAttribute("type", pending.Type.ToString());

                    pendingsNode.AppendChild(pendingNode);
                }
                allianceNode.AppendChild(pendingsNode);

                alliancesNode.AppendChild(allianceNode);
            }
            root.AppendChild(alliancesNode);

            XmlElement configurationsNode = xmlDoc.CreateElement("configurations");

            foreach (
                CoachConfiguration conf in Configurations.GetConfigurations(Configuration.ConfigurationType.Coach)
                )
            {
                XmlElement configurationNode = xmlDoc.CreateElement("configuration");
                configurationNode.SetAttribute("name", conf.Name);
                configurationNode.SetAttribute("id", conf.ID);
                configurationNode.SetAttribute("standard", conf.Standard.ToString());

                configurationNode.SetAttribute("minimumseats", conf.MinimumSeats.ToString(CultureInfo.InvariantCulture));

                XmlElement classesNode = xmlDoc.CreateElement("classes");

                foreach (CoachClassConfiguration aClass in conf.Classes)
                {
                    XmlElement classNode = xmlDoc.CreateElement("class");
                    classNode.SetAttribute("seating", aClass.SeatingCapacity.ToString(CultureInfo.InvariantCulture));
                    classNode.SetAttribute("regularseating", aClass.RegularSeatingCapacity.ToString(CultureInfo.InvariantCulture));
                    classNode.SetAttribute("type", aClass.Type.ToString());

                    foreach (CoachFacility aFac in aClass.Facilities)
                    {
                        classNode.SetAttribute(aFac.Type.ToString(), aFac.Uid);
                    }

                    classesNode.AppendChild(classNode);
                }
                configurationNode.AppendChild(classesNode);

                configurationsNode.AppendChild(configurationNode);
            }

            root.AppendChild(configurationsNode);

            XmlElement routeConfigurationsNode = xmlDoc.CreateElement("routeclassesconfigurations");

            foreach (
                RouteClassesConfiguration configuration in
                    Configurations.GetConfigurations(Configuration.ConfigurationType.Routeclasses))
            {
                XmlElement routeConfigurationNode = xmlDoc.CreateElement("routeclassesconfiguration");
                routeConfigurationNode.SetAttribute("name", configuration.Name);
                routeConfigurationNode.SetAttribute("id", configuration.ID);
                routeConfigurationNode.SetAttribute("standard", configuration.Standard.ToString());

                XmlElement classesNode = xmlDoc.CreateElement("classes");

                foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                {
                    XmlElement classNode = xmlDoc.CreateElement("class");
                    classNode.SetAttribute("type", classConfiguration.Type.ToString());

                    foreach (RouteFacility aFac in classConfiguration.GetFacilities())
                    {
                        classNode.SetAttribute(aFac.Type.ToString(), aFac.Name);
                    }

                    classesNode.AppendChild(classNode);
                }

                routeConfigurationNode.AppendChild(classesNode);
                routeConfigurationsNode.AppendChild(routeConfigurationNode);
            }

            root.AppendChild(routeConfigurationsNode);

            XmlElement difficultyNode = xmlDoc.CreateElement("difficulty");
            difficultyNode.SetAttribute("name", GameObject.GetInstance().Difficulty.Name);
            difficultyNode.SetAttribute(
                "money",
                GameObject.GetInstance().Difficulty.MoneyLevel.ToString(new CultureInfo("de-DE", false)));
            difficultyNode.SetAttribute(
                "price",
                GameObject.GetInstance().Difficulty.PriceLevel.ToString(new CultureInfo("de-DE", false)));
            difficultyNode.SetAttribute(
                "loan",
                GameObject.GetInstance().Difficulty.LoanLevel.ToString(new CultureInfo("de-DE", false)));
            difficultyNode.SetAttribute(
                "passengers",
                GameObject.GetInstance().Difficulty.PassengersLevel.ToString(new CultureInfo("de-DE", false)));
            difficultyNode.SetAttribute(
                "ai",
                GameObject.GetInstance().Difficulty.AILevel.ToString(new CultureInfo("de-DE", false)));

            root.AppendChild(difficultyNode);

            if (GameObject.GetInstance().Scenario != null)
            {
                XmlElement scenarioNode = xmlDoc.CreateElement("scenario");
                scenarioNode.SetAttribute("name", GameObject.GetInstance().Scenario.Scenario.Name);
                scenarioNode.SetAttribute("success", GameObject.GetInstance().Scenario.IsSuccess.ToString());

                if (GameObject.GetInstance().Scenario.ScenarioFailed != null)
                {
                    scenarioNode.SetAttribute("failed", GameObject.GetInstance().Scenario.ScenarioFailed.ID);
                }

                XmlElement failuresNode = xmlDoc.CreateElement("failures");

                foreach (ScenarioFailureObject sfo in GameObject.GetInstance().Scenario.GetScenarioFailures())
                {
                    XmlElement failureNode = xmlDoc.CreateElement("failure");

                    failureNode.SetAttribute("id", sfo.Failure.ID);
                    failureNode.SetAttribute("count", sfo.Failures.ToString(CultureInfo.InvariantCulture));
                    failureNode.SetAttribute("lastfailuretime", sfo.LastFailureTime.ToString(new CultureInfo("de-DE")));

                    failuresNode.AppendChild(failureNode);
                }

                scenarioNode.AppendChild(failuresNode);

                root.AppendChild(scenarioNode);
            }

            XmlElement gameSettingsNode = xmlDoc.CreateElement("gamesettings");
            gameSettingsNode.SetAttribute("name", GameObject.GetInstance().Name);
            gameSettingsNode.SetAttribute("human", GameObject.GetInstance().HumanCarrier.Profile.IATACode);
            gameSettingsNode.SetAttribute("maincarrier", GameObject.GetInstance().MainCarrier.Profile.IATACode);
            gameSettingsNode.SetAttribute("timezone", GameObject.GetInstance().TimeZone.UTCOffset.ToString());
            gameSettingsNode.SetAttribute("mailonlandings", Infrastructure.Settings.GetInstance().MailsOnLandings.ToString());
            gameSettingsNode.SetAttribute("mailonbadweather", Infrastructure.Settings.GetInstance().MailsOnBadWeather.ToString());
            gameSettingsNode.SetAttribute("stationcode", Infrastructure.Settings.GetInstance().StationCodeDisplay.ToString());
            gameSettingsNode.SetAttribute("minutesperturn", Infrastructure.Settings.GetInstance().MinutesPerTurn.ToString(CultureInfo.InvariantCulture));
            gameSettingsNode.SetAttribute("language", AppSettings.GetInstance().GetLanguage().Name);
            gameSettingsNode.SetAttribute("dayround", GameObject.GetInstance().DayRoundEnabled.ToString());

            XmlElement itemsNodes = xmlDoc.CreateElement("calendaritems");

            foreach (CalendarItem item in CalendarItems.GetCalendarItems())
            {
                XmlElement itemNode = xmlDoc.CreateElement("calendaritem");

                itemNode.SetAttribute("type", item.Type.ToString());
                itemNode.SetAttribute("date", item.Date.ToString(new CultureInfo("de-DE")));
                itemNode.SetAttribute("header", item.Header);
                itemNode.SetAttribute("subject", item.Subject);

                itemsNodes.AppendChild(itemNode);
            }

            gameSettingsNode.AppendChild(itemsNodes);

            XmlElement newsNodes = xmlDoc.CreateElement("news");

            foreach (News news in GameObject.GetInstance().NewsBox.GetNews())
            {
                XmlElement newsNode = xmlDoc.CreateElement("new");
                // chs, 2011-19-10 change to use a specific culture info for saving date format      
                newsNode.SetAttribute("date", news.Date.ToString(new CultureInfo("de-DE")));
                newsNode.SetAttribute("type", news.Type.ToString());
                newsNode.SetAttribute("subject", news.Subject);
                newsNode.SetAttribute("body", news.Body);
                newsNode.SetAttribute("isread", news.IsRead.ToString());

                newsNodes.AppendChild(newsNode);
            }
            gameSettingsNode.AppendChild(newsNodes);

            root.AppendChild(gameSettingsNode);

            sw.Stop();

            //Console.WriteLine("Time elapsed: {0} ms.", sw.ElapsedMilliseconds);

            //WaitFor.tasks
            using (var fs = new FileStream(path, FileMode.Create))
            {
                Stream s = new GZipStream(fs, CompressionMode.Compress);

                xmlDoc.Save(s);
                s.Close();
            }

            // xmlDoc.Save(path);
        }

        #endregion

        #region Methods

        private static void LoadCarrier(XmlElement carrierNode)
        {
            // chs, 2011-21-10 changed for the possibility of creating a new carrier
            string carrierName = carrierNode.Attributes["name"].Value;
            string carrierIATA = carrierNode.Attributes["code"].Value;

            Boolean carrierIsSubsidiary = Convert.ToBoolean(carrierNode.Attributes["subsidiary"].Value);
            Country carrierCountry = Countries.GetCountry(carrierNode.Attributes["country"].Value);
            string color = carrierNode.Attributes["color"].Value;
            string carrierCEO = carrierNode.Attributes["CEO"].Value;
            double money = Convert.ToDouble(carrierNode.Attributes["money"].Value, new CultureInfo("de-DE", false));
            int reputation = Convert.ToInt16(carrierNode.Attributes["reputation"].Value);
            var mentality =
                (Carrier.CarrierMentality)
                TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Carrier.CarrierMentality), carrierNode.Attributes["mentality"].Value);
            var market =
                (CarrierFocus) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CarrierFocus), carrierNode.Attributes["market"].Value);
            var license =
                (Carrier.CarrierLicense)
                TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Carrier.CarrierLicense), carrierNode.Attributes["license"].Value);
            Route.RouteType routefocus = carrierNode.HasAttribute("routefocus")
                                             ? (Route.RouteType) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Route.RouteType), carrierNode.Attributes["routefocus"].Value)
                                             : Route.RouteType.Passenger;

            Boolean isReal = !carrierNode.HasAttribute("real") || Convert.ToBoolean(carrierNode.Attributes["real"].Value);
            int founded = carrierNode.HasAttribute("founded")
                              ? Convert.ToInt16(carrierNode.Attributes["founded"].Value)
                              : 1950;
            int folded = carrierNode.HasAttribute("folded")
                             ? Convert.ToInt16(carrierNode.Attributes["folded"].Value)
                             : 2199;

            Carrier carrier;
            if (carrierIsSubsidiary)
            {
                Carrier parent = Carriers.GetCarrier(carrierNode.Attributes["parentcarrier"].Value);
                carrier = new SubsidiaryCarrier(
                    parent,
                    new CarrierProfile(carrierName, carrierIATA, color, carrierCEO, isReal, founded, folded),
                    mentality,
                    market,
                    license,
                    routefocus) {Profile = {Country = carrierCountry}};
                parent.AddSubsidiaryCarrier((SubsidiaryCarrier) carrier);
            }
            else
            {
                carrier =
                    new Carrier(
                        new CarrierProfile(carrierName, carrierIATA, color, carrierCEO, isReal, founded, folded),
                        mentality,
                        market,
                        license,
                        routefocus) {Profile = {Country = carrierCountry}};
            }
            if (carrierNode.HasAttribute("logo"))
            {
                string logo = AppSettings.GetDataPath() + "/graphics/carrierlogos/"
                              + carrierNode.Attributes["logo"].Value;

                carrier.Profile.AddLogo(new CarrierLogo(logo));
            }
            carrier.Fleet.Clear();
            carrier.Stations.Clear();
            carrier.Routes.Clear();
            carrier.Alliances.Clear();

            carrier.Money = money;
            carrier.Reputation = reputation;

            XmlNodeList logosList = carrierNode.SelectNodes("logos/logo");

            if (logosList != null)
                foreach (XmlElement logoElement in logosList)
                {
                    int logoFromYear = Convert.ToInt16(logoElement.Attributes["from"].Value);
                    int logoToYear = Convert.ToInt16(logoElement.Attributes["to"].Value);
                    string logoPath = logoElement.Attributes["path"].Value;

                    if (!File.Exists(logoPath))
                    {
                        logoPath = AppSettings.GetDataPath() + "/graphics/carrierlogos/"
                                   + System.IO.Path.GetFileName(logoPath);
                    }

                    carrier.Profile.AddLogo(new CarrierLogo(logoFromYear, logoToYear, logoPath));
                }

            var carrierContractNode = (XmlElement) carrierNode.SelectSingleNode("contract");
            if (carrierContractNode != null)
            {
                Manufacturer contractManufacturer =
                    Manufacturers.GetManufacturer(carrierContractNode.Attributes["manufacturer"].Value);
                DateTime contractSigningDate = DateTime.Parse(
                    carrierContractNode.Attributes["signingdate"].Value,
                    new CultureInfo("de-DE", false));
                int contractLength = Convert.ToInt16(carrierContractNode.Attributes["length"].Value);
                double contractDiscount = Convert.ToDouble(
                    carrierContractNode.Attributes["discount"].Value,
                    new CultureInfo("de-DE", false));
                int contractCoaches = Convert.ToInt16(carrierContractNode.Attributes["coaches"].Value);

                var contract = new ManufacturerContract(
                    contractManufacturer,
                    contractSigningDate,
                    contractLength,
                    contractDiscount) {PurchasedCoaches = contractCoaches};

                carrier.Contract = contract;
            }

            // chs, 2011-17-10 added for loading of passenger happiness
            var carrierPassengerNode = (XmlElement) carrierNode.SelectSingleNode("passengerhappiness");
            if (carrierPassengerNode != null)
            {
                double passengerHappiness = Convert.ToDouble(
                    carrierPassengerNode.Attributes["value"].Value,
                    new CultureInfo("de-DE", false));
                PassengerHelpers.SetPassengerHappiness(carrier, passengerHappiness);
            }

            XmlNodeList carrierFacilitiesList = carrierNode.SelectNodes("facilities/facility");
            if (carrierFacilitiesList != null)
                foreach (XmlElement carrierFacilityNode in carrierFacilitiesList)
                {
                    string carrierFacility = carrierFacilityNode.Attributes["uid"].Value;

                    carrier.AddFacility(CarrierFacilities.GetFacility(carrierFacility));
                }

            XmlNodeList carrierPoliciesList = carrierNode.SelectNodes("policies/policy");

            foreach (XmlElement carrierPolicyNode in carrierPoliciesList)
            {
                string policyName = carrierPolicyNode.Attributes["name"].Value;
                object policyValue = carrierPolicyNode.Attributes["value"].Value;

                int number;
                carrier.AddCarrierPolicy(int.TryParse(policyValue.ToString(), out number) ? new CarrierPolicy(policyName, number) : new CarrierPolicy(policyName, policyValue));
            }

            XmlNodeList carrierLoanList = carrierNode.SelectNodes("loans/loan");
            foreach (XmlElement carrierLoanNode in carrierLoanList)
            {
                DateTime date = DateTime.Parse(
                    carrierLoanNode.Attributes["date"].Value,
                    new CultureInfo("de-DE", false));
                double rate = Convert.ToDouble(
                    carrierLoanNode.Attributes["rate"].Value,
                    new CultureInfo("de-DE", false));
                double amount = Convert.ToDouble(
                    carrierLoanNode.Attributes["amount"].Value,
                    new CultureInfo("de-DE", false));
                int length = Convert.ToInt16(carrierLoanNode.Attributes["length"].Value);
                double payment = Convert.ToDouble(
                    carrierLoanNode.Attributes["payment"].Value,
                    new CultureInfo("de-DE", false));

                var loan = new Loan(date, amount, length, rate) {PaymentLeft = payment};

                carrier.AddLoan(loan);
            }

            XmlNodeList carrierStatList = carrierNode.SelectNodes("stats/stat");
            foreach (XmlElement carrierStatNode in carrierStatList)
            {
                int year = Convert.ToInt32(carrierStatNode.Attributes["year"].Value);
                string carrierStatType = carrierStatNode.Attributes["type"].Value;
                double value = Convert.ToDouble(
                    carrierStatNode.Attributes["value"].Value,
                    new CultureInfo("de-DE", false));

                carrier.Statistics.SetStatisticsValue(year, StatisticsTypes.GetStatisticsType(carrierStatType), value);
            }

            XmlNodeList carrierInvoiceList = carrierNode.SelectNodes("invoices/invoice");

            foreach (XmlElement carrierInvoiceNode in carrierInvoiceList)
            {
                var type =
                    (Invoice.InvoiceType)
                    TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Invoice.InvoiceType), carrierInvoiceNode.Attributes["type"].Value);
                int invoiceYear = Convert.ToInt16(carrierInvoiceNode.Attributes["year"].Value);
                int invoiceMonth = Convert.ToInt16(carrierInvoiceNode.Attributes["month"].Value);
                double invoiceAmount = Convert.ToDouble(
                    carrierInvoiceNode.Attributes["amount"].Value,
                    new CultureInfo("de-DE", false));

                carrier.SetInvoice(type, invoiceYear, invoiceMonth, 1, invoiceAmount);
            }

            // chs, 2011-13-10 added for loading of carrier advertisements
            XmlNodeList advertisementList = carrierNode.SelectNodes("advertisements/advertisement");

            foreach (XmlElement advertisementNode in advertisementList)
            {
                var type =
                    (AdvertisementType.CarrierAdvertisementType)
                    TheBusProject.Infrastructure.DataParsing.ParseEnum(
                        typeof (AdvertisementType.CarrierAdvertisementType),
                        advertisementNode.Attributes["type"].Value);
                string advertisementName = advertisementNode.Attributes["name"].Value;

                carrier.SetCarrierAdvertisement(AdvertisementTypes.GetType(type, advertisementName));
            }
            // chs, 2011-17-10 added for loading of fees
            var fees = new CarrierFees();

            XmlNodeList carrierFeeList = carrierNode.SelectNodes("fees/fee");
            foreach (XmlElement feeNode in carrierFeeList)
            {
                string feeType = feeNode.Attributes["type"].Value;
                double feeValue = Convert.ToDouble(feeNode.Attributes["value"].Value, new CultureInfo("de-DE", false));

                fees.SetValue(FeeTypes.GetType(feeType), feeValue);
            }

            carrier.Fees = fees;

            XmlNodeList tripschoolsList = carrierNode.SelectNodes("tripschools/tripschool");

            foreach (XmlElement tripschoolNode in tripschoolsList)
            {
                string fsID = tripschoolNode.Attributes["id"].Value;
                Station fsStation = Stations.GetStationFromID(tripschoolNode.Attributes["station"].Value);

                var fs = new TripSchool(fsStation) {ID = fsID};

                XmlNodeList coachesList = tripschoolNode.SelectNodes("trainingcoaches/trainingcoach");

                foreach (XmlElement coachNode in coachesList)
                {
                    TrainingCoachType coachType =
                        TrainingCoachTypes.GetCoachType(coachNode.Attributes["type"].Value);
                    DateTime coachDate = DateTime.Parse(
                        coachNode.Attributes["date"].Value,
                        new CultureInfo("de-DE", false));

                    fs.AddTrainingCoach(new TrainingCoach(coachType, coachDate, fs));
                }

                XmlNodeList studentsList = tripschoolNode.SelectNodes("students/student");

                foreach (XmlElement studentNode in studentsList)
                {
                    string firstname = studentNode.Attributes["firstname"].Value;
                    string lastname = studentNode.Attributes["lastname"].Value;
                    DateTime birthdate = DateTime.Parse(
                        studentNode.Attributes["birthdate"].Value,
                        new CultureInfo("de-DE", false));
                    Town town = Towns.GetTown(studentNode.Attributes["town"].Value);
                    DateTime startdate = DateTime.Parse(
                        studentNode.Attributes["startdate"].Value,
                        new CultureInfo("de-DE", false));

                    fs.AddStudent(
                        new DriverStudent(
                            new DriverProfile(firstname, lastname, birthdate, town),
                            startdate,
                            null,
                            null,
                            ""));
                }

                carrier.AddTripSchool(fs);
            }

            XmlNodeList carrierFleetList = carrierNode.SelectNodes("fleet/coach");

            foreach (XmlElement carrierCoachNode in carrierFleetList)
            {
                Coach coach = Coaches.GetCoach(carrierCoachNode.Attributes["coach"].Value);

                string fCoachName = carrierCoachNode.Attributes["name"].Value;
                Station homebase = Stations.GetStation(carrierCoachNode.Attributes["homebase"].Value);
                var purchasedtype =
                    (FleetCoach.PurchasedType)
                    TheBusProject.Infrastructure.DataParsing.ParseEnum(
                        typeof (FleetCoach.PurchasedType),
                        carrierCoachNode.Attributes["purchased"].Value);
                DateTime purchasedDate = DateTime.Parse(
                    carrierCoachNode.Attributes["date"].Value,
                    new CultureInfo("de-DE", false));
                var status =
                    (FleetCoach.CoachStatus)
                    TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (FleetCoach.CoachStatus), carrierCoachNode.Attributes["status"].Value);
                DateTime groundedDate = DateTime.Parse(
                    carrierCoachNode.Attributes["groundeddate"].Value,
                    new CultureInfo("de-DE", false));

                // Coordinate latitude = Coordinate.Parse(carrierCoachNode.Attributes["latitude"].Value);
                //Coordinate longitude = Coordinate.Parse(carrierCoachNode.Attributes["longitude"].Value);

                string[] latitude = carrierCoachNode.Attributes["value"].Value.Split('°', '\'', '\'');
                string[] longitude = carrierCoachNode.Attributes["value"].Value.Split('°', '\'', '\'');
                var coords = new int[6];
                foreach (string l in latitude)
                {
                    int c = 0;
                    int.TryParse(l, out coords[c]);
                    c++;
                }

                foreach (string l in longitude)
                {
                    int c = 3;
                    int.TryParse(l, out coords[c]);
                    c++;
                }

                //cleaning up
                latitude = null;
                longitude = null;

                var pos = new GeoCoordinate(
                    MathHelpers.DMStoDeg(coords[0], coords[1], coords[2]),
                    MathHelpers.DMStoDeg(coords[3], coords[4], coords[5]));

                var fCoach = new FleetCoach(purchasedtype, purchasedDate, carrier, coach, homebase) {CurrentPosition = null, Status = status, GroundedToDate = groundedDate};

                XmlNodeList coachStatList = carrierCoachNode.SelectNodes("stats/stat");

                foreach (XmlElement coachStatNode in coachStatList)
                {
                    int year = Convert.ToInt32(coachStatNode.Attributes["year"].Value);
                    string statType = coachStatNode.Attributes["type"].Value;
                    double statValue = Convert.ToDouble(
                        coachStatNode.Attributes["value"].Value,
                        new CultureInfo("de-DE", false));
                    fCoach.Statistics.SetStatisticsValue(
                        year,
                        StatisticsTypes.GetStatisticsType(statType),
                        statValue);
                }

                carrier.AddCoach(fCoach);
            }
            XmlNodeList routeList = carrierNode.SelectNodes("routes/route");

            foreach (XmlElement routeNode in routeList)
            {
                carrier.AddRoute(LoadRoute(routeNode, carrier));
            }

            XmlNodeList tripNodes = carrierNode.SelectNodes("trips/trip");
            foreach (XmlElement tripNode in tripNodes)
            {
                FleetCoach coach =
                    carrier.Fleet.Find(a => a.Coach.ID == tripNode.Attributes["coach"].Value);

                if (coach != null)
                {
                    Route route = carrier.Routes.Find(r => r.Id == tripNode.Attributes["route"].Value);

                    if (route != null)
                    {
                        string destination = tripNode.Attributes["destination"].Value;

                        var day = (DayOfWeek) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (DayOfWeek), tripNode.Attributes["day"].Value);
                        TimeSpan time = TimeSpan.Parse(tripNode.Attributes["time"].Value);
                        DateTime tripTime = DateTime.Parse(
                            tripNode.Attributes["triptime"].Value,
                            new CultureInfo("de-DE", false));

                        var stopoverNode = (XmlElement) tripNode.SelectSingleNode("stopover");

                        Boolean isStopoverTrip = stopoverNode != null;

                        if (destination != "Service")
                        {
                            RouteTimeTableEntry rtte =
                                route.TimeTable.Entries.Find(
                                    e => e.Destination.TripCode == destination && e.Day == day && e.Time == time);

                            var currentTrip = new Trip(rtte) {TripTime = tripTime};
                            currentTrip.Classes.Clear();

                            if (currentTrip.IsPassengerTrip())
                            {
                                XmlNodeList tripClassList = tripNode.SelectNodes("tripclasses/tripclass");

                                foreach (XmlElement tripClassNode in tripClassList)
                                {
                                    var coachClassType =
                                        (CoachClass.ClassType)
                                        TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                            typeof (CoachClass.ClassType),
                                            tripClassNode.Attributes["type"].Value);
                                    int tripPassengers =
                                        Convert.ToInt16(tripClassNode.Attributes["passengers"].Value);

                                    currentTrip.Classes.Add(
                                        new TripCoachClass(
                                            ((PassengerRoute) route).GetRouteCoachClass(coachClassType),
                                            tripPassengers));
                                }
                            }
                            if (isStopoverTrip)
                            {
                                int currentStopoverTrip =
                                    Convert.ToInt32(stopoverNode.Attributes["currenttrip"].Value);

                                XmlNodeList stopoverClassesList = stopoverNode.SelectNodes("classes/class");

                                var stopEntryAllClasses =
                                    new Dictionary<RouteTimeTableEntry, List<TripCoachClass>>();

                                foreach (XmlElement stopoverClassNode in stopoverClassesList)
                                {
                                    RouteTimeTableEntry stopoverEntry =
                                        carrier.Routes.SelectMany(r => r.TimeTable.Entries)
                                               .ToList()
                                               .Find(e => e.ID == stopoverClassNode.Attributes["id"].Value);

                                    XmlNodeList stopoverTripClassList =
                                        stopoverClassNode.SelectNodes("tripclasses/tripclass");

                                    List<TripCoachClass> stopoverTripClasses = (from XmlElement stopoverTripClassNode in stopoverTripClassList
                                                                                       let stopoverCoachClassType =
                                                                                           (CoachClass.ClassType)
                                                                                           TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CoachClass.ClassType), stopoverTripClassNode.Attributes["type"].Value)
                                                                                       let stopoverTripPassengers = Convert.ToInt16(stopoverTripClassNode.Attributes["passengers"].Value)
                                                                                       select
                                                                                           new TripCoachClass(((PassengerRoute) route).GetRouteCoachClass(stopoverCoachClassType),
                                                                                                                   stopoverTripPassengers)).ToList();

                                    stopEntryAllClasses.Add(stopoverEntry, stopoverTripClasses);
                                }

                                XmlNodeList stopoverCargosList = stopoverNode.SelectNodes("cargos/cargo");

                                var stopEntryAllCargo = new Dictionary<RouteTimeTableEntry, double>();

                                foreach (XmlElement stopoverCargoNode in stopoverCargosList)
                                {
                                    RouteTimeTableEntry stopoverEntry =
                                        carrier.Routes.SelectMany(r => r.TimeTable.Entries)
                                               .ToList()
                                               .Find(e => e.ID == stopoverCargoNode.Attributes["id"].Value);
                                    double cargoValue = Convert.ToDouble(
                                        stopoverCargoNode.Attributes["value"].Value,
                                        new CultureInfo("de-DE", false));

                                    stopEntryAllCargo.Add(stopoverEntry, cargoValue);
                                }

                                var stopoverTrip = new StopoverTrip(rtte)
                                    {
                                        AllClasses = stopEntryAllClasses,
                                        AllCargo = stopEntryAllCargo,
                                        CurrentTrip = currentStopoverTrip,
                                        TripTime = currentTrip.TripTime,
                                        Classes = currentTrip.Classes
                                    };

                                coach.CurrentTrip = stopoverTrip;
                            }
                            else
                            {
                                coach.CurrentTrip = currentTrip;
                            }
                        }
                        else
                        {
                            coach.CurrentTrip =
                                new Trip(
                                    new RouteTimeTableEntry(
                                        route.TimeTable,
                                        GameObject.GetInstance().GameTime.DayOfWeek,
                                        GameObject.GetInstance().GameTime.TimeOfDay,
                                        new RouteEntryDestination(coach.Homebase, "Service", null),
                                        null));

                            coach.Status = FleetCoach.CoachStatus.OnService;
                        }
                    }
                    else
                    {
                        coach.Status = FleetCoach.CoachStatus.Stopped;
                    }
                }
            }
            Carriers.AddCarrier(carrier);
        }

        //loads a route
        private static Route LoadRoute(
            XmlElement routeNode,
            Carrier carrier,
            Route.RouteType routetype = Route.RouteType.Passenger)
        {
            string id = routeNode.Attributes["id"].Value;
            Station dest1 = Stations.GetStation(routeNode.Attributes["destination1"].Value);
            Station dest2 = Stations.GetStation(routeNode.Attributes["destination2"].Value);
            Boolean isBanned = Convert.ToBoolean(routeNode.Attributes["isbanned"].Value);

            if (routeNode.HasAttribute("type"))
            {
                routetype = (Route.RouteType) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Route.RouteType), routeNode.Attributes["type"].Value);
            }

            Route route;

            route = new PassengerRoute(id, dest1, dest2, DateTime.Now, 0);

            route.Banned = isBanned;

            /* foreach (StopoverRoute stopover in route.Stopovers)
            {
                XmlElement routeStopoverNode = xmlDoc.CreateElement("stopover");
                routeStopoverNode.SetAttribute("station", stopover.Stopover.Profile.ID);

                XmlElement stopoverLegsNode = xmlDoc.CreateElement("legs");
                foreach (Route leg in stopover.Legs)
                {
                    XmlElement stopoverLegNode = xmlDoc.CreateElement("leg");

                    stopoverLegNode.AppendChild(SaveRoute(xmlDoc, leg));

                    stopoverLegsNode.AppendChild(stopoverLegNode);
                }
                routeStopoverNode.AppendChild(stopoverLegsNode);
                routeStopoversNode.AppendChild(routeStopoverNode);*/

            XmlNodeList routeStopoverList = routeNode.SelectNodes("stopovers/stopover");

            if (routeStopoverList != null)
                foreach (XmlElement routeStopoverNode in routeStopoverList)
                {
                    Station stopoverStation = Stations.GetStationFromID(routeStopoverNode.Attributes["station"].Value);

                    var stopoverRoute = new StopoverRoute(stopoverStation);

                    XmlNodeList legsList = routeStopoverNode.SelectNodes("legs/leg");

                    if (legsList != null)
                        foreach (XmlElement legNode in legsList)
                        {
                            stopoverRoute.AddLeg(LoadRoute((XmlElement) legNode.SelectSingleNode("route"), carrier, routetype));
                        }

                    route.AddStopover(stopoverRoute);
                }

            if (routetype == Route.RouteType.Passenger)
            {
                ((PassengerRoute) route).Classes.Clear();

                XmlNodeList routeClassList = routeNode.SelectNodes("routeclasses/routeclass");

                if (routeClassList != null)
                    foreach (XmlElement routeClassNode in routeClassList)
                    {
                        var coachClassType =
                            (CoachClass.ClassType)
                            TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (CoachClass.ClassType), routeClassNode.Attributes["type"].Value);
                        double fareprice = Convert.ToDouble(
                            routeClassNode.Attributes["fareprice"].Value,
                            new CultureInfo("de-DE", false));
                        var seatingType =
                            (RouteCoachClass.SeatingType)
                            TheBusProject.Infrastructure.DataParsing.ParseEnum(
                                typeof (RouteCoachClass.SeatingType),
                                routeClassNode.Attributes["seating"].Value);

                        var rClass = new RouteCoachClass(
                            coachClassType,
                            RouteCoachClass.SeatingType.ReservedSeating,
                            fareprice) {Seating = seatingType};

                        foreach (RouteFacility.FacilityType ftype in Enum.GetValues(typeof (RouteFacility.FacilityType)))
                        {
                            if (routeClassNode.HasAttribute(ftype.ToString()))
                            {
                                RouteFacility facility =
                                    RouteFacilities.GetFacility(routeClassNode.Attributes[ftype.ToString()].Value);
                                rClass.AddFacility(facility);
                            }
                        }

                        ((PassengerRoute) route).AddRouteCoachClass(rClass);
                    }
            }

            var timeTable = new RouteTimeTable(route);

            XmlNodeList timetableList = routeNode.SelectNodes("timetable/timetableentry");

            if (timetableList != null)
                foreach (XmlElement entryNode in timetableList)
                {
                    Station entryDest = Stations.GetStation(entryNode.Attributes["destination"].Value);
                    string tripCode = entryNode.Attributes["tripcode"].Value;
                    var day = (DayOfWeek) TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (DayOfWeek), entryNode.Attributes["day"].Value);
                    TimeSpan time = TimeSpan.Parse(entryNode.Attributes["time"].Value);
                    FleetCoach coach = entryNode.Attributes["coach"].Value == "-"
                                                 ? null
                                                 : carrier.Fleet.Find(a => a.Coach.ID == entryNode.Attributes["coach"].Value);

                    var entry = new RouteTimeTableEntry(
                        timeTable,
                        day,
                        time,
                        new RouteEntryDestination(entryDest, tripCode, null),
                        null);

                    if (entryNode.HasAttribute("id"))
                    {
                        entry.ID = entryNode.Attributes["id"].Value;
                    }

                    if (entryNode.HasAttribute("mainentry"))
                    {
                        entry.MainEntry =
                            carrier.Routes.SelectMany(r => r.TimeTable.Entries)
                                   .ToList()
                                   .Find(e => e.ID == entryNode.Attributes["mainentry"].Value);
                    }

                    entry.Coach = coach;

                    if (coach != null && !coach.Routes.Contains(route))
                    {
                        coach.Routes.Add(route);
                    }

                    timeTable.AddEntry(entry);
                }
            route.TimeTable = timeTable;

            XmlNodeList routeInvoiceList = routeNode.SelectNodes("invoices/invoice");

            if (routeInvoiceList != null)
                foreach (XmlElement routeInvoiceNode in routeInvoiceList)
                {
                    var type =
                        (Invoice.InvoiceType)
                        TheBusProject.Infrastructure.DataParsing.ParseEnum(typeof (Invoice.InvoiceType), routeInvoiceNode.Attributes["type"].Value);
                    int invoiceYear = Convert.ToInt16(routeInvoiceNode.Attributes["year"].Value);
                    int invoiceMonth = Convert.ToInt16(routeInvoiceNode.Attributes["month"].Value);
                    double invoiceAmount = Convert.ToDouble(
                        routeInvoiceNode.Attributes["amount"].Value,
                        new CultureInfo("de-DE", false));

                    route.SetRouteInvoice(type, invoiceYear, invoiceMonth, 1, invoiceAmount);
                }

            return route;
        }

        //saves a route
        private static XmlElement SaveRoute(XmlDocument xmlDoc, Route route)
        {
            XmlElement routeNode = xmlDoc.CreateElement("route");
            routeNode.SetAttribute("id", route.Id);
            routeNode.SetAttribute("destination1", route.Destination1.Profile.IATACode);
            routeNode.SetAttribute("destination2", route.Destination2.Profile.IATACode);
            routeNode.SetAttribute("isbanned", route.Banned.ToString());
            routeNode.SetAttribute("type", route.Type.ToString());

            XmlElement routeStopoversNode = xmlDoc.CreateElement("stopovers");

            foreach (StopoverRoute stopover in route.Stopovers)
            {
                XmlElement routeStopoverNode = xmlDoc.CreateElement("stopover");
                routeStopoverNode.SetAttribute("station", stopover.Stopover.Profile.ID);

                XmlElement stopoverLegsNode = xmlDoc.CreateElement("legs");
                foreach (Route leg in stopover.Legs)
                {
                    XmlElement stopoverLegNode = xmlDoc.CreateElement("leg");

                    stopoverLegNode.AppendChild(SaveRoute(xmlDoc, leg));

                    stopoverLegsNode.AppendChild(stopoverLegNode);
                }
                routeStopoverNode.AppendChild(stopoverLegsNode);
                routeStopoversNode.AppendChild(routeStopoverNode);
            }

            routeNode.AppendChild(routeStopoversNode);

            if (route.Type == Route.RouteType.Passenger)
            {
                XmlElement routeClassesNode = xmlDoc.CreateElement("routeclasses");

                foreach (RouteCoachClass aClass in ((PassengerRoute) route).Classes)
                {
                    XmlElement routeClassNode = xmlDoc.CreateElement("routeclass");
                    routeClassNode.SetAttribute("type", aClass.Type.ToString());
                    routeClassNode.SetAttribute("fareprice", aClass.FarePrice.ToString(new CultureInfo("de-DE", false)));

                    foreach (RouteFacility facility in aClass.GetFacilities())
                    {
                        routeClassNode.SetAttribute(facility.Type.ToString(), facility.Uid);
                    }
                    // chs, 2011-18-10 added for saving of type of seating
                    routeClassNode.SetAttribute("seating", aClass.Seating.ToString());

                    routeClassesNode.AppendChild(routeClassNode);
                }
                routeNode.AppendChild(routeClassesNode);
            }


            XmlElement timetableNode = xmlDoc.CreateElement("timetable");

            foreach (RouteTimeTableEntry entry in route.TimeTable.Entries)
            {
                XmlElement ttEntryNode = xmlDoc.CreateElement("timetableentry");
                ttEntryNode.SetAttribute("destination", entry.Destination.Station.Profile.IATACode);
                ttEntryNode.SetAttribute("tripcode", entry.Destination.TripCode);
                ttEntryNode.SetAttribute("day", entry.Day.ToString());
                ttEntryNode.SetAttribute("time", entry.Time.ToString());
                ttEntryNode.SetAttribute("coach", entry.Coach != null ? entry.Coach.Coach.ID : "-");
                ttEntryNode.SetAttribute("id", entry.ID);
                if (entry.MainEntry != null)
                {
                    ttEntryNode.SetAttribute("mainentry", entry.MainEntry.ID);
                }

                timetableNode.AppendChild(ttEntryNode);
            }

            routeNode.AppendChild(timetableNode);

            XmlElement routeInvoicesNode = xmlDoc.CreateElement("invoices");
            foreach (MonthlyInvoice invoice in route.GetInvoices().MonthlyInvoices)
            {
                XmlElement routeInvoiceNode = xmlDoc.CreateElement("invoice");
                routeInvoiceNode.SetAttribute("type", invoice.Type.ToString());
                routeInvoiceNode.SetAttribute("year", invoice.Year.ToString(CultureInfo.InvariantCulture));
                routeInvoiceNode.SetAttribute("month", invoice.Month.ToString(CultureInfo.InvariantCulture));
                routeInvoiceNode.SetAttribute("amount", invoice.Amount.ToString(new CultureInfo("de-DE", false)));

                routeInvoicesNode.AppendChild(routeInvoiceNode);
            }
            routeNode.AppendChild(routeInvoicesNode);

            return routeNode;
        }

        #endregion

        /*
        //creates the saves.xml
        public static void CreateBaseXml(string path)
        {
            XmlDocument doc = new XmlDocument();// Create the XML Declaration, and append it to XML document
            XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", null, null);
            doc.AppendChild(dec);// Create the root element

            XmlElement root = doc.CreateElement("saves");
            doc.AppendChild(root);


            doc.Save(path + "/saves.xml");
        }*/
        //deletes a saved game
    }
}