using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     What owning an coach costs with realistic costs: depreciation and the money tied up in it, 0.5% of its
    ///     current value a month (6% a year; a lease costs about 8.7% a year, so owning stays cheaper). It is paid
    ///     whether the coach flies or not, so idle coach cost money. Tough costs make it 40% more. Computer
    ///     carriers sell owned coach they have no use for, slowly (one a week) and at a used-market price (75%); an
    ///     coach that has found no route for four months goes faster (up to two a day): it is the wrong coach for the
    ///     carrier's network.
    /// </summary>
    public static class OwnershipService
    {
        /// <summary>
        ///     What owning a coach costs each month, as a share of its value: depreciation and the money tied up in
        ///     it. A coach written off over fifteen years loses about 6.7% of its value a year on its own, and the
        ///     capital costs more on top, so a bit under 1% a month is the honest figure. Greyhound's depreciation
        ///     and operating rents together came to 14.3% of its revenue.
        /// </summary>
        public const double MonthlyShare = 0.009;

        /// <summary>What a sold coach fetches, as a share of its value, and how often an carrier can sell one.</summary>
        public const double SalePriceShare = 0.75;
        public const int DaysBetweenSales = 7;

        private static readonly WeakDates<Carrier> LastSale = new WeakDates<Carrier>();

        /// <summary>An coach idle this long (owned, beyond the reserve) is sold without waiting for the weekly sale.</summary>
        public const int LongIdleDays = 120;
        public const int LongIdleSalesPerDay = 2;

        // since when each coach has been idle (not saved: after loading a game the count starts again)
        private static readonly WeakDates<FleetCoach> IdleSince = new WeakDates<FleetCoach>();

        /// <summary>Counter for the tests: coach sold after a long time idle.</summary>
        public static int LongIdleSold;

        public static bool IsOwned(FleetCoach f)
        {
            return f.Purchased == FleetCoach.PurchasedType.Bought || f.Purchased == FleetCoach.PurchasedType.BoughtDownPayment;
        }

        /// <summary>The month's ownership cost of an coach (0 when leased, not delivered yet, or with the original costs).</summary>
        public static double MonthlyCost(FleetCoach f)
        {
            if (!TripCosts.Enabled || f?.Coach == null || !IsOwned(f)) return 0;
            if (f.Coach.BuiltDate > GameObject.GetInstance().GameTime) return 0;
            double value = Math.Max(0, f.Coach.GetPrice());
            return value*MonthlyShare*(TripCosts.Level >= 2 ? 1.4 : 1.0);
        }

        /// <summary>Monthly: charges the carrier for the coach it owns.</summary>
        public static double ProcessMonth(Carrier carrier)
        {
            if (!TripCosts.Enabled) return 0;
            var now = GameObject.GetInstance().GameTime;
            double total = 0;
            foreach (var f in carrier.Fleet.ToList())
            {
                double cost = MonthlyCost(f);
                if (cost <= 0) continue;
                total += cost;
                f.Data?.AddOperatingValue(new OperatingValue("Ownership", now.Year, now.Month, -cost));
            }
            if (total > 0) CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Rents, -total);
            return total;
        }

        /// <summary>How many idle coach a computer carrier keeps in reserve.</summary>
        public static int Reserve(Carrier carrier)
        {
            return Math.Max(2, (int) Math.Round(carrier.Fleet.Count*0.10));
        }

        /// <summary>
        ///     A computer carrier with more idle owned coach than its reserve sells the oldest one at 75% of its value,
        ///     at most one a week. Returns the coach sold, or null.
        /// </summary>
        public static FleetCoach SellSurplus(Carrier carrier)
        {
            if (!TripCosts.Enabled || carrier == null || carrier.IsHuman) return null;
            var now = GameObject.GetInstance().GameTime;
            List<FleetCoach> idle;
            lock (carrier.Fleet)
            {
                idle = carrier.Fleet.Where(f => f.Coach.BuiltDate <= now && !f.HasRoute && IsOwned(f) && f.UsableBy(carrier)
                                                && (f.Status == FleetCoach.CoachStatus.Stopped || IdleAfterRoute(f, now))).ToList();
            }
            int reserve = Reserve(carrier);

            // keep count of how long each coach has been idle
            List<FleetCoach> longIdle;
            lock (IdleSince)
            {
                foreach (var f in idle)
                {
                    DateTime since;
                    if (!IdleSince.TryGetValue(f, out since)) IdleSince[f] = now;
                }
                List<FleetCoach> flying;
                lock (carrier.Fleet) flying = carrier.Fleet.Where(f => f.HasRoute).ToList();
                foreach (var f in flying) IdleSince.Remove(f);
                longIdle = idle.Where(f => { DateTime since; return IdleSince.TryGetValue(f, out since) && (now - since).TotalDays >= LongIdleDays; })
                               .OrderBy(f => f.Coach.BuiltDate).ToList();
            }
            if (idle.Count <= reserve) return null;

            // the wrong coach for the network: sold without waiting for the weekly sale
            FleetCoach soldLong = null;
            for (int i = 0; i < Math.Min(LongIdleSalesPerDay, Math.Min(longIdle.Count, idle.Count - reserve)); i++)
            {
                soldLong = Sell(carrier, longIdle[i], now);
                System.Threading.Interlocked.Increment(ref LongIdleSold);
            }
            if (soldLong != null) return soldLong;

            lock (LastSale)
            {
                DateTime last;
                if (LastSale.TryGetValue(carrier, out last) && (now - last).TotalDays < DaysBetweenSales) return null;
                LastSale[carrier] = now;
            }
            return Sell(carrier, idle.OrderBy(f => f.Coach.BuiltDate).First(), now);
        }

        // an coach whose route was given up keeps its "to route start" status; once it is in the yard it is idle
        private static bool IdleAfterRoute(FleetCoach f, DateTime now)
        {
            if (f.Status != FleetCoach.CoachStatus.ToRouteStart || f.Routes.Count > 0) return false;
            var trip = f.CurrentTrip;
            try
            {
                return trip == null || trip.Entry == null || trip.GetExpectedLandingTime() <= now;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static FleetCoach Sell(Carrier carrier, FleetCoach sell, DateTime now)
        {
            double price = Math.Max(0, sell.Coach.GetPrice()*SalePriceShare);
            lock (carrier.Fleet)
            {
                carrier.RemoveCoach(sell);
            }
            foreach (var driver in sell.Drivers.ToList()) sell.RemoveDriver(driver);
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Purchases, price);
            return sell;
        }
    }
}
