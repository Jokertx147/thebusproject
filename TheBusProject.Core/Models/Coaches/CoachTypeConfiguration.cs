using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.General;

namespace TheBusProject.Models.Coaches
{
    //the configuration for a configuration for an coach type
    [Serializable]
    public class CoachTypeConfiguration : Configuration
    {
        #region Constructors and Destructors

        public CoachTypeConfiguration(string name, CoachType type, Period<DateTime> period, Boolean standard)
            : base(ConfigurationType.CoachType, name, standard)
        {
            Coach = type;
            Period = period;
            Classes = new List<CoachClassConfiguration>();
        }

        //returns the number of classes

        private CoachTypeConfiguration(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("coach")]
        public CoachType Coach { get; set; }

        [Versioning("classes")]
        public List<CoachClassConfiguration> Classes { get; set; }

        [Versioning("period")]
        public Period<DateTime> Period { get; set; }

        #endregion

        #region Public Methods and Operators

        public new void GetObjectData(SerializationInfo info, StreamingContext context)
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
}