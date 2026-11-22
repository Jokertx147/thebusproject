using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers
{
    //the class for a codeshare agreement - if one way then it is from carrier1 to carrier2 (carrier1 codeshares its routes with carrier2)
    [Serializable]
    public class CodeshareAgreement : BaseModel
    {
        #region Constants

        public const double TicketSalePercent = 1;

        #endregion

        #region Constructors and Destructors

        public CodeshareAgreement(Carrier carrier1, Carrier carrier2, CodeshareType type)
        {
            Carrier1 = carrier1;
            Carrier2 = carrier2;
            Type = type;
        }

        private CodeshareAgreement(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum CodeshareType
        {
            OneWay,

            BothWays
        }

        #endregion

        #region Public Properties

        [Versioning("carrier1")]
        public Carrier Carrier1 { get; set; }

        [Versioning("carrier2")]
        public Carrier Carrier2 { get; set; }

        [Versioning("type")]
        public CodeshareType Type { get; set; }

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