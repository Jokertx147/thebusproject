using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Drivers
{
    //the class for a driver on training
    [Serializable]
    public class DriverTraining : BaseModel
    {
        #region Constructors and Destructors

        public DriverTraining(string coachfamily, DateTime enddate)
        {
            EndDate = enddate;
            CoachFamily = coachfamily;
        }

        private DriverTraining(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("coach")]
        public string CoachFamily { get; set; }

        [Versioning("date")]
        public DateTime EndDate { get; set; }

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