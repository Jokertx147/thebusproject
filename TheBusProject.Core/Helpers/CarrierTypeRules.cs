using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     A passenger carrier flies passenger routes with passenger coach and a cargo carrier cargo routes with
    ///     freighters. To do both, an carrier owns a subsidiary of the other kind. An carrier's kind can only change while
    ///     it has no routes and no coach. Every carrier that isn't a cargo carrier (passenger, helicopter, or the old
    ///     "passengers and cargo" profile setting) is a passenger carrier.
    /// </summary>
    public static class CarrierTypeRules
    {
        public static bool IsCargoCarrier(Carrier a)
        {
            return a != null && a.CarrierRouteFocus == Route.RouteType.Cargo;
        }

        /// <summary>Only "no carrier" (game setup checks) is not bound to a kind.</summary>
        private static bool FliesBoth(Carrier a)
        {
            return a == null;
        }

        public static bool CanFlyRoute(Carrier a, Route.RouteType type)
        {
            if (FliesBoth(a)) return true;
            return IsCargoCarrier(a) ? type == Route.RouteType.Cargo : type != Route.RouteType.Cargo;
        }

        public static bool CanUseType(Carrier a, CoachType type)
        {
            if (FliesBoth(a) || type == null) return true;
            bool freighter = type.TypeCoach == CoachType.TypeOfCoach.Cargo;
            return IsCargoCarrier(a) ? freighter : !freighter;
        }

        public static string Kind(Carrier a)
        {
            if (FliesBoth(a)) return "passenger and cargo carrier";
            return IsCargoCarrier(a) ? "cargo carrier" : "passenger carrier";
        }

        private static string Other(Carrier a)
        {
            return IsCargoCarrier(a) ? "passenger" : "cargo";
        }

        /// <summary>Why the carrier can't fly a route of this kind, or null.</summary>
        public static string RouteBlocker(Carrier a, Route.RouteType type)
        {
            if (CanFlyRoute(a, type)) return null;
            return a.Profile.Name + " is a " + Kind(a) + " and can only run " + (IsCargoCarrier(a) ? "cargo" : "passenger") +
                   " routes. To run " + Other(a) + " routes, start a " + Other(a) + " subsidiary (Carriers > Subsidiaries).";
        }

        /// <summary>Why the carrier can't buy or lease an coach of this type, or null.</summary>
        public static string TypeBlocker(Carrier a, CoachType type)
        {
            if (CanUseType(a, type)) return null;
            return a.Profile.Name + " is a " + Kind(a) + ": " + (IsCargoCarrier(a) ? "only freighters" : "no freighters") +
                   ". A " + Other(a) + " subsidiary can run the " + type.Name + ".";
        }

        /// <summary>Why the carrier's kind can't change now, or null.</summary>
        public static string KindChangeBlocker(Carrier a, Route.RouteType newFocus)
        {
            if (a == null || IsCargoCarrier(a) == (newFocus == Route.RouteType.Cargo)) return null;
            if (a.Routes.Count > 0 || a.Fleet.Count > 0)
                return "A carrier's kind (passenger or cargo) can only change while it has no routes and no coach. Start a subsidiary of the other kind instead (Carriers > Subsidiaries).";
            return null;
        }

        /// <summary>Why two carriers can't be merged into one, or null.</summary>
        public static string MergeBlocker(Carrier into, Carrier other)
        {
            if (into == null || other == null || FliesBoth(into) || FliesBoth(other)) return null;
            if (IsCargoCarrier(into) == IsCargoCarrier(other)) return null;
            return other.Profile.Name + " is a " + Kind(other) + " and " + into.Profile.Name + " a " + Kind(into) +
                   ": they can't become one carrier. Keep it as a subsidiary instead.";
        }
    }
}
