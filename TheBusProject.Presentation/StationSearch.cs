using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General.Countries.Towns;

namespace TheBusProject.Presentation
{
    /// <summary>Finding an station by what a player would type, shared by the screens that search stations.</summary>
    public static class StationSearch
    {
        /// <summary>
        ///     Whether the text matches the station: its IATA code, its name, its town, its state (by name or short
        ///     name, so "Hawaii" and "HI" both find Kona and Hilo) or its country. Empty text matches everything.
        /// </summary>
        public static bool Matches(Station a, string text)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return true;
            if (a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            Town town = a.Profile.Town;
            if (town == null) return false;
            if (town.Name != null && town.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (town.State != null
                && ((town.State.Name != null && town.State.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    || string.Equals(town.State.ShortName, s, StringComparison.OrdinalIgnoreCase))) return true;
            return a.Profile.Country != null && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The one station a typed code or name means: an exact IATA code wins, else the first match by name.</summary>
        public static Station Resolve(string text, IEnumerable<Station> among = null)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return null;
            List<Station> all = (among ?? Stations.GetAllStations()).ToList();
            return all.FirstOrDefault(a => string.Equals(a.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase))
                   ?? all.OrderBy(a => a.Profile.Name).FirstOrDefault(a => Matches(a, s));
        }
    }
}
