using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.Drivers
{
    //the class for a trip school
    [Serializable]
    public class TripSchool : BaseModel
    {
        #region Constants

        public const int MaxNumberOfInstructors = 15;

        public const int MaxNumberOfStudentsPerInstructor = 2;

        #endregion

        #region Constructors and Destructors

        public TripSchool(Station station)
        {
            Guid id = Guid.NewGuid();

            Station = station;
            Name = $"Trip School {Station.Profile.Town.Name}";
            Students = new List<DriverStudent>();
            Instructors = new List<Instructor>();
            TrainingCoaches = new List<TrainingCoach>();
            ID = id.ToString();
        }

        private TripSchool(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            Students.RemoveAll(s => s == null);
        }

        #endregion

        #region Public Properties

        [Versioning("station")]
        public Station Station { get; set; }

        [Versioning("id")]
        public string ID { get; set; }

        [Versioning("instructors")]
        public List<Instructor> Instructors { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        public int NumberOfInstructors => Instructors.Count;

        public int NumberOfStudents => Students.Count;

        [Versioning("students")]
        public List<DriverStudent> Students { get; set; }

        [Versioning("coaches")]
        public List<TrainingCoach> TrainingCoaches { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //adds an instructor to the trip school
        public void AddInstructor(Instructor instructor)
        {
            Instructors.Add(instructor);
        }

        //removes an instructor from the trip school

        //adds a student to the trip school
        public void AddStudent(DriverStudent student)
        {
            Students.Add(student);
        }

        public void AddTrainingCoach(TrainingCoach coach)
        {
            TrainingCoaches.Add(coach);
        }

        public void RemoveInstructor(Instructor instructor)
        {
            Instructors.Remove(instructor);
        }

        //removes a student from the trip school
        public void RemoveStudent(DriverStudent student)
        {
            Students.Remove(student);
        }

        public void RemoveTrainingCoach(TrainingCoach coach)
        {
            TrainingCoaches.Remove(coach);
        }

        #endregion
    }
}