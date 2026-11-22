using System.Collections.Generic;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Helpers
{
    /// <summary>One landed trip's money, for checking the game's economy.</summary>
    public class TripRecord
    {
        public Carrier Carrier;
        public string Type;
        public double Km, Hours;
        public int Seats, Passengers;
        public double Tickets, Fees, Meals, Fuel, Landing, Tax, CabinWages;
    }

    /// <summary>Keeps the last landed trips when switched on (used by the cost check, off in normal play).</summary>
    public static class TripLog
    {
        public static bool Enabled;
        private static readonly List<TripRecord> List = new List<TripRecord>();

        public static void Add(FleetCoach coach, double km, double hours, double tickets, double fees, double meals, double fuel, double landing, double tax, double wages)
        {
            if (!Enabled) return;
            var r = new TripRecord
            {
                Carrier = coach.Coach.Carrier, Type = coach.Coach.Type.Name, Km = km, Hours = hours,
                Seats = coach.CurrentTrip.IsPassengerTrip() ? coach.Coach.GetTotalSeatCapacity() : 0, Passengers = coach.CurrentTrip.IsPassengerTrip() ? coach.CurrentTrip.GetTotalPassengers() : 0,
                Tickets = tickets, Fees = fees, Meals = meals, Fuel = fuel, Landing = landing, Tax = tax, CabinWages = wages
            };
            lock (List)
            {
                List.Add(r);
                if (List.Count > 200000) List.RemoveRange(0, 50000);
            }
        }

        public static List<TripRecord> Records()
        {
            lock (List) return new List<TripRecord>(List);
        }

        public static void Clear()
        {
            lock (List) List.Clear();
        }
    }
}
