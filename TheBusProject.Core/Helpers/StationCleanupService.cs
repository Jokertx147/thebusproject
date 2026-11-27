using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>An area a game can be set in: a whole continent or one region of it.</summary>
    public class GameArea
    {
        public string Name { get; set; }
        public HashSet<string> RegionUids { get; set; }
        public bool IsContinent { get; set; }
        public int Stations { get; set; }

        public bool Contains(Station station)
        {
            string uid = station?.Profile?.Country?.Region?.Uid;
            return uid != null && RegionUids.Contains(uid);
        }
    }

    /// <summary>
    ///     Takes stations out of a game that is set in one continent or region. An earlier version added every station of
    ///     the data files that a save didn't name when loading it, so a North America game came back as the whole world,
    ///     and saving it again kept the extra stations. A save doesn't say which area the game was started in (and the
    ///     carriers don't tell either: the opponents are the carriers nearest to the player, so a world game looks like
    ///     a regional one), so the player picks the area. Stations outside it that something uses are always kept.
    /// </summary>
    public static class StationCleanupService
    {
        private static Continent ContinentOf(Region region)
        {
            if (region?.Uid == null) return null;
            return Continents.GetContinents().FirstOrDefault(c => c.Regions.Exists(r => r.Uid == region.Uid));
        }

        private static string RegionName(Region region)
        {
            return Regions.GetDisplayName(region) ?? region.Name ?? region.Uid;
        }

        /// <summary>The continents the game's stations are on (when there are several).</summary>
        public static int ContinentCount()
        {
            return Stations.GetAllStations().Select(a => ContinentOf(a.Profile?.Country?.Region)).Where(c => c != null).Distinct().Count();
        }

        /// <summary>
        ///     The areas to choose from: each continent the game has stations on and, where a continent has stations in
        ///     several regions, each of those regions. The continent where the player's carrier is comes first.
        /// </summary>
        public static List<GameArea> Areas()
        {
            var stations = Stations.GetAllStations().ToList();
            var human = GameObject.GetInstance()?.HumanCarrier;
            Continent home = ContinentOf(human?.Stations.FirstOrDefault()?.Profile.Country.Region ?? human?.Profile?.Country?.Region);

            HashSet<Station> used = InUse();
            var list = new List<GameArea>();
            var byContinent = stations.GroupBy(a => ContinentOf(a.Profile?.Country?.Region)).Where(g => g.Key != null)
                                      .OrderByDescending(g => g.Key == home).ThenByDescending(g => g.Count());
            foreach (var continent in byContinent)
            {
                list.Add(new GameArea
                {
                    Name = (continent.Key.Name ?? continent.Key.Uid) + " (the whole continent)",
                    RegionUids = new HashSet<string>(continent.Key.Regions.Select(r => r.Uid)),
                    IsContinent = true,
                    Stations = continent.Count()
                });
                // regions only where the carriers are (the list would be long otherwise)
                if (!continent.Any(used.Contains)) continue;
                var regions = continent.GroupBy(a => a.Profile.Country.Region.Uid).ToList();
                if (regions.Count < 2) continue;
                foreach (var region in regions.OrderByDescending(r => r.Count()))
                {
                    list.Add(new GameArea
                    {
                        Name = RegionName(region.First().Profile.Country.Region) + " (region only)",
                        RegionUids = new HashSet<string> {region.Key},
                        Stations = region.Count()
                    });
                }
            }
            return list;
        }

        /// <summary>The stations something in the game refers to: carriers' stations, routes, coach, gates, hubs, facilities.</summary>
        public static HashSet<Station> InUse()
        {
            var used = new HashSet<Station>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                foreach (Station a in carrier.Stations.ToList()) used.Add(a);
                foreach (Route r in carrier.Routes)
                {
                    used.Add(r.Destination1);
                    used.Add(r.Destination2);
                    foreach (var stop in r.Stopovers.ToList())
                    {
                        used.Add(stop.Stopover);
                        foreach (var leg in stop.Legs.ToList())
                        {
                            used.Add(leg.Destination1);
                            used.Add(leg.Destination2);
                        }
                    }
                }
                foreach (var f in carrier.Fleet.ToList())
                {
                    used.Add(f.Homebase);
                    used.Add(f.CurrentPosition);
                    used.Add(f.FerryTo);
                    used.Add(f.CharterDestination);
                }
            }
            foreach (Station a in Stations.GetAllStations())
            {
                if (a.PlayerEdited || a.HasCarrierFacility() || a.CarrierContracts.Count > 0 || a.GetHubs().Count > 0) used.Add(a);
            }
            used.Remove(null);
            return used;
        }

        /// <summary>The stations outside the area that would be taken out, and how many outside it are kept because they are used.</summary>
        public static List<Station> Outside(GameArea area, out int keptInUse)
        {
            HashSet<Station> used = InUse();
            var outside = Stations.GetAllStations().Where(a => !area.Contains(a)).ToList();
            keptInUse = outside.Count(used.Contains);
            return outside.Where(a => !used.Contains(a)).ToList();
        }

        /// <summary>
        ///     Takes out the unused stations outside the area and builds the passenger demand again for the
        ///     stations that are left. Returns how many were taken out. Run it with the clock stopped.
        /// </summary>
        public static int RemoveOutside(GameArea area)
        {
            int kept;
            var remove = new HashSet<Station>(Outside(area, out kept));
            if (remove.Count == 0) return 0;

            Stations.RemoveStations(remove.Contains);

            // the demand of the stations that are left still names the removed ones: build it again
            foreach (Station station in Stations.GetAllStations())
            {
                station.Statics = new StationStatics(station);
            }
            PassengerHelpers.CreateDestinationDemand();
            DemandOverrides.Apply();

            RouteIndex.Invalidate();
            ConnectionService.Invalidate();
            AIPricingService.Invalidate();
            Infrastructure.StartupLog.Write("Game area: " + remove.Count + " stations outside " + area.Name + " taken out, " + Stations.GetAllStations().Count + " left");
            return remove.Count;
        }
    }
}
