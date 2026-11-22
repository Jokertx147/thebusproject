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
    /// <summary>
    ///     Buying gates from the computer carriers: an carrier gives up gates it doesn't need for its trips,
    ///     and the buyer pays it a transfer fee and gets a normal gate contract for them.
    /// </summary>
    public static class GateTrading
    {
        //a gate is sold for one and a half years of the standard gate rent, to the carrier that gives it up
        public static double GetPricePerGate(Station station)
        {
            return 1.5 * StationHelpers.GetYearlyContractPayment(station, StationContract.ContractType.Full, 1, 2);
        }

        //true when the contract is one that can be bought from: a computer carrier's passenger gates, no own terminal
        public static bool IsTradable(Station station, StationContract contract)
        {
            return contract != null
                   && !contract.Carrier.IsHuman
                   && contract.TerminalType == Terminal.TerminalType.Passenger
                   && contract.Terminal == null
                   && !contract.IsExclusiveDeal
                   && contract.NumberOfGates > 0
                   && station.CarrierContracts.Contains(contract)
                   && !station.HasHub(contract.Carrier);
        }

        //how many of the contract's gates the carrier can give up and still fly everything it flies from the station
        public static int GetSpareGates(Station station, StationContract contract)
        {
            if (!IsTradable(station, contract))
            {
                return 0;
            }

            Carrier seller = contract.Carrier;

            int total = station.GetCarrierContracts(seller)
                               .Where(c => c.TerminalType == Terminal.TerminalType.Passenger)
                               .Sum(c => c.NumberOfGates);

            int ownedGates = station.Terminals.GetGates().Count(g => g.Carrier == seller);

            try
            {
                bool hasRoutes = StationHelpers.GetStationRoutes(station, seller).Count > 0;

                if (!hasRoutes)
                {
                    return Math.Min(contract.NumberOfGates, ownedGates);
                }

                // the busiest 15 minutes of the carrier: how many of its routes use the station at the same time
                var everySlot = new StationContract(
                    seller,
                    station,
                    StationContract.ContractType.Full,
                    Terminal.TerminalType.Passenger,
                    GameObject.GetInstance().GameTime,
                    1,
                    1,
                    0,
                    false);

                int busiest = StationHelpers
                    .GetOccupiedSlotTimes(station, seller, new List<StationContract> { everySlot }, Weather.Season.AllYear)
                    .GroupBy(t => t.Ticks)
                    .Select(g => g.Count())
                    .DefaultIfEmpty(0)
                    .Max();

                // the rule of CanFillRoutesEntries: with one gate no two routes may meet, with more gates fewer routes than gates
                int minimum = busiest <= 1 ? 1 : busiest + 1;

                return Math.Max(0, Math.Min(Math.Min(contract.NumberOfGates, ownedGates), total - minimum));
            }
            catch (Exception)
            {
                // an carrier with an unusual timetable is simply not selling
                return 0;
            }
        }

        //buys gates from a computer carrier; returns false with a message that says why not
        public static bool BuyGates(
            Station station,
            StationContract sellerContract,
            int gates,
            StationContract.ContractType type,
            out string message)
        {
            Carrier buyer = GameObject.GetInstance().HumanCarrier;
            DateTime now = GameObject.GetInstance().GameTime;

            if (gates < 1)
            {
                message = "Pick how many gates you want to buy.";
                return false;
            }

            int spare = GetSpareGates(station, sellerContract);

            if (spare < gates)
            {
                message = spare == 0
                              ? sellerContract.Carrier.Profile.Name + " needs all of its gates for its trips."
                              : sellerContract.Carrier.Profile.Name + " can only spare " + spare + " gate(s).";
                return false;
            }

            double price = GetPricePerGate(station) * gates;

            if (price > buyer.Money)
            {
                message = "You can't afford this: the gates cost " + price.ToString("N0") + ".";
                return false;
            }

            Carrier seller = sellerContract.Carrier;

            //take the gates from the seller
            var taken = station.Terminals.GetGates().Where(g => g.Carrier == seller).Take(gates).ToList();

            if (taken.Count < gates)
            {
                message = seller.Profile.Name + " doesn't hold that many gates.";
                return false;
            }

            double perGateRent = sellerContract.YearlyPayment / sellerContract.NumberOfGates;
            sellerContract.NumberOfGates -= gates;
            sellerContract.YearlyPayment = perGateRent * sellerContract.NumberOfGates;

            if (sellerContract.NumberOfGates <= 0)
            {
                station.RemoveCarrierContract(sellerContract);
            }

            CarrierHelpers.AddCarrierInvoice(buyer, now, Invoice.InvoiceType.Purchases, -price);
            CarrierHelpers.AddCarrierInvoice(seller, now, Invoice.InvoiceType.Rents, price);

            //the buyer gets a normal two year contract for them
            bool hasCheckin = station.GetStationFacility(buyer, StationFacility.FacilityType.CheckIn).TypeLevel > 0;

            if (!hasCheckin && type == StationContract.ContractType.Full)
            {
                StationFacility checkin =
                    StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);

                station.AddStationFacility(buyer, checkin, now);
                CarrierHelpers.AddCarrierInvoice(buyer, now, Invoice.InvoiceType.Purchases, -checkin.Price);
            }

            var contract = new StationContract(
                buyer,
                station,
                type,
                Terminal.TerminalType.Passenger,
                now,
                gates,
                2,
                StationHelpers.GetYearlyContractPayment(station, type, gates, 2),
                true);

            StationHelpers.AddCarrierContract(contract);

            foreach (Gate gate in taken)
            {
                gate.Carrier = buyer;
            }

            message = string.Format("Bought {0} gate(s) from {1} for {2:N0}.", gates, seller.Profile.Name, price);
            return true;
        }
    }
}
