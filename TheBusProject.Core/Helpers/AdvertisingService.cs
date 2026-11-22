using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Advertising. Each channel (newspaper, radio, TV, internet) is set to none, local, national or global and
    ///     costs money every month. Together they set the reputation the carrier moves toward (50 with no
    ///     advertising), and a better reputation makes passengers choose the carrier more often. Campaigns aim
    ///     extra advertising at one country or one station for a few months.
    /// </summary>
    public static class AdvertisingService
    {
        public static readonly string[] CampaignSizes = { "Small", "Medium", "Large" };

        public static string ChannelName(AdvertisementType.CarrierAdvertisementType type)
        {
            switch (type)
            {
                case AdvertisementType.CarrierAdvertisementType.Newspaper: return "Newspapers";
                case AdvertisementType.CarrierAdvertisementType.Radio: return "Radio";
                case AdvertisementType.CarrierAdvertisementType.Tv: return "TV";
                default: return "Internet";
            }
        }

        /// <summary>The channels that exist in the current year (the internet from 1995, TV from 1950, ...).</summary>
        public static List<AdvertisementType.CarrierAdvertisementType> Channels()
        {
            int year = GameObject.GetInstance().GameTime.Year;
            return Enum.GetValues(typeof (AdvertisementType.CarrierAdvertisementType))
                       .Cast<AdvertisementType.CarrierAdvertisementType>()
                       .Where(t => year >= (int) t)
                       .ToList();
        }

        public static List<AdvertisementType> Levels(AdvertisementType.CarrierAdvertisementType channel)
        {
            return AdvertisementTypes.GetTypes(channel).OrderBy(t => t.ReputationLevel).ToList();
        }

        public static AdvertisementType Current(Carrier carrier, AdvertisementType.CarrierAdvertisementType channel)
        {
            return carrier.Advertisements.ContainsKey(channel) ? carrier.Advertisements[channel] : AdvertisementTypes.GetBasicType(channel);
        }

        public static void Set(Carrier carrier, AdvertisementType level)
        {
            carrier.SetCarrierAdvertisement(level);
        }

        /// <summary>The sum of the levels of the channels in use (0 to 12).</summary>
        public static int TotalLevel(Carrier carrier)
        {
            return Channels().Sum(c => Current(carrier, c).ReputationLevel);
        }

        public static double ChannelsMonthlyCost(Carrier carrier)
        {
            return Channels().Sum(c => Current(carrier, c).Price);
        }

        public static double CampaignsMonthlyCost(Carrier carrier)
        {
            var now = GameObject.GetInstance().GameTime;
            return Campaigns(carrier).Where(c => c.End > now).Sum(c => c.MonthlyCost);
        }

        /// <summary>The reputation the carrier moves toward with its current advertising.</summary>
        public static int TargetReputation(Carrier carrier)
        {
            return Math.Min(100, 50 + 4*TotalLevel(carrier));
        }

        /// <summary>How much more often passengers choose the carrier because of its reputation: 0.85 at 0, 1 at 50, 1.15 at 100.</summary>
        public static double ReputationFactor(Carrier carrier)
        {
            return 0.85 + Math.Max(0, Math.Min(100, carrier.Reputation))*0.003;
        }

        public static List<AdCampaign> Campaigns(Carrier carrier)
        {
            return carrier.AdCampaigns ?? (carrier.AdCampaigns = new List<AdCampaign>());
        }

        public static double CampaignBoost(int level)
        {
            return level >= 3 ? 0.35 : level == 2 ? 0.2 : 0.1;
        }

        /// <summary>The extra passenger share at an station from running campaigns (1 when none applies).</summary>
        public static double CampaignFactor(Carrier carrier, Station station)
        {
            var list = carrier.AdCampaigns;
            if (list == null || list.Count == 0) return 1;
            var now = GameObject.GetInstance().GameTime;
            double best = 0;
            foreach (var c in list)
            {
                if (c.End <= now || c.Start > now) continue;
                bool hits = c.IsStation
                    ? c.Target == station.Profile.IATACode
                    : station.Profile.Country != null && c.Target == station.Profile.Country.Uid;
                if (hits) best = Math.Max(best, CampaignBoost(c.Level));
            }
            return 1 + best;
        }

        /// <summary>The whole advertising effect on passengers choosing the carrier at an station.</summary>
        public static double StationFactor(Carrier carrier, Station station)
        {
            return ReputationFactor(carrier)*CampaignFactor(carrier, station);
        }

        /// <summary>The monthly price of a campaign: bigger targets and bigger campaigns cost more.</summary>
        public static double CampaignMonthlyCost(bool isStation, string target, int level)
        {
            double size = 1;
            if (isStation)
            {
                var a = Stations.GetStation(target);
                if (a != null) size = 1 + 0.5*(int) a.Profile.Size;
            }
            else
            {
                var c = Countries.GetCountry(target);
                int stations = c == null ? 10 : Stations.GetAllStations().Count(a => a.Profile.Country == c);
                size = 2 + Math.Min(8, stations/25.0);
            }
            double perLevel = level >= 3 ? 30000 : level == 2 ? 15000 : 6000;
            return GeneralHelpers.GetInflationPrice(perLevel*size);
        }

        public static string TargetName(AdCampaign c)
        {
            if (c.IsStation)
            {
                var a = Stations.GetStation(c.Target);
                return a == null ? c.Target : a.Profile.IATACode + " " + a.Profile.Name;
            }
            var country = Countries.GetCountry(c.Target);
            return country == null ? c.Target : country.Name;
        }

        public static bool StartCampaign(Carrier carrier, bool isStation, string target, int level, int months, out string message)
        {
            if (string.IsNullOrEmpty(target))
            {
                message = "Pick a country or a station.";
                return false;
            }
            if (months < 1 || months > 24 || level < 1 || level > 3)
            {
                message = "Pick a size and 1 to 24 months.";
                return false;
            }
            var now = GameObject.GetInstance().GameTime;
            if (Campaigns(carrier).Any(c => c.End > now && c.IsStation == isStation && c.Target == target))
            {
                message = "A campaign there is already running. End it first or wait until it is over.";
                return false;
            }
            double monthly = CampaignMonthlyCost(isStation, target, level);
            var campaign = new AdCampaign(target, isStation, level, now, now.AddMonths(months), monthly);
            Campaigns(carrier).Add(campaign);
            // the first month is paid now, the rest on the 1st of each month
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.CarrierExpenses, -monthly);
            message = string.Format(
                "{0} campaign in {1} until {2}: +{3:0}% passenger share there, {4} a month.",
                CampaignSizes[level - 1], TargetName(campaign), campaign.End.ToShortDateString(), CampaignBoost(level)*100,
                new ValueCurrencyFormatter().Convert(monthly));
            return true;
        }

        public static void EndCampaign(Carrier carrier, AdCampaign campaign)
        {
            Campaigns(carrier).Remove(campaign);
        }

        /// <summary>Monthly: pays the campaigns, removes finished ones and moves the reputation toward its target.</summary>
        public static void ProcessMonth(Carrier carrier)
        {
            var now = GameObject.GetInstance().GameTime;
            var list = Campaigns(carrier);
            foreach (var c in list.ToList())
            {
                if (c.End <= now)
                {
                    list.Remove(c);
                    if (carrier.IsHuman)
                    {
                        GameObject.GetInstance().NewsBox.AddNews(
                            new News(News.NewsType.CarrierNews, now, "Campaign over", "Your advertising campaign in " + TargetName(c) + " has ended."));
                    }
                    continue;
                }
                // the first month was paid at the start; one payment on the 1st for each further month booked
                int monthsBooked = (c.End.Year - c.Start.Year)*12 + c.End.Month - c.Start.Month;
                int monthIndex = (now.Year - c.Start.Year)*12 + now.Month - c.Start.Month;
                if (monthIndex >= 1 && monthIndex < monthsBooked)
                    CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.CarrierExpenses, -c.MonthlyCost);
            }

            int target = TargetReputation(carrier);
            int rep = carrier.Reputation;
            if (rep != target)
            {
                int step = Math.Max(1, Math.Abs(target - rep)/4);
                carrier.Reputation = rep < target ? Math.Min(target, rep + step) : Math.Max(target, rep - step);
            }
        }
    }
}
