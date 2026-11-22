using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using TheBusProject.Helpers.Workers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helper class for loading and saving using serialization
    public class SerializedLoadSaveHelpers
    {
        #region Public Methods and Operators

        public static void DeleteSavedGame(string name)
        {
            string fileName = AppSettings.GetCommonApplicationDataPath() + "/saves/" + name + ".sav";

            File.Delete(fileName);
        }

        //returns if a saved game exits

        //loads a game 
        public static void LoadGame(string file)
        {
            string fileName = AppSettings.GetCommonApplicationDataPath() + "/saves/" + file + ".sav";

            StartupLog.Write("LoadGame: reading " + fileName + " (" + (File.Exists(fileName) ? new FileInfo(fileName).Length / 1024 + " KB" : "missing") + ")");
            var deserializedSaveObject = FileSerializer.Deserialize<SaveObject>(fileName);
            StartupLog.Write("LoadGame: file read, " + (deserializedSaveObject == null ? "NOTHING could be read" : "restoring the game"));
            if (deserializedSaveObject == null)
            {
                throw new InvalidDataException("The save file could not be read, see startup.log");
            }

            /*
          
           DataContractSerializer serializer = new DataContractSerializer(typeof(SaveObject));
           SaveObject deserializedSaveObject;
           string loading;
           int version;

           using (FileStream stream = new FileStream(fileName, FileMode.Open))
           {
               using (DeflateStream decompress = new DeflateStream(stream, CompressionMode.Decompress))
               {
                   deserializedSaveObject = (SaveObject)serializer.ReadObject(decompress);
               }
           }

           loading = deserializedSaveObject.savetype;
           version = deserializedSaveObject.saveversionnumber;
           */
            //Parrarel for loading the game
            StartupLog.Write("LoadGame: restoring lists");
            Parallel.Invoke(
                () =>
                    {
                        Carriers.Clear();

                        foreach (Carrier carrier in deserializedSaveObject.CarriersList)
                        {
                            Carriers.AddCarrier(carrier);
                        }
                    },
                () =>
                    {
                        Setup.LoadStations();

                        List<Station> stations = Stations.GetAllStations();

                        Stations.Clear();

                        foreach (Station station in deserializedSaveObject.StationsList)
                        {
                            station.Statics = new StationStatics(station);
                            Stations.AddStation(station);
                        }

                        foreach (string iata in deserializedSaveObject.StationsFromStringList)
                        {
                            Station station = stations.FirstOrDefault(a => a.Profile.IATACode == iata);

                            if (station != null)
                            {
                                Stations.AddStation(station);
                            }
                        }

                        // stations added to the data files after this game was saved (like a new addon station)
                        // are in neither list, so bring them in from the data files. Only those: a game set in one
                        // region (or with major stations only) leaves most data file stations out on purpose, and
                        // adding them made the game world four times bigger after a load. Saves made before the
                        // save remembered the data file stations don't say which are new, so nothing is added.
                        var known = new HashSet<string>(
                            Stations.GetAllStations().Select(a => a.Profile.IATACode));
                        var inDataWhenSaved = deserializedSaveObject.DataStationsList == null
                            ? null
                            : new HashSet<string>(deserializedSaveObject.DataStationsList);
                        int added = 0;

                        foreach (Station station in stations.Where(a => inDataWhenSaved != null && !known.Contains(a.Profile.IATACode) && !inDataWhenSaved.Contains(a.Profile.IATACode)))
                        {
                            Stations.AddStation(station);
                            known.Add(station.Profile.IATACode);
                            added++;
                        }

                        if (added > 0)
                        {
                            StartupLog.Write("LoadGame: added " + added + " station(s) that are new in the data files");
                        }
                    },
                () =>
                    {
                        Coaches.Clear();

                        foreach (Coach coach in deserializedSaveObject.CoachesList)
                        {
                            Coaches.AddCoach(coach);
                        }
                    },
                () =>
                    {
                        CalendarItems.Clear();

                        foreach (CalendarItem item in deserializedSaveObject.CalendarItemsList)
                        {
                            CalendarItems.AddCalendarItem(item);
                        }
                    },
                () =>
                    {
                        Configurations.Clear();

                        foreach (Configuration configuration in deserializedSaveObject.ConfigurationList)
                        {
                            Configurations.AddConfiguration(configuration);
                        }
                    },
                () =>
                    {
                        RandomEvents.Clear();

                        foreach (RandomEvent e in deserializedSaveObject.EventsList)
                        {
                            RandomEvents.AddEvent(e);
                        }
                    },
                () =>
                    {
                        Alliances.Clear();

                        foreach (Alliance alliance in deserializedSaveObject.AllianceList)
                        {
                            Alliances.AddAlliance(alliance);
                        }
                    },
                () =>
                    {
                        StationFacilities.Clear();

                        foreach (StationFacility facility in deserializedSaveObject.StationFacilitiesList)
                        {
                            StationFacilities.AddFacility(facility);
                        }
                    },
                () =>
                    {
                        FeeTypes.Clear();

                        foreach (FeeType type in deserializedSaveObject.FeeTypeslist)
                        {
                            FeeTypes.AddType(type);
                        }
                    },
                () =>
                    {
                        AdvertisementTypes.Clear();

                        foreach (AdvertisementType addtype in deserializedSaveObject.AdvertisementTypesList)
                        {
                            AdvertisementTypes.AddAdvertisementType(addtype);
                        }
                    },
                () =>
                    {
                        CoachFacilities.Clear();

                        foreach (CoachFacility coachfas in deserializedSaveObject.CoachFacilitiesList)
                        {
                            CoachFacilities.AddFacility(coachfas);
                        }
                    },
                () =>
                    {
                        RouteFacilities.Clear();

                        foreach (RouteFacility routefas in deserializedSaveObject.RouteFacilitiesList)
                        {
                            RouteFacilities.AddFacility(routefas);
                        }
                    },
                () =>
                    {
                        GameObject.SetInstance(deserializedSaveObject.Instance);
                        // the money shown in the header is a separate value that is not part of the save
                        if (GameObject.GetInstance().HumanCarrier != null)
                        {
                            GameObject.GetInstance().HumanMoney = GameObject.GetInstance().HumanCarrier.Money;
                        }
                        Infrastructure.Settings.SetInstance(deserializedSaveObject.Settings);
                    },
                () =>
                    {
                        if (deserializedSaveObject.CarrierFacilitiesList != null)
                        {
                            CarrierFacilities.Clear();

                            foreach (CarrierFacility carrierfac in deserializedSaveObject.CarrierFacilitiesList)
                            {
                                CarrierFacilities.AddFacility(carrierfac);
                            }
                        }
                    }); //close parallel.invoke

            DemandOverrides.Restore(deserializedSaveObject.DemandOverridesList);
            StartupLog.Write("LoadGame: lists restored");
            foreach (Station bad in Stations.GetAllStations().Where(a => a.Profile.Town == null || a.Profile.Town.Country == null || a.Profile.Town.Name == null).Take(20))
            {
                StartupLog.Write("LoadGame: station with incomplete town: " + bad.Profile.IATACode + " " + bad.Profile.Name);
            }
            //for 0.3.9.2 and the issue with no saved facilities on a route classes
            IEnumerable<RouteClassConfiguration> emptyRouteClassesFacilities =
                Configurations.GetConfigurations(Configuration.ConfigurationType.Routeclasses)
                              .SelectMany(c => ((RouteClassesConfiguration) c).Classes.Where(cl => cl.Facilities == null));

            foreach (RouteClassConfiguration rClassConfiguration in emptyRouteClassesFacilities)
            {
                rClassConfiguration.Facilities = new List<RouteFacility>();
            }


            StartupLog.Write("LoadGame: running SetupLoadedGame");
            Setup.SetupLoadedGame();
            StartupLog.Write("LoadGame: SetupLoadedGame done");
            //Maybe this helps? But i doubt this is the best way
            Action action = () =>
                {
                    var swPax = new Stopwatch();
                    swPax.Start();

                    PassengerHelpers.CreateDestinationDemand();
                    DemandOverrides.Apply();

                    //Console.WriteLine("Demand have been created in {0} ms.", swPax.ElapsedMilliseconds);
                    swPax.Stop();
                };


            StartupLog.Write("LoadGame: creating pilots");
            //create some pilots for the game
            int pilotsPool = 100*Carriers.GetAllCarriers().Count;
            GeneralHelpers.CreatePilots(pilotsPool);
            int instructorsPool = 75*Carriers.GetAllCarriers().Count;
            GeneralHelpers.CreateInstructors(instructorsPool);

            StartupLog.Write("LoadGame: creating start up coaches");
            //creates some coaches for the game
            CoachHelpers.CreateStartUpCoaches();

            RouteIndex.Invalidate();
            int compacted = GameObjectHelpers.CompactInvoices();
            StartupLog.Write("LoadGame: " + compacted + " daily amounts of past months put together");
            int moves = CompetitorService.Restore(deserializedSaveObject.RivalMovesList);
            int trips = TripRecords.Restore(deserializedSaveObject.FlownTripsList);
            int opsMonths = CarrierReportService.Restore(deserializedSaveObject.MonthlyOpsList);
            StartupLog.Write("LoadGame: " + opsMonths + " carrier months for the reports restored");
            StartupLog.Write("LoadGame: " + moves + " rival moves and " + trips + " recorded trips restored");
            StartupLog.Write("LoadGame: starting the game paused");
            //Start the game paused
            GameObjectWorker.GetInstance().StartPaused();

            //Task is needed this unlocks the game agian.
            // the demand is built before the load finishes (the loading screen stays up meanwhile); building it in the
            // background let the player start the clock first, and the trips then ran with no demand
            // saves made after loading with an earlier version can hold every station of the world. Which area the
            // game was started in isn't saved, so point the player to Options when an older save looks like that
            try
            {
                if (deserializedSaveObject.DataStationsList == null && StationCleanupService.ContinentCount() > 1)
                {
                    var areas = StationCleanupService.Areas().Where(a => a.IsContinent).ToList();
                    var used = StationCleanupService.InUse();
                    // nearly everything the carriers use on one continent
                    var main = areas.OrderByDescending(a => used.Count(a.Contains)).FirstOrDefault();
                    int inMain = main == null ? 0 : used.Count(main.Contains);
                    var usedAreas = main == null ? new List<GameArea>() : new List<GameArea> {main};
                    if (main != null && used.Count > 0 && inMain >= 0.9*used.Count)
                    {
                        StartupLog.Write("LoadGame: older save with stations on " + areas.Count + " continents, " + inMain + " of " + used.Count + " used ones in " + usedAreas[0].Name);
                        GameObject.GetInstance().NewsBox.AddNews(new News(News.NewsType.StandardNews, GameObject.GetInstance().GameTime,
                            "Stations outside your area?",
                            "This game has " + Stations.GetAllStations().Count + " stations on " + areas.Count + " continents, but nearly everything the carriers use is in " + usedAreas[0].Name.Replace(" (the whole continent)", "") + ". If you started it in one continent or region, an earlier version may have added the others by mistake when it loaded the game. Options > Game area can take out the unused ones, which makes the game faster. Keep them if this is a world game."));
                    }
                }
            }
            catch (Exception ex)
            {
                StartupLog.Write("LoadGame: checking the game area failed: " + ex);
            }

            StartupLog.Write("LoadGame: creating the demand");
            var demandWatch = Stopwatch.StartNew();
            // the demand is built only for stations without any, and only between those: when an earlier game in this
            // session left demand on the other stations, the saved stations got demand only to each other. Start every
            // station afresh so the demand is built between all of them, as on the first load.
            foreach (Station station in Stations.GetAllStations())
            {
                station.Statics = new StationStatics(station);
            }
            try
            {
                action();
            }
            catch (Exception ex)
            {
                StartupLog.Write("LoadGame: creating the demand failed: " + ex);
            }
            StartupLog.Write("LoadGame: demand ready after " + demandWatch.ElapsedMilliseconds + " ms");
            StartupLog.Write("LoadGame: finished");
        }

        public static void SaveGame(string name)
        {
            var sw = new Stopwatch();
            sw.Start();

            StartupLog.Write("SaveGame: " + name + " start");
            var so = new SaveObject();
            so.DemandOverridesList = DemandOverrides.ToStrings();
            so.DataStationsList = Setup.DataStationCodes?.ToList();
            // the carriers' empty ("none") facilities at stations they don't use: made again when asked for
            int pruned = Stations.GetAllStations().Sum(a => a.PruneNoneFacilities());
            StartupLog.Write("SaveGame: " + pruned + " empty station facilities left out");
            so.RivalMovesList = CompetitorService.ToSave();
            so.FlownTripsList = TripRecords.ToSave(Carriers.GetAllCarriers());
            so.MonthlyOpsList = CarrierReportService.ToSave(Carriers.GetAllCarriers());
            Parallel.Invoke(
                () =>
                    {
                        so.StationsList = new List<Station>();
                        so.StationsFromStringList = new List<string>();

                        IEnumerable<Station> stationsInUse =
                            Stations.GetAllStations()
                                    .Where(
                                        a =>
                                        Carriers.GetAllCarriers().Exists(al => al.Stations.Contains(a))
                                        || a.HasCarrierFacility()
                                        || a.PlayerEdited);
                        Station[] inUse = stationsInUse as Station[] ?? stationsInUse.ToArray();
                        so.StationsList.AddRange(inUse);

                        foreach (Station station in Stations.GetStations(a => !inUse.Contains(a)))
                        {
                            so.StationsFromStringList.Add(station.Profile.IATACode);
                        }
                        StartupLog.Write("SaveGame: " + so.StationsList.Count + " stations saved in full (" + string.Join(" ", so.StationsList.Select(a => a.Profile.IATACode + (a.PlayerEdited ? "*" : ""))) + "), " + so.StationsFromStringList.Count + " by code");
                    },
                () =>
                    {
                        so.CarriersList = new List<Carrier>();
                        so.CarriersList.AddRange(Carriers.GetAllCarriers());
                    },
                () =>
                    {
                        so.CoachesList = new List<Coach>();
                        so.CoachesList.AddRange(Coaches.GetAllCoaches().Where(a => a.Carrier != null));
                    },
                () =>
                    {
                        so.CalendarItemsList = new List<CalendarItem>();
                        so.CalendarItemsList.AddRange(CalendarItems.GetCalendarItems());
                    },
                () =>
                    {
                        so.ConfigurationList = new List<Configuration>();
                        so.ConfigurationList.AddRange(Configurations.GetConfigurations());
                    },
                () =>
                    {
                        so.EventsList = new List<RandomEvent>();
                        so.EventsList.AddRange(RandomEvents.GetEvents());
                    },
                () =>
                    {
                        so.AllianceList = new List<Alliance>();
                        so.AllianceList.AddRange(Alliances.GetAlliances());
                    },
                () =>
                    {
                        so.StationFacilitiesList = new List<StationFacility>();
                        so.StationFacilitiesList.AddRange(StationFacilities.GetFacilities());
                    },
                () =>
                    {
                        so.FeeTypeslist = new List<FeeType>();
                        so.FeeTypeslist.AddRange(FeeTypes.GetTypes());
                    },
                () =>
                    {
                        so.AdvertisementTypesList = new List<AdvertisementType>();
                        so.AdvertisementTypesList.AddRange(AdvertisementTypes.GetTypes());
                    },
                () =>
                    {
                        so.CoachFacilitiesList = new List<CoachFacility>();
                        so.CoachFacilitiesList.AddRange(CoachFacilities.GetAllFacilities());
                    },
                () =>
                    {
                        so.RouteFacilitiesList = new List<RouteFacility>();
                        so.RouteFacilitiesList.AddRange(RouteFacilities.GetAllFacilities());
                    },
                () =>
                    {
                        so.Instance = GameObject.GetInstance();
                        so.Settings = Infrastructure.Settings.GetInstance();
                    },
                () =>
                    {
                        so.CarrierFacilitiesList = new List<CarrierFacility>();
                        so.CarrierFacilitiesList.AddRange(CarrierFacilities.GetFacilities());
                    });

            string fileName = AppSettings.GetCommonApplicationDataPath() + "/saves/" + name + ".sav";

            StartupLog.Write("SaveGame: collected the data in " + sw.ElapsedMilliseconds + " ms, writing " + fileName);
            FileSerializer.Serialize(fileName, so);
            StartupLog.Write("SaveGame: written, " + (File.Exists(fileName) ? new FileInfo(fileName).Length / 1024 + " KB" : "NO FILE"));

            sw.Stop();
            Console.WriteLine(@"Saving: {0} ms", sw.ElapsedMilliseconds);

            // (the statistics of routes, carriers and stations used to be cleared here after every save but the
            // autosave, so saving a game wiped the passenger totals shown on the route screens; they are kept now)
            /*
            SaveObject so = new SaveObject();
            Parallel.Invoke(() =>
            {
                so.stationsList = new List<Station>();
                so.stationsList.AddRange(Stations.GetAllStations());
            }, () =>
            {
                so.carriersList = new List<Carrier>();
                so.carriersList.AddRange(Carriers.GetAllCarriers());
            }, () =>
            {
                so.coachesList = new List<Coach>();
                so.coachesList.AddRange(Coaches.GetAllCoaches());
            }, () =>
            {
                so.calendaritemsList = new List<CalendarItem>();
                so.calendaritemsList.AddRange(CalendarItems.GetCalendarItems());
            }, () =>
            {
                so.configurationList = new List<Configuration>();
                so.configurationList.AddRange(Configurations.GetConfigurations());
            }, () =>
            {
                so.eventsList = new List<RandomEvent>();
                so.eventsList.AddRange(RandomEvents.GetEvents());
            }, () =>
            {
                so.allianceList = new List<Alliance>();
                so.allianceList.AddRange(Alliances.GetAlliances());
            }, () =>
            {
                so.Stationfacilitieslist = new List<StationFacility>();
                so.Stationfacilitieslist.AddRange(StationFacilities.GetFacilities());
            }, () =>
            {
                so.feeTypeslist = new List<FeeType>();
                so.feeTypeslist.AddRange(FeeTypes.GetTypes());
            }, () =>
            {
                so.advertisementTypeslist = new List<AdvertisementType>();
                so.advertisementTypeslist.AddRange(AdvertisementTypes.GetTypes());
            }, () =>
            {
                so.coachfacilitieslist = new List<CoachFacility>();
                so.coachfacilitieslist.AddRange(CoachFacilities.GetAllFacilities());
            }, () =>
            {
                so.routefacilitieslist = new List<RouteFacility>();
                so.routefacilitieslist.AddRange(RouteFacilities.GetAllFacilities());
            }, () =>
            {
                so.instance = GameObject.GetInstance();
                so.settings = Settings.GetInstance();
                so.savetype = "039";
                so.saveversionnumber = 1;
            });
            */
            /*
            DataContractSerializer serializer = new DataContractSerializer(typeof(SaveObject), null, Int32.MaxValue, false, true, null);

            using (Stream stream = new FileStream(fileName, FileMode.Create))
            {
                using (DeflateStream compress = new DeflateStream(stream, CompressionLevel.Fastest))
                {
                    serializer.WriteObject(compress, so);
                }
            }
          */
        }

        public static bool SaveGameExists(string name)
        {
            string fileName = AppSettings.GetCommonApplicationDataPath() + "/saves/" + name + ".sav";

            return File.Exists(fileName);
        }

        #endregion

        //deletes a saved game
    }

    [Serializable]
    public class SaveObject
    {
        #region Public Properties

        [Versioning("stationfacilities")]
        public List<StationFacility> StationFacilitiesList { get; set; }

        [Versioning("advertisementtypes")]
        public List<AdvertisementType> AdvertisementTypesList { get; set; }

        [Versioning("carrierfacilities")]
        public List<CarrierFacility> CarrierFacilitiesList { get; set; }

        [Versioning("coachfacilities")]
        public List<CoachFacility> CoachFacilitiesList { get; set; }

        [Versioning("coaches")]
        public List<Coach> CoachesList { get; set; }

        [Versioning("carriers")]
        public List<Carrier> CarriersList { set; get; }

        [Versioning("stations")]
        public List<Station> StationsList { set; get; }

        [Versioning("stationsfromstrings")]
        public List<string> StationsFromStringList { get; set; }

        [Versioning("alliances")]
        public List<Alliance> AllianceList { get; set; }

        [Versioning("calendaritems")]
        public List<CalendarItem> CalendarItemsList { get; set; }

        [Versioning("configurations")]
        public List<Configuration> ConfigurationList { get; set; }

        [Versioning("events")]
        public List<RandomEvent> EventsList { get; set; }

        [Versioning("feetypes")]
        public List<FeeType> FeeTypeslist { get; set; }

        // demand values edited by the player, see DemandOverrides
        [Versioning("demandoverrides")]
        public List<string> DemandOverridesList { get; set; }

        [Versioning("instance")]
        public GameObject Instance { get; set; }

        // the codes of every station in the data files at the time of saving, to tell new ones on load
        [Versioning("datastations")]
        public List<string> DataStationsList { get; set; }

        [Versioning("routefacilities")]
        public List<RouteFacility> RouteFacilitiesList { get; set; }

        // the rivals' moves for the competitor screen, and each coach's last trips for the trip board
        [Versioning("rivalmoves")]
        public List<SavedRivalMove> RivalMovesList { get; set; }

        [Versioning("flowntrips")]
        public List<SavedTrips> FlownTripsList { get; set; }

        // each carrier's trips by month for the monthly report
        [Versioning("monthlyops")]
        public List<SavedMonthOps> MonthlyOpsList { get; set; }

        public string SaveType { get; set; }

        public int SaveVersionNumber { get; set; }

        [Versioning("settings")]
        public Infrastructure.Settings Settings { get; set; }

        #endregion
    }

    /// <summary>
    ///     Finds types by name while reading and writing a save. The game stores many values (nested enums, nested classes,
    ///     closed generics such as Period of DateTime) inside serialization info with their runtime type, so a fixed list of
    ///     known types cannot cover them all.
    /// </summary>
    public sealed class SaveTypeResolver : DataContractResolver
    {
        private readonly System.Xml.XmlDictionary _dictionary = new System.Xml.XmlDictionary();

        private static bool IsBuiltIn(Type type)
        {
            return type.IsPrimitive || type == typeof (string) || type == typeof (decimal) || type == typeof (DateTime)
                   || type == typeof (TimeSpan) || type == typeof (Guid) || type == typeof (DateTimeOffset)
                   || type == typeof (byte[]);
        }

        public override bool TryResolveType(Type type, Type declaredType, DataContractResolver knownTypeResolver,
            out System.Xml.XmlDictionaryString typeName, out System.Xml.XmlDictionaryString typeNamespace)
        {
            if (IsBuiltIn(type))
            {
                return knownTypeResolver.TryResolveType(type, declaredType, null, out typeName, out typeNamespace);
            }

            // everything else is named by its assembly and full name, and found again by the same
            typeNamespace = _dictionary.Add(type.Assembly.FullName);
            typeName = _dictionary.Add(type.FullName);
            return true;
        }

        public override Type ResolveName(string typeName, string typeNamespace, Type declaredType,
            DataContractResolver knownTypeResolver)
        {
            if (typeNamespace.StartsWith("http://", StringComparison.Ordinal))
            {
                return knownTypeResolver.ResolveName(typeName, typeNamespace, declaredType, null);
            }

            var assemblyName = new System.Reflection.AssemblyName(typeNamespace).Name;
            Type found = System.Reflection.Assembly.Load(assemblyName).GetType(typeName);

            // saves made before the game logic moved to TheBusProject.Core name the old TheBusProject assembly
            if (found == null && assemblyName == "TheBusProject")
            {
                found = typeof(SerializedLoadSaveHelpers).Assembly.GetType(typeName);
            }

            return found;
        }
    }

    public static class FileSerializer
    {
        //BinaryFormatter is removed from modern .NET, so saves use GraphSerializer (a field based graph serializer).
        private static DataContractSerializer CreateSerializer(Type type)
        {
            return new DataContractSerializer(type, new DataContractSerializerSettings
            {
                DataContractResolver = new SaveTypeResolver(),
                MaxItemsInObjectGraph = int.MaxValue,
                PreserveObjectReferences = true
            });
        }

        #region Public Methods and Operators

        public static T Deserialize<T>(string filename)
        {
            T objectToSerialize = default(T);

            Stream stream = null;

            try
            {
                objectToSerialize = GraphSerializer.Load<T>(filename);
            }

            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
                StartupLog.Write("Reading the save failed: " + err);
            }

            finally
            {
                stream?.Close();
            }

            return objectToSerialize;
        }

        public static void Serialize(string filename, object objectToSerialize)
        {
            if (objectToSerialize == null)
            {
                throw new ArgumentNullException(nameof(objectToSerialize));
            }

            // write to a temporary file and replace the save only when that worked, so a failed save never leaves
            // an empty or half written file behind
            var temp = filename + ".tmp";

            try
            {
                GraphSerializer.Save(temp, objectToSerialize);

                File.Copy(temp, filename, true);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                StartupLog.Write("Writing the save failed: " + e);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }
            }
        }

        #endregion
    }
}
