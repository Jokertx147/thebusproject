using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>What one coach earned in a month, and what it cost to have.</summary>
    public class CoachFigures
    {
        public FleetCoach Coach { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }

        /// <summary>Its share of the profit of the routes it flies, before the coach itself is paid for.</summary>
        public double RouteShare { get; set; }

        /// <summary>What owning or leasing it costs for the month.</summary>
        public double Ownership { get; set; }

        /// <summary>The share of the routes' profit less what the coach costs: what it really made.</summary>
        public double Profit => RouteShare - Ownership;

        /// <summary>Trips a week it is rostered for.</summary>
        public int WeeklyTrips { get; set; }

        /// <summary>How full those trips were, averaged over its routes by the trips it flies on each.</summary>
        public double Load { get; set; }

        /// <summary>The routes it flies, as "ATL-MCO, ATL-LGA".</summary>
        public string Routes { get; set; }

        public bool Idle => WeeklyTrips == 0;
    }

    /// <summary>
    ///     Profit per coach. The game invoices a route, not an coach, so an coach is credited with the share of
    ///     each of its routes' profit that matches the trips it operates on that route: two coach splitting a route
    ///     evenly take half each. What the coach costs to own or lease is then charged to the coach itself, which
    ///     the route figures leave out, so the two together account for the money once.
    ///
    ///     An coach with no trips earns nothing and still costs its ownership, so it shows as a loss of exactly
    ///     what it costs to keep.
    /// </summary>
    public static class CoachProfitService
    {
        public static CoachFigures Figures(FleetCoach f, int year, int month)
        {
            var figures = new CoachFigures
            {
                Coach = f,
                Name = f.Name,
                TypeName = f.Coach.Type.Name,
                Ownership = OwnershipService.MonthlyCost(f)
            };

            var routes = f.Routes.ToList();
            double share = 0, loadWeight = 0, loaded = 0;
            int trips = 0;
            foreach (Route route in routes)
            {
                var entries = route.TimeTable.Entries.ToList();
                int mine = entries.Count(e => e.Coach == f);
                if (mine == 0) continue;

                int all = Math.Max(mine, entries.Count);
                double profit = RouteProfitService.Month(route, year, month);
                share += profit*mine/all;
                trips += mine;

                double load = ConnectionService.MonthLoad(route);
                if (!double.IsNaN(load) && load > 0)
                {
                    loaded += load*mine;
                    loadWeight += mine;
                }
            }

            figures.RouteShare = share;
            figures.WeeklyTrips = trips;
            figures.Load = loadWeight > 0 ? loaded/loadWeight : 0;
            figures.Routes = string.Join(
                ", ",
                routes.Where(r => r.TimeTable.Entries.Any(e => e.Coach == f))
                      .Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode));
            return figures;
        }

        /// <summary>Every delivered coach of the carrier, for the month given.</summary>
        public static List<CoachFigures> All(Carrier carrier, int year, int month)
        {
            return carrier.DeliveredFleet.ToList().Select(f => Figures(f, year, month)).ToList();
        }

        /// <summary>Every delivered coach, for the last full month.</summary>
        public static List<CoachFigures> LastMonth(Carrier carrier)
        {
            DateTime last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return All(carrier, last.Year, last.Month);
        }
    }
}
