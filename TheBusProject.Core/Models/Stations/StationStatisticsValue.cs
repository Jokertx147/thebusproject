using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Models.Stations
{
    //the class for an station statistics value
    [Serializable]
    public class StationStatisticsValue : StatisticsValue
    {
        #region Constructors and Destructors

        public StationStatisticsValue(Carrier carrier, int year, StatisticsType stat, int value)
            : base(year, stat, value)
        {
            Carrier = carrier;
        }

        public StationStatisticsValue(Carrier carrier, int year, StatisticsType stat)
            : this(carrier, year, stat, 0)
        {
        }

        private StationStatisticsValue(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        #endregion

        #region Public Methods and Operators

        public new void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}