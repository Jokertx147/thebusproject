using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers
{
    [Serializable]
    //the member of an alliance
    public class AllianceMember : BaseModel
    {
        #region Constructors and Destructors

        public AllianceMember(Carrier carrier, DateTime joinedDate)
        {
            Carrier = carrier;
            JoinedDate = joinedDate;
            ExitedDate = new DateTime(2199, 1, 1);
        }

        private AllianceMember(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("existed")]
        public DateTime ExitedDate { get; set; }

        [Versioning("joined")]
        public DateTime JoinedDate { get; set; }

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