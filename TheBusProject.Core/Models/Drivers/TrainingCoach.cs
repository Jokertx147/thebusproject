using System;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Drivers
{
    //the class for a training coach
    [Serializable]
    public class TrainingCoach : BaseModel
    {
        #region Constructors and Destructors

        public TrainingCoach(TrainingCoachType type, DateTime boughtDate, TripSchool flighschool)
        {
            Type = type;
            TripSchool = flighschool;
            BoughtDate = boughtDate;
        }

        private TrainingCoach(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        public int Age => MathHelpers.GetAge(BoughtDate);

        [Versioning("bought")]
        public DateTime BoughtDate { get; set; }

        [Versioning("flighschool")]
        public TripSchool TripSchool { get; set; }

        [Versioning("type")]
        public TrainingCoachType Type { get; set; }

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