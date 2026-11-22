using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers
{
    [Serializable]
    //the class for a policy for an carrier
    public class CarrierPolicy : BaseModel
    {
        #region Constructors and Destructors

        public CarrierPolicy(string name, object policyValue)
        {
            Name = name;
            PolicyValue = policyValue;
        }

        private CarrierPolicy(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("value")]
        public object PolicyValue { get; set; }

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