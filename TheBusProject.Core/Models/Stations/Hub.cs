using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Models.Stations
{
    [Serializable]
    //the class for a hub at an station
    public class Hub : BaseModel
    {
        #region Static Fields

        public static StationFacility MinimumServiceFacility => StationFacilities.GetFacility("Basic ServiceCenter");
        //StationFacilities.GetFacility("Large ServiceCenter");

        #endregion

        #region Constructors and Destructors

        public Hub(Carrier carrier, HubType type)
        {
            Carrier = carrier;
            Type = type;
        }

        private Hub(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("type")]
        public HubType Type { get; set; }

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