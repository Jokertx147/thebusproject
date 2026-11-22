using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Models.Coaches
{
    /*! Coach class.
 * This class is used for a passenger class onboard of a coach
 * The class needs parameters for the coach, type of class and the seating capacity
 */

    [Serializable]
    public class CoachClass : BaseModel
    {
        #region Constructors and Destructors

        public CoachClass(ClassType type, int seatingCapacity)
        {
            Type = type;
            SeatingCapacity = seatingCapacity;
            RegularSeatingCapacity = seatingCapacity;
            Facilities = new Dictionary<CoachFacility.FacilityType, CoachFacility>();
        }

        private CoachClass(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum ClassType
        {
            [EnumMember(Value = "Economy")] EconomyClass = 1921,

            [EnumMember(Value = "Business")] BusinessClass = 1976,

            [EnumMember(Value = "First")] FirstClass = 1960
        }

        #endregion

        #region Public Properties

        public List<CoachFacility> AllFacilities => GetFacilities();

        [Versioning("facilities")]
        public Dictionary<CoachFacility.FacilityType, CoachFacility> Facilities { get; set; }

        [Versioning("regularseating")]
        public int RegularSeatingCapacity { get; set; }

        [Versioning("seats")]
        public int SeatingCapacity { get; set; }

        [Versioning("type")]
        public ClassType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public static List<ClassType> GetCoachTypes()
        {
            var types = new List<ClassType> {ClassType.EconomyClass, ClassType.BusinessClass, ClassType.FirstClass};

            return types;
        }

        public void CreateBasicFacilities(Carrier carrier)
        {
            foreach (CoachFacility.FacilityType type in Enum.GetValues(typeof (CoachFacility.FacilityType)))
            {
                SetFacility(carrier, CoachFacilities.GetBasicFacility(type));
                //this.Facilities.Add(type, CoachFacilities.GetBasicFacility(type));
            }
        }

        public void ForceSetFacility(CoachFacility facility)
        {
            Facilities[facility.Type] = facility;
        }

        public List<CoachFacility> GetFacilities()
        {
            return Facilities.Values.ToList();
        }

        public CoachFacility GetFacility(CoachFacility.FacilityType type)
        {
            if (Facilities.ContainsKey(type))
            {
                return Facilities[type];
            }
            return null;
        }

        public void SetFacility(Carrier carrier, CoachFacility facility)
        {
            Facilities[facility.Type] = facility;

            if (carrier != null)
            {
                CarrierHelpers.AddCarrierInvoice(
                    carrier,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.Purchases,
                    -facility.PricePerSeat*facility.PercentOfSeats/100.0*SeatingCapacity);
            }
        }

        #endregion

        //returns all coach types in the correct order
    }
}