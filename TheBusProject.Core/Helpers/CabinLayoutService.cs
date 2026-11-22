using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Cabin layouts the player saves: the classes, the business and first class seats and the seats, audio and
    ///     video of every class. A layout fits any passenger coach with room for it; economy takes whatever floor
    ///     the other classes leave. Layouts are kept with the game's other configurations, so they are in the save file.
    /// </summary>
    public static class CabinLayoutService
    {
        /// <summary>The player's layouts, by name.</summary>
        public static List<CoachConfiguration> Saved()
        {
            return Configurations.GetConfigurations(Configuration.ConfigurationType.Coach)
                                 .OfType<CoachConfiguration>()
                                 .Where(c => !c.Standard)
                                 .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                                 .ToList();
        }

        /// <summary>Why the name can't be used, or null.</summary>
        public static string GetSaveBlocker(string name, IList<CoachClass> classes)
        {
            string n = (name ?? "").Trim();
            if (n.Length < 2) return "Give the layout a name.";
            if (n.Length > 40) return "The name is too long (40 letters at most).";
            if (classes == null || !classes.Any(c => c.Type == CoachClass.ClassType.EconomyClass))
                return "A layout needs an economy class.";
            if (Configurations.GetConfigurations(Configuration.ConfigurationType.Coach)
                              .Any(c => c.Standard && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)))
                return "One of the game's layouts already has that name.";
            return null;
        }

        /// <summary>True when a layout of the player's has this name (saving replaces it).</summary>
        public static bool Exists(string name)
        {
            string n = (name ?? "").Trim();
            return Saved().Any(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The floor (in economy seats) the premium classes take plus one economy seat.</summary>
        public static int MinimumFloor(IEnumerable<CoachClass> classes)
        {
            int floor = 0;
            foreach (var c in classes)
            {
                double uses = c.GetFacility(CoachFacility.FacilityType.Seat)?.SeatUses ?? 1;
                if (uses <= 0) uses = 1;
                if (c.Type == CoachClass.ClassType.EconomyClass) floor += (int) Math.Ceiling(uses - 1e-9);
                else floor += Math.Max(c.RegularSeatingCapacity, (int) Math.Ceiling(c.SeatingCapacity*uses - 1e-9));
            }
            return Math.Max(1, floor);
        }

        /// <summary>Saves the classes as a layout (replacing a layout of the player's with the same name).</summary>
        public static CoachConfiguration Save(string name, IList<CoachClass> classes, out string message)
        {
            message = GetSaveBlocker(name, classes);
            if (message != null) return null;
            string n = name.Trim();
            foreach (var old in Saved().Where(c => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)).ToList())
                Configurations.RemoveConfiguration(old);

            var conf = new CoachConfiguration(n, MinimumFloor(classes), false);
            foreach (var c in classes.OrderBy(c => c.Type != CoachClass.ClassType.EconomyClass))
            {
                var cc = new CoachClassConfiguration(c.Type, c.SeatingCapacity, c.RegularSeatingCapacity);
                foreach (var f in c.GetFacilities().Where(f => f != null)) cc.AddFacility(f);
                conf.AddClassConfiguration(cc);
            }
            Configurations.AddConfiguration(conf);
            message = "Saved the layout \"" + n + "\" (" + Describe(conf) + ").";
            return conf;
        }

        public static void Delete(CoachConfiguration conf)
        {
            if (conf == null || conf.Standard) return;
            Configurations.RemoveConfiguration(conf);
        }

        /// <summary>For example "8F 24C + economy".</summary>
        public static string Describe(CoachConfiguration conf)
        {
            var parts = new List<string>();
            foreach (var t in new[] { CoachClass.ClassType.FirstClass, CoachClass.ClassType.BusinessClass })
            {
                var c = conf.Classes.FirstOrDefault(k => k.Type == t);
                if (c != null) parts.Add(c.SeatingCapacity + (t == CoachClass.ClassType.FirstClass ? "F" : "C"));
            }
            return parts.Count == 0 ? "economy only" : string.Join(" ", parts) + " + economy";
        }

        /// <summary>Whether the layout can be used on a type: enough floor, enough classes, classes that exist this year.</summary>
        public static bool Fits(CoachConfiguration c, CoachPassengerType type, int year)
        {
            return c.MinimumSeats <= type.MaxSeatingCapacity
                   && c.Classes.Count <= Math.Max(1, type.MaxCoachClasses)
                   && c.Classes.Any(k => k.Type == CoachClass.ClassType.EconomyClass)
                   && c.Classes.All(k => year >= (int) k.Type);
        }
    }
}
