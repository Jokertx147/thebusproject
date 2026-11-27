using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Passengers;

namespace TheBusProject.Models.Stations
{
    //some static values for an station
    public class StationStatics
    {
        #region Fields

        private readonly Dictionary<Station, double> _stationDistances;


        private readonly List<DestinationDemand> _passengerDemand;

        // the same demand objects by destination code (the first one for a code, as a search of the list finds):
        // looking a destination up in the list took a pass over thousands of entries. Built on the first look-up,
        // so only stations that are actually asked about (the ones in use) pay the memory for it.
        private Dictionary<string, DestinationDemand> _passengerIndex;

        private static Dictionary<string, DestinationDemand> BuildIndex(List<DestinationDemand> list)
        {
            var index = new Dictionary<string, DestinationDemand>(list.Count);
            foreach (DestinationDemand d in list)
                if (d?.Destination != null && !index.ContainsKey(d.Destination)) index[d.Destination] = d;
            return index;
        }

        private static void AddToIndex(Dictionary<string, DestinationDemand> index, DestinationDemand d)
        {
            if (index != null && d?.Destination != null && !index.ContainsKey(d.Destination)) index[d.Destination] = d;
        }

        // nearby stations, worked out from the positions on the first question (the distances kept above only hold
        // the stations the demand building happened to store for this one): within NearbyRange km, closest first
        private List<KeyValuePair<double, Station>> _nearby;
        private double _nearbyRange;

        // the position (from 1) of each class in the class list, which divides the demand between the classes
        private static readonly Dictionary<CoachClass.ClassType, int> ClassFactors =
            Enum.GetValues(typeof (CoachClass.ClassType)).Cast<CoachClass.ClassType>()
                .Select((t, i) => new {t, i}).GroupBy(x => x.t).ToDictionary(g => g.Key, g => g.Last().i + 1);

        #endregion

        #region Constructors and Destructors

        public StationStatics(Models.Stations.Station station)
        {
            _stationDistances = new Dictionary<Station, double>();
            _passengerDemand = new List<DestinationDemand>();
            Station = station;
        }

        #endregion

        #region Public Properties

        public Models.Stations.Station Station { get; set; }

        #endregion

        #region Public Methods and Operators


        //adds a distance to the station
        public void AddDistance(Models.Stations.Station station, double distance)
        {
            lock (_stationDistances)
            {
                if (!_stationDistances.ContainsKey(station))
                {
                    _stationDistances.Add(station, distance);
                }
            }
        }


        //sets (replaces) the base passenger demand to a destination
        public void SetPassengerDemand(Models.Stations.Station destination, ushort rate)
        {
            lock (_passengerDemand)
            {
                if (_passengerIndex == null) _passengerIndex = BuildIndex(_passengerDemand);
                DestinationDemand demand;
                _passengerIndex.TryGetValue(destination.Profile.IATACode, out demand);
                if (demand == null)
                {
                    demand = new DestinationDemand(destination.Profile.IATACode, rate);
                    _passengerDemand.Add(demand);
                    AddToIndex(_passengerIndex, demand);
                }
                else
                {
                    demand.Rate = rate;
                }
            }
        }

        public void AddPassengerDemand(DestinationDemand demand)
        {
            lock (_passengerDemand)
            {
                _passengerDemand.Add(demand);
                AddToIndex(_passengerIndex, demand);
            }
        }

        public List<Station> GetStationsWithin(double range)
        {
            List<KeyValuePair<double, Station>> nearby;
            lock (_stationDistances)
            {
                if (_nearby == null || range > _nearbyRange)
                {
                    // every station of the game within the range (at least 1,000 km, so later questions reuse it);
                    // like the distances stored while building the demand, not stations of the same town or closer
                    // than 50 km
                    double keep = Math.Max(1000, range);
                    var me = Station.Profile.Coordinates.ConvertToGeoCoordinate();
                    var list = new List<KeyValuePair<double, Station>>();
                    foreach (Models.Stations.Station station in Models.Stations.Stations.GetAllStations())
                    {
                        if (station == Station || station.Profile.Town == Station.Profile.Town) continue;
                        double d = me.GetDistanceTo(station.Profile.Coordinates.ConvertToGeoCoordinate())/1000;
                        if (d > 50 && d <= keep) list.Add(new KeyValuePair<double, Station>(d, station));
                    }
                    list.Sort((a, b) => a.Key.CompareTo(b.Key));
                    _nearby = list;
                    _nearbyRange = keep;
                }
                nearby = _nearby;
            }

            var stations = new List<Station>();
            foreach (var kv in nearby)
            {
                if (kv.Key > range) break;
                stations.Add(kv.Value);
            }
            return stations;
        }

        public List<DestinationDemand> GetDemands()
        {
            var demands = new List<DestinationDemand>();

            demands.AddRange(_passengerDemand);

            return demands;
        }

        //returns if the station have passenger demand to another station


        //returns the destination passengers for a specific destination for a class
        public ushort GetDestinationPassengersRate(Models.Stations.Station destination, CoachClass.ClassType type)
        {
            DestinationDemand pax;
            lock (_passengerDemand)
            {
                if (_passengerIndex == null) _passengerIndex = BuildIndex(_passengerDemand);
                _passengerIndex.TryGetValue(destination.Profile.IATACode ?? "", out pax);
            }

            int classFactor;
            ClassFactors.TryGetValue(type, out classFactor);

            if (pax == null)
            {
                return 0;
            }
            return (ushort) (pax.Rate/classFactor);
        }

        public int GetDestinationPassengersSum()
        {
            int sum;
            lock (_passengerDemand)
            {
                sum = _passengerDemand.Sum(p => p.Rate);
            }
            return sum;
        }

        //returns all demands

        //returns the distance for an station
        public double GetDistance(Models.Stations.Station station)
        {
            double distance;
            lock (_stationDistances)
            {
                distance = _stationDistances.ContainsKey(station) ? _stationDistances[station] : 0;
            }

            return distance;
        }


        public bool HasDestinationPassengersRate(Models.Stations.Station destination)
        {
            lock (_passengerDemand)
            {
                if (_passengerIndex == null) _passengerIndex = BuildIndex(_passengerDemand);
                return _passengerIndex.ContainsKey(destination.Profile.IATACode ?? "");
            }
        }

        #endregion

        //adds passenger demand to the station


        //returns all stations within a range
    }
}