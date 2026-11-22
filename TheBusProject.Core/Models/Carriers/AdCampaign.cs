using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers
{
    /// <summary>A time-limited advertising campaign aimed at one country or one station.</summary>
    [Serializable]
    public class AdCampaign : BaseModel
    {
        public AdCampaign(string target, bool isStation, int level, DateTime start, DateTime end, double monthlyCost)
        {
            Target = target;
            IsStation = isStation;
            Level = level;
            Start = start;
            End = end;
            MonthlyCost = monthlyCost;
        }

        private AdCampaign(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        /// <summary>The station's IATA code or the country's id.</summary>
        [Versioning("target")]
        public string Target { get; set; }

        [Versioning("isstation")]
        public bool IsStation { get; set; }

        /// <summary>1 small, 2 medium, 3 large.</summary>
        [Versioning("level")]
        public int Level { get; set; }

        [Versioning("start")]
        public DateTime Start { get; set; }

        [Versioning("end")]
        public DateTime End { get; set; }

        [Versioning("monthlycost")]
        public double MonthlyCost { get; set; }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);
            base.GetObjectData(info, context);
        }
    }
}
