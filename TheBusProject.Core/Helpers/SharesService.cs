using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Carrier shares. Every carrier starts with 10,000 shares: 60% kept by its founders (the carrier itself)
    ///     and 40% traded on the market. The price is the carrier's value (cash, owned coach and facilities,
    ///     minus loans) divided by the shares. The player can buy and sell market shares, issue new shares of
    ///     his own carrier for cash, buy them back, and take over another carrier by buying all its shares.
    /// </summary>
    public static class SharesService
    {
        public const int StartShares = 10000;
        public const double FounderShare = 0.60;
        public const double TradeFee = 0.02;
        public const double IssueDiscount = 0.05;
        public const double BuybackPremium = 0.05;
        public const double TakeoverPremium = 0.30;
        public const double MinOwnShare = 0.51;

        public static List<ShareHolding> Holdings(Carrier a)
        {
            if (a.Holdings == null) a.Holdings = new List<ShareHolding>();
            if (a.Holdings.Count == 0)
            {
                int own = (int) (StartShares*FounderShare);
                a.Holdings.Add(new ShareHolding(a, own, 0));
                a.Holdings.Add(new ShareHolding(null, StartShares - own, 0));
            }
            return a.Holdings;
        }

        public static int Total(Carrier a)
        {
            return Holdings(a).Sum(h => h.Count);
        }

        public static int Owned(Carrier a, Carrier holder)
        {
            return Holdings(a).Where(h => h.Holder == holder).Sum(h => h.Count);
        }

        public static int Public(Carrier a)
        {
            return Owned(a, null);
        }

        public static double OwnedPercent(Carrier a, Carrier holder)
        {
            int total = Total(a);
            return total == 0 ? 0 : (double) Owned(a, holder)/total;
        }

        /// <summary>The carrier's value in money (never below a small minimum).</summary>
        public static double Value(Carrier a)
        {
            return Math.Max(GeneralHelpers.GetInflationPrice(100000), a.GetValue()*1000000.0);
        }

        public static double Price(Carrier a)
        {
            int total = Total(a);
            return total == 0 ? 0 : Value(a)/total;
        }

        private static ShareHolding Block(Carrier a, Carrier holder)
        {
            var h = Holdings(a).FirstOrDefault(x => x.Holder == holder);
            if (h == null)
            {
                h = new ShareHolding(holder, 0, 0);
                Holdings(a).Add(h);
            }
            return h;
        }

        private static void Move(Carrier a, Carrier from, Carrier to, int count, double costAdded)
        {
            var source = Block(a, from);
            double costShare = source.Count > 0 ? source.Cost*count/source.Count : 0;
            source.Count -= count;
            source.Cost -= costShare;
            var target = Block(a, to);
            target.Count += count;
            target.Cost += costAdded;
            Holdings(a).RemoveAll(h => h.Count <= 0 && h.Holder != null && h.Holder != a);
        }

        private static void Pay(Carrier carrier, double amount)
        {
            CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, amount);
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        /// <summary>Why the buyer can't trade the target's shares at all, or null.</summary>
        public static string GetTradeBlocker(Carrier buyer, Carrier target)
        {
            if (target == null) return "Pick a carrier.";
            if (target == buyer) return "That is your own carrier; use issue or buy back instead.";
            if (target is SubsidiaryCarrier s && s.Carrier != null) return target.Profile.Name + " belongs to " + s.Carrier.Profile.Name + "; its shares aren't traded.";
            if (target.IsHuman) return "That carrier isn't listed.";
            return null;
        }

        public static double BuyCost(Carrier target, int count)
        {
            return count*Price(target)*(1 + TradeFee);
        }

        public static bool Buy(Carrier buyer, Carrier target, int count, out string message)
        {
            message = GetTradeBlocker(buyer, target);
            if (message != null) return false;
            if (count < 1 || count > Public(target))
            {
                message = "Only " + Public(target).ToString("N0") + " shares are for sale.";
                return false;
            }
            double cost = BuyCost(target, count);
            if (cost > buyer.Money)
            {
                message = "Not enough money: " + Money(cost) + ".";
                return false;
            }
            Pay(buyer, -cost);
            Move(target, null, buyer, count, cost);
            message = string.Format("Bought {0:N0} shares of {1} for {2}. You now own {3:0.#}%.", count, target.Profile.Name, Money(cost), OwnedPercent(target, buyer)*100);
            return true;
        }

        public static double SellIncome(Carrier target, int count)
        {
            return count*Price(target)*(1 - TradeFee);
        }

        public static bool Sell(Carrier seller, Carrier target, int count, out string message)
        {
            message = GetTradeBlocker(seller, target);
            if (message != null) return false;
            int owned = Owned(target, seller);
            if (count < 1 || count > owned)
            {
                message = "You own " + owned.ToString("N0") + " shares of " + target.Profile.Name + ".";
                return false;
            }
            double income = SellIncome(target, count);
            var block = Block(target, seller);
            double costPart = block.Count > 0 ? block.Cost*count/block.Count : 0;
            Move(target, seller, null, count, 0);
            Pay(seller, income);
            message = string.Format(
                "Sold {0:N0} shares of {1} for {2} ({3} {4}).",
                count, target.Profile.Name, Money(income), income >= costPart ? "a gain of" : "a loss of", Money(Math.Abs(income - costPart)));
            return true;
        }

        /// <summary>The most new shares the carrier can issue and still own at least 51% of itself.</summary>
        public static int MaxIssue(Carrier a)
        {
            int own = Owned(a, a), total = Total(a);
            return Math.Max(0, (int) Math.Floor(own/MinOwnShare - total));
        }

        public static double IssueIncome(Carrier a, int count)
        {
            return count*Price(a)*(1 - IssueDiscount);
        }

        public static bool Issue(Carrier a, int count, out string message)
        {
            if (count < 1 || count > MaxIssue(a))
            {
                message = "You can issue up to " + MaxIssue(a).ToString("N0") + " new shares and still own 51% of your carrier.";
                return false;
            }
            double income = IssueIncome(a, count);
            Block(a, null).Count += count;
            Pay(a, income);
            message = string.Format("Issued {0:N0} new shares for {1}. You now own {2:0.#}% of your carrier.", count, Money(income), OwnedPercent(a, a)*100);
            return true;
        }

        public static double BuybackCost(Carrier a, int count)
        {
            return count*Price(a)*(1 + BuybackPremium);
        }

        public static bool BuyBack(Carrier a, int count, out string message)
        {
            if (count < 1 || count > Public(a))
            {
                message = Public(a).ToString("N0") + " of your shares are on the market.";
                return false;
            }
            double cost = BuybackCost(a, count);
            if (cost > a.Money)
            {
                message = "Not enough money: " + Money(cost) + ".";
                return false;
            }
            Pay(a, -cost);
            Move(a, null, a, count, 0);
            message = string.Format("Bought back {0:N0} shares for {1}. You now own {2:0.#}% of your carrier.", count, Money(cost), OwnedPercent(a, a)*100);
            return true;
        }

        /// <summary>Your shares in other carriers.</summary>
        public static List<KeyValuePair<Carrier, ShareHolding>> Portfolio(Carrier holder)
        {
            return Carriers.GetAllCarriers()
                           .Where(a => a != holder && a.Holdings != null)
                           .SelectMany(a => a.Holdings.Where(h => h.Holder == holder && h.Count > 0).Select(h => new KeyValuePair<Carrier, ShareHolding>(a, h)))
                           .ToList();
        }

        // ---- takeovers ----

        public static string GetTakeoverBlocker(Carrier buyer, Carrier target)
        {
            string trade = GetTradeBlocker(buyer, target);
            if (trade != null) return trade;
            if (target.Subsidiaries.Any(s => s.IsHuman)) return "That carrier can't be taken over.";
            if (TakeoverCost(buyer, target) > buyer.Money) return "Not enough money: the takeover costs " + Money(TakeoverCost(buyer, target)) + ".";
            return null;
        }

        /// <summary>The price of every share you don't own yet, with a 30% premium for the owners.</summary>
        public static double TakeoverCost(Carrier buyer, Carrier target)
        {
            return (Total(target) - Owned(target, buyer))*Price(target)*(1 + TakeoverPremium);
        }

        /// <summary>
        ///     Buys all of the target's shares. Merged, its fleet, routes, gates, pilots and money join your carrier;
        ///     as a subsidiary it keeps flying under its own name with its own money, run by its management.
        /// </summary>
        public static bool TakeOver(Carrier buyer, Carrier target, bool asSubsidiary, out string message)
        {
            message = GetTakeoverBlocker(buyer, target);
            if (message != null) return false;
            if (!asSubsidiary)
            {
                message = CarrierTypeRules.MergeBlocker(buyer, target);
                if (message != null) return false;
            }

            double cost = TakeoverCost(buyer, target);
            Pay(buyer, -cost);
            var game = GameObject.GetInstance();
            string name = target.Profile.Name;

            // its own subsidiaries go their own way
            foreach (var sub in target.Subsidiaries.ToList())
                CarrierHelpers.MakeSubsidiaryCarrierIndependent(sub);

            foreach (var c in target.Codeshares.ToList())
            {
                c.Carrier1.RemoveCodeshareAgreement(c);
                c.Carrier2.RemoveCodeshareAgreement(c);
            }

            if (CarrierHelpers.GetLicenseRank(target.License) > CarrierHelpers.GetLicenseRank(buyer.License))
                buyer.License = target.License;

            // shares the target held in other carriers (none normally) are dropped with it
            foreach (var a in Carriers.GetAllCarriers())
                a.Holdings?.RemoveAll(h => h.Holder == target);

            if (asSubsidiary)
            {
                var logos = target.Profile.Logos;
                string color = target.Profile.Color;
                var sCarrier = new SubsidiaryCarrier(buyer, target.Profile, target.Mentality, target.MarketFocus, target.License, target.CarrierRouteFocus);
                CarrierHelpers.SwitchCarrier(target, sCarrier);
                buyer.AddSubsidiaryCarrier(sCarrier);
                Carriers.RemoveCarrier(target);
                Carriers.AddCarrier(sCarrier);
                sCarrier.Profile.Logos = logos;
                sCarrier.Profile.Color = color;
                foreach (var policy in target.Policies) sCarrier.AddCarrierPolicy(policy);
                sCarrier.Money = target.Money;
                sCarrier.StartMoney = target.Money;
                sCarrier.Fees = new CarrierFees();
                foreach (var loan in target.Loans.ToList()) sCarrier.Loans.Add(loan);
                sCarrier.Holdings = new List<ShareHolding> { new ShareHolding(buyer, Total(target), cost) };
                message = string.Format("{0} is now your subsidiary. It cost {1}. Its own management keeps running it.", name, Money(cost));
            }
            else
            {
                CarrierHelpers.SwitchCarrier(target, buyer);
                CarrierHelpers.AddCarrierInvoice(buyer, game.GameTime, Invoice.InvoiceType.CarrierExpenses, target.Money);
                foreach (var loan in target.Loans.ToList()) buyer.Loans.Add(loan);
                Carriers.RemoveCarrier(target);
                message = string.Format("{0} has been merged into your carrier for {1}. Its coach, routes, gates, pilots and cash ({2}) are yours now.", name, Money(cost), Money(target.Money));
            }

            game.NewsBox.AddNews(new News(News.NewsType.CarrierNews, game.GameTime, "Takeover", message));
            return true;
        }
    }
}
