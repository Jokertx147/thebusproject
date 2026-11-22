using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Drivers
{
    //the class for a trip school instructor
    [Serializable]
    public class Instructor : BaseModel
    {
        #region Constructors and Destructors

        public Instructor(DriverProfile profile, DriverRating rating)
        {
            Profile = profile;
            Rating = rating;
            Students = new List<DriverStudent>();
        }

        private Instructor(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                Rating = GeneralHelpers.GetDriverRating();
            }
        }

        #endregion

        #region Public Properties

        [Versioning("tripschool")]
        public TripSchool TripSchool { get; set; }

        [Versioning("profile")]
        public DriverProfile Profile { get; set; }

        [Versioning("rating")]
        public DriverRating Rating { get; set; }

        [Versioning("students")]
        public List<DriverStudent> Students { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 2);

            base.GetObjectData(info, context);
        }

        //adds a student to the instructor
        public void AddStudent(DriverStudent student)
        {
            Students.Add(student);
        }

        //removes a students from the instructor
        public void RemoveStudent(DriverStudent student)
        {
            Students.Remove(student);
        }

        #endregion
    }

    //the list of trip school instructors
    public class Instructors
    {
        #region Static Fields

        private static readonly List<Instructor> instructors = new List<Instructor>();

        #endregion

        #region Public Methods and Operators

        public static void AddInstructor(Instructor instructor)
        {
            instructors.Add(instructor);
        }

        public static void Clear()
        {
            instructors.Clear();
        }

        //returns all instructors
        public static List<Instructor> GetInstructors()
        {
            return instructors;
        }

        public static int GetNumberOfUnassignedInstructors()
        {
            return instructors.FindAll(i => i.TripSchool == null).Count;
        }

        //returns all instructors not assigned to a trip school
        public static List<Instructor> GetUnassignedInstructors()
        {
            return instructors.FindAll(i => i.TripSchool == null);
        }

        //clears the list of instructors

        //removes an instructor from the list
        public static void RemoveInstructor(Instructor instructor)
        {
            instructors.Remove(instructor);
        }

        #endregion

        //adds an instructor to the list

        //counts the number of unassigned instructors
    }
}