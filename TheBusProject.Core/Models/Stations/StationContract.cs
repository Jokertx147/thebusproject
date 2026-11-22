using System;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;

namespace TheBusProject.Models.Stations
{
    [Serializable]
    //the class for a contract at an station for an carrier
    public class StationContract : BaseModel
    {
        #region Constructors and Destructors

        public StationContract(
            Carrier carrier,
            Station station,
            ContractType type,
            Terminal.TerminalType terminaltype,
            DateTime date,
            int numberOfGates,
            int length,
            double yearlyPayment,
            bool autorenew,
            bool payFull = false,
            bool isExclusiveDeal = false,
            Terminal terminal = null)
        {
            Type = type;
            PayFull = payFull;
            Carrier = carrier;
            Station = station;
            ContractDate = date;
            Length = length;
            YearlyPayment = yearlyPayment;
            NumberOfGates = numberOfGates;
            IsExclusiveDeal = isExclusiveDeal;
            Terminal = terminal;
            ExpireDate = ContractDate.AddYears(Length);
            AutoRenew = autorenew;
            TerminalType = terminaltype;
        }

        private StationContract(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                Type = ContractType.Full;
            }
            if (Version == 2)
            {
                AutoRenew = true;
            }
            if (Version < 4)
                TerminalType = Terminal.TerminalType.Passenger;
        }

        #endregion

        #region Enums

        public enum ContractType
        {
            Full,

            FullService,

            MediumService,

            LowService
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("station")]
        public Station Station { get; set; }

        [Versioning("renew", Version = 3)]
        public Boolean AutoRenew { get; set; }

        [Versioning("date")]
        public DateTime ContractDate { get; set; }

        [Versioning("expire")]
        public DateTime ExpireDate { get; set; }

        [Versioning("isexclusive")]
        public Boolean IsExclusiveDeal { get; set; }

        [Versioning("terminaltype", Version = 4)]
        public Terminal.TerminalType TerminalType { get; set; }

        [Versioning("length")]
        public int Length { get; set; }

        public int MonthsLeft => GetMonthsLeft();

        [Versioning("gates")]
        public int NumberOfGates { get; set; }

        [Versioning("payfull")]
        public Boolean PayFull { get; set; }

        [Versioning("terminal")]
        public Terminal Terminal { get; set; }

        [Versioning("type", Version = 2)]
        public ContractType Type { get; set; }

        [Versioning("yearlypayment")]
        public double YearlyPayment { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 4);

            base.GetObjectData(info, context);
        }

        public int GetMonthsLeft()
        {
            return MathHelpers.GetMonthsBetween(
                GameObject.GetInstance().GameTime,
                ContractDate.AddYears(Length));
        }

        #endregion
    }
}