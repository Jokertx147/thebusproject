using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>Selling and leasing out the carrier's own coach: lease an idle coach to another carrier for rent, or list it for sale at a price.</summary>
    public static class CoachDealService
    {
        public static readonly int[] LeaseMonths = {12, 36, 60};
        public static readonly double[] SalePrices = {0.9, 1.0, 1.15};
        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        public static bool IsLeasedOut(FleetCoach f)
        {
            return f.LeaseOutTo != null && f.LeaseOutUntil > GameObject.GetInstance().GameTime;
        }

        public static bool IsListed(FleetCoach f)
        {
            return f.SaleAsk > 0;
        }

        /// <summary>Why the coach cannot be leased out or listed, or null when it can.</summary>
        public static string GetBlocker(FleetCoach f)
        {
            GameObject game = GameObject.GetInstance();
            if (f.Purchased != FleetCoach.PurchasedType.Bought) return "only a coach you own outright can be leased out or sold this way";
            if (f.Coach.BuiltDate > game.GameTime) return "not delivered yet";
            if (f.Routes.Count > 0) return "it still has routes";
            if (f.Status != FleetCoach.CoachStatus.Stopped) return "it is not stopped";
            if (CharterService.IsOnCharter(f)) return "it is out on a charter";
            if (IsLeasedOut(f)) return "it is already leased out";
            if (FerryService.IsFerrying(f)) return "it is on a ferry trip";
            if (IsListed(f)) return "it is already listed for sale";
            if (MaintenanceService.IsGrounded(f)) return "it is grounded";
            if (MaintenanceService.IsCovering(f) || MaintenanceService.IsCovered(f)) return "it is tied up in a maintenance cover";
            return null;
        }

        public static double RentFor(FleetCoach f, int months)
        {
            double factor = months >= 60 ? 0.9 : months >= 36 ? 0.95 : 1.0;
            return Math.Round(f.Coach.GetLeasingPrice()*factor);
        }

        public static double EarlyEndFee(FleetCoach f)
        {
            return f.LeaseOutRent*3;
        }

        private static Carrier PickLessee(Carrier owner, FleetCoach f)
        {
            var candidates = Carriers.GetAllCarriers().Where(a => a != owner && !a.IsHuman && a.Profile.Country != null && a.Money > 0).ToList();
            if (candidates.Count == 0) return null;
            return candidates[Rnd.Next(candidates.Count)];
        }

        public static bool LeaseOut(Carrier carrier, FleetCoach f, int months, out string message)
        {
            GameObject game = GameObject.GetInstance();
            string blocker = GetBlocker(f);
            if (blocker != null)
            {
                message = f.Name + ": " + blocker + ".";
                return false;
            }
            Carrier lessee = PickLessee(carrier, f);
            if (lessee == null)
            {
                message = "No carrier wants it right now.";
                return false;
            }
            foreach (var driver in f.Drivers.ToList()) f.RemoveDriver(driver);
            f.LeaseOutTo = lessee;
            f.LeaseOutStart = game.GameTime.Date;
            f.LeaseOutUntil = game.GameTime.Date.AddMonths(months);
            f.LeaseOutRent = RentFor(f, months);
            message = f.Name + " is leased to " + lessee.Profile.Name + " for " + months + " months at " + f.LeaseOutRent.ToString("N0") + " a month. The first rent comes on the 1st.";
            return true;
        }

        /// <summary>Ends the lease: early (pay a fee) or when it runs out. The coach comes back a little worn.</summary>
        public static string EndLease(Carrier carrier, FleetCoach f, bool early)
        {
            GameObject game = GameObject.GetInstance();
            if (f.LeaseOutTo == null) return null;
            string lessee = f.LeaseOutTo.Profile.Name;
            double months = Math.Max(0, (game.GameTime.Date - f.LeaseOutStart).TotalDays/30.0);
            f.Coach.Condition = Math.Max(40, f.Coach.Condition - months*0.12);
            f.Coach.Flown += months*25000;
            double fee = 0;
            if (early)
            {
                fee = EarlyEndFee(f);
                CarrierHelpers.AddCarrierInvoice(carrier, game.GameTime, Invoice.InvoiceType.Rents, -fee);
            }
            f.LeaseOutTo = null;
            f.LeaseOutUntil = DateTime.MinValue;
            f.LeaseOutRent = 0;
            return f.Name + " is back from " + lessee + (early ? ", the early end cost " + fee.ToString("N0") + "." : ".");
        }

        public static bool List(FleetCoach f, double ratio, out string message)
        {
            string blocker = GetBlocker(f);
            if (blocker != null)
            {
                message = f.Name + ": " + blocker + ".";
                return false;
            }
            f.SaleAsk = Math.Round(f.Coach.GetPrice()*ratio);
            f.SaleSince = GameObject.GetInstance().GameTime.Date;
            message = f.Name + " is for sale at " + f.SaleAsk.ToString("N0") + ". A buyer may come any day.";
            return true;
        }

        public static void Withdraw(FleetCoach f)
        {
            f.SaleAsk = 0;
        }

        /// <summary>Chance per day that a buyer takes it at this price.</summary>
        public static double BuyerChance(FleetCoach f)
        {
            double value = Math.Max(1, f.Coach.GetPrice());
            double ratio = f.SaleAsk/value;
            return Math.Max(0.005, Math.Min(0.12, 0.04 + (1.0 - ratio)*0.4));
        }

        public static List<string> ProcessDaily(Carrier carrier)
        {
            GameObject game = GameObject.GetInstance();
            var news = new List<string>();
            foreach (FleetCoach f in carrier.Fleet.ToList())
            {
                if (f.LeaseOutTo != null)
                {
                    if (game.GameTime.Day == 1 && f.LeaseOutStart < game.GameTime.Date)
                    {
                        CarrierHelpers.AddCarrierInvoice(carrier, game.GameTime, Invoice.InvoiceType.Rents, f.LeaseOutRent);
                    }
                    if (f.LeaseOutUntil <= game.GameTime)
                    {
                        news.Add(EndLease(carrier, f, false));
                    }
                }
                else if (f.SaleAsk > 0)
                {
                    if (GetBlocker0(f) != null)
                    {
                        f.SaleAsk = 0;
                        continue;
                    }
                    if (Rnd.NextDouble() < BuyerChance(f))
                    {
                        double price = f.SaleAsk;
                        carrier.RemoveCoach(f);
                        CarrierHelpers.AddCarrierInvoice(carrier, game.GameTime, Invoice.InvoiceType.Purchases, price);
                        foreach (var driver in f.Drivers.ToList()) f.RemoveDriver(driver);
                        news.Add(f.Name + " (" + f.Coach.Type.Name + ") sold for " + price.ToString("N0") + " after " + (int) (game.GameTime.Date - f.SaleSince).TotalDays + " days.");
                        f.SaleAsk = 0;
                    }
                }
            }
            if (news.Count > 0 && carrier == game.HumanCarrier)
            {
                game.NewsBox.AddNews(new News(News.NewsType.CarrierNews, game.GameTime, "Coach deals", string.Join("\n", news)));
            }
            return news;
        }

        //a listed coach that got routes, grounded or sent away is taken off the market
        private static string GetBlocker0(FleetCoach f)
        {
            if (f.Routes.Count > 0) return "routes";
            if (f.Status != FleetCoach.CoachStatus.Stopped) return "status";
            if (CharterService.IsOnCharter(f)) return "charter";
            return null;
        }
    }
}
