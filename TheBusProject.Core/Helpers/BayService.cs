using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Bays the player pays for: a new bay of a chosen length (takes about a year), or a longer existing
    ///     bay (takes weeks, and the bay stays in use at its old length meanwhile).
    /// </summary>
    public static class BayService
    {
        // a loading bay, in metres: a 45 foot coach is 13.7 m and wants 14, a double-decker the same
        public const long MinLength = 10;
        public const long MaxLength = 22;
        public const long MinExtension = 1;
        public const int MaxBays = 12;

        public static readonly Bay.SurfaceType[] Surfaces = { Bay.SurfaceType.Concrete, Bay.SurfaceType.Asphalt };

        private static double SizeFactor(Station station)
        {
            return 1 + 0.1*(int) station.Profile.Size;
        }

        /// <summary>The price of a new bay (in base-year money, before inflation).</summary>
        public static double NewPrice(Station station, long length, Bay.SurfaceType surface)
        {
            // base-year money: a 14 m covered bay runs to about $100,000 in 2007
            double perMeter = surface == Bay.SurfaceType.Concrete ? 800 : 600;
            return GeneralHelpers.GetInflationPrice((4000 + perMeter*length)*SizeFactor(station));
        }

        public static int NewDays(long length)
        {
            return 30 + (int) (length/2);
        }

        public static double ExtensionPrice(Station station, Bay bay, long newLength)
        {
            long added = Math.Max(0, newLength - bay.Length);
            double perMeter = bay.Surface == Bay.SurfaceType.Concrete ? 800 : 600;
            return GeneralHelpers.GetInflationPrice((1500 + perMeter*added)*SizeFactor(station));
        }

        public static int ExtensionDays(long added)
        {
            return 14 + (int) (added*3);
        }

        public static string Status(Bay r)
        {
            var now = GameObject.GetInstance().GameTime;
            if (r.BuiltDate > now) return "under construction, ready " + r.BuiltDate.ToShortDateString();
            if (r.IsExtending) return "being extended to " + r.ExtendTo.ToString("N0") + " m, ready " + r.ExtendDate.ToShortDateString();
            return "in use";
        }

        public static string NextName(Station station)
        {
            int n = station.Bays.Count + 1;
            string name;
            do
            {
                name = "New bay " + n++;
            } while (station.Bays.Any(r => r.Name == name));
            return name;
        }

        public static string GetBuildBlocker(Carrier carrier, Station station, long length, string name)
        {
            if (length < MinLength || length > MaxLength) return "The length must be between " + MinLength.ToString("N0") + " and " + MaxLength.ToString("N0") + " m.";
            if (station.Bays.Count(r => r.Type == Bay.BayType.Regular) >= MaxBays) return station.Profile.IATACode + " already has " + MaxBays + " bays.";
            if (!string.IsNullOrWhiteSpace(name) && station.Bays.Any(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return "There is already a bay called " + name.Trim() + ".";
            return null;
        }

        public static bool Build(Carrier carrier, Station station, long length, Bay.SurfaceType surface, string name, out string message)
        {
            message = GetBuildBlocker(carrier, station, length, name);
            if (message != null) return false;
            double price = NewPrice(station, length, surface);
            var now = GameObject.GetInstance().GameTime;
            var ready = now.AddDays(NewDays(length));
            string n = string.IsNullOrWhiteSpace(name) ? NextName(station) : name.Trim();
            station.Bays.Add(new Bay(n, length, Bay.BayType.Regular, surface, ready, false) { BuiltBy = carrier });
            station.PlayerEdited = true;
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Purchases, -price);
            message = string.Format(
                "{0} ({1:N0} m, {2}) ordered at {3} for {4}. Ready {5}.",
                n, length, surface.ToString().ToLower(), station.Profile.IATACode, Money(price), ready.ToShortDateString());
            return true;
        }

        public static string GetExtendBlocker(Station station, Bay bay, long newLength)
        {
            if (bay == null) return "Pick a bay.";
            if (bay.Type != Bay.BayType.Regular) return "Helipads can't be extended.";
            if (bay.BuiltDate > GameObject.GetInstance().GameTime) return "The bay is still being built.";
            if (bay.IsExtending) return "The bay is already being extended.";
            if (newLength - bay.Length < MinExtension) return "Extend by at least " + MinExtension + " m (to " + (bay.Length + MinExtension).ToString("N0") + " m or more).";
            if (newLength > MaxLength) return "The longest bay allowed is " + MaxLength.ToString("N0") + " m.";
            return null;
        }

        public static bool Extend(Carrier carrier, Station station, Bay bay, long newLength, out string message)
        {
            message = GetExtendBlocker(station, bay, newLength);
            if (message != null) return false;
            double price = ExtensionPrice(station, bay, newLength);
            var now = GameObject.GetInstance().GameTime;
            bay.ExtendTo = newLength;
            bay.ExtendDate = now.AddDays(ExtensionDays(newLength - bay.Length));
            bay.BuiltBy = carrier;
            station.PlayerEdited = true;
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Purchases, -price);
            message = string.Format(
                "{0} at {1} is being extended from {2:N0} m to {3:N0} m for {4}. Ready {5}; it stays in use until then.",
                bay.Name, station.Profile.IATACode, bay.Length, newLength, Money(price), bay.ExtendDate.ToShortDateString());
            return true;
        }

        /// <summary>"DUB has 2,800 m", plus an extension or a new bay on the way.</summary>
        public static string Describe(Station station)
        {
            var now = GameObject.GetInstance().GameTime;
            string text = station.Profile.IATACode + " has " + station.GetMaxBayLength().ToString("N0") + " m";
            var ext = station.Bays.Where(r => r.IsExtending).OrderByDescending(r => r.ExtendTo).FirstOrDefault();
            var built = station.Bays.Where(r => r.BuiltDate > now).OrderByDescending(r => r.Length).FirstOrDefault();
            if (ext != null) text += " (an extension to " + ext.ExtendTo.ToString("N0") + " m is ready " + ext.ExtendDate.ToShortDateString() + ")";
            else if (built != null) text += " (a " + built.Length.ToString("N0") + " m bay opens " + built.BuiltDate.ToShortDateString() + ")";
            return text;
        }

        /// <summary>
        ///     The player's coach (of a kind, delivered and not on a route) whose bay need is above the length: one
        ///     entry per type with what it needs, longest first.
        /// </summary>
        public static List<string> TooShortFor(long length, bool cargo)
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            return carrier.DeliveredFleet.Where(f => (f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Cargo) == cargo && f.Coach.MinBayLength > length)
                          .GroupBy(f => f.Coach.Type.Name).Select(g => Tuple.Create(g.Key, g.Max(f => f.Coach.MinBayLength)))
                          .OrderByDescending(t => t.Item2).Select(t => t.Item1 + " (needs " + t.Item2.ToString("N0") + " m)").ToList();
        }

        /// <summary>Finishes extensions and announces finished bays. Called once a day.</summary>
        public static void ProcessDaily()
        {
            var game = GameObject.GetInstance();
            var today = game.GameTime.Date;
            foreach (var station in Stations.GetAllStations().Where(a => a.PlayerEdited))
            {
                foreach (var r in station.Bays.ToList())
                {
                    if (r.IsExtending && r.ExtendDate.Date <= today)
                    {
                        long old = r.Length;
                        r.Length = r.ExtendTo;
                        r.ExtendTo = 0;
                        if (r.BuiltBy != null && r.BuiltBy.IsHuman)
                            Post(station, "Bay extended", string.Format("{0} at {1} is now {2:N0} m long (was {3:N0} m).", r.Name, station.Profile.IATACode, r.Length, old));
                    }
                    if (r.BuiltBy != null && r.BuiltBy.IsHuman && r.BuiltDate.Date == today)
                        Post(station, "New bay open", string.Format("{0} ({1:N0} m) at {2} is open.", r.Name, r.Length, station.Profile.IATACode));
                }
            }
        }

        private static void Post(Station station, string subject, string body)
        {
            GameObject.GetInstance().NewsBox.AddNews(new News(News.NewsType.StationNews, GameObject.GetInstance().GameTime, subject, body));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }
    }
}
