using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Models.Coaches
{
    //the class for the statistics for an coach
    [Serializable]
    public class CoachStatistics : GeneralStatistics
    {
        #region Fields

        [Versioning("coach")] private readonly FleetCoach _coach;

        #endregion

        #region Constructors and Destructors

        public CoachStatistics(FleetCoach coach)
        {
            _coach = coach;
        }

        private CoachStatistics(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        public double Balance => GetBalance();

        public double FillingDegree => GetFillingDegree();

        public double IncomePerPassenger => GetIncomePerPassenger();

        #endregion

        #region Public Methods and Operators

        public new void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion

        #region Methods

        private double GetBalance()
        {
            return GetStatisticsValue(StatisticsTypes.GetStatisticsType("Coach_Income"));
        }

        //get the degree of filling
        private double GetFillingDegree()
        {
            double avgPassengers = GetStatisticsValue(StatisticsTypes.GetStatisticsType("Passengers"))
                                   /GetStatisticsValue(StatisticsTypes.GetStatisticsType("Arrivals"));

            double totalPassengers = Convert.ToDouble(_coach.Coach.GetTotalSeatCapacity());

            double fillingDegree = avgPassengers/totalPassengers;

            if (totalPassengers.Equals(0))
            {
                return 0;
            }
            return avgPassengers/totalPassengers;
        }

        //gets the income per passenger
        private double GetIncomePerPassenger()
        {
            double totalPassengers =
                Convert.ToDouble(GetStatisticsValue(StatisticsTypes.GetStatisticsType("Passengers")));

            if (totalPassengers.Equals(0))
            {
                return 0;
            }
            return GetBalance()/totalPassengers;
        }

        #endregion

        //gets the balance
    }
}