using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Coaches
{
    //the configuration of an coach 
    [Serializable]
    public class CoachConfiguration : Configuration
    {
        #region Constructors and Destructors

        public CoachConfiguration(string name, int minimumSeats, Boolean standard)
            : base(ConfigurationType.Coach, name, standard)
        {
            MinimumSeats = minimumSeats;
            Classes = new List<CoachClassConfiguration>();
        }

        //returns the number of classes

        private CoachConfiguration(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("classes")]
        public List<CoachClassConfiguration> Classes { get; set; }

        [Versioning("minseats")]
        public int MinimumSeats { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public void AddClassConfiguration(CoachClassConfiguration conf)
        {
            Classes.Add(conf);
        }

        public int GetNumberOfClasses()
        {
            return Classes.Count;
        }

        #endregion
    }

    //the configuration of an coach class
    [Serializable]
    public class CoachClassConfiguration : BaseModel
    {
        #region Constructors and Destructors

        public CoachClassConfiguration(CoachClass.ClassType type, int seating, int regularseating)
        {
            SeatingCapacity = seating;
            RegularSeatingCapacity = regularseating;
            Type = type;
            Facilities = new List<CoachFacility>();
        }

        private CoachClassConfiguration(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("facilities")]
        public List<CoachFacility> Facilities { get; set; }

        [Versioning("regularseating")]
        public int RegularSeatingCapacity { get; set; }

        [Versioning("seatingcapacity")]
        public int SeatingCapacity { get; set; }

        [Versioning("type")]
        public CoachClass.ClassType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //adds a facility to the configuration
        public void AddFacility(CoachFacility facility)
        {
            Facilities.Add(facility);
        }

        //returns all facilities
        public List<CoachFacility> GetFacilities()
        {
            return Facilities;
        }

        //returns the facility of a specific type
        public CoachFacility GetFacility(CoachFacility.FacilityType type)
        {
            return Facilities.Find(f => f.Type == type);
        }

        #endregion
    }
}