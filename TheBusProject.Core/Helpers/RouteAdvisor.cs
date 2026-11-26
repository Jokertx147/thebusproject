using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     A suggested new passenger route from an station, with the numbers behind the suggestion.
    /// </summary>
    public class RouteAdvice
    {
        public Station Destination { get; set; }

        public double Distance { get; set; }

        //passengers per day in both directions, all classes
        public int DailyDemand { get; set; }

        public int Competitors { get; set; }

        public double RevenuePerDay { get; set; }

        //revenue after the competitors have taken their share; this is what the list is ranked by
        public double ScoreRevenuePerDay { get; set; }

        public bool FleetCanFly { get; set; }

        public string Advice { get; set; }
    }

    /// <summary>
    ///     Finds the routes from an station that look the most lucrative for the human carrier.
    ///     The numbers are estimates from demand, fares and competition; they do not include running costs.
    /// </summary>
    public static class RouteAdvisor
    {
        public static List<RouteAdvice> GetAdvice(Station origin, int maxResults = 30)
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            DateTime now = GameObject.GetInstance().GameTime;

            List<Carrier> carriers = Carriers.GetAllCarriers();

            // how many other carriers already fly each station pair, and which pairs the player already flies
            var competitors = new Dictionary<string, HashSet<Carrier>>();
            var ownPairs = new HashSet<string>();

            foreach (Carrier other in carriers)
            {
                foreach (Route route in new List<Route>(other.Routes))
                {

                    string key = PairKey(route.Destination1, route.Destination2);

                    if (other == carrier)
                    {
                        ownPairs.Add(key);
                        continue;
                    }

                    HashSet<Carrier> set;
                    if (!competitors.TryGetValue(key, out set))
                    {
                        set = new HashSet<Carrier>();
                        competitors[key] = set;
                    }

                    set.Add(other);
                }
            }

            List<CoachType> types = CoachTypes.GetTypes(
                t => t.Produced.From <= now && t.Produced.To > now);
            double maxRange = types.Count > 0 ? types.Max(t => t.Range) : double.MaxValue;

            // what the player's own fleet can do
            List<FleetCoach> fleet = carrier.Fleet.ToList();

            var classes = Enum.GetValues(typeof (CoachClass.ClassType)).Cast<CoachClass.ClassType>().ToList();

            var result = new List<RouteAdvice>();

            foreach (Station destination in Stations.GetAllStations())
            {
                if (destination == origin || destination.Profile?.Country == null || !GeneralHelpers.IsStationActive(destination))
                {
                    continue;
                }

                string key = PairKey(origin, destination);

                if (ownPairs.Contains(key))
                {
                    continue;
                }

                double distance = MathHelpers.GetDistance(origin, destination);

                if (distance <= Route.MinRouteDistance || distance > maxRange)
                {
                    continue;
                }

                if (!CarrierHelpers.HasCarrierLicens(carrier, origin, destination)
                    || !AIHelpers.IsRouteInCorrectArea(origin, destination)
                    || TripRestrictions.HasRestriction(origin.Profile.Country, destination.Profile.Country, now, TripRestriction.RestrictionType.Trips)
                    || TripRestrictions.HasRestriction(destination.Profile.Country, origin.Profile.Country, now, TripRestriction.RestrictionType.Trips)
                    || TripRestrictions.HasRestriction(carrier, origin.Profile.Country, destination.Profile.Country, now))
                {
                    continue;
                }

                double fare = PassengerHelpers.GetPassengerPrice(origin, destination);
                int demand = 0;
                double revenue = 0;

                foreach (CoachClass.ClassType type in classes)
                {
                    int both = origin.GetDestinationPassengersRate(destination, type)
                               + destination.GetDestinationPassengersRate(origin, type);

                    demand += both;
                    revenue += both * fare * GeneralHelpers.ClassToPriceFactor(type);
                }

                if (demand <= 0)
                {
                    continue;
                }

                int others = competitors.ContainsKey(key) ? competitors[key].Count : 0;

                long minBay = Math.Min(origin.GetMaxBayLength(), destination.GetMaxBayLength());

                bool fleetCanFly = fleet.Any(f => f.Coach.Range >= distance && f.Coach.MinBayLength <= minBay);

                result.Add(
                    new RouteAdvice
                    {
                        Destination = destination,
                        Distance = distance,
                        DailyDemand = demand,
                        Competitors = others,
                        RevenuePerDay = revenue,
                        ScoreRevenuePerDay = revenue / (1 + others),
                        FleetCanFly = fleetCanFly
                    });
            }

            List<RouteAdvice> best = result.OrderByDescending(r => r.ScoreRevenuePerDay).Take(maxResults).ToList();

            if (best.Count > 0)
            {
                double strongDemand = result.OrderByDescending(r => r.DailyDemand).ElementAt(Math.Max(0, result.Count / 10)).DailyDemand;

                foreach (RouteAdvice advice in best)
                {
                    var notes = new List<string>();

                    notes.Add(advice.Competitors == 0 ? "No competitors" : advice.Competitors + " competitor(s)");

                    if (advice.DailyDemand >= strongDemand)
                    {
                        notes.Add("strong demand");
                    }

                    if (!advice.FleetCanFly)
                    {
                        notes.Add("your fleet cannot run it yet");
                    }

                    advice.Advice = string.Join(", ", notes);
                }
            }

            return best;
        }

        private static string PairKey(Station a, Station b)
        {
            string x = a.Profile.IATACode ?? "";
            string y = b.Profile.IATACode ?? "";

            return string.CompareOrdinal(x, y) < 0 ? x + "|" + y : y + "|" + x;
        }
    }

}
