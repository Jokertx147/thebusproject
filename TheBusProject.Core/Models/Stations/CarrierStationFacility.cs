using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Models.Stations
{
    [Serializable]
    //the class for the an station facility for an carrier
    public class CarrierStationFacility : BaseModel
    {
        #region Constructors and Destructors

        public CarrierStationFacility(Carrier carrier, Station station, StationFacility facility, DateTime date)
        {
            Carrier = carrier;
            Facility = facility;
            FinishedDate = date;
            Station = station;
        }

        private CarrierStationFacility(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("station")]
        public Station Station { get; set; }

        [Versioning("facility")]
        public StationFacility Facility { get; set; }

        [Versioning("finished")]
        public DateTime FinishedDate { get; set; }

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