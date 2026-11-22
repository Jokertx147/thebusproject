using System;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Coaches
{
    //the contract for an carrier for a manufacturer
    [Serializable]
    public class ManufacturerContract : BaseModel
    {
        #region Constructors and Destructors

        public ManufacturerContract(Manufacturer manufacturer, DateTime date, int length, double discount)
        {
            Manufacturer = manufacturer;
            Coaches = length;
            SigningDate = date;
            Length = length;
            Discount = discount;
            ExpireDate = date.AddYears(Length);
            PurchasedCoaches = 0;
        }

        //returns the termination fee for the contract

        private ManufacturerContract(SerializationInfo info, StreamingContext ctxt) :base(info,ctxt)
        {
            
        }

        #endregion

        #region Public Properties

        [Versioning("coaches")]
        public int Coaches { get; set; }

        [Versioning("discount")]
        public double Discount { get; set; }

        [Versioning("expiredate")]
        public DateTime ExpireDate { get; set; }

        [Versioning("length")]
        public int Length { get; set; }

        [Versioning("manufacturer")]
        public Manufacturer Manufacturer { get; set; }

        [Versioning("purchasedcoaches")]
        public int PurchasedCoaches { get; set; }

        [Versioning("signingdate")]
        public DateTime SigningDate { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info,context);
        }

        public double GetDiscount()
        {
            if (Length <= 3)
            {
                Discount = (PurchasedCoaches/2) + 1;
            }
            else if (Length <= 5)
            {
                Discount = (PurchasedCoaches/2) + 2;
            }
            else if (Length <= 7)
            {
                Discount = (PurchasedCoaches/2) + 4;
            }
            else if (Length <= 15)
            {
                Discount = (PurchasedCoaches/2) + 7;
            }
            else
            {
                Discount = 1;
            }

            return Discount;
        }

        public double GetTerminationFee()
        {
            return GeneralHelpers.GetInflationPrice(Length*1000000);
        }

        #endregion
    }
}