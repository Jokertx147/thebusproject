using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers.CarrierCooperation
{
    //the class for a cooperation
    [Serializable]
    public class Cooperation : BaseModel
    {
        #region Constructors and Destructors

        public Cooperation(CooperationType type, Carrier carrier, DateTime built)
        {
            BuiltDate = built;
            Carrier = carrier;
            Type = type;
        }

        private Cooperation(SerializationInfo info, StreamingContext ctxt) :base(info, ctxt)
        {

        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("built")]
        public DateTime BuiltDate { get; set; }

        [Versioning("type")]
        public CooperationType Type { get; set; }

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