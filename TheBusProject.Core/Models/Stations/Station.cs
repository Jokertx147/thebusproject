using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.CarrierCooperation;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Stations
{
    [Serializable]
    //the class for an station
    public class Station : BaseModel
    {
        #region Fields

        [Versioning("contracts")] private List<StationContract> _contracts;
        [Versioning("facilities")] private List<CarrierStationFacility> _facilities;

        private List<Hub> _hubs;

        #endregion

        #region Constructors and Destructors

        public Station(StationProfile profile)
        {
            Profile = profile;
            Income = 0;
            DestinationPassengers = new List<DestinationDemand>();
            _facilities = new List<CarrierStationFacility>();
            Cooperations = new List<Cooperation>();
            Statistics = new StationStatistics();
            Weather = new Weather[5];
            Terminals = new Terminals(this);
            Bays = new List<Bay>();
            _hubs = new List<Hub>();
            DestinationPassengerStatistics = new Dictionary<Station, long>();
            LastExpansionDate = new DateTime(1900, 1, 1);
            Statics = new StationStatics(this);
            CarrierContracts = new List<StationContract>();
        }

        private Station(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version < 3)
                LandingFee = StationHelpers.GetStandardLandingFee(this);

            if (Weather.Contains(null))
            {
                StationHelpers.CreateStationWeather(this);
            }
        }

        #endregion

        #region Public Properties

        public List<StationContract> CarrierContracts
        {
            get { return GetCarrierContracts(); }
            set { _contracts = value; }
        }

        [Versioning("cooperations", Version = 2)]
        public List<Cooperation> Cooperations { get; set; }

        [Versioning("hubs")]
        public List<Hub> Hubs
        {
            private get { return GetHubs(); }
            // the list object itself is never swapped: every reader and writer takes its lock, and a new list
            // here would leave two threads locking two different objects and guarding nothing
            set
            {
                lock (_hubs)
                {
                    _hubs.Clear();
                    if (value != null) _hubs.AddRange(value);
                }
            }
        }

        [Versioning("income")]
        public long Income { get; set; }

        public bool IsHub => GetHubs().Count > 0;

        [Versioning("lastexpansiondate")]
        public DateTime LastExpansionDate { get; set; }

        [Versioning("profile")]
        public StationProfile Profile { get; set; }

        [Versioning("bays")]
        public List<Bay> Bays { get; set; }

        /// <summary>True when the player changed the station (type, bays), so the save keeps the whole station.</summary>
        [Versioning("playeredited")]
        public bool PlayerEdited { get; set; }

        // distances and demand to every other station: worked out again when a game is loaded, so it is not saved
        // (saving it made saves several times bigger and pulled every station it named into the save)
        [NonSerialized]
        private StationStatics _statics;

        public StationStatics Statics
        {
            get { return _statics ?? (_statics = new StationStatics(this)); }
            set { _statics = value; }
        }

        [Versioning("statistics")]
        public StationStatistics Statistics { get; set; }

        [Versioning("terminals")]
        public Terminals Terminals { get; set; }

        [Versioning("weather")]
        public Weather[] Weather { get; set; }

        [Versioning("landingfee", Version = 3)]
        public double LandingFee { get; set; }

        #endregion

        #region Properties



        [Versioning("passengerstatistics")]
        private Dictionary<Station, long> _destinationPassengerStatistics;
        private Dictionary<Station, long> DestinationPassengerStatistics
        {
            get { return _destinationPassengerStatistics ?? (_destinationPassengerStatistics = new Dictionary<Station, long>()); }
            set { _destinationPassengerStatistics = value; }
        }

        [Versioning("destinationpassengers")]
        private List<DestinationDemand> _destinationPassengers;
        private List<DestinationDemand> DestinationPassengers
        {
            get { return _destinationPassengers ?? (_destinationPassengers = new List<DestinationDemand>()); }
            set { _destinationPassengers = value; }
        }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        public void AddCarrierContract(StationContract contract)
        {
            lock (_contracts)
            {
                _contracts.Add(contract);
            }

            if (!contract.Carrier.Stations.Contains(this))
            {
                contract.Carrier.AddStation(this);
            }
        }

        public void AddStationFacility(Carrier carrier, StationFacility facility, DateTime finishedDate)
        {
            //this.Facilities.RemoveAll(f => f.Carrier == carrier && f.Facility.Type == facility.Type);
            lock (_facilities) { _facilities.Add(new CarrierStationFacility(carrier, this, facility, finishedDate)); }
        }

        //sets the facility for an carrier
        public void AddStationFacility(CarrierStationFacility facility)
        {
            //this.Facilities.RemoveAll(f => f.Carrier == facility.Carrier && f.Facility.Type == facility.Facility.Type);
            lock (_facilities) { _facilities.Add(facility); }
        }


        public void AddCooperation(Cooperation cooperation)
        {
            Cooperations.Add(cooperation);
        }



        //sets the total (economy) passenger demand to a destination, replacing any earlier value
        public void SetDestinationPassengersDemand(Station destination, int value)
        {
            lock (DestinationPassengers)
            {
                DestinationPassengers.RemoveAll(d => d.Destination == destination.Profile.IATACode);
            }

            DemandOverrides.Set(this, destination, value);
            Statics.SetPassengerDemand(destination, (ushort) Math.Max(0, Math.Min(ushort.MaxValue, value)));
        }


        public void AddDestinationPassengersRate(DestinationDemand passengers)
        {
            Statics.AddPassengerDemand(passengers);
        }

        public void AddDestinationPassengersRate(Station destination, ushort rate)
        {
            lock (DestinationPassengers)
            {
                DestinationDemand destinationPassengers = GetDestinationPassengersObject(destination);

                if (destinationPassengers != null)
                {
                    destinationPassengers.Rate += rate;
                }
                else
                {
                    DestinationPassengers.Add(new DestinationDemand(destination.Profile.IATACode, rate));
                }
            }
        }

        public void AddHub(Hub hub)
        {
            lock (_hubs) _hubs.Add(hub);
        }

        //adds a major destination to the station
        public void AddMajorDestination(string destination, int pax)
        {
            if (Profile.MajorDestionations.ContainsKey(destination))
            {
                Profile.MajorDestionations[destination] += pax;
            }
            else
            {
                Profile.MajorDestionations.Add(destination, pax);
            }
        }

        public void AddPassengerDestinationStatistics(Station destination, long passengers)
        {
            lock (DestinationPassengerStatistics)
            {
                if (!DestinationPassengerStatistics.ContainsKey(destination))
                {
                    DestinationPassengerStatistics.Add(destination, passengers);
                }
                else
                {
                    DestinationPassengerStatistics[destination] += passengers;
                }
            }
        }

        public void AddTerminal(Terminal terminal)
        {
            Terminals.AddTerminal(terminal);
        }

        //returns a list of major destinations and pax

        //clears the list of carrier contracts
        public void ClearCarrierContracts()
        {
            lock (_contracts)
            {
                CarrierContracts.Clear();
            }
        }


        public void ClearDestinationPassengerStatistics()
        {
            DestinationPassengerStatistics.Clear();
        }

        public void ClearDestinationPassengers()
        {
            DestinationPassengers.Clear();
        }

        public void ClearFacilities()
        {
            _facilities = new List<CarrierStationFacility>();
        }

        //cleares the list of facilities for an carrier
        public void ClearFacilities(Carrier carrier)
        {
            lock (_facilities) { _facilities.RemoveAll(f => f.Carrier == carrier); }
        }

        /// <summary>
        ///     Removes the "none" facilities (level 0) that are an carrier's only entry for that type here: asking for the
        ///     facility makes one again (as for any station the carrier never used), and a new game made them for every
        ///     carrier at every station, a seventh of a saved game. A level 0 entry after a real facility (one taken
        ///     away) stays. Returns how many went.
        /// </summary>
        public int PruneNoneFacilities()
        {
            DateTime now = GameObject.GetInstance().GameTime;
            lock (_facilities)
            {
                var lone = _facilities.Where(f => f.Facility != null && f.Facility.TypeLevel == 0 && f.Facility.ServiceLevel == 0 && f.FinishedDate <= now)
                                      .Where(f => _facilities.Count(o => o.Carrier == f.Carrier && o.Facility != null && o.Facility.Type == f.Facility.Type) == 1)
                                      .ToList();
                foreach (var f in lone) _facilities.Remove(f);
                return lone.Count;
            }
        }

        //returns if an carrier is building a facility
        public bool IsBuildingFacility(Carrier carrier, StationFacility.FacilityType type)
        {
            var facilities = new List<CarrierStationFacility>();
            lock (_facilities)
            {
                return facilities.Exists(f => f.Carrier == carrier && f.Facility.Type == type && f.FinishedDate > GameObject.GetInstance().GameTime);
            }
        }

        public CarrierStationFacility GetCarrierStationFacility(Carrier carrier, StationFacility.FacilityType type)
        {
            List<CarrierStationFacility> facilities;
            lock (_facilities)
            {
                facilities = (from f in _facilities
                              where
                                  f.Carrier == carrier && f.Facility.Type == type
                                  && GameObject.GetInstance().GameTime >= f.FinishedDate
                              orderby f.FinishedDate descending
                              select f).ToList();

                if (!facilities.Any())
                {
                    StationFacility noneFacility = StationFacilities.GetFacilities(type).Find(f => f.TypeLevel == 0);
                    AddStationFacility(carrier, noneFacility, GameObject.GetInstance().GameTime);

                    facilities.Add(GetCarrierStationFacility(carrier, type));
                }
            }
            return facilities.FirstOrDefault();
        }

        public StationFacility GetCarrierBuildingFacility(Carrier carrier, StationFacility.FacilityType type)
        {
            CarrierStationFacility facility;
            lock (_facilities)
            {
                facility = _facilities.FirstOrDefault(
                    f =>
                    f.Carrier == carrier && f.Facility.Type == type
                    && GameObject.GetInstance().GameTime < f.FinishedDate);
            }

            return facility?.Facility;
        }

        //adds an carrier station contract to the station

        //returns the contracts for an carrier
        public List<StationContract> GetCarrierContracts(Carrier carrier)
        {
            return CarrierContracts.FindAll(a => a.Carrier == carrier);
        }

        //returns if an carrier has a contract of a specific type

        //return all carrier contracts
        public List<StationContract> GetCarrierContracts()
        {
            List<StationContract> contracts;
            lock (_contracts)
            {
                contracts = new List<StationContract>(_contracts);
            }
            return contracts;
        }

        public double GetCarrierReputation(Carrier carrier)
        {
            //The score could be station facilities for the carrier, routes, connecting routes, hotels, service level per route etc
            double score = 0;

            score += Cooperations.Where(c => c.Carrier == carrier).Sum(c => c.Type.ServiceLevel*9);

            List<CarrierStationFacility> facilities;

            lock (_facilities)
            {
                facilities = new List<CarrierStationFacility>(_facilities);
            }
            score += facilities.Where(f => f.Carrier == carrier).Sum(f => f.Facility.ServiceLevel*10);

            IEnumerable<Route> stationRoutes =
                carrier.Routes.Where(r => r.Destination1 == this || r.Destination2 == this);
            var enumerable = stationRoutes as Route[] ?? stationRoutes.ToArray();
            score += 7*enumerable.Count();
            score += 6
                     *enumerable.Where(r => r.Type == Route.RouteType.Passenger)
                                   .Sum(r => ((PassengerRoute) r).GetServiceLevel(CoachClass.ClassType.EconomyClass));

            score +=
                carrier.Alliances.Sum(
                    a =>
                    5
                    *a.Members.SelectMany(m => m.Carrier.Routes)
                      .Count(r => r.Destination1 == this || r.Destination2 == this));

            score = carrier.Codeshares.Select(codesharing => (codesharing.Carrier1 == carrier ? codesharing.Carrier2 : codesharing.Carrier1).Routes.Count(r => r.Destination2 == this || r.Destination1 == this)).Aggregate(score, (current, codesharingRoutes) => current + 4*codesharingRoutes);

            // happier customers choose the carrier more often
            return score*RatingsService.CustomerFactor(carrier)*AdvertisingService.StationFactor(carrier, this);
        }

        public List<CarrierStationFacility> GetStationFacilities(Carrier carrier)
        {
            List<CarrierStationFacility> fac;

            lock (_facilities)
            {
                fac = _facilities.FindAll(f => f.Carrier == carrier);
            }

            return fac;
        }

        //returns all facilities
        public List<CarrierStationFacility> GetStationFacilities()
        {
            return _facilities;
        }

        public StationFacility GetStationFacility(
            Carrier carrier,
            StationFacility.FacilityType type,
            Boolean useStation = false)
        {
            if (!useStation)
            {
                return GetCarrierStationFacility(carrier, type).Facility;
            }
            StationFacility carrierFacility = GetCurrentStationFacility(carrier, type);
            StationFacility stationFacility = GetCurrentStationFacility(null, type);

            return stationFacility == null || carrierFacility.TypeLevel > stationFacility.TypeLevel
                       ? carrierFacility
                       : stationFacility;
        }

        public List<StationFacility> GetCurrentStationFacilities(Carrier carrier)
        {
            return (from StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)) select GetCurrentStationFacility(carrier, type)).ToList();
        }

        public StationFacility GetCurrentStationFacility(Carrier carrier, StationFacility.FacilityType type)
        {
            List<StationFacility> facilities;

            // the copy is taken under the lock: computer carriers add facilities from other threads, and copying the
            // list while one was added failed the whole game day now and then
            List<CarrierStationFacility> tFacilities;
            lock (_facilities)
            {
                tFacilities = new List<CarrierStationFacility>(_facilities);
                facilities = (from f in tFacilities
                              where
                                  f.Carrier == carrier && f.Facility.Type == type
                                  && f.FinishedDate <= GameObject.GetInstance().GameTime
                              orderby f.FinishedDate descending
                              select f.Facility).ToList();
                int numberOfFacilities = facilities.Count();

                if (numberOfFacilities == 0 && carrier != null)
                {
                    StationFacility noneFacility = StationFacilities.GetFacilities(type).Find(f => f.TypeLevel == 0);
                    AddStationFacility(carrier, noneFacility, GameObject.GetInstance().GameTime);

                    facilities.Add(noneFacility);
                }
            }
            return facilities.FirstOrDefault();
        }


        //returns the maximum value for the run ways



        //adds a passenger rate for a destination

        //returns all stations where the station has demand
        public List<Station> GetDestinationDemands()
        {
            var destinations = new List<DestinationDemand>();

            destinations.AddRange(Statics.GetDemands());
            destinations.AddRange(DestinationPassengers);

            return destinations.Select(d => Stations.GetStation(d.Destination)).Distinct().ToList();
        }

        public long GetDestinationPassengerStatistics(Station destination)
        {
            long passengers;
            lock (DestinationPassengerStatistics)
            {
                passengers = DestinationPassengerStatistics.ContainsKey(destination) ? DestinationPassengerStatistics[destination] : 0;
            }

            return passengers;
        }

        //returns if the destination has passengers rate

        //returns a destination passengers object
        public DestinationDemand GetDestinationPassengersObject(Station destination)
        {
            return DestinationPassengers.Find(a => a.Destination == destination.Profile.IATACode);
        }

        // each class's place in the class list, from 1 (the extra demand is divided by it, as before)
        private static readonly Dictionary<CoachClass.ClassType, int> ClassNumbers =
            Enum.GetValues(typeof (CoachClass.ClassType)).Cast<CoachClass.ClassType>().Select((t, i) => new {t, i}).ToDictionary(x => x.t, x => x.i + 1);

        public ushort GetDestinationPassengersRate(Station destination, CoachClass.ClassType type)
        {
            // most stations have no extra demand on top of the data's: no search then
            var extra = DestinationPassengers;
            DestinationDemand pax = extra == null || extra.Count == 0 ? null : extra.Find(a => a.Destination == destination.Profile.IATACode);

            if (pax == null)
            {
                return Statics.GetDestinationPassengersRate(destination, type);
            }

            int classFactor;
            ClassNumbers.TryGetValue(type, out classFactor);
            return
                (ushort)
                (Statics.GetDestinationPassengersRate(destination, type) + (ushort) (pax.Rate/classFactor));
        }

        //returns the sum of passenger demand
        public int GetDestinationPassengersSum()
        {
            int sum;
            lock (DestinationPassengers)
            {
                sum = DestinationPassengers.Sum(d => d.Rate);
            }

            return sum + Statics.GetDestinationPassengersSum();
        }

        public List<DestinationDemand> GetDestinationsPassengers()
        {
            return DestinationPassengers;
        }

        //adds a number of passengers to destination to the statistics

        //returns the price for a gate
        public long GetGatePrice()
        {
            // What a bay costs for a year, before the contract's service multiplier. This used to run from $25,000
            // at the smallest station to $67,000 at the largest, which is wrong at both ends and wrong in shape: a
            // bay cost the carrier more than twice what owning the coach that used it cost, and a kerbside stop in
            // a small town was billed $2,000 a month. Greyhound's whole operating rents line was 8% of its revenue
            // and most of that was the 1,501 coaches it leased, not its stations; out in the country it does not
            // rent a bay at all, it pays an agent a commission on the tickets, which this game charges separately.
            // So the small places are cheap now and the curve is steep, because the gap between a kerb in Lubbock
            // and a stand at the Port Authority is enormous and a factor of 2.7 never said so.
            int s = (int) Profile.Size + 1;
            long sizeValue = 15 + 14*s*s;
            return Convert.ToInt64(GeneralHelpers.GetInflationPrice(sizeValue));
        }

        public List<Hub> GetHubs(HubType.TypeOfHub type)
        {
            List<Hub> hubs;
            lock (_hubs) hubs = new List<Hub>(_hubs);

            return hubs.FindAll(h => h.Type.Type == type);
        }

        //returns all hubs
        public List<Hub> GetHubs()
        {
            List<Hub> hubs;
            lock (_hubs) hubs = new List<Hub>(_hubs);

            return hubs;
        }

        /*
        //returns the fee for landing at the station
        public double getLandingFee()
        {
            long sizeValue = 151 * ((int)this.Profile.Size + 1);
            return GeneralHelpers.GetInflationPrice(sizeValue);
        }*/

        public Dictionary<Station, int> GetMajorDestinations()
        {
            return Profile.MajorDestionations.ToDictionary(md => Stations.GetStation(md.Key), md => md.Value);
        }

        public long GetMaxBayLength()
        {
            if (Bays.Count == 0)
            {
                return 0;
            }

            IEnumerable<long> query = from r in Bays
                                      where r.BuiltDate <= GameObject.GetInstance().GameTime
                                      select r.Length;
            return query.Max();
        }

        public long GetTerminalGatePrice()
        {
            long price = 75000*((int) Profile.Size + 1);
            return Convert.ToInt64(GeneralHelpers.GetInflationPrice(price));
        }

        public long GetTerminalPrice()
        {
            long price = 500000 + 15000*((int) Profile.Size + 1);
            return Convert.ToInt64(GeneralHelpers.GetInflationPrice(price));
        }

        /*
        //adds a facility to an carrier
        public void addStationFacility(Carrier carrier, StationFacility facility, DateTime finishedDate)
        {
            lock (this.Facilities)
            {
                this.Facilities.Add(new CarrierStationFacility(carrier, this, facility, finishedDate));
            }
        }*/
        //sets the facility for an carrier

        //returns if the station has a facility for any carrier
        public Boolean HasCarrierFacility()
        {
            lock (_facilities) return _facilities.Exists(f => f.Carrier != null && f.Facility.TypeLevel > 0);
        }

        public Boolean HasAsHomebase(Carrier carrier)
        {
            return carrier.Fleet.Any(coach => coach.Homebase == this);
        }

        public Boolean HasContractType(Carrier carrier, StationContract.ContractType type)
        {
            return CarrierContracts.Exists(c => c.Carrier == carrier && c.Type == type);
        }


        public Boolean HasDestinationPassengerStatistics(Station destination)
        {
            return DestinationPassengerStatistics.ContainsKey(destination);
        }

        public Boolean HasDestinationPassengersRate(Station destination)
        {
            return Statics.HasDestinationPassengersRate(destination)
                   || DestinationPassengers.Exists(a => a.Destination == destination.Profile.IATACode);
        }

        //returns the facilities being build for an carrier

        //returns if an carrier has any facilities at the station
        public Boolean HasFacilities(Carrier carrier)
        {
            Boolean hasFacilities = false;
            foreach (StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)))
            {
                if (GetStationFacility(carrier, type).TypeLevel > 0)
                {
                    hasFacilities = true;
                }
            }
            return hasFacilities;
        }

        //returns if an carrier has any facilities besides a specific type
        public Boolean HasFacilities(Carrier carrier, StationFacility.FacilityType ftype)
        {
            Boolean hasFacilities = false;
            foreach (StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)))
            {
                if (type != ftype)
                {
                    if (GetStationFacility(carrier, type).TypeLevel > 0)
                    {
                        hasFacilities = true;
                    }
                }
            }
            return hasFacilities;
        }

        public Boolean HasHub(Carrier carrier)
        {
            return Hubs.Exists(h => h.Carrier == carrier);
        }

        public void RemoveCarrierContract(StationContract contract)
        {
            lock (_contracts)
            {
                _contracts.Remove(contract);
            }

            if (!CarrierContracts.Exists(c => c.Carrier == contract.Carrier))
            {
                contract.Carrier.RemoveStation(this);
            }
        }

        public void RemoveCooperation(Cooperation cooperation)
        {
            Cooperations.Remove(cooperation);
        }

        //returns if an carrier has any coaches with the station as home base

        //removes the facility for an carrier
        public void RemoveFacility(Carrier carrier, StationFacility facility)
        {
            lock (_facilities) { _facilities.RemoveAll(f => f.Carrier == carrier && f.Facility.Type == facility.Type); }
        }

        //clears the list of facilites

        //removes a hub
        public void RemoveHub(Hub hub)
        {
            lock (_hubs) _hubs.Remove(hub);
        }

        //returns if an carrier have a hub

        //removes a terminal from the station
        public void RemoveTerminal(Terminal terminal)
        {
            StationContract terminalContract =
                CarrierContracts.Find(c => c.Terminal != null && c.Terminal == terminal);

            if (terminalContract != null)
            {
                RemoveCarrierContract(terminalContract);
            }

            Terminals.RemoveTerminal(terminal);
        }

        #endregion

        //adds a cooperation to the station
    }

    //the collection of stations
    public class Stations
    {
        #region Static Fields

        public static double LargeStations;

        public static double LargestStations;

        public static double MediumStations,
                             SmallStations;

        public static double SmallestStations;

        public static double VeryLargeStations;

        public static double VerySmallStations;

        private static List<Station> _stations = new List<Station>();

        #endregion

        #region Public Methods and Operators

        public static void AddStation(Station station)
        {
            _stations.Add(station);
        }

        //clears the list
        public static void Clear()
        {
            _stations = new List<Station>();
        }

        //returns if a specific station is in the list
        public static Boolean Contains(Station station)
        {
            return _stations.Contains(station);
        }

        public static int Count()
        {
            return GetAllActiveStations().Count;
        }

        /// <summary>
        ///     Counts the stations of each size (the demand formula shares an station's passengers among them). Sizes
        ///     change with the year: counted again when the demand is made, for the game's date then (counted only when
        ///     the data was loaded, a new game and a loaded one got slightly different demand).
        /// </summary>
        // every station of the data files (the whole world, also those outside the game's area): the demand formula
        // shares an station's passengers among all the world's stations of a size, so in a game of one area the
        // passengers to the rest of the world aren't there (counting only the game's stations put them all on its
        // routes: a fifth more demand)
        private static List<StationProfile> _world;

        /// <summary>Remembers the data files' stations (when they are loaded, before a game keeps only its area).</summary>
        public static void RememberWorld()
        {
            _world = _stations.Select(a => a.Profile).ToList();
        }

        public static void CountSizes()
        {
            var profiles = _world != null && _world.Count > 0 ? _world : GetAllActiveStations().Select(a => a.Profile).ToList();
            var sizes = profiles.Where(p => p.Period == null || (p.Period.From <= GameObject.GetInstance().GameTime && p.Period.To > GameObject.GetInstance().GameTime) || GameObject.GetInstance().GameTime == default(DateTime))
                                .Select(p => p.Size).ToList();
            LargestStations = sizes.Count(s => s == GeneralHelpers.Size.Largest);
            VeryLargeStations = sizes.Count(s => s == GeneralHelpers.Size.VeryLarge);
            LargeStations = sizes.Count(s => s == GeneralHelpers.Size.Large);
            MediumStations = sizes.Count(s => s == GeneralHelpers.Size.Medium);
            SmallStations = sizes.Count(s => s == GeneralHelpers.Size.Small);
            VerySmallStations = sizes.Count(s => s == GeneralHelpers.Size.VerySmall);
            SmallestStations = sizes.Count(s => s == GeneralHelpers.Size.Smallest);
        }

        public static double CountLarge()
        {
            return LargeStations;
        }

        public static double CountLargest()
        {
            return LargestStations;
        }

        public static double CountMedium()
        {
            return MediumStations;
        }

        public static double CountSmall()
        {
            return SmallStations;
        }

        public static double CountSmallest()
        {
            return SmallestStations;
        }

        public static double CountVeryLarge()
        {
            return VeryLargeStations;
        }

        public static double CountVerySmall()
        {
            return VerySmallStations;
        }

        //adds an station

        //returns an station based on iata code
        public static Station GetStation(string iata)
        {
            List<Station> tStations = _stations.FindAll(a => a.Profile.IATACode == iata);

            if (tStations.Count == 1)
            {
                return tStations[0];
            }

            Station tStation = tStations.Find(GeneralHelpers.IsStationActive);

            return tStation;
        }

        //returns an station based on match
        public static Station GetStation(Predicate<Station> match)
        {
            return _stations.Find(match);
        }

        //returns an station based on id

        //returns a possible match for coordinates
        public static Station GetStation(GeoCoordinate coordinates)
        {
            return GetAllActiveStations().Find(a => false);
        }

        public static Station GetStationFromID(string id)
        {
            Station station = _stations.Find(a => a.Profile.ID == id);

            if (station != null)
            {
                return station;
            }
            return _stations.Find(a => a.Profile.ID.StartsWith(id.Substring(0, 8)));
            //stations.Find(a=>a.Profile.ID.StartsWith(id.Substring(0, id.LastIndexOf('-'))));
        }

        //returns all stations with a specific size
        public static List<Station> GetStations(GeneralHelpers.Size size)
        {
            return GetStations(station => station.Profile.Size == size);
        }

        //returns all stations from a specific country
        public static List<Station> GetStations(Country country)
        {
            return GetStations(station => station.Profile.Country == country);
        }

        //returns all stations from a specific region
        public static List<Station> GetStations(Region region)
        {
            return GetStations(station => station.Profile.Country.Region == region);
        }

        //returns a list of stations
        public static List<Station> GetStations(Predicate<Station> match)
        {
            return GetAllActiveStations().FindAll(match);
        }

        public static List<Station> GetAllActiveStations()
        {
            return _stations.FindAll(GeneralHelpers.IsStationActive);
        }

        //returns all stations
        public static List<Station> GetAllStations(Predicate<Station> match)
        {
            return _stations.FindAll(match);
        }

        public static List<Station> GetAllStations()
        {
            return _stations;
        }

        //returns the total number of stations

        //removes all stations with a specific match
        public static void RemoveStations(Predicate<Station> match)
        {
            _stations.RemoveAll(match);
        }

        #endregion
    }
}