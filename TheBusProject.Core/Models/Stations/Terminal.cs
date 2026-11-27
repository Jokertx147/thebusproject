using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Stations
{
    // chs, 2011-27-10 added for the possibility of purchasing a terminal
    /*!
     * Class for a terminal at an station
     * Constructor needs parameter for station, owner (carrier), date of delivery and number of gates
     **/
    // chs, 2011-27-10 changed so a terminal has a devilery date
    [Serializable]
    public class Terminal : BaseModel
    {
        #region Constructors and Destructors

        public Terminal(Station station, string name, int gates, DateTime deliveryDate, TerminalType type)
            : this(station, null, name, gates, deliveryDate, type)
        {
        }

        public Terminal(Station station, Carrier carrier, string name, int gates, DateTime deliveryDate, TerminalType type)
        {
            Station = station;
            Carrier = carrier;
            Name = name;
            DeliveryDate = new DateTime(deliveryDate.Year, deliveryDate.Month, deliveryDate.Day);
            Type = type;

            Gates = new Gates(gates, DeliveryDate, carrier);
        }

        private Terminal(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
                Type = TerminalType.Passenger;
        }

        #endregion

        #region Public Properties

        public enum TerminalType
        {
            Passenger
        }

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("station")]
        public Station Station { get; set; }

        [Versioning("delivery")]
        public DateTime DeliveryDate { get; set; }

        [Versioning("gates")]
        public Gates Gates { get; set; }

        [Versioning("type", Version = 2)]
        public TerminalType Type { get; set; }

        public bool IsBuilt => GameObject.GetInstance().GameTime > DeliveryDate;

        public bool IsBuyable
        {
            get
            {
                int freeGates = Station.Terminals.GetFreeGates(Type);

                return freeGates > Gates.NumberOfGates && Station.Terminals.GetNumberOfStationTerminals(Type) > 1
                       && Carrier == null;
            }
        }

        [Versioning("name")]
        public string Name { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 2);

            base.GetObjectData(info, context);
        }

        // chs 11-04-11: changed for the possibility of extending a terminal
        //extends a terminal with a number of gates
        public void ExtendTerminal(int gates)
        {
            DateTime deliveryDate = GameObject.GetInstance().GameTime.AddDays(gates*10);
            for (int i = 0; i < gates; i++)
            {
                var gate = new Gate(deliveryDate) {Carrier = Carrier};

                Gates.AddGate(gate);
            }
        }

        //returns if the terminal has been built

        //returns the number of free gates
        public int GetFreeGates()
        {
            if (Carrier != null)
            {
                return Gates.NumberOfGates;
            }
            int terminalIndex =
                Station.Terminals.StationTerminals.Where(
                    a => a.Carrier == null && a.Type == Type && a.DeliveryDate <= GameObject.GetInstance().GameTime)
                       .ToList()
                       .IndexOf(this);

            int terminalGates =
                Station.Terminals.StationTerminals.Where(
                    a => a.Carrier != null && a.Type == Type && a.DeliveryDate <= GameObject.GetInstance().GameTime)
                       .Sum(t => t.Gates.NumberOfGates);

            int contracts = Station.CarrierContracts.Where(c => c.TerminalType == Type).Sum(c => c.NumberOfGates) - terminalGates;

            int gates = 0;

            int i = 0;

            List<Terminal> freeTerminals =
                Station.Terminals.StationTerminals.Where(
                    a => a.Carrier == null && a.Type == Type && a.DeliveryDate <= GameObject.GetInstance().GameTime).ToList();

            // when the contracts need more gates than the free terminals have, every free terminal counts as used
            while (gates < contracts && i < freeTerminals.Count)
            {
                gates += freeTerminals[i].Gates.NumberOfGates;

                if (gates < contracts)
                {
                    i++;
                }
            }

            if (terminalIndex > i || contracts == 0)
            {
                return Gates.NumberOfGates;
            }

            if (terminalIndex < i)
            {
                return 0;
            }

            if (terminalIndex == i)
            {
                return gates - contracts;
            }

            return Gates.NumberOfGates;
        }

        public void PurchaseTerminal(Carrier carrier)
        {
            Carrier = carrier;

            double yearlyPayment = StationHelpers.GetYearlyContractPayment(
                Station,
                StationContract.ContractType.Full,
                Gates.NumberOfGates,
                20);

            StationHelpers.AddCarrierContract(
                new StationContract(
                    Carrier,
                    Station,
                    StationContract.ContractType.Full,
                    Type,
                    GameObject.GetInstance().GameTime,
                    Gates.NumberOfGates,
                    20,
                    yearlyPayment*0.60,
                    true));
        }

        #endregion
    }

    //the collection of terminals at an station
    [Serializable]
    public class Terminals : BaseModel
    {
        #region Fields

        [Versioning("terminals")] public List<Terminal> StationTerminals;

        #endregion

        #region Constructors and Destructors

        public Terminals(Station station)
        {
            Station = station;
            StationTerminals = new List<Terminal>();
        }

        private Terminals(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("station")]
        public Station Station { get; set; }

        public int NumberOfFreeGates => GetFreeGates();

        public int NumberOfGates => GetNumberOfGates();

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public void AddTerminal(Terminal terminal)
        {
            StationTerminals.Add(terminal);
        }

        public void Clear()
        {
            foreach (Terminal terminal in StationTerminals)
            {
                terminal.Gates.Clear();
            }

            StationTerminals = new List<Terminal>();
        }

        //returns all terminals

        //returns all delivered terminals
        public List<Terminal> GetDeliveredTerminals()
        {
            return
                StationTerminals.FindAll(
                    (terminal => terminal.DeliveryDate <= GameObject.GetInstance().GameTime));
        }

        public int GetFreeGates()
        {
            return GetFreeGates(Terminal.TerminalType.Passenger);
        }

        public int GetFreeGates(Terminal.TerminalType type)
        {
            return GetNumberOfGates(type) - GetInuseGates(type);
        }

        public double GetFreeSlotsPercent(Carrier carrier, Terminal.TerminalType type)
        {
            const double numberOfSlots = (22 - 6)*4*7; //from 06.00 to 22.00 each quarter each day (7 days a week) 

            double usedSlots = StationHelpers.GetOccupiedSlotTimes(Station, carrier, Weather.Season.AllYear, type).Count;

            double percent = ((numberOfSlots - usedSlots)/numberOfSlots)*100;

            return percent;
        }

        //returns the list of gates
        public List<Gate> GetGates()
        {
            return GetDeliveredTerminals().SelectMany(terminal => terminal.Gates.GetGates()).ToList();
        }

        public List<Gate> GetGates(Carrier carrier)
        {
            return GetDeliveredTerminals().SelectMany(terminal => terminal.Gates.GetGates().Where(a => a.Carrier != null && a.Carrier == carrier)).ToList();
        }

        //adds a terminal to the list

        //returns the number of gates in use
        public int GetInuseGates(Terminal.TerminalType type)
        {
            return
                Station.CarrierContracts.Where(c => c.ContractDate <= GameObject.GetInstance().GameTime && c.TerminalType == type)
                       .Sum(c => c.NumberOfGates);
        }

        public double GetInusePercent(Terminal.TerminalType type)
        {
            int freeGates = GetFreeGates(type);
            int totalGates = GetNumberOfGates(type);

            int usedGates = totalGates - freeGates;

            if (usedGates > 0)
            {
/*
                freeGates = 12;
*/
            }

            double inusePercent = Convert.ToDouble(usedGates)/Convert.ToDouble(totalGates)*100.0;

            return inusePercent;
        }

        public int GetNumberOfStationTerminals()
        {
            return
                GetDeliveredTerminals()
                    .FindAll((terminal => terminal.Carrier == null))
                    .Count;
        }

        public int GetNumberOfStationTerminals(Terminal.TerminalType terminaltype)
        {
            return GetDeliveredTerminals().Count(t => t.Type == terminaltype);
        }

        //returns the number of free gates

        //returns the total number of gates
        public int GetNumberOfGates(Terminal.TerminalType type)
        {
            return StationTerminals.Where(t => t.Type == type).Sum(t => t.Gates.NumberOfDeliveredGates);
        }

        public int GetNumberOfGates()
        {
            return GetNumberOfGates(Terminal.TerminalType.Passenger);
        }

        //returns the number of gates for an carrier
        public int GetNumberOfGates(Carrier carrier)
        {
            Terminal.TerminalType type = Terminal.TerminalType.Passenger;
            return
                Station.CarrierContracts.Where(
                    c => c.Carrier == carrier && c.TerminalType == type && c.ContractDate <= GameObject.GetInstance().GameTime)
                       .Sum(c => c.NumberOfGates);
        }

        public int GetOrdereredGates()
        {
            return StationTerminals.Sum(a => a.Gates.NumberOfOrderedGates)
                   + StationTerminals.FindAll(a => a.DeliveryDate > GameObject.GetInstance().GameTime)
                                     .Sum(t => t.Gates.NumberOfGates);
        }

        public List<Terminal> GetTerminals()
        {
            return StationTerminals;
        }

        //returns the percent of free gate slots for the station

        //returns if an carrier has terminal
        public Boolean HasTerminal(Carrier carrier)
        {
            return StationTerminals.Any(terminal => terminal.Carrier == carrier);
        }

        public void RemoveTerminal(Terminal terminal)
        {
            StationTerminals.Remove(terminal);
        }

        public void SwitchCarrier(Carrier carrierFrom, Carrier carrierTo)
        {
            List<StationContract> contracts = Station.GetCarrierContracts(carrierFrom);

            foreach (StationContract contractFrom in contracts)
            {
                contractFrom.Carrier = carrierTo;

                for (int i = 0; i < contractFrom.NumberOfGates; i++)
                {
                    Gate gate = contractFrom.Station.Terminals.GetGates().FirstOrDefault(g => g.Carrier == carrierFrom);
                    if (gate != null) gate.Carrier = carrierTo;
                }
            }

            carrierFrom.RemoveStation(Station);

            if (!carrierTo.Stations.Contains(Station))
            {
                carrierTo.AddStation(Station);
            }
        }

        #endregion
    }
}