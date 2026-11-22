using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Pilots
{
    //the class for the rating for a pilot
    [Serializable]
    public class PilotRating : BaseModel, IComparable<PilotRating>
    {
        #region Constructors and Destructors

        public PilotRating(string name, int trainingdays, int costindex)
        {
            Name = name;
            TrainingDays = trainingdays;
            CostIndex = costindex;
            Coaches = new List<TrainingCoachType>();
        }

        //adds a training coach which the rating (pilot) uses in training

        private PilotRating(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("trainingcoaches")]
        public List<TrainingCoachType> Coaches { get; set; }

        [Versioning("costindex")]
        public int CostIndex { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("trainingdays")]
        public int TrainingDays { get; set; }

        #endregion

        #region Public Methods and Operators

        public int CompareTo(PilotRating other)
        {
            return CostIndex.CompareTo(other.CostIndex);
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public void AddCoach(TrainingCoachType coach)
        {
            Coaches.Add(coach);
        }

        #endregion
    }

    //the list of pilot ratings
    public class PilotRatings
    {
        #region Static Fields

        private static List<PilotRating> _ratings = new List<PilotRating>();

        #endregion

        #region Public Methods and Operators

        public static void AddRating(PilotRating rating)
        {
            _ratings.Add(rating);
        }

        public static void Clear()
        {
            _ratings = new List<PilotRating>();
        }

        //returns the rating with a name
        public static PilotRating GetRating(string name)
        {
            return _ratings.FirstOrDefault(r => r.Name == name);
        }

        //returns the list of ratings 
        public static List<PilotRating> GetRatings()
        {
            return _ratings;
        }

        #endregion

        //adds a rating to the list

        //clears the pilot ratings
    }
}