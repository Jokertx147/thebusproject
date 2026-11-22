using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Coaches
{
    //the class for saving an coach schedule
    [Serializable]
    public class CoachScheduleConfiguration : Configuration
    {
        public CoachScheduleConfiguration(string name, CoachType type) : base(ConfigurationType.CoachSchedule, name, false)
        {
            CoachType = type;
            Entries = new List<RouteTimeTableEntry>();
        }

        private CoachScheduleConfiguration(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        [Versioning("coachtype")]
        public CoachType CoachType { get; set; }

        [Versioning("entries")]
        public List<RouteTimeTableEntry> Entries { get; set; }

        //adds an entry to the configuration
        public void AddEntry(RouteTimeTableEntry entry)
        {
            Entries.Add(entry);
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }
    }
}