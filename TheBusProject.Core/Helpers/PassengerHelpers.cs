using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Holidays;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helper class for the passengers
    public class PassengerHelpers
    {
        #region Static Fields

        private static readonly Dictionary<GeneralHelpers.Size, double> CargoFactors =
            new Dictionary<GeneralHelpers.Size, double>();

        private static readonly Dictionary<Carrier, double> HappinessPercent = new Dictionary<Carrier, double>();

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        #endregion

        #region Public Methods and Operators

        public static void AddPassengerHappiness(Carrier carrier)
        {
            /*
            lock (HappinessPercent)
            {
                if (HappinessPercent.ContainsKey(carrier))
                    HappinessPercent[carrier] += 1;
                else
                    HappinessPercent.Add(carrier, 1);
            }
             * */
        }

        public static void ChangePaxDemand(double factor)
        {
            ChangePaxDemand(Stations.GetAllActiveStations(), factor);
        }

        //changes the demand for a list of stations with a factor
        public static void ChangePaxDemand(List<Station> stations, double factor)
        {
            //increases the passenger demand between stations with 5%
            //Parallel.ForEach(stations, station => { ChangePaxDemand(station, factor); });

            foreach (Station station in stations)
            {
                ChangePaxDemand(station, factor);
            }
        }

        //changes the demand for all stations belonging to an carrier with a factor
        public static void ChangePaxDemand(Carrier carrier, double factor)
        {
            foreach (Station a in carrier.Stations)
            {
                ChangePaxDemand(a, factor);
            }
        }

        //changes the demand between two stations
        public static void ChangePaxDemand(Station station1, Station station2, int value)
        {
            DestinationDemand destPax = station1.GetDestinationPassengersObject(station2);

            if (destPax != null && value > 0)
            {
                destPax.Rate += Convert.ToUInt16(value);
            }
            else
            {
                if (value > 0)
                {
                    station1.AddDestinationPassengersRate(station2, Convert.ToUInt16(value));
                }
            }
        }

        //changes the demand for an station with a factor
        public static void ChangePaxDemand(Station station, double factor)
        {
            double value = (100 + factor)/100;
            //factor 0.5 - 2;
            foreach (DestinationDemand destPax in station.GetDestinationsPassengers())
            {
                if (destPax.Rate > 0)
                {
                    ushort oRate = destPax.Rate;
                    if ((oRate*value).Equals(destPax.Rate))
                    {
                        destPax.Rate =
                            Convert.ToUInt16(destPax.Rate + Convert.ToUInt16(Rnd.Next(0, Math.Max(1, (int) factor))));
                    }
                    else
                    {
                        destPax.Rate = (ushort) (destPax.Rate*value);
                    }
                }
                else
                {
                    destPax.Rate = (ushort) Rnd.Next(1, Math.Max(1, (int) factor));
                }
            }
        }

        public static void CreateCarrierDestinationDemand()
        {
            Stations.CountSizes();
            //sets up the factors if they are not already setted up

            if (CargoFactors.Count == 0)
            {
                CargoFactors.Add(GeneralHelpers.Size.Largest, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VeryLarge, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Large, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Medium, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Small, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VerySmall, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Smallest, 0.23);
            }

            IEnumerable<Station> stations = Carriers.GetAllCarriers().SelectMany(a => a.Stations);

            foreach (Station station in stations)
            {
                //Parallel.ForEach(
                //    Stations.GetAllStations(),
                //    dStation =>
                foreach (Station dStation in Stations.GetAllStations())
                {
                    station.Statics.AddDistance(dStation, MathHelpers.GetDistance(station, dStation));

                    if (station != dStation && station.Profile.Town != dStation.Profile.Town
                        && MathHelpers.GetDistance(station, dStation) > 50)
                    {
                        CreateDestinationPassengers(station, dStation);

                        CreateDestinationCargo(station, dStation);
                    }
                } //);
                if (station.Statics.GetDestinationPassengersSum() == 0)
                {
                    Station station1 = station;
                    List<Station> subStations =
                        Stations.GetAllStations(a => a.Profile.Country == station1.Profile.Country)
                                .DefaultIfEmpty()
                                .ToList();
                    subStations.RemoveAll(a => a == null);

                    if (subStations.Any())
                    {
                        CreateDestinationPassengers(station, subStations);
                    }
                    else
                    {
                        Station station2 = station;
                        subStations =
                            Stations.GetAllStations(a => a.Profile.Country.Region == station2.Profile.Country.Region)
                                    .ToList();
                        CreateDestinationPassengers(station, subStations);
                    }
                }
            }
        }

        public static void CreateDestinationCargo(Station station, Station dStation)
        {
            //origin station out
            double originMassPerDay = GetStationCargoMass(station)/2;
            double originDemand = (originMassPerDay*CargoFactors[dStation.Profile.Cargo])
                                  /Stations.CargoStationsSizes[dStation.Profile.Cargo];
            //destination station in
            double destinationMassPerDay = GetStationCargoMass(dStation)/2;
            double destinationDemand = (destinationMassPerDay*CargoFactors[station.Profile.Cargo])
                                       /Stations.CargoStationsSizes[station.Profile.Cargo];

            double distance = MathHelpers.GetDistance(station, dStation);

            double distanceFactor = distance > 5000 ? 1 : 0.5;
            double sameCountryFactor = station.Profile.Country == dStation.Profile.Country ? 0.75 : 1;
            double sameRegionFactor = sameCountryFactor.Equals(1)
                                      && station.Profile.Country.Region == dStation.Profile.Country.Region
                                          ? 0.5
                                          : 1;

            double minMass = Math.Min(originMassPerDay, destinationMassPerDay);

            double volume = minMass/distanceFactor;
            volume = volume/sameCountryFactor;
            volume = volume/sameRegionFactor;
            //converts to pounds of cargo
            volume *= 35.1;

            if (volume >= 1)
            {
                station.AddDestinationCargoRate(new DestinationDemand(dStation.Profile.IATACode, (ushort) volume));
            }
        }

        /// <summary>
        ///     Makes every station's demand again (as loading a game does): on a new year, so the year's growth is the same
        ///     whether the game is played on or saved and loaded. The player's edits to the demand are kept.
        /// </summary>
        public static void RebuildDemand()
        {
            foreach (Station station in Stations.GetAllStations())
            {
                station.Statics = new StationStatics(station);
            }
            CreateDestinationDemand();
            DemandOverrides.Apply();
        }

        public static void CreateDestinationDemand()
        {
            Stations.CountSizes();
            if (CargoFactors.Count == 0)
            {
                CargoFactors.Add(GeneralHelpers.Size.Largest, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VeryLarge, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Large, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Medium, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Small, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.VerySmall, 0.23);
                CargoFactors.Add(GeneralHelpers.Size.Smallest, 0.23);
            }

            List<Station> stations = Stations.GetAllStations(a => a.Statics.GetDestinationPassengersSum() == 0);
            int count = stations.Count;
            // the stations whose demand was made before (the carriers' stations, at the start of a game): they have
            // their demand to every station, but the others' demand to them is made here, or nobody would travel to
            // them (a new game had no passengers from Charlotte, Orlando or Chicago to Atlanta)
            var built = new HashSet<Station>(stations);
            List<Station> before = Stations.GetAllStations(a => !built.Contains(a));
            var beforePoints = before.Select(a => a.Profile.Coordinates.ConvertToGeoCoordinate()).ToArray();
            // each station's position once, instead of converting it for every pair
            var points = stations.Select(a => a.Profile.Coordinates.ConvertToGeoCoordinate()).ToArray();

            //var stations = Stations.GetStations(a => a != station && a.Profile.Town != station.Profile.Town && MathHelpers.GetDistance(a.Profile.Coordinates, station.Profile.Coordinates) > 50);

            Parallel.For(
                0,
                count,
                i =>
                    {
                        for (int k = 0; k < before.Count; k++)
                        {
                            if (stations[i].Profile.Town == before[k].Profile.Town) continue;
                            double km = points[i].GetDistanceTo(beforePoints[k])/1000;
                            if (km > 50)
                            {
                                CreateDestinationPassengers(stations[i], before[k]);
                                CreateDestinationCargo(stations[i], before[k]);
                            }
                        }

                        // the pairs of one station in a plain loop: the outer loop already uses every processor,
                        // and starting a parallel loop for each station cost more than the work in it
                        for (int j = i + 1; j < count; j++)
                                {
                                    if (stations[i].Profile.Town == stations[j].Profile.Town) continue;
                                    double distance = points[i].GetDistanceTo(points[j])/1000;
                                    if (distance > 50)
                                    {
                                        stations[j].Statics.AddDistance(stations[i], distance);

                                        CreateDestinationPassengers(stations[j], stations[i]);
                                        CreateDestinationPassengers(stations[i], stations[j]);

                                        CreateDestinationCargo(stations[j], stations[i]);
                                        CreateDestinationCargo(stations[i], stations[j]);
                                    }
                                }

                    });

            // an station left without any demand gets a little to the country's (or region's) biggest stations: only
            // now that every pair is made (checked inside the parallel loop, another thread could still be adding the
            // station's pairs, and the made-up demand came first: Altoona to Washington Dulles had 1 a day for 28)
            // (to any of the game's stations: the carriers' stations, made earlier in a new game, too)
            List<Station> all = Stations.GetAllActiveStations();
            for (int i = 0; i < count; i++)
            {
                if (stations[i].Statics.GetDestinationPassengersSum() != 0) continue; // (the built demand only, not the extra from events)
                List<Station> subStations = all.FindAll(a => a != stations[i] && a.Profile.Country == stations[i].Profile.Country);
                if (!subStations.Any())
                {
                    subStations = all.FindAll(a => a != stations[i] && a.Profile.Country.Region == stations[i].Profile.Country.Region);
                }
                if (subStations.Any()) CreateDestinationPassengers(stations[i], subStations);
            }
        }

        public static void CreateDestinationPassengers(Station station)
        {
            List<Station> stations =
                Stations.GetStations(
                    a =>
                    a != station && a.Profile.Town != station.Profile.Town
                    && MathHelpers.GetDistance(
                        a.Profile.Coordinates.ConvertToGeoCoordinate(),
                        station.Profile.Coordinates.ConvertToGeoCoordinate()) > 50);
            //Parallel.ForEach(stations, dStation =>
            foreach (Station dStation in stations)
            {
                CreateDestinationPassengers(station, dStation);
            } //);
            if (station.GetDestinationsPassengers().Sum(d => d.Rate) == 0)
            {
                List<Station> subStations =
                    stations.FindAll(a => a.Profile.Country == station.Profile.Country).DefaultIfEmpty().ToList();
                subStations.RemoveAll(a => a == null);
                if (subStations.Any())
                {
                    CreateDestinationPassengers(station, subStations);
                }
                else
                {
                    subStations =
                        stations.FindAll(a => a.Profile.Country.Region == station.Profile.Country.Region).ToList();
                    CreateDestinationPassengers(station, subStations);
                }
            }
        }

        // station size names for the demand table (an enum's name was asked for twice per station pair)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<GeneralHelpers.Size, string> SizeNames =
            new System.Collections.Concurrent.ConcurrentDictionary<GeneralHelpers.Size, string>();

        // the demand table below was written when the sizes were spelled Very_large and Very_small; the enum lost the
        // underscore in the port, which left both those sizes falling through every switch and earning no demand at all
        private static string SizeName(GeneralHelpers.Size size)
        {
            return SizeNames.GetOrAdd(
                size,
                s => s == GeneralHelpers.Size.VeryLarge ? "Very_large"
                     : s == GeneralHelpers.Size.VerySmall ? "Very_small"
                     : s.ToString());
        }

        public static void CreateDestinationPassengers(Station station, Station dStation)
        {
            // the same pair gets the same numbers every time the demand is built
            using (MathHelpers.SeedFor(station.Profile.IATACode + ">" + dStation.Profile.IATACode))
                CreateDestinationPassengersSeeded(station, dStation);
        }

        private static void CreateDestinationPassengersSeeded(Station station, Station dStation)
        {
            //short_haul, long_haul, demand both ways
            double estimatedPassengerLevel = 0;
            bool isSameCountry = station.Profile.Country == dStation.Profile.Country;
            bool isSameContinent = station.Profile.Country.Region == dStation.Profile.Country.Region
                                      && !isSameCountry;

            String dStationSize = SizeName(dStation.Profile.Size);
            String stationSize = SizeName(station.Profile.Size);
            double dist = MathHelpers.GetDistance(dStation, station);

            if (station.Profile.MajorDestionations.Keys.Contains(dStation.Profile.IATACode))
            {
                estimatedPassengerLevel =
                    (Convert.ToDouble(station.Profile.MajorDestionations[dStation.Profile.IATACode])*1000)/365;
                estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
            }
            else
            {
                switch (stationSize)
                {
                        #region Origin"Largest" switches

                    case "Largest":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 40000*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 40000*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 40000*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 40000*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 40000*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Very_small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVery_small = 40000*0.04/Stations.VerySmallStations;
                                    paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVery_small*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                            case "Smallest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 40000*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Very_large" switches

                    case "Very_large":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 20000*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 20000*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 20000*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 20000*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 20000*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 20000*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":

                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 20000*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Large" switches

                    case "Large":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 10000*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 10000*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 10000*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 10000*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 10000*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 10000*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":

                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 10000*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Medium" switches

                    case "Medium":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 6000*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 6000*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 6000*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 6000*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 6000*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":

                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 6000*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 6000*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Small" switches

                    case "Small":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 1250*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 1250*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 1250*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 1250*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 1250*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 1250*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 1250*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Very_small" switches

                    case "Very_small":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 225*0.21/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 225*0.24/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 225*0.24/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxMedium = 225*0.15/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmall = 225*0.10/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Very_small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 225*0.04/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 225*0.02/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion

                        #region Origin "Smallest" switches

                    case "Smallest":
                        switch (dStationSize)
                        {
                            case "Largest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLargest = station.Profile.Pax*0.25/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = paxLargest*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLargest = 50*0.25/Stations.LargestStations;
                                    paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                                    estimatedPassengerLevel = (paxLargest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }

                                break;

                            case "Very_large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVeryLarge = station.Profile.Pax*0.32/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = paxVeryLarge*1000/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVeryLarge = 50*0.32/Stations.VeryLargeStations;
                                    paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                                    estimatedPassengerLevel = (paxVeryLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }

                                break;

                            case "Large":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxLarge = station.Profile.Pax*0.32/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxLarge = 50*0.32/Stations.LargeStations;
                                    paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                                    estimatedPassengerLevel = (paxLarge*1000)/365;

                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Medium":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxMedium = station.Profile.Pax*0.09/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 2;
                                }
                                else
                                {
                                    double paxMedium = 50*0.09/Stations.MediumStations;
                                    paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                                    estimatedPassengerLevel = (paxMedium*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 2;
                                }
                                break;

                            case "Small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmall = station.Profile.Pax*0.02/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 1.2;
                                }
                                else
                                {
                                    double paxSmall = 50*0.02/Stations.SmallStations;
                                    paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                                    estimatedPassengerLevel = (paxSmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                    estimatedPassengerLevel *= 1.2;
                                }
                                break;

                            case "Very_small":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxVerySmall = station.Profile.Pax*0/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxVerySmall = 50*0/Stations.VerySmallStations;
                                    paxVerySmall *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                                    estimatedPassengerLevel = (paxVerySmall*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;

                            case "Smallest":
                                if (station.Profile.Pax > 0)
                                {
                                    double paxSmallest = station.Profile.Pax*0/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                else
                                {
                                    double paxSmallest = 50*0/Stations.SmallestStations;
                                    paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                                    estimatedPassengerLevel = (paxSmallest*1000)/365;
                                    estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                                }
                                break;
                        }
                        break;

                        #endregion
                }
            }

            #region Demand with "if" statements

            //PLEASE don't change the same country/continent/international values. Most of these were specifically calculated and are not yet calculated 
            //by the program itself! Based largely on US station system values.
            /*     #region largest stations
                 if (dStationSize.Equals("Largest") && stationSize.Equals("Largest"))
                 {
                     if (station.Profile.Pax > 0)
                     {
                         double paxLargest = station.Profile.Pax * 0.21 / Stations.LargestStations;
                         paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                         estimatedPassengerLevel = (paxLargest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxLargest = 40000 * 0.21 / Stations.LargestStations;
                         paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                         estimatedPassengerLevel = (paxLargest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dStationSize.Equals("Very_large") && stationSize.Equals("Largest"))
                 {
                     if (station.Profile.Pax == 0)
                     {
                         double paxVeryLarge = 40000 * 0.24 / Stations.VeryLargeStations;
                         paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                         estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxVeryLarge = station.Profile.Pax * 0.24 / Stations.VeryLargeStations;
                         paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                         estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dStationSize.Equals("Large") && stationSize.Equals("Largest"))
                 {
                     if (station.Profile.Pax == 0)
                     {
                         double paxLarge = 40000 * 0.24 / Stations.LargeStations;
                         paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                         estimatedPassengerLevel = (paxLarge * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxLarge = station.Profile.Pax * 0.24 / Stations.LargeStations;
                         paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                         estimatedPassengerLevel = (paxLarge * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dStationSize.Equals("Medium") && stationSize.Equals("Largest"))
                 {
                     if (station.Profile.Pax == 0)
                     {
                         double paxMedium = 40000 * 0.15 / Stations.MediumStations;
                         paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                         estimatedPassengerLevel = (paxMedium * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                     else
                     {
                         double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                         paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                         estimatedPassengerLevel = (paxMedium * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.55;
                     }
                 }

                 if (dStationSize.Equals("Small") && stationSize.Equals("Largest"))
                 {
                     if (station.Profile.Pax == 0)
                     {
                         double paxSmall = 40000 * 0.10 / Stations.SmallStations;
                         paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                         estimatedPassengerLevel = (paxSmall * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.2;
                     }
                     else
                     {
                         double paxSmall = station.Profile.Pax * 0.10 / Stations.SmallStations;
                         paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                         estimatedPassengerLevel = (paxSmall * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.2;
                     }
                 }

                 if (dStationSize.Equals("Very_small") && stationSize.Equals("Largest"))
                 {
                     if (station.Profile.Pax == 0)
                     {
                         double paxVery_small = 40000 * 0.04 / Stations.VerySmallStations;
                         paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                         estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.1;
                     }
                     else
                     {
                         double paxVery_small = station.Profile.Pax * 0.04 / Stations.VerySmallStations;
                         paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                         estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.67;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 1.39;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0.1;
                     }
                 }

                 if (dStationSize.Equals("Smallest") && stationSize.Equals("Largest"))
                 {
                     if (dist > 1600)
                     { estimatedPassengerLevel = 0; }

                     else if (station.Profile.Pax == 0)
                     {
                         double paxSmallest = 40000 * 0.02 / Stations.SmallestStations;
                         paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                         estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.8;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 0.8;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0;
                     }
                     else
                     {
                         double paxSmallest = 40000 * 0.02 / Stations.SmallestStations;
                         paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                         estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                         estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                         if (isSameCountry)
                             estimatedPassengerLevel *= 1.8;
                         if (isSameContinent)
                             estimatedPassengerLevel *= 0.8;
                         if (!isSameContinent && !isSameCountry)
                             estimatedPassengerLevel *= 0;
                     }
                 }
             }
                 #endregion
             #region very large stations
             if (dStationSize.Equals("Largest") && stationSize.Equals("Very_large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLargest = 20000 * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = station.Profile.Pax * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Very_large") && stationSize.Equals("Very_large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 20000 * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = station.Profile.Pax * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Large") && stationSize.Equals("Very_large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLarge = 20000 * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = station.Profile.Pax * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Medium") && stationSize.Equals("Very_large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxMedium = 20000 * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Small") && stationSize.Equals("Very_large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxSmall = 20000 * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.7;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.35;
                 }
                 else
                 {
                     double paxSmall = station.Profile.Pax * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.7;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.35;
                 }
             }

             if (dStationSize.Equals("Very_small") && stationSize.Equals("Very_large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVery_small = 20000 * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.2;
                 }
                 else
                 {
                     double paxVery_small = station.Profile.Pax * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.2;
                 }
             }

             if (dStationSize.Equals("Smallest") && stationSize.Equals("Very_large"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0; }

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmallest = 20000 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.8;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
                 else
                 {
                     double paxSmallest = 20000 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.8;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
             }

             #endregion
             #region large stations
             if (dStationSize.Equals("Largest") && stationSize.Equals("Large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLargest = 10000 * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = station.Profile.Pax * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Very_large") && stationSize.Equals("Large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 10000 * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = station.Profile.Pax * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Large") && stationSize.Equals("Large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLarge = 10000 * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = station.Profile.Pax * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Medium") && stationSize.Equals("Large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxMedium = 10000 * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Small") && stationSize.Equals("Large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxSmall = 10000 * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.15;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.3;
                 }
                 else
                 {
                     double paxSmall = station.Profile.Pax * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.75;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.15;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.3;
                 }
             }

             if (dStationSize.Equals("Very_small") && stationSize.Equals("Large"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVery_small = 10000 * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.9;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.13;
                 }
                 else
                 {
                     double paxVery_small = station.Profile.Pax * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.9;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.13;
                 }
             }

             if (dStationSize.Equals("Smallest") && stationSize.Equals("Large"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0; }

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmallest = 10000 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.7;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
                 else
                 {
                     double paxSmallest = 10000 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.7;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
             }
             #endregion
             #region medium stations
             if (dStationSize.Equals("Largest") && stationSize.Equals("Medium"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLargest = 6000 * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = station.Profile.Pax * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Very_large") && stationSize.Equals("Medium"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 6000 * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = station.Profile.Pax * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Large") && stationSize.Equals("Medium"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLarge = 6000 * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = station.Profile.Pax * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Medium") && stationSize.Equals("Medium"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxMedium = 6000 * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Small") && stationSize.Equals("Medium"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxSmall = 6000 * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.25;
                 }
                 else
                 {
                     double paxSmall = station.Profile.Pax * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.8;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.25;
                 }
             }

             if (dStationSize.Equals("Very_small") && stationSize.Equals("Medium"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0; }
                
                 else if (station.Profile.Pax == 0)
                 {
                     double paxVery_small = 6000 * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.95;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
                 else
                 {
                     double paxVery_small = station.Profile.Pax * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.95;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.1;
                 }
             }

             if (dStationSize.Equals("Smallest") && stationSize.Equals("Medium"))
             {
                 if (dist > 1200 )
                 { estimatedPassengerLevel = 0; }

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmallest = 6000 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
                 else
                 {
                     double paxSmallest = 6000 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
             }
             #endregion
             #region small stations
             if (dStationSize.Equals("Largest") && stationSize.Equals("Small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLargest = 1250 * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = station.Profile.Pax * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Very_large") && stationSize.Equals("Small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 1250 * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = station.Profile.Pax * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Large") && stationSize.Equals("Small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLarge = 1250 * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = station.Profile.Pax * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Medium") && stationSize.Equals("Small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxMedium = 1250 * 0.17 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Small") && stationSize.Equals("Small"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmall = 1250 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= .9;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
                 else
                 {
                     double paxSmall = station.Profile.Pax * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.9;
                     if (isSameContinent)
                         estimatedPassengerLevel *= .7;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
             }

             if (dStationSize.Equals("Very_small") && stationSize.Equals("Small"))
             {
                 if (dist > 1200)
                 {estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxVery_small = 1250 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.1;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.4;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
                 else
                 {
                     double paxVery_small = station.Profile.Pax * 0.04 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.1;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.4;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.05;
                 }
             }

             if (dStationSize.Equals("Smallest") && stationSize.Equals("Small"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmallest = 0 * 0.02 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.25;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
                 else
                 {
                     double paxSmallest = station.Profile.Pax * 0 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
             }
             #endregion
             #region very small stations
             if (dStationSize.Equals("Largest") && stationSize.Equals("Very_small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLargest = 350 * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLargest = station.Profile.Pax * 0.21 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel; if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Very_large") && stationSize.Equals("Very_small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 350 * 0.27 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxVeryLarge = station.Profile.Pax * 0.24 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Large") && stationSize.Equals("Very_small"))
             {
                 if (station.Profile.Pax == 0)
                 {
                     double paxLarge = 350 * 0.27 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxLarge = station.Profile.Pax * 0.24 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 1.67;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.39;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Medium") && stationSize.Equals("Very_small"))
             {
                 if ( dist > 1600)
                 { estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxMedium = 350 * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
                 else
                 {
                     double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.15;
                 }
             }

             if (dStationSize.Equals("Small") && stationSize.Equals("Very_small"))
             {
                 if (dist > 1200 )
                 { estimatedPassengerLevel = 0; }

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmall = 350 * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.25;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.35;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
                 else
                 {
                     double paxSmall = station.Profile.Pax * 0.10 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.25;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.35;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                 }
             }

             if (dStationSize.Equals("Very_small") && stationSize.Equals("Very_small"))
             {
                 if (dist > 800)
                 { estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxVery_small = 350 * 0 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
                 else
                 {
                     double paxVery_small = station.Profile.Pax * 0 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
             }

             if (dStationSize.Equals("Smallest") && stationSize.Equals("Very_small"))
             {
                 if ( dist > 800)
                 {estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmallest = 350 * 0 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
                 else
                 {
                     double paxSmallest = 350 * 0 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                 }
             }
             #endregion
             #region smallest stations
             if (dStationSize.Equals("Largest") && stationSize.Equals("Smallest"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0; }
                 else if (station.Profile.Pax == 0)
                 {
                     double paxLargest = 50 * 0.25 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = paxLargest * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxLargest = station.Profile.Pax * 0.25 / Stations.LargestStations;
                     paxLargest *= MathHelpers.GetRandomDoubleNumber(0.9, 1.11);
                     estimatedPassengerLevel = (paxLargest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 if (dist < 1600)
                 { estimatedPassengerLevel *= 2; }
                 else if (dist < 2400)
                 { estimatedPassengerLevel *= 0.5; }
                 else
                 { estimatedPassengerLevel = 0; }
             }

             if (dStationSize.Equals("Very_large") && stationSize.Equals("Smallest"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0; }
                 else if (station.Profile.Pax == 0)
                 {
                     double paxVeryLarge = 50 * 0.32 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = paxVeryLarge * 1000 / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxVeryLarge = station.Profile.Pax * 0.32 / Stations.VeryLargeStations;
                     paxVeryLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.12);
                     estimatedPassengerLevel = (paxVeryLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 if (dist < 1600)
                 { estimatedPassengerLevel *= 2; }
                 else if (dist < 2400)
                 { estimatedPassengerLevel *= 0.5; }
                 else
                 { estimatedPassengerLevel = 0; }
             }

             if (dStationSize.Equals("Large") && stationSize.Equals("Smallest"))
             {
                 if (dist > 1600)
                 { estimatedPassengerLevel = 0; }
                 else if (station.Profile.Pax == 0)
                 {
                     double paxLarge = 50 * 0.32 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxLarge = station.Profile.Pax * 0.32 / Stations.LargeStations;
                     paxLarge *= MathHelpers.GetRandomDoubleNumber(0.9, 1.14);
                     estimatedPassengerLevel = (paxLarge * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 if (dist < 1600)
                 { estimatedPassengerLevel *= 2; }
                 else if (dist < 2400)
                 { estimatedPassengerLevel *= 0.5; }
                 else
                 { estimatedPassengerLevel = 0; }
             }

             if (dStationSize.Equals("Medium") && stationSize.Equals("Smallest"))
             {
                 if (dist > 1200)
                 {estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxMedium = 50 * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 2;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxMedium = station.Profile.Pax * 0.15 / Stations.MediumStations;
                     paxMedium *= MathHelpers.GetRandomDoubleNumber(0.9, 1.16);
                     estimatedPassengerLevel = (paxMedium * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 2;
                     if (isSameCountry && dist < 500)
                         estimatedPassengerLevel *= 5;
                     else if (isSameCountry && dist < 1000)
                         estimatedPassengerLevel *= 3;
                     else estimatedPassengerLevel *= 1.25;
                     if (isSameContinent && dist < 2000)
                         estimatedPassengerLevel *= 2;
                     else estimatedPassengerLevel *= 1.25;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0.55;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
             }

             if (dStationSize.Equals("Small") && stationSize.Equals("Smallest"))
             {
                 if (dist > 800)
                 {estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmall = 50 * 0 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 1.2;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.0;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxSmall = station.Profile.Pax * 0 / Stations.SmallStations;
                     paxSmall *= MathHelpers.GetRandomDoubleNumber(0.95, 1.10);
                     estimatedPassengerLevel = (paxSmall * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     estimatedPassengerLevel *= 1.2;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.0;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 1.00;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
             }

             if (dStationSize.Equals("Very_small") && stationSize.Equals("Smallest"))
             {
                 if (dist > 500)
                 {estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxVery_small = 50 * 0 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxVery_small = station.Profile.Pax * 0 / Stations.VerySmallStations;
                     paxVery_small *= MathHelpers.GetRandomDoubleNumber(0.97, 1.06);
                     estimatedPassengerLevel = (paxVery_small * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.35;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.75;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
             }

             if (dStationSize.Equals("Smallest") && stationSize.Equals("Smallest"))
             {
                 if (dist > 200)
                 {estimatedPassengerLevel = 0;}

                 else if (station.Profile.Pax == 0)
                 {
                     double paxSmallest = 50 * 0 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel *= 0;
                     if (estimatedPassengerLevel < 10)
                     { estimatedPassengerLevel *= 0; }
                     else { estimatedPassengerLevel *= 1; }
                 }
                 else
                 {
                     double paxSmallest = 50 * 0 / Stations.SmallestStations;
                     paxSmallest *= MathHelpers.GetRandomDoubleNumber(0.98, 1.04);
                     estimatedPassengerLevel = (paxSmallest * 1000) / 365;
                     estimatedPassengerLevel *= GameObject.GetInstance().Difficulty.PassengersLevel;
                     if (isSameCountry)
                         estimatedPassengerLevel *= 2.5;
                     if (isSameContinent)
                         estimatedPassengerLevel *= 0.5;
                     if (!isSameContinent && !isSameCountry)
                         estimatedPassengerLevel = 0;
                 }

                      

             }*/

            #endregion

            double value = estimatedPassengerLevel*GetDemandYearFactor(GameObject.GetInstance().GameTime.Year)
                           *BusDistanceFactor(dist);

            var rate = (ushort) value;

            if (rate > 0)
            {
                station.AddDestinationPassengersRate(new DestinationDemand(dStation.Profile.IATACode, rate));
                //DatabaseObject.GetInstance().addToTransaction(station, dStation, classType, rate);
            }
        }

        /// <summary>
        ///     How much of a city pair's travel goes by coach, by the length of the trip. The coach owns the middle
        ///     distances: too short and people drive or take the city bus, too long and they fly or stay at home. The
        ///     curve peaks between about 200 and 450 km (a half day's ride, no airport worth the trouble) and is all
        ///     but gone past 2,000.
        /// </summary>
        public static double BusDistanceFactor(double km)
        {
            if (km <= 0) return 0;

            // the short end: a 30 km hop is a city bus's job, not an intercity coach's
            double near = km >= 150 ? 1 : Math.Max(0.05, 0.15 + 0.85*Math.Max(0, km - 40)/110.0);

            // the sweet spot: the corridor runs people drive today and would rather not
            double bonus = 1;
            if (km > 120 && km < 800)
            {
                double ramp = km < 200 ? (km - 120)/80.0 : km <= 450 ? 1 : (800 - km)/350.0;
                bonus = 1 + 0.8*Math.Max(0, Math.Min(1, ramp));
            }

            // the long end: every 600 km past 450 cuts what is left by about a third
            double far = km <= 450 ? 1 : Math.Exp(-(km - 450)/600.0);

            return near*bonus*far;
        }


        // chs, 2011-13-10 added for loading of passenger happiness



        public static int GetTripPassengers(
            Station stationCurrent,
            Station stationDestination,
            FleetCoach coach,
            CoachClass.ClassType type)
        {
            double distance = MathHelpers.GetDistance(stationCurrent, stationDestination);

            Route currentRoute =
                coach.Routes.Find(
                    r =>
                    r.Stopovers.SelectMany(s => s.Legs)
                     .ToList()
                     .Exists(
                         l =>
                         (l.Destination1 == stationCurrent || l.Destination1 == stationDestination)
                         && (l.Destination2 == stationDestination || l.Destination2 == stationCurrent))
                    || (r.Destination1 == stationCurrent || r.Destination1 == stationDestination)
                    && (r.Destination2 == stationDestination || r.Destination2 == stationCurrent));

            if (currentRoute == null)
            {
                return 0;
            }

            //double basicPrice = GetPassengerPrice(currentRoute.Destination1, currentRoute.Destination2, type);
            //double routePrice = ((PassengerRoute)currentRoute).getFarePrice(type);

            //double priceDiff = basicPrice / routePrice;

            long pfAll = Profiler.Start();
            long pf = Profiler.Start();
            double routeScoreFactor = RouteScore(currentRoute)/5;
            Profiler.Stop("pax: route score", pf);

            double demand = stationCurrent.GetDestinationPassengersRate(stationDestination, type);

            // and the ones riding over this leg on a longer ticket: every pair of places the service calls at
            demand += RouteStops.ThroughDemand(currentRoute, stationCurrent, stationDestination, type);

            // passengers changing to or from the carrier's (and its partners') other routes
            pf = Profiler.Start();
            double connecting = ConnectionService.TripDemand(coach.Coach.Carrier, currentRoute, stationCurrent, stationDestination, type);
            Profiler.Stop("pax: connecting demand", pf);
            pf = Profiler.Start();
            double nearby = GetNearbyPassengerDemand(stationCurrent, stationDestination, coach, type);
            Profiler.Stop("pax: nearby demand", pf);
            double connectingShare = demand + connecting + nearby > 0 ? connecting/(demand + connecting + nearby) : 0;

            double traceBase = demand + connecting + nearby;
            double passengerDemand = (demand + connecting + nearby)
                                     *GetSeasonFactor(stationDestination)*GetHolidayFactor(stationDestination)
                                     *GetHolidayFactor(stationCurrent)*DayOfWeekFactor(GameObject.GetInstance().GameTime);

            passengerDemand *= GameObject.GetInstance().Difficulty.PassengersLevel;
            passengerDemand *= routeScoreFactor;

            // fewer passengers above the market fare, more below it
            if (currentRoute is PassengerRoute pricedRoute)
            {
                passengerDemand *= PricingService.DemandFactor(pricedRoute, type);
            }

            if (coach.Coach.Carrier.MarketFocus == CarrierFocus.Global && distance > 3000
                && stationCurrent.Profile.Country != stationDestination.Profile.Country)
            {
                passengerDemand = passengerDemand*1.15;
            }

            if (coach.Coach.Carrier.MarketFocus == CarrierFocus.Regional && distance < 1500)
            {
                passengerDemand = passengerDemand*1.15;
            }

            if (coach.Coach.Carrier.MarketFocus == CarrierFocus.Domestic && distance < 1500
                && stationDestination.Profile.Country == stationCurrent.Profile.Country)
            {
                passengerDemand = passengerDemand*1.15;
            }

            if (coach.Coach.Carrier.MarketFocus == CarrierFocus.Local && distance < 1000)
            {
                passengerDemand = passengerDemand*1.15;
            }

            pf = Profiler.Start();
            Hub hub = stationDestination.GetHubs().Find(h => h.Carrier == coach.Coach.Carrier);

            if (hub != null)
            {
                switch (hub.Type.Type)
                {
                    case HubType.TypeOfHub.FocusCity:
                        if (stationDestination.Profile.Country == stationCurrent.Profile.Country)
                        {
                            passengerDemand = passengerDemand*1.15;
                        }
                        break;

                    case HubType.TypeOfHub.RegionalHub:
                        if (stationDestination.Profile.Country.Region == stationCurrent.Profile.Country.Region)
                        {
                            passengerDemand = passengerDemand*1.20;
                        }
                        break;

                    case HubType.TypeOfHub.Hub:
                        passengerDemand = passengerDemand*1.20;
                        break;

                    case HubType.TypeOfHub.FortressHub:
                        passengerDemand = passengerDemand*1.30;
                        break;
                }
            }

            // every carrier's flying passenger routes on this pair, nonstop or as a stopover leg
            var routes = new List<Route>();
            foreach (Route r in RouteIndex.Direct(stationCurrent, stationDestination))
            {
                if ((r.Type == Route.RouteType.Passenger) && r.HasCoach) routes.Add(r);
            }
            foreach (Route r in RouteIndex.WithLeg(stationCurrent, stationDestination))
            {
                if ((r.Type == Route.RouteType.Passenger) && r.HasCoach) routes.Add(r);
            }

            double tripsPerDay = Convert.ToDouble(routes.Sum(r => r.TimeTable.Entries.Count))/7;

            Profiler.Stop("pax: routes on the pair", pf);
            double traceDaily = passengerDemand;
            passengerDemand = passengerDemand/tripsPerDay;

            double totalCapacity;
            if (routes.Count > 0 && routes.Count(r => r.HasCoach) > 0)
            {
                totalCapacity =
                    routes.Where(r => r.HasCoach)
                          .Sum(r => r.GetCoaches().Max(a => a.Coach.GetTotalSeatCapacity()));
                //SelectMany(r => r.Stopovers.Where(s=>s.Legs.Count >0))).Sum(s=>s.;//a => a.Routes.SelectMany(r=>r.Stopovers.SelectMany(s=>s.Legs.Where(l=>r.HasCoach && (l.Destination1 == stationCurrent || l.Destination1 == stationDestination) && (l.Destination2 == stationDestination || l.Destination2 == stationCurrent))).Sum(r=>r.getCoaches().Max(a=>a.Coach.getTotalSeatCapacity()));
            }
            else
            {
                totalCapacity = 0;
                // routes.Where(r => r.HasCoach).Sum(r => r.getCoaches().Max(a => a.Coach.getTotalSeatCapacity()));
            }

            double capacityPercent = passengerDemand > totalCapacity ? 1 : passengerDemand/totalCapacity;

            var rations = new Dictionary<Route, double>();

            foreach (Route route in routes)
            {
                double level = ((PassengerRoute) route).GetServiceLevel(type)
                               /((PassengerRoute) route).GetFarePrice(type);

                rations.Add(route, level);
            }

            double totalRatio = rations.Values.Sum();

            double routeRatioPercent = 1;

            if (PricingService.Realistic)
            {
                // the cheaper, better carrier gets more of the passengers on a shared route
                routeRatioPercent = PricingService.CompetitionFactor(currentRoute, routes, type);
            }
            else if (rations.ContainsKey(currentRoute))
            {
                routeRatioPercent = Math.Max(1, rations[currentRoute]/Math.Max(1, totalRatio));
            }

            pf = Profiler.Start();
            double reputation = ReputationScale(stationCurrent, coach.Coach.Carrier);
            Profiler.Stop("pax: reputation of all carriers", pf);

            if (reputation < 76)
            {
                reputation = 75;
            }

            double reputationPercent = reputation/100;

            double routePriceDiff = 1; // priceDiff < 0.75 ? priceDiff : 1;

            routePriceDiff *= GameObject.GetInstance().Difficulty.PriceLevel;

            double randomPax = Convert.ToDouble(Rnd.Next(97, 103))/100;

            var pax =
                (int)
                Math.Min(
                    coach.Coach.GetCoachClass(type).SeatingCapacity,
                    (coach.Coach.GetCoachClass(type).SeatingCapacity*routeRatioPercent
                     *reputationPercent*capacityPercent*routePriceDiff*randomPax));

            if (pax < 0)
            {
                pax = 0;
            }

            ConnectionService.Record(currentRoute, pax, pax*connectingShare, coach.Coach.GetCoachClass(type).SeatingCapacity);
            Profiler.Stop("pax: whole calculation", pfAll);

            TripTrace?.Invoke(new TripPaxTrace
            {
                Carrier = coach.Coach.Carrier, Route = currentRoute, From = stationCurrent, Type = type, BaseDemand = traceBase, Direct = demand, Connecting = connecting, Nearby = nearby, DailyDemand = traceDaily,
                TripsPerDay = tripsPerDay, PerTrip = passengerDemand, TotalCapacity = totalCapacity, CapacityPercent = capacityPercent,
                Competition = routeRatioPercent, Reputation = reputationPercent, Score = routeScoreFactor,
                Seats = coach.Coach.GetCoachClass(type).SeatingCapacity, Passengers = pax
            });

            return pax;
        }

        /// <summary>What went into one trip's passengers in one class (for the economy probe; off in normal play).</summary>
        public class TripPaxTrace
        {
            public Carrier Carrier;
            public Route Route;
            public Station From;
            public CoachClass.ClassType Type;
            public double BaseDemand, Direct, Connecting, Nearby, DailyDemand, TripsPerDay, PerTrip, TotalCapacity, CapacityPercent, Competition, Reputation, Score;
            public int Seats, Passengers;
        }

        public static Action<TripPaxTrace> TripTrace;

        //returns the number of passengers for a trip
        public static int GetTripPassengers(FleetCoach coach, CoachClass.ClassType type)
        {
            Station stationCurrent = coach.CurrentTrip.GetDepartureStation();
            Station stationDestination = coach.CurrentTrip.Entry.Destination.Station;

            return GetTripPassengers(stationCurrent, stationDestination, coach, type);
        }

        public static double GetHappinessValue(Carrier carrier)
        {
            if (HappinessPercent.ContainsKey(carrier))
            {
                return HappinessPercent[carrier];
            }
            return 0;
        }

        //returns the holiday factor for an station

        //returns the suggested passenger price for a route
        public static double GetPassengerPrice(Station dest1, Station dest2)
        {
            // the market fare (see PricingService; the original curve when the original costs are chosen)
            return PricingService.MarketFare(MathHelpers.GetDistance(dest1, dest2));
        }

        public static double GetPassengerPrice(Station dest1, Station dest2, CoachClass.ClassType type)
        {
            return GetPassengerPrice(dest1, dest2)*GeneralHelpers.ClassToPriceFactor(type);
        }

        public static double GetPassengersHappiness(Carrier carrier)
        {
            double passengers = carrier.Statistics.GetYears().Sum(year => Convert.ToDouble(carrier.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("Passengers"))));
            double value = GetHappinessValue(carrier);

            if (passengers.Equals(0))
            {
                return 0;
            }
            return value/passengers*100.0;
        }


        public static int GetStopoverTripPassengers(
            FleetCoach coach,
            CoachClass.ClassType type,
            Station dept,
            Station dest,
            List<Route> routes,
            bool isInbound)
        {
            Route currentRoute =
                routes.Find(
                    r =>
                    (r.Destination1 == dept && r.Destination2 == dest)
                    || (r.Destination2 == dept && r.Destination1 == dest));
            int index = routes.IndexOf(currentRoute);

            int passengers = 0;
            for (int i = 0; i <= index; i++)
            {
                if (isInbound)
                {
                    passengers += GetTripPassengers(routes[i].Destination2, dest, coach, type);
                }
                else
                {
                    passengers += GetTripPassengers(routes[i].Destination1, dest, coach, type);
                }
            }

            if (passengers < 0)
            {
                passengers = 0;
            }

            return Math.Min(coach.Coach.GetCoachClass(type).SeatingCapacity, passengers);
        }

        //returns the number of passengers for a trip on a stopover route
        public static int GetStopoverTripPassengers(FleetCoach coach, CoachClass.ClassType type)
        {
            RouteTimeTableEntry mainEntry = coach.CurrentTrip.Entry.MainEntry;
            RouteTimeTableEntry entry = coach.CurrentTrip.Entry;

            List<Route> legs = mainEntry.TimeTable.Route.Stopovers.SelectMany(s => s.Legs).ToList();

            bool isInbound = mainEntry.DepartureStation == mainEntry.TimeTable.Route.Destination2;

            int passengers;
            //inboound
            if (isInbound)
            {
                legs.Reverse();
                passengers = GetTripPassengers(
                    mainEntry.TimeTable.Route.Destination2,
                    mainEntry.TimeTable.Route.Destination1,
                    coach,
                    type);
            }
            else
            {
                passengers = GetTripPassengers(
                    mainEntry.TimeTable.Route.Destination1,
                    mainEntry.TimeTable.Route.Destination2,
                    coach,
                    type);
            }

            int index = legs.IndexOf(entry.TimeTable.Route);

            for (int i = index; i < legs.Count; i++)
            {
                if (isInbound)
                {
                    passengers += GetTripPassengers(
                        entry.TimeTable.Route.Destination1,
                        legs[i].Destination1,
                        coach,
                        type);
                }
                else
                {
                    passengers += GetTripPassengers(
                        entry.TimeTable.Route.Destination1,
                        legs[i].Destination2,
                        coach,
                        type);
                }
            }

            if (passengers < 0)
            {
                passengers = 0;
            }

            return Math.Min(coach.Coach.GetCoachClass(type).SeatingCapacity, passengers);
        }

        public static void SetCargoSize()
        {
            List<Station> stations = Stations.GetAllActiveStations();

            foreach (Station station in stations)
            {
                var volume = (int) station.Profile.CargoVolume;
                if (volume < 200)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.Smallest;
                    break;
                }

                if (volume < 400)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.VerySmall;
                    break;
                }
                if (volume < 750)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.Small;
                    break;
                }
                if (volume < 1500)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.Medium;
                    break;
                }

                if (volume < 2500)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.Large;
                    break;
                }

                if (volume > 2500)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.VeryLarge;
                    break;
                }
                if (volume > 4500)
                {
                    station.Profile.Cargo = GeneralHelpers.Size.Largest;
                    break;
                }
            }
        }

        public static void SetPassengerHappiness(Carrier carrier, double value)
        {
            if (HappinessPercent.ContainsKey(carrier))
            {
                HappinessPercent[carrier] = value;
            }
            else
            {
                HappinessPercent.Add(carrier, value);
            }
        }

        #endregion

        #region Methods

        private static void CreateDestinationPassengers(Station station, List<Station> subStations)
        {
            List<Station> largestStations =
                subStations.FindAll(
                    a =>
                    a.Profile.Size == GeneralHelpers.Size.Largest
                    || a.Profile.Size == GeneralHelpers.Size.VeryLarge);

            int maxValue = Math.Max(2, (int) Math.Ceiling(station.Profile.Pax/2));

            // the same every time the demand is built
            using (MathHelpers.SeedFor(station.Profile.IATACode + ">fallback"))
            {
                if (largestStations.Count > 0)
                {
                    foreach (Station lStation in largestStations.OrderBy(a => a.Profile.IATACode))
                    {
                        station.AddDestinationPassengersRate(
                            new DestinationDemand(lStation.Profile.IATACode, (ushort) MathHelpers.GetRandomInt(1, maxValue)));
                    }
                }
                else
                {
                    subStations = subStations.OrderByDescending(a => a.Profile.Size).ThenBy(a => a.Profile.IATACode).ToList();
                    station.AddDestinationPassengersRate(
                        new DestinationDemand(subStations[0].Profile.IATACode, (ushort) MathHelpers.GetRandomInt(1, maxValue)));
                }
            }
        }

        //creates the station destination passengers a destination

        //returns the cargo mass per day for an station
        private static double GetStationCargoMass(Station station)
        {
            //density cargo 3000 kg/cu m.
            //1 cu m. vejer 3000 kg. eller 3.0 metric ton

            /*--------------------READ THE TWO LINES BELOW --- VERY USEFUL ----------------*/

            //for reference, most commercial coach have capacity of about 5.5(5.3-5.8)lb per cu ft
            //to convert values to cu meters, multiply the cargo cu footage * 35.3 (194lb/m3 and 88kg/m3 at standard density)

            //total cargo volume from/to station in tonnes
            double totalCargoVolume = station.Profile.CargoVolume.Equals(0)
                                          ? ((int) station.Profile.Cargo + 1)*10000
                                          : station.Profile.CargoVolume*1000; //in metric ton
            const double cargoDensity = 3000; //kg/cu m

            //in cargo mass
            double cargoMass = totalCargoVolume/(cargoDensity/1000);

            double cargoMassPerDay = cargoMass/365;

            return cargoMassPerDay;
        }

        //sets the cargo sizes based on the volume

        //returns the demand factor based on the year of playing
        private static double GetDemandYearFactor(int year)
        {
            double yearDiff = Convert.ToDouble(year - GameObject.StartYear)/10;

            return 0.15*(yearDiff + 1);
        }

        /// <summary>
        ///     How busy the day of the week is, averaging one over the seven. People go away on a Friday and come
        ///     back on a Sunday, and midweek the coach is half empty. Without this every trip of a weekly
        ///     timetable carried the same load, so a service sized to be worth running was equally full on a
        ///     Tuesday and a Friday, and the coaches sat at eighty per cent all year where the trade runs at about
        ///     fifty-five. The real figure is low precisely because the timetable has to be built for the busy
        ///     days and then run on the quiet ones.
        /// </summary>
        public static double DayOfWeekFactor(DateTime when)
        {
            switch (when.DayOfWeek)
            {
                case DayOfWeek.Monday: return 0.92;
                case DayOfWeek.Tuesday: return 0.73;
                case DayOfWeek.Wednesday: return 0.78;
                case DayOfWeek.Thursday: return 0.97;
                case DayOfWeek.Friday: return 1.36;
                case DayOfWeek.Saturday: return 0.97;
                default: return 1.27;
            }
        }

        private static double GetHolidayFactor(Station station)
        {
            if (HolidayYear.IsHoliday(station.Profile.Country, GameObject.GetInstance().GameTime))
            {
                HolidayYearEvent holiday = HolidayYear.GetHoliday(
                    station.Profile.Country,
                    GameObject.GetInstance().GameTime);

                if (holiday.Holiday.Travel == Holiday.TravelType.Both
                    || holiday.Holiday.Travel == Holiday.TravelType.Travel)
                {
                    return 1.5;
                }
            }
            return 1;
        }

        /// <summary>Passengers from stations near the start who would use the trip (for the route forecast).</summary>
        public static double NearbyDemandFor(Station from, Station to, FleetCoach coach, CoachClass.ClassType type)
        {
            return GetNearbyPassengerDemand(from, to, coach, type);
        }

        /// <summary>The season's effect on the demand to an station today (for the route forecast).</summary>
        /// <summary>A holiday's effect on the demand at an station today (for the route forecast).</summary>
        public static double HolidayFactorFor(Station station)
        {
            return GetHolidayFactor(station);
        }

        /// <summary>A route's service score as the trips use it today (out of 10).</summary>
        public static double RouteScoreFor(Route route)
        {
            return RouteScore(route);
        }

        public static double SeasonFactorFor(Station station)
        {
            return GetSeasonFactor(station);
        }

        /// <summary>The share the carrier's reputation at the station gives (0.75 to 1, as for a trip).</summary>
        public static double ReputationShare(Station station, Carrier carrier)
        {
            return Math.Max(75, ReputationScale(station, carrier))/100;
        }

        // every carrier's reputation score at an station (before scaling), worked out once a game day per station
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Station, Tuple<DateTime, Dictionary<Carrier, double>>> ReputationScoreCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Station, Tuple<DateTime, Dictionary<Carrier, double>>>();

        /// <summary>
        ///     The share the carrier's reputation at the station would give with `added` more to its score (a new route there
        ///     adds 7 and 6 for each point of its service), against every carrier's reputation there today (0.75 to 1).
        /// </summary>
        public static double ReputationShareWith(Station station, Carrier carrier, double added)
        {
            DateTime day = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, Dictionary<Carrier, double>> cached;
            Dictionary<Carrier, double> scores;
            if (ReputationScoreCache.TryGetValue(station, out cached) && cached.Item1 == day) scores = cached.Item2;
            else
            {
                scores = new Dictionary<Carrier, double>();
                foreach (Carrier a in Carriers.GetAllCarriers())
                {
                    if (!scores.ContainsKey(a)) scores.Add(a, station.GetCarrierReputation(a));
                }
                ReputationScoreCache.AddOrUpdate(station, Tuple.Create(day, scores));
            }
            var with = new Dictionary<Carrier, double>(scores);
            double mine;
            if (!with.TryGetValue(carrier, out mine)) mine = station.GetCarrierReputation(carrier);
            with[carrier] = mine + added;
            return Math.Max(75, StatisticsHelpers.GetRatingScale(with)[carrier])/100;
        }

        // a route's service score (coach age, meals, seats, fare and so on), worked out once a game day per route
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Route, Tuple<DateTime, double>> RouteScoreCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Route, Tuple<DateTime, double>>();

        private static double RouteScore(Route route)
        {
            DateTime day = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, double> cached;
            if (RouteScoreCache.TryGetValue(route, out cached) && cached.Item1 == day) return cached.Item2;
            double score = RouteHelpers.GetRouteTotalScore(route);
            RouteScoreCache.AddOrUpdate(route, Tuple.Create(day, score));
            return score;
        }

        // each carrier's reputation at an station on a 1-100 scale against all carriers, worked out once a game day
        // per station (it looks at every carrier's routes and facilities there, which used to happen for every trip)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Station, Tuple<DateTime, IDictionary<Carrier, double>>> ReputationCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Station, Tuple<DateTime, IDictionary<Carrier, double>>>();

        private static double ReputationScale(Station station, Carrier carrier)
        {
            DateTime day = GameObject.GetInstance().GameTime.Date;
            Tuple<DateTime, IDictionary<Carrier, double>> cached;
            double value;
            if (ReputationCache.TryGetValue(station, out cached) && cached.Item1 == day && cached.Item2.TryGetValue(carrier, out value))
            {
                return value;
            }

            IDictionary<Carrier, double> carrierScores = new Dictionary<Carrier, double>();
            foreach (Carrier a in Carriers.GetAllCarriers())
            {
                if (!carrierScores.ContainsKey(a)) carrierScores.Add(a, station.GetCarrierReputation(a));
            }
            if (!carrierScores.ContainsKey(carrier)) carrierScores.Add(carrier, station.GetCarrierReputation(carrier));

            IDictionary<Carrier, double> scale = StatisticsHelpers.GetRatingScale(carrierScores);
            ReputationCache.AddOrUpdate(station, Tuple.Create(day, scale));
            return scale[carrier];
        }

        private static double GetNearbyPassengerDemand(
            Station stationCurrent,
            Station stationDestination,
            FleetCoach coach,
            CoachClass.ClassType type)
        {
            TimeSpan tripTime = MathHelpers.GetTripTime(stationCurrent, stationDestination, coach.Coach.Type);

            double maxDistance = (tripTime.TotalHours*0.5)*100;

            IEnumerable<Station> nearbyStations =
                StationHelpers.GetStationsNearStation(stationCurrent, maxDistance)
                              .DefaultIfEmpty()
                              .Where(a => !StationHelpers.HasRoute(a, stationDestination));

            double demand = 0;

            foreach (Station station in nearbyStations)
            {
                if (station != null)
                {
                    double distance = MathHelpers.GetDistance(stationCurrent, station);

                    double stationDemand = station.GetDestinationPassengersRate(stationDestination, type);

                    if (distance < 150)
                    {
                        demand += stationDemand*0.75;
                    }

                    if (distance >= 150 && distance < 225)
                    {
                        demand += stationDemand*0.5;
                    }

                    if (distance >= 225 && distance < 300)
                    {
                        demand += stationDemand*0.25;
                    }

                    if (distance >= 300 && distance < 400)
                    {
                        demand += stationDemand*0.10;
                    }
                }
            }

            return demand;
        }

        private static Station GetRandomDestination(Station currentStation)
        {
            var stationsList = new Dictionary<Station, int>();
            Stations.GetStations(
                a =>
                currentStation != null && a != currentStation
                && !TripRestrictions.HasRestriction(
                    currentStation.Profile.Country,
                    a.Profile.Country,
                    GameObject.GetInstance().GameTime,
                    TripRestriction.RestrictionType.Trips))
                    .ForEach(
                        a =>
                        stationsList.Add(
                            a,
                            (int) a.Profile.Size*(a.Profile.Country == currentStation.Profile.Country ? 7 : 3)));

            if (stationsList.Count > 0)
            {
                return AIHelpers.GetRandomItem(stationsList);
            }
            return null;
        }

        // (the factors were written as 150/100 and 50/100, whole-number divisions that came out as 1 and 0: a summer
        // station got no passengers at all from September to February, a winter station none from March to August)
        private static double GetSeasonFactor(Station station)
        {
            bool isSummer = GameObject.GetInstance().GameTime.Month >= 3
                               && GameObject.GetInstance().GameTime.Month < 9;

            if (station.Profile.Season == Weather.Season.AllYear)
            {
                return 1;
            }
            if (station.Profile.Season == Weather.Season.Summer)
            {
                if (isSummer)
                {
                    return 1.5;
                }
                return 0.5;
            }
            if (station.Profile.Season == Weather.Season.Winter)
            {
                if (isSummer)
                {
                    return 0.5;
                }
                return 1.5;
            }

            return 1;
        }

        #endregion

        //returns the passengers happiness for an carrier

        //adds happiness to an carrier

        //creates the random station destination for a list of destinations

        //changes the demand for all stations with a factor
    }
}