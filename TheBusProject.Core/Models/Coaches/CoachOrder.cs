using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Coaches
{
    //the class for an coach order
    [Serializable]
    public class CoachOrder : BaseModel
    {
        #region Constructors and Destructors

        public CoachOrder(CoachType type, List<CoachClass> classes, int amount, bool customConfiguration)
        {
            Type = type;
            Amount = amount;
            Classes = classes;
            CustomConfiguration = customConfiguration;
        }

        private CoachOrder(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("amount")]
        public int Amount { get; set; }

        [Versioning("classes")]
        public List<CoachClass> Classes { get; set; }

        [Versioning("custom")]
        public bool CustomConfiguration { get; set; }

        [Versioning("type")]
        public CoachType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}