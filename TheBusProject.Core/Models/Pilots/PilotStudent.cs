using System;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Pilots
{
    //the class for a pilot student
    [Serializable]
    public class PilotStudent : BaseModel
    {
        #region Constants

        public const double StudentCost = 33381.69;

        #endregion

        #region Constructors and Destructors

        public PilotStudent(
            PilotProfile profile,
            DateTime startDate,
            Instructor instructor,
            PilotRating rating,
            string coachfamily)
        {
            Rating = rating;
            Profile = profile;
            StartDate = startDate;
            EndDate = StartDate.AddDays(Rating.TrainingDays);
            Instructor = instructor;
            CoachFamily = coachfamily;
        }

        private PilotStudent(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                Rating = GeneralHelpers.GetPilotStudentRating(this);
            }
            if (Version < 3)
            {
                Coach = null;
                CoachFamily = "";
            }
        }

        #endregion

        #region Public Properties

        [Versioning("coach", Version = 3)]
        public TrainingCoach Coach { get; set; }

        [Versioning("coachfamily", Version = 3)]
        public string CoachFamily { get; set; }

        [Versioning("enddate")]
        public DateTime EndDate { get; set; }

        [Versioning("instructor")]
        public Instructor Instructor { get; set; }

        [Versioning("profile")]
        public PilotProfile Profile { get; set; }

        [Versioning("rating", Version = 2)]
        public PilotRating Rating { get; set; }

        [Versioning("startdate")]
        public DateTime StartDate { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}