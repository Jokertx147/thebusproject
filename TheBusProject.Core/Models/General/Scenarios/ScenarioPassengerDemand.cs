using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Models.General.Scenarios
{
    //the class for passenger demand at a scenario
    [Serializable]
    public class ScenarioPassengerDemand : BaseModel
    {
        #region Constructors and Destructors

        public ScenarioPassengerDemand(double factor, DateTime enddate, Country country, Station station)
        {
            Country = country;
            Factor = factor;
            EndDate = enddate;
            Station = station;
        }

        private ScenarioPassengerDemand(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("station")]
        public Station Station { get; set; }

        [Versioning("country")]
        public Country Country { get; set; }

        [Versioning("enddate")]
        public DateTime EndDate { get; set; }

        [Versioning("factor")]
        public double Factor { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}