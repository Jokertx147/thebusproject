using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     The player's subsidiary carriers. They fly under their own name with their own money and are run by their
    ///     own management (the computer); the player starts them, moves money in and out, sells them or folds them
    ///     back into the main carrier.
    /// </summary>
    public static class SubsidiaryService
    {
        public const int MaxSubsidiaries = 5;

        public static double MinStartMoney => GeneralHelpers.GetInflationPrice(2000000);

        public static List<SubsidiaryCarrier> Mine(Carrier carrier)
        {
            return carrier.Subsidiaries.ToList();
        }

        public static string GetCreateBlocker(Carrier parent, string name, string iata, Station homebase, double money)
        {
            if (parent.Subsidiaries.Count >= MaxSubsidiaries) return "You can have up to " + MaxSubsidiaries + " subsidiaries.";
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return "Give it a name.";
            if (Carriers.GetAllCarriers().Any(a => string.Equals(a.Profile.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                return "Another carrier is already called " + name.Trim() + ".";
            string code = (iata ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(code, "^[A-Z0-9]{2}$")) return "The carrier code must be 2 letters or digits, like ZX.";
            if (Carriers.GetAllCarriers().Any(a => a.Profile.IATACode == code)) return "The code " + code + " is already used by another carrier.";
            if (homebase == null) return "Pick a home station.";
            if (money < MinStartMoney) return "Give it at least " + Money(MinStartMoney) + " to start with.";
            if (money > parent.Money) return "You don't have " + Money(money) + ".";
            return null;
        }

        public static SubsidiaryCarrier Create(
            Carrier parent, string name, string iata, Station homebase, double money,
            Carrier.CarrierMentality mentality, CarrierFocus market, Route.RouteType focus, out string message)
        {
            message = GetCreateBlocker(parent, name, iata, homebase, money);
            if (message != null) return null;
            var sub = CarrierHelpers.CreateSubsidiaryCarrier(parent, money, name.Trim(), iata.Trim().ToUpperInvariant(), mentality, market, focus, homebase);
            sub.AutoManaged = true;
            sub.Holdings = new List<ShareHolding> { new ShareHolding(parent, SharesService.StartShares, money) };
            if (!sub.Stations.Contains(homebase)) sub.AddStation(homebase);
            message = string.Format(
                "{0} ({1}) is running from {2} with {3}. Its management will buy coach and open routes.",
                sub.Profile.Name, sub.Profile.IATACode, homebase.Profile.IATACode, Money(money));
            return sub;
        }

        /// <summary>Moves money from the parent to the subsidiary (positive) or back (negative).</summary>
        public static bool Transfer(Carrier parent, SubsidiaryCarrier sub, double amount, out string message)
        {
            var now = GameObject.GetInstance().GameTime;
            if (amount > 0)
            {
                if (amount > parent.Money)
                {
                    message = "You don't have " + Money(amount) + ".";
                    return false;
                }
                CarrierHelpers.AddCarrierInvoice(parent, now, Invoice.InvoiceType.CarrierExpenses, -amount);
                CarrierHelpers.AddCarrierInvoice(sub, now, Invoice.InvoiceType.CarrierExpenses, amount);
                message = Money(amount) + " moved to " + sub.Profile.Name + ".";
                return true;
            }
            double back = -amount;
            if (back <= 0 || back > sub.Money)
            {
                message = sub.Profile.Name + " has " + Money(Math.Max(0, sub.Money)) + ".";
                return false;
            }
            CarrierHelpers.AddCarrierInvoice(sub, now, Invoice.InvoiceType.CarrierExpenses, -back);
            CarrierHelpers.AddCarrierInvoice(parent, now, Invoice.InvoiceType.CarrierExpenses, back);
            message = Money(back) + " moved from " + sub.Profile.Name + " to your carrier.";
            return true;
        }

        /// <summary>What a buyer pays for the subsidiary: 90% of its value.</summary>
        public static double SalePrice(SubsidiaryCarrier sub)
        {
            return Math.Max(0, sub.GetValue()*1000000.0*0.9);
        }

        public static void Sell(Carrier parent, SubsidiaryCarrier sub, out string message)
        {
            double price = SalePrice(sub);
            CarrierHelpers.MakeSubsidiaryCarrierIndependent(sub);
            sub.Holdings = new List<ShareHolding>();
            CarrierHelpers.AddCarrierInvoice(parent, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, price);
            message = sub.Profile.Name + " was sold for " + Money(price) + " and now runs on its own.";
        }

        /// <summary>Folds the subsidiary into the main carrier: coach, gates, stations and money come over.</summary>
        public static bool FoldIn(Carrier parent, SubsidiaryCarrier sub, out string message)
        {
            message = null;
            string name = sub.Profile.Name;
            int fleet = sub.Fleet.Count, routes = sub.Routes.Count;
            double money = sub.Money;
            foreach (var route in sub.Routes.ToList())
            {
                route.Carrier = parent;
                sub.RemoveRoute(route);
                parent.AddRoute(route);
            }
            foreach (var driver in sub.Drivers.ToList())
            {
                sub.RemoveDriver(driver);
                driver.Carrier = parent;
                parent.AddDriver(driver);
            }
            CarrierHelpers.CloseSubsidiaryCarrier(sub);
            message = string.Format("{0} is now part of your carrier: {1} coaches, {2} route(s) and {3} came over.", name, fleet, routes, Money(money));
            return true;
        }

        public static double LastMonthProfit(Carrier a)
        {
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return Enum.GetValues(typeof (Invoice.InvoiceType)).Cast<Invoice.InvoiceType>()
                       .Where(t => t != Invoice.InvoiceType.CarrierExpenses)
                       .Sum(t => a.GetInvoicesAmountMonth(last.Year, last.Month, t));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }
    }
}
