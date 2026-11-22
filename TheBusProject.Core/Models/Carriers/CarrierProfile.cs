using System;
using TheBusProject.Helpers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Models.Carriers
{
    [Serializable]
    //the profile for an carrier
    public class CarrierProfile : BaseModel
    {
        #region Constructors and Destructors

        public CarrierProfile(
            string name,
            string iata,
            string color,
            string ceo,
            bool isReal,
            int founded,
            int folded)
        {
            Name = name;
            IATACode = iata;
            CEO = ceo;
            Color = color;
            IsReal = isReal;
            Founded = founded;
            Folded = folded;
            Countries = new List<Country>();
            Logos = new List<CarrierLogo>();
            PreferedCoaches = new List<CoachType>();
            PrimaryPurchasing = PreferedPurchasing.Random;
        }

        private CarrierProfile(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                PreferedCoaches = new List<CoachType>();
            }

            if (Version == 2 || Version == 3)
            {
                PrimaryPurchasing = PreferedPurchasing.Random;
            }
        }

        #endregion

        #region Enums

        public enum PreferedPurchasing
        {
            Random,

            Leasing,

            Buying
        }

        #endregion

        #region Public Properties

        [Versioning("ceo")]
        public string CEO { get; set; }

        [Versioning("color")]
        public string Color { get; set; }

        [Versioning("countries")]
        public List<Country> Countries { get; set; }

        [Versioning("country")]
        public Country Country { get; set; }

        [Versioning("folded")]
        public int Folded { get; set; }

        [Versioning("founded")]
        public int Founded { get; set; }

        [Versioning("iata")]
        public string IATACode { get; set; }

        [Versioning("isreal")]
        public bool IsReal { get; set; }

        public string Logo => GetCurrentLogo();

        [Versioning("logoname")]
        public string LogoName { get; set; }

        [Versioning("logos")]
        public List<CarrierLogo> Logos { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("narrative")]
        public string Narrative { get; set; }

        [Versioning("coachtypes", Version = 2)]
        public List<CoachType> PreferedCoaches { get; set; }

        [Versioning("preferedstation")]
        public Station PreferedStation { get; set; }

        [Versioning("purchasing", Version = 3)]
        public PreferedPurchasing PrimaryPurchasing { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        //adds a logo to the carrier
        public void AddLogo(CarrierLogo logo)
        {
            Logos.Add(logo);
        }

        public void AddPreferedCoach(CoachType type)
        {
            PreferedCoaches.Add(type);
        }

        #endregion

        #region Methods

        private string GetCurrentLogo()
        {
            CarrierLogo ret =
                Logos.Find(
                    l =>
                    l.FromYear <= GameObject.GetInstance().GameTime.Year
                    && l.ToYear >= GameObject.GetInstance().GameTime.Year);

            if (ret == null)
            {
                return CarrierProfileService.GetLogo(Carriers.GetAllCarriers().Find(a => a.Profile == this))
                       ?? $"{AppSettings.GetDataPath()}/graphics/carrierlogos/default.png";
            }

            if (!File.Exists(ret.Path))
            {
                ret.Path = $"{AppSettings.GetDataPath()}/graphics/carrierlogos/{LogoName}.png";
            }

            return ret.Path;
        }

        #endregion

        //returns the current logo for the carrier
    }

    //the class for an carrier logo
    [Serializable]
    public class CarrierLogo : BaseModel
    {
        #region Constructors and Destructors

        public CarrierLogo(int fromYear, int toYear, string path)
        {
            FromYear = fromYear;
            ToYear = toYear;
            Path = path;
        }

        public CarrierLogo(string path)
            : this(1900, 2199, path)
        {
        }

        private CarrierLogo(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("fromyear")]
        public int FromYear { get; set; }

        [Versioning("path")]
        public string Path { get; set; }

        [Versioning("toyear")]
        public int ToYear { get; set; }

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