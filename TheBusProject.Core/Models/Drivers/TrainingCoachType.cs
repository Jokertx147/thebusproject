using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Drivers
{
    //the class for the coach for training for students
    [Serializable]
    public class TrainingCoachType : BaseModel
    {
        #region Constructors and Destructors

        public TrainingCoachType(string name, double price, int maxnumberofstudents, int typelevel)
        {
            Name = name;
            APrice = price;
            TypeLevel = typelevel;
            MaxNumberOfStudents = maxnumberofstudents;
        }

        private TrainingCoachType(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("price")]
        public double APrice { get; set; }

        [Versioning("maxstudents")]
        public int MaxNumberOfStudents { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        public double Price => GeneralHelpers.GetInflationPrice(APrice);

        [Versioning("level")]
        public int TypeLevel { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }

    //the list of training coaches types
    public class TrainingCoachTypes
    {
        #region Static Fields

        private static readonly List<TrainingCoachType> Types = new List<TrainingCoachType>();

        #endregion

        #region Public Methods and Operators

        public static void AddCoachType(TrainingCoachType type)
        {
            Types.Add(type);
        }

        //returns all coach types

        //clears the list of coaches 
        public static void Clear()
        {
            Types.Clear();
        }

        public static TrainingCoachType GetCoachType(string name)
        {
            return Types.Find(t => t.Name == name);
        }

        public static List<TrainingCoachType> GetCoachTypes()
        {
            return Types;
        }

        #endregion

        //adds a type to the list
    }
}