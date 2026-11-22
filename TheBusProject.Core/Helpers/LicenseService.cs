using System;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>The carrier's flying license: Regional, Domestic, then Long haul. A higher one is bought for money.</summary>
    public static class LicenseService
    {
        public static string Name(Carrier.CarrierLicense license)
        {
            switch (CarrierHelpers.GetLicenseRank(license))
            {
                case 0: return "Regional";
                case 1: return "Domestic";
                default: return "Long haul";
            }
        }

        public static string Allows(Carrier.CarrierLicense license)
        {
            switch (CarrierHelpers.GetLicenseRank(license))
            {
                case 0: return "Trips inside one continent or one union of countries.";
                case 1: return "The same, plus trips inside a country and trips shorter than 2,000 km between continents.";
                default: return "Trips anywhere, including long distance between continents.";
            }
        }

        public static bool IsHighest(Carrier carrier)
        {
            return CarrierHelpers.GetLicenseRank(carrier.License) >= 2;
        }

        public static Carrier.CarrierLicense Next(Carrier carrier)
        {
            return CarrierHelpers.GetNextLicense(carrier.License);
        }

        public static double Price(Carrier carrier)
        {
            return GeneralHelpers.GetInflationPrice(CarrierHelpers.GetLicenseRank(carrier.License) == 0 ? 3000000 : 15000000);
        }

        /// <summary>Why the next license cannot be bought now, or null.</summary>
        public static string GetBlocker(Carrier carrier)
        {
            if (IsHighest(carrier)) return "You already have the highest license.";
            if (carrier.Money < Price(carrier)) return "Not enough money: it costs " + Price(carrier).ToString("N0") + ".";
            return null;
        }

        public static bool Upgrade(Carrier carrier, out string message)
        {
            string blocker = GetBlocker(carrier);
            if (blocker != null)
            {
                message = blocker;
                return false;
            }
            double price = Price(carrier);
            var next = Next(carrier);
            CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, -price);
            carrier.License = next;
            message = "Your carrier now has a " + Name(next) + " license. " + Allows(next);
            return true;
        }
    }
}
