using System.Collections.Generic;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Models.General
{
    //the object for the start data
    public class StartDataObject
    {
        #region Public Properties

        public Carrier Carrier { get; set; }

        public Station Station { get; set; }

        public string CEO { get; set; }

        public Continent Continent { get; set; }

        public DifficultyLevel Difficulty { get; set; }

        public CarrierFocus Focus { get; set; }

        public Country HomeCountry { get; set; }

        public bool IsPaused { get; set; }

        public bool LocalCurrency { get; set; }

        public bool MajorStations { get; set; }

        public bool InternationalStations { get; set; }

        public int NumberOfOpponents { get; set; }

        public List<Carrier> Opponents { get; set; }

        public bool RandomOpponents { get; set; }

        public bool RealData { get; set; }

        public Region Region { get; set; }

        public bool SameRegion { get; set; }

        public GameTimeZone TimeZone { get; set; }

        public bool UseDayTurns { get; set; }

        public int Year { get; set; }

        public List<Country> SelectedCountries { get; set; }

        #endregion
    }
}