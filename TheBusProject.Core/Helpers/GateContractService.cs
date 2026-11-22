using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>Managing the player's station gate contracts: extend, auto renew, more or fewer gates, end.</summary>
    public static class GateContractService
    {
        public static readonly int[] Lengths = { 1, 2, 3, 5, 10, 15, 20 };

        /// <summary>All of the carrier's station contracts.</summary>
        public static List<StationContract> GetContracts(Carrier carrier)
        {
            return Stations.GetAllStations()
                           .SelectMany(a => a.CarrierContracts.ToList())
                           .Where(c => c.Carrier == carrier)
                           .ToList();
        }

        public static string TypeName(StationContract.ContractType type)
        {
            switch (type)
            {
                case StationContract.ContractType.Full: return "Gates only";
                case StationContract.ContractType.LowService: return "Low service";
                case StationContract.ContractType.MediumService: return "Medium service";
                case StationContract.ContractType.FullService: return "Full service";
                default: return type.ToString();
            }
        }

        public static int MonthsLeft(StationContract c)
        {
            var now = GameObject.GetInstance().GameTime;
            return Math.Max(0, (c.ExpireDate.Year - now.Year)*12 + c.ExpireDate.Month - now.Month);
        }

        /// <summary>The penalty for ending a contract early: a tenth of the rent still to pay.</summary>
        public static double EndPenalty(StationContract c)
        {
            return c.YearlyPayment/12*MonthsLeft(c)/10;
        }

        public static int RoutesUsing(StationContract c)
        {
            return StationHelpers.GetStationRoutes(c.Station, c.Carrier).Count;
        }

        /// <summary>The yearly rent after extending by some years, at today's prices (longer contracts are cheaper per year).</summary>
        public static double ExtendedPayment(StationContract c, int years)
        {
            if (c.Terminal != null) return c.YearlyPayment;
            return StationHelpers.GetYearlyContractPayment(c.Station, c.Type, c.NumberOfGates, years);
        }

        public static bool Extend(StationContract c, int years, out string message)
        {
            if (years < 1 || years > 30)
            {
                message = "Pick between 1 and 30 years.";
                return false;
            }
            var now = GameObject.GetInstance().GameTime;
            DateTime from = c.ExpireDate > now ? c.ExpireDate : now;
            c.ExpireDate = from.AddYears(years);
            c.Length = years;
            c.YearlyPayment = ExtendedPayment(c, years);
            message = string.Format(
                "The contract at {0} now runs until {1}, at {2} a year.",
                c.Station.Profile.IATACode, c.ExpireDate.ToShortDateString(), new ValueCurrencyFormatter().Convert(c.YearlyPayment));
            return true;
        }

        public static void SetAutoRenew(StationContract c, bool on)
        {
            c.AutoRenew = on;
        }

        public static int SetAutoRenewAll(Carrier carrier, bool on)
        {
            int n = 0;
            foreach (var c in GetContracts(carrier).Where(c => c.AutoRenew != on))
            {
                c.AutoRenew = on;
                n++;
            }
            return n;
        }

        /// <summary>The extra yearly rent for more gates on the contract.</summary>
        public static double AddGatesPrice(StationContract c, int gates)
        {
            return StationHelpers.GetYearlyContractPayment(c.Station, c.Type, gates, Math.Max(1, c.Length));
        }

        public static string GetAddGatesBlocker(StationContract c, int gates)
        {
            if (gates < 1) return "Pick at least one gate.";
            if (c.Terminal != null) return "Gates in your own terminal are added by extending the terminal.";
            int free = c.Station.Terminals.GetFreeGates(c.TerminalType);
            if (free < gates) return free == 0 ? "There are no free gates at " + c.Station.Profile.IATACode + "." : "Only " + free + " gate(s) are free.";
            return null;
        }

        public static bool AddGates(StationContract c, int gates, out string message)
        {
            message = GetAddGatesBlocker(c, gates);
            if (message != null) return false;
            c.YearlyPayment += AddGatesPrice(c, gates);
            c.NumberOfGates += gates;
            MarkGates(c.Station, c.Carrier, c.TerminalType, gates, true);
            message = string.Format("{0} gate(s) added at {1}. The contract now has {2} gates.", gates, c.Station.Profile.IATACode, c.NumberOfGates);
            return true;
        }

        public static string GetRemoveGatesBlocker(StationContract c, int gates)
        {
            if (gates < 1) return "Pick at least one gate.";
            if (c.Terminal != null) return "Gates in your own terminal can't be given back.";
            if (gates >= c.NumberOfGates) return "Keep at least one gate, or end the contract instead.";

            // would the trips still fit with fewer gates?
            c.NumberOfGates -= gates;
            try
            {
                var mine = c.Station.CarrierContracts.Where(x => x.Carrier == c.Carrier).ToList();
                if (!StationHelpers.CanFillRoutesEntries(c.Station, c.Carrier, mine, Weather.Season.AllYear))
                    return "Your trips at " + c.Station.Profile.IATACode + " would no longer fit with fewer gates.";
            }
            finally
            {
                c.NumberOfGates += gates;
            }
            return null;
        }

        public static bool RemoveGates(StationContract c, int gates, out string message)
        {
            message = GetRemoveGatesBlocker(c, gates);
            if (message != null) return false;
            double share = (double) gates/c.NumberOfGates;
            c.YearlyPayment -= c.YearlyPayment*share;
            c.NumberOfGates -= gates;
            MarkGates(c.Station, c.Carrier, c.TerminalType, gates, false);
            message = string.Format("{0} gate(s) given back at {1}. The contract now has {2} gates.", gates, c.Station.Profile.IATACode, c.NumberOfGates);
            return true;
        }

        public static string GetEndBlocker(StationContract c)
        {
            if (c.Terminal != null) return "This contract belongs to your own terminal and can't be ended.";
            var others = c.Station.CarrierContracts.Where(a => a.Carrier == c.Carrier && a != c).ToList();
            if (!StationHelpers.CanFillRoutesEntries(c.Station, c.Carrier, others, Weather.Season.AllYear))
                return "Your trips at " + c.Station.Profile.IATACode + " would no longer fit. Remove or move those trips first.";
            return null;
        }

        public static bool End(StationContract c, out string message)
        {
            message = GetEndBlocker(c);
            if (message != null) return false;
            double penalty = EndPenalty(c);
            CarrierHelpers.AddCarrierInvoice(c.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -penalty);
            c.Station.RemoveCarrierContract(c);
            MarkGates(c.Station, c.Carrier, c.TerminalType, c.NumberOfGates, false);
            message = "Contract at " + c.Station.Profile.IATACode + " ended (penalty " + new ValueCurrencyFormatter().Convert(penalty) + ").";
            return true;
        }

        // gates carry the carrier that uses them; the free gate count itself comes from the contracts
        private static void MarkGates(Station station, Carrier carrier, Terminal.TerminalType type, int count, bool take)
        {
            var gates = station.Terminals.GetDeliveredTerminals()
                               .Where(t => t.Type == type)
                               .SelectMany(t => t.Gates.GetGates())
                               .ToList();
            if (gates.Count == 0) gates = station.Terminals.GetGates();
            for (int i = 0; i < count; i++)
            {
                var gate = take ? gates.FirstOrDefault(g => g.Carrier == null) : gates.LastOrDefault(g => g.Carrier == carrier);
                if (gate == null) break;
                gate.Carrier = take ? carrier : null;
            }
        }

        /// <summary>A new contract with the chosen number of gates and years (the route creator signs 2 gates for 2 years).</summary>
        public static bool Sign(Carrier carrier, Station station, StationContract.ContractType type, Terminal.TerminalType terminalType, int gates, int years, out string message)
        {
            int free = station.Terminals.GetFreeGates(terminalType);
            if (gates < 1 || free < gates)
            {
                message = free == 0 ? "There are no free gates at " + station.Profile.IATACode + "." : "Only " + free + " gate(s) are free.";
                return false;
            }
            var now = GameObject.GetInstance().GameTime;
            if (type == StationContract.ContractType.Full)
            {
                bool cargo = terminalType == Terminal.TerminalType.Cargo;
                var facilityType = cargo ? StationFacility.FacilityType.Cargo : StationFacility.FacilityType.CheckIn;
                if (station.GetStationFacility(carrier, facilityType, cargo).TypeLevel == 0)
                {
                    var basic = StationFacilities.GetFacilities(facilityType).Find(f => f.TypeLevel == 1);
                    station.AddStationFacility(carrier, basic, now);
                    CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Purchases, -basic.Price);
                }
            }
            double yearly = StationHelpers.GetYearlyContractPayment(station, type, gates, years);
            StationHelpers.AddCarrierContract(new StationContract(carrier, station, type, terminalType, now, gates, years, yearly, true));
            MarkGates(station, carrier, terminalType, gates, true);
            message = string.Format(
                "Leased {0} gate(s) at {1} for {2} year(s), {3} a year. It renews by itself.",
                gates, station.Profile.IATACode, years, new ValueCurrencyFormatter().Convert(yearly));
            return true;
        }
    }
}
