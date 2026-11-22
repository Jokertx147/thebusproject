using System;
using System.Globalization;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;

namespace TheBusProject.Models.Carriers
{
    [Serializable]
    public class CarrierInsurance
    {
        public enum InsuranceScope
        {
            Station,
            Domestic,
            Hub,
            Global
        }

        public enum InsuranceType
        {
            PublicLiability,
            PassengerLiability,
            CombinedSingleLimit,
            FullCoverage
        }

        public enum PaymentTerms
        {
            Annual,
            Biannual,
            Quarterly,
            Monthly
        }

        public CarrierInsurance(InsuranceType insType, InsuranceScope insScope, PaymentTerms paymentTerms, int insAmount)
        {
            Deductible = 0;
            TermLength = 0;
            CancellationFee = 0;
            InsuredAmount = insAmount;
            InsType = insType;
            InsScope = insScope;
            InsTerms = paymentTerms;
        }

        public InsuranceType InsType { get; set; }

        public InsuranceScope InsScope { get; set; }

        public PaymentTerms InsTerms { get; set; }

        public int InsuredAmount { get; set; }

        public double Deductible { get; set; }

        public double TermLength { get; set; }

        public double PaymentAmount { get; set; }

        public int CancellationFee { get; set; }

        public string PolicyIndex { get; set; }

        public bool AllFleetCoaches { get; set; }

        public DateTime InsuranceEffective { get; set; }
        public DateTime InsuranceExpires { get; set; }
        public DateTime NextPaymentDue { get; set; }
        public double RemainingPayments { get; set; }
    }

    [Serializable]
    public class InsuranceClaim
    {
        public InsuranceClaim(Carrier carrier, FleetCoach coach, Station station, DateTime date, int damage)
        {
            Damage = damage;
            Carrier = carrier;
            Coach = coach;
            Station = station;
            Date = date;
            Index = GameObject.GetInstance().GameTime.ToString(CultureInfo.InvariantCulture) + carrier + damage.ToString(CultureInfo.InvariantCulture);
            SettlementDate = MathHelpers.GetRandomDate(GameObject.GetInstance().GameTime.AddMonths(2), GameObject.GetInstance().GameTime.AddMonths(24));
        }

        public Carrier Carrier { get; set; }
        public CarrierInsurance Policy { get; set; }

        public int Damage { get; set; }

        public DateTime Date { get; set; }
        public FleetCoach Coach { get; set; }
        public Station Station { get; set; }

        public DateTime SettlementDate { get; set; }

        public string Index { get; set; }
    }
}