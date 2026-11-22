using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Codeshare agreements for the player. A partner gets 1% of the ticket income on the other carrier's trips
    ///     (one way: only the carrier sharing its routes pays), and both carriers get more passengers at stations
    ///     the partner flies to.
    /// </summary>
    public static class CodeshareService
    {
        private static readonly Dictionary<Carrier, DateTime> Refusals = new Dictionary<Carrier, DateTime>();
        public const int RefusalDays = 90;

        public static Carrier Partner(CodeshareAgreement c, Carrier me)
        {
            return c.Carrier1 == me ? c.Carrier2 : c.Carrier1;
        }

        public static CodeshareAgreement Find(Carrier a, Carrier b)
        {
            return a.Codeshares.FirstOrDefault(c => (c.Carrier1 == a && c.Carrier2 == b) || (c.Carrier1 == b && c.Carrier2 == a));
        }

        public static string Describe(CodeshareAgreement c, Carrier me)
        {
            if (c.Type == CodeshareAgreement.CodeshareType.BothWays) return "Both ways: each carrier sells the other's trips";
            return c.Carrier1 == me ? "One way: the partner sells your trips" : "One way: you sell the partner's trips";
        }

        /// <summary>The one-time fee for signing (the game's codeshare price).</summary>
        public static double SigningFee(Carrier partner, Carrier me)
        {
            return CarrierHelpers.GetCodesharingPrice(partner, me);
        }

        /// <summary>Why the partner would say no, or an empty list when it would say yes. Follows the game's own rules.</summary>
        public static List<string> Reasons(Carrier partner, Carrier asker, CodeshareAgreement.CodeshareType type)
        {
            var reasons = new List<string>();
            double coeff = type == CodeshareAgreement.CodeshareType.OneWay ? 0.25 : 0.40;

            var partnerCountries = partner.Stations.Select(a => a.Profile.Country).Distinct().ToList();
            var askerCountries = asker.Stations.Select(a => a.Profile.Country).Distinct().ToList();
            int sameCountries = askerCountries.Intersect(partnerCountries).Count();
            int sameStations = asker.Stations.Distinct().Intersect(partner.Stations).Count();
            int partnerCodeshareCountries = partner.GetCodesharingCarriers().SelectMany(a => a.Stations)
                                                   .Select(a => a.Profile.Country).Distinct().Intersect(partnerCountries).Count();

            if (partner.Routes.Count > 3*asker.Routes.Count)
                reasons.Add(string.Format("You are too small for them: they run {0} routes, you need at least {1}.", partner.Routes.Count, (int) Math.Ceiling(partner.Routes.Count/3.0)));
            if (partner.Stations.Count > 0 && sameStations >= partner.Stations.Count*coeff)
                reasons.Add(string.Format("You share too many stations ({0} of their {1}); they want partners that add new places.", sameStations, partner.Stations.Count));
            if (partnerCountries.Count > 0 && sameCountries >= partnerCountries.Count*0.75)
                reasons.Add(string.Format("You run to most of the same countries ({0} of their {1}).", sameCountries, partnerCountries.Count));
            if (partnerCountries.Count > 0 && partnerCodeshareCountries >= partnerCountries.Count*coeff && partner.Codeshares.Count > 0)
                reasons.Add("Their other codeshare partners already cover those countries.");
            return reasons;
        }

        /// <summary>Why a proposal can't even be made, or null.</summary>
        public static string GetBlocker(Carrier me, Carrier partner)
        {
            if (partner == null) return "Pick a carrier.";
            if (partner == me) return "That is your own carrier.";
            if (Find(me, partner) != null) return "You already codeshare with " + partner.Profile.Name + ".";
            if (partner is SubsidiaryCarrier sub && sub.Carrier == me) return "That is your own subsidiary.";
            if (me.Routes.Count == 0) return "Open some routes first; there is nothing to share yet.";
            if (Refusals.TryGetValue(partner, out var when) && (GameObject.GetInstance().GameTime - when).TotalDays < RefusalDays)
                return partner.Profile.Name + " said no recently. Try again after " + when.AddDays(RefusalDays).ToShortDateString() + ".";
            return null;
        }

        public static bool Propose(Carrier me, Carrier partner, CodeshareAgreement.CodeshareType type, out string message)
        {
            message = GetBlocker(me, partner);
            if (message != null) return false;

            var reasons = Reasons(partner, me, type);
            if (reasons.Count > 0 || !CarrierHelpers.AcceptCodesharing(partner, me, type))
            {
                Refusals[partner] = GameObject.GetInstance().GameTime;
                message = partner.Profile.Name + " said no. " + string.Join(" ", reasons);
                return false;
            }

            double fee = SigningFee(partner, me);
            CarrierHelpers.AddCarrierInvoice(me, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, -fee);
            // the partner shares its routes with you (one way), or both share (both ways)
            var agreement = new CodeshareAgreement(partner, me, type);
            partner.AddCodeshareAgreement(agreement);
            me.AddCodeshareAgreement(agreement);
            message = partner.Profile.Name + " agreed to a codeshare (" + Describe(agreement, me).ToLower() + ").";
            return true;
        }

        public static void End(Carrier me, CodeshareAgreement c)
        {
            c.Carrier1.RemoveCodeshareAgreement(c);
            c.Carrier2.RemoveCodeshareAgreement(c);
        }

        /// <summary>Stations both carriers fly to.</summary>
        public static int SharedStations(Carrier a, Carrier b)
        {
            return a.Stations.Intersect(b.Stations).Count();
        }

        /// <summary>The partner's routes that touch one of your stations: these bring you more passengers there.</summary>
        public static int PartnerRoutesAtMyStations(Carrier me, Carrier partner)
        {
            var mine = new HashSet<Station>(me.Stations);
            return partner.Routes.ToList().Count(r => mine.Contains(r.Destination1) || mine.Contains(r.Destination2));
        }

        /// <summary>What you got from the partner last month (1% of its ticket income when it shares its routes with you).</summary>
        public static double MonthlyIncome(Carrier me, CodeshareAgreement c)
        {
            var partner = Partner(c, me);
            bool partnerShares = c.Carrier1 == partner || c.Type == CodeshareAgreement.CodeshareType.BothWays;
            if (!partnerShares) return 0;
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return partner.GetInvoicesAmountMonth(last.Year, last.Month, Invoice.InvoiceType.Tickets)*CodeshareAgreement.TicketSalePercent/100;
        }

        /// <summary>What you paid the partner last month (1% of your ticket income when you share your routes with it).</summary>
        public static double MonthlyCost(Carrier me, CodeshareAgreement c)
        {
            bool iShare = c.Carrier1 == me || c.Type == CodeshareAgreement.CodeshareType.BothWays;
            if (!iShare) return 0;
            var last = GameObject.GetInstance().GameTime.AddMonths(-1);
            return me.GetInvoicesAmountMonth(last.Year, last.Month, Invoice.InvoiceType.Tickets)*CodeshareAgreement.TicketSalePercent/100;
        }

        /// <summary>Carriers worth asking, best first: many routes at your stations and few shared stations.</summary>
        public static List<Carrier> Candidates(Carrier me)
        {
            return Carriers.GetAllCarriers()
                           .Where(a => a != me && !a.IsHuman && Find(me, a) == null && !(a is SubsidiaryCarrier s && s.Carrier == me))
                           .OrderByDescending(a => PartnerRoutesAtMyStations(me, a) - SharedStations(me, a))
                           .ThenByDescending(a => a.Routes.Count)
                           .ToList();
        }

        // ---- offers from computer carriers ----

        public static List<CodeshareAgreement> Offers(Carrier me)
        {
            return me.CodeshareOffers ?? (me.CodeshareOffers = new List<CodeshareAgreement>());
        }

        /// <summary>A computer carrier asks the player for a codeshare (kept until the player answers).</summary>
        public static void AddOffer(Carrier me, Carrier asker)
        {
            var offers = Offers(me);
            if (Find(me, asker) != null || offers.Any(o => o.Carrier1 == asker || o.Carrier2 == asker)) return;
            offers.Add(new CodeshareAgreement(me, asker, CodeshareAgreement.CodeshareType.BothWays));
            while (offers.Count > 5) offers.RemoveAt(0);
            GameObject.GetInstance().NewsBox.AddNews(
                new News(
                    News.NewsType.AllianceNews,
                    GameObject.GetInstance().GameTime,
                    "Codeshare offer",
                    asker.Profile.Name + " has asked you for a codeshare agreement. Answer it under Carriers > Codeshares."));
        }

        public static void AnswerOffer(Carrier me, CodeshareAgreement offer, bool accept, out string message)
        {
            var partner = Partner(offer, me);
            Offers(me).Remove(offer);
            if (!accept)
            {
                message = "You turned down " + partner.Profile.Name + ".";
                return;
            }
            if (Find(me, partner) != null)
            {
                message = "You already codeshare with " + partner.Profile.Name + ".";
                return;
            }
            offer.Carrier1.AddCodeshareAgreement(offer);
            offer.Carrier2.AddCodeshareAgreement(offer);
            message = "You now codeshare with " + partner.Profile.Name + ".";
        }
    }
}
