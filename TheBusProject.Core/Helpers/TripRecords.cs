using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>One trip an coach flew (or that was cancelled): what it carried and what it earned.</summary>
    public class FlownTrip
    {
        public RouteTimeTableEntry Entry { get; set; }
        public string TripCode { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public DateTime Takeoff { get; set; }
        public DateTime Landing { get; set; }

        /// <summary>The minutes it left late.</summary>
        public int DelayMinutes { get; set; }

        public bool Cancelled { get; set; }
        public string CancelReason { get; set; }
        public bool IsCargo { get; set; }
        public int Passengers { get; set; }
        public int Seats { get; set; }
        public double Cargo { get; set; }
        public double Income { get; set; }

        /// <summary>Income less the trip's own costs (fuel, fees, crew, handling, maintenance, sales), before ownership.</summary>
        public double Profit { get; set; }
    }

    /// <summary>One coach's recorded trips as a saved game keeps them.</summary>
    public class SavedTrips
    {
        public FleetCoach Coach;
        public List<FlownTrip> Trips;
    }

    /// <summary>
    ///     Each coach's last trips, for the world map's trip board. Saved with the game (an older save has none:
    ///     the board shows the trips from loading on).
    /// </summary>
    public static class TripRecords
    {
        public const int Keep = 60;

        private static readonly ConditionalWeakTable<FleetCoach, List<FlownTrip>> Trips = new ConditionalWeakTable<FleetCoach, List<FlownTrip>>();

        private static void Add(FleetCoach coach, FlownTrip f)
        {
            var list = Trips.GetValue(coach, x => new List<FlownTrip>());
            lock (list)
            {
                list.Add(f);
                if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
            }
        }

        /// <summary>A trip landed: its income less its own costs.</summary>
        public static void Landed(FleetCoach coach, DateTime landing, double income, double profit)
        {
            try
            {
                Trip trip = coach?.CurrentTrip;
                if (trip?.Entry == null) return;
                var f = new FlownTrip
                {
                    Entry = trip.Entry, TripCode = trip.Entry.Destination.TripCode,
                    From = trip.GetDepartureStation()?.Profile.IATACode, To = trip.Entry.Destination.Station?.Profile.IATACode,
                    Takeoff = trip.TripTime, Landing = landing, Income = income, Profit = profit, IsCargo = trip.IsCargoTrip() && !trip.IsPassengerTrip()
                };
                f.DelayMinutes = Math.Max(0, (int) Math.Round((trip.TripTime - trip.ScheduledTripTime).TotalMinutes));
                if (trip.ScheduledTripTime == default(DateTime)) f.DelayMinutes = 0;
                if (trip.IsPassengerTrip())
                {
                    f.Passengers = trip.GetTotalPassengers();
                    f.Seats = coach.Coach.GetTotalSeatCapacity();
                }
                else
                {
                    f.Cargo = trip.Cargo;
                }
                Add(coach, f);
                CarrierReportService.Flown(coach, f, trip.Entry.TimeTable?.Route);
            }
            catch (Exception)
            {
                // a record that can't be made leaves the trip as it is
            }
        }

        /// <summary>A trip was cancelled before it left.</summary>
        public static void Cancelled(FleetCoach coach, Trip trip, string reason)
        {
            try
            {
                CarrierReportService.Cancelled(coach);
                if (trip?.Entry == null) return;
                Add(coach, new FlownTrip
                {
                    Entry = trip.Entry, TripCode = trip.Entry.Destination.TripCode,
                    From = trip.Entry.DepartureStation?.Profile.IATACode, To = trip.Entry.Destination.Station?.Profile.IATACode,
                    Takeoff = trip.TripTime, Landing = trip.TripTime, Cancelled = true, CancelReason = reason
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>The coach's recorded trips, oldest first.</summary>
        public static List<FlownTrip> Of(FleetCoach coach)
        {
            List<FlownTrip> list;
            if (coach == null || !Trips.TryGetValue(coach, out list)) return new List<FlownTrip>();
            lock (list) return list.ToList();
        }

        public static void Clear()
        {
            Trips.Clear();
        }

        /// <summary>The trips of every coach in the fleets, for a saved game.</summary>
        public static List<SavedTrips> ToSave(IEnumerable<Models.Carriers.Carrier> carriers)
        {
            var result = new List<SavedTrips>();
            foreach (var carrier in carriers)
            {
                List<FleetCoach> fleet;
                lock (carrier.Fleet) fleet = carrier.Fleet.ToList();
                foreach (FleetCoach f in fleet)
                {
                    List<FlownTrip> list = Of(f);
                    if (list.Count > 0) result.Add(new SavedTrips {Coach = f, Trips = list});
                }
            }
            return result;
        }

        /// <summary>The trips of a loaded game.</summary>
        public static int Restore(List<SavedTrips> saved)
        {
            Clear();
            if (saved == null) return 0;
            int n = 0;
            foreach (SavedTrips s in saved)
            {
                if (s?.Coach == null || s.Trips == null) continue;
                var list = Trips.GetValue(s.Coach, x => new List<FlownTrip>());
                lock (list)
                {
                    list.AddRange(s.Trips.Where(x => x != null).OrderBy(x => x.Takeoff));
                    if (list.Count > Keep) list.RemoveRange(0, list.Count - Keep);
                    n += list.Count;
                }
            }
            return n;
        }
    }
}
