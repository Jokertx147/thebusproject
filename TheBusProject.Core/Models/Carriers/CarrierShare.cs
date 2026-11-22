using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers
{
    //the class for a share in an carrier
    [Serializable]
    public class CarrierShare : BaseModel
    {
        #region Constructors and Destructors

        public CarrierShare(Carrier carrier, double price)
        {
            Price = price;
            Carrier = carrier;
        }

        private CarrierShare(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("price")]
        public double Price { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion

        //ændres til antal, percent per carrier
    }
}