using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers.Subsidiary
{
    [Serializable]
    //the class for a merger between two carriers either as a regular merger or where one of them gets subsidiary of the other
    public class CarrierMerger : BaseModel
    {
        #region Constructors and Destructors

        public CarrierMerger(string name, Carrier carrier1, Carrier carrier2, DateTime date, MergerType type)
        {
            Name = name;
            Carrier1 = carrier1;
            Carrier2 = carrier2;
            Date = date;
            Type = type;
        }

        private CarrierMerger(SerializationInfo info, StreamingContext ctxt) : base(info,ctxt)
        {
            
        }

        #endregion

        #region Enums

        public enum MergerType
        {
            Merger,

            Subsidiary
        }

        #endregion

        #region Public Properties

        public Carrier Carrier1 { get; set; }

        public Carrier Carrier2 { get; set; }

        public DateTime Date { get; set; }

        public string Name { get; set; }

        public string NewName { get; set; }

        public MergerType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info,context);
        }

        #endregion
    }

    //the list of carrier mergers 
    public class CarrierMergers
    {
        #region Static Fields

        private static readonly List<CarrierMerger> Mergers = new List<CarrierMerger>();

        #endregion

        #region Public Methods and Operators

        public static void AddCarrierMerger(CarrierMerger merger)
        {
            Mergers.Add(merger);
        }

        public static void Clear()
        {
            Mergers.Clear();
        }

        //returns all mergers
        public static List<CarrierMerger> GetCarrierMergers()
        {
            return Mergers;
        }

        //returns all mergers for a specific date
        public static List<CarrierMerger> GetCarrierMergers(DateTime date)
        {
            return Mergers.FindAll(m => m.Date.ToShortDateString() == date.ToShortDateString());
        }

        //removes a merger from the list
        public static void RemoveCarrierMerger(CarrierMerger merger)
        {
            Mergers.Remove(merger);
        }

        #endregion

        //adds a merger to the list of mergers

        //clears the list of mergers
    }
}