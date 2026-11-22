//locked for verison 0.3.6t2 (this serves no purpose whatsoever)

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Scenarios;

namespace TheBusProject.Models.General
{
    [Serializable]
    //the class for the game object
    public class GameObject : BaseModel, INotifyPropertyChanged
    {
        #region Constants

        public const int StartYear = 1960;

        #endregion

        #region Static Fields

        private static GameObject _gameInstance;

        #endregion

        #region Fields

        [Versioning("gametime")] private DateTime _gameTime;

        [Versioning("humanmoney")] private double _humanMoney;

        #endregion

        #region Constructors and Destructors

        private GameObject()
        {
            //this.PassengerDemandFactor = 100;
            GameTime = new DateTime(2007, 12, 31, 10, 0, 0);
            TimeZone =
                TimeZones.GetTimeZones()
                         .Find(gtz => gtz.UTCOffset == new TimeSpan(0, 0, 0));
            Difficulty = DifficultyLevels.GetDifficultyLevel("Easy");
            NewsBox = new NewsBox();
            PagePerformanceCounterEnabled = false;
            FinancePageEnabled = false;
            DayRoundEnabled = true;
            Contracts = new ObservableCollection<SpecialContractType>();
        }

        private GameObject(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
                Contracts = new ObservableCollection<SpecialContractType>();
        }

        #endregion

        #region Public Events

        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Public Properties

        [Versioning("currencycountry")]
        public Country CurrencyCountry { get; set; }

        /// <summary>How much trips cost (see TripCosts): 0 original game, 1 realistic, 2 tough.</summary>
        [Versioning("costlevel")]
        public int CostLevel { get; set; } = 1;

        [Versioning("dayroundenabled")]
        public Boolean DayRoundEnabled { get; set; }

        [Versioning("difficulty")]
        public DifficultyLevel Difficulty { get; set; }

        public Boolean FinancePageEnabled { get; set; }
        /*
        public double FuelPrice 
        {
            get
            {
                return this._fuelprice;
            }
            set
            {
                this._fuelprice = value;
                this.NotifyPropertyChanged("FuelPrice");
            }
        }
        */

        public DateTime GameTime
        {
            get { return _gameTime; }
            set
            {
                _gameTime = value;
                NotifyPropertyChanged("GameTime");
            }
        }

        [Versioning("contracts", Version = 2)]
        public ObservableCollection<SpecialContractType> Contracts { get; set; }

        //public DateTime GameTime { get; set; }

        [Versioning("humancarrier")]
        public Carrier HumanCarrier { get; private set; }

        /// <summary>The player's money: always the human carrier's money (kept apart only before there is one).</summary>
        public double HumanMoney
        {
            get { return HumanCarrier != null ? HumanCarrier.Money : _humanMoney; }
            set
            {
                _humanMoney = value;
                if (HumanCarrier != null)
                {
                    HumanCarrier.Money = value; // notifies through the carrier
                }
                else
                {
                    NotifyPropertyChanged("HumanMoney");
                }
            }
        }

        /// <summary>Tells bound views (the Windows top bar) that the player's money changed.</summary>
        public void RaiseHumanMoneyChanged()
        {
            NotifyPropertyChanged("HumanMoney");
        }

        [Versioning("maincarrier")]
        public Carrier MainCarrier { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("newsbox")]
        public NewsBox NewsBox { get; set; }

        public Boolean PagePerformanceCounterEnabled { get; set; }

        [Versioning("scenario")]
        public ScenarioObject Scenario { get; set; }

        [Versioning("startdate")]
        public DateTime StartDate { get; set; }

        public long StartMoney => GetStartMoney();

        [Versioning("timezone")]
        public GameTimeZone TimeZone { get; set; }

        [Versioning("fuelprice")]
        public double FuelPrice { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 2);

            base.GetObjectData(info, context);
        }

        /// <summary>The running game, or null (unlike GetInstance, never creates one).</summary>
        public static GameObject PeekInstance()
        {
            return _gameInstance;
        }

        public static GameObject GetInstance()
        {
            return _gameInstance ?? (_gameInstance = new GameObject());
        }

        //sets the instance to an instance

        //restarts the instance
        public static void RestartInstance()
        {
            _gameInstance = new GameObject();
        }

        public static void SetInstance(GameObject instance)
        {
            _gameInstance = instance;
        }

        public void AddHumanMoney(double value)
        {
            if (HumanCarrier != null) HumanCarrier.AddMoney(value);
            else HumanMoney += value;
        }

        public void SetHumanCarrier(Carrier carrier)
        {
            HumanCarrier = carrier;
            HumanMoney = carrier.Money;
        }

        #endregion

        #region Methods

        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private long GetStartMoney()
        {
            double baseStartMoney = 7500000;

            baseStartMoney *= Difficulty.MoneyLevel;

            return Convert.ToInt64(GeneralHelpers.GetInflationPrice(baseStartMoney));
        }

        #endregion

        //returns the game instance
    }
}