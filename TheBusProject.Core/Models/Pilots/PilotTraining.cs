using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Pilots
{
    //the class for a pilot on training
    [Serializable]
    public class PilotTraining : BaseModel
    {
        #region Constructors and Destructors

        public PilotTraining(string coachfamily, DateTime enddate)
        {
            EndDate = enddate;
            CoachFamily = coachfamily;
        }

        private PilotTraining(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
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