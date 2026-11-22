using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Drivers
{
    //the class for the rating for a driver
    [Serializable]
    public class DriverRating : BaseModel, IComparable<DriverRating>
    {
        #region Constructors and Destructors

        public DriverRating(string name, int trainingdays, int costindex)
        {
            Name = name;
            TrainingDays = trainingdays;
            CostIndex = costindex;
            Coaches = new List<TrainingCoachType>();
        }

        //adds a training coach which the rating (driver) uses in training

        private DriverRating(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
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

        public int CompareTo(DriverRating other)
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

    //the list of driver ratings
    public class DriverRatings
    {
        #region Static Fields

        private static List<DriverRating> _ratings = new List<DriverRating>();

        #endregion

        #region Public Methods and Operators

        public static void AddRating(DriverRating rating)
        {
            _ratings.Add(rating);
        }

        public static void Clear()
        {
            _ratings = new List<DriverRating>();
        }

        //returns the rating with a name
        public static DriverRating GetRating(string name)
        {
            return _ratings.FirstOrDefault(r => r.Name == name);
        }

        //returns the list of ratings 
        public static List<DriverRating> GetRatings()
        {
            return _ratings;
        }

        #endregion

        //adds a rating to the list

        //clears the driver ratings
    }
}