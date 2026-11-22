using System;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     The game's statistics of the trips: the departures (counted at take-off; the landing's figures are counted
    ///     in FleetCoachHelpers.SetTripStats), the on-time share, and how long they are kept.
    /// </summary>
    public static class TripStatistics
    {
        /// <summary>A trip that lands this late or less is on time (as the monthly report counts it).</summary>
        public const int OnTimeMinutes = 15;

        /// <summary>Years of station statistics kept: this one and the two before (an station has a line per carrier and figure).</summary>
        public const int StationYears = 3;

        /// <summary>A trip takes off: a departure for the carrier, the coach, the station it leaves and its route.</summary>
        public static void TakeOff(FleetCoach coach)
        {
            Trip trip = coach?.CurrentTrip;
            Carrier carrier = coach?.Coach?.Carrier;
            if (trip?.Entry == null || carrier == null) return;
            int year = GameObject.GetInstance().GameTime.Year;
            StatisticsType departures = StatisticsTypes.GetStatisticsType("Departures");
            carrier.Statistics.AddStatisticsValue(year, departures, 1);
            coach.Statistics.AddStatisticsValue(year, departures, 1);
            trip.Entry.DepartureStation?.Statistics.AddStatisticsValue(year, carrier, departures, 1);
            Route route = trip.Entry.TimeTable?.Route;
            if (route == null) return;
            if (trip.IsPassengerTrip() && route is PassengerRoute pr)
            {
                foreach (TripCoachClass fac in trip.Classes.ToList())
                {
                    RouteCoachClass rac = fac?.CoachClass == null ? null : pr.GetRouteCoachClass(fac.CoachClass.Type);
                    if (rac != null) route.Statistics.AddStatisticsValue(rac, departures, 1);
                }
            }
            if (trip.IsCargoTrip()) route.Statistics.AddStatisticsValue(departures, 1);
        }

        /// <summary>
        ///     The carrier's share of trips on time over all the years it has flown (adding the years' percentages, as the
        ///     ratings did, gave 180% after two years).
        /// </summary>
        public static double OnTimePercent(Carrier carrier)
        {
            double arrivals = carrier.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("Arrivals"));
            if (arrivals <= 0) return 0;
            return 100*carrier.Statistics.GetStatisticsValue(StatisticsTypes.GetStatisticsType("On-Time"))/arrivals;
        }

        /// <summary>Drops the station statistics older than the years kept.</summary>
        public static void PruneStationStatistics()
        {
            int oldest = GameObject.GetInstance().GameTime.Year - (StationYears - 1);
            foreach (Station station in Stations.GetAllStations())
            {
                var stats = station.Statistics?.Stats;
                if (stats == null) continue;
                lock (stats) stats.RemoveAll(s => s == null || s.Year < oldest);
            }
        }
    }
}
