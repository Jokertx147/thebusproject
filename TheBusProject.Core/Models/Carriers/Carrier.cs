using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Pilots;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Carriers
{
    [Serializable]
    //the class for an carrier
    public class Carrier : BaseModel
    {
        private List<KeyValuePair<DateTime, KeyValuePair<double, double>>> _dailyOperatingBalanceHistory;

        #region Constructors and Destructors

        public Carrier(
            CarrierProfile profile,
            CarrierMentality mentality,
            CarrierFocus marketFocus,
            CarrierLicense license,
            Route.RouteType routeFocus)
        {
            Scores = new CarrierScores();
            Shares = new List<CarrierShare>();
            Stations = new List<Station>();
            Fleet = new List<FleetCoach>();
            Routes = new List<Route>();
            FutureCarriers = new List<FutureSubsidiaryCarrier>();
            Subsidiaries = new List<SubsidiaryCarrier>();
            Advertisements = new Dictionary<AdvertisementType.CarrierAdvertisementType, AdvertisementType>();
            Codeshares = new List<CodeshareAgreement>();
            Statistics = new GeneralStatistics();
            Facilities = new List<CarrierFacility>();
            Invoices = new Invoices();
            Budget = new CarrierBudget();
            BudgetHistory = new Dictionary<DateTime, CarrierBudget>();
            TestBudget = new Dictionary<DateTime, CarrierBudget>();
            Profile = profile;
            CarrierRouteFocus = routeFocus;
            Loans = new List<Loan>();
            Reputation = 50;
            Alliances = new List<Alliance>();
            Mentality = mentality;
            MarketFocus = marketFocus;
            License = license;
            Policies = new List<CarrierPolicy>();
            EventLog = new List<RandomEvent>();
            Ratings = new CarrierRatings();
            OverallScore = CountedScores = 0;
            GameScores = new Dictionary<DateTime, int>();
            InsuranceClaims = new List<InsuranceClaim>();
            InsurancePolicies = new List<CarrierInsurance>();
            SpecialContracts = new List<SpecialContract>();

            CreateStandardAdvertisement();

            Pilots = new List<Pilot>();
            TripSchools = new List<TripSchool>();
            AutoAssignPilots = true;
            Budget = new CarrierBudget();
        }

        protected Carrier(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            Shares = new List<CarrierShare>();

            if (Version == 1)
            {
                CarrierHelpers.CreateStandardCarrierShares(this, 100);
            }
            if (Version < 4)
            {
                Routes = new List<Route>();
                Advertisements =
                    new Dictionary<AdvertisementType.CarrierAdvertisementType, AdvertisementType>();
                CreateStandardAdvertisement();
            }
            if (Version < 5)
                SpecialContracts = new List<SpecialContract>();

            if (Invoices == null)
            {
                Invoices = new Invoices();
            }
        }

        #endregion

        #region Enums

        public enum CarrierLicense
        {
            Domestic,

            Regional,

            ShortHaul,

            LongHaul
        }

        public enum CarrierMentality
        {
            Safe,

            Moderate,

            Aggressive
        }

        public enum CarrierValue
        {
            VeryLow,

            Low,

            Normal,

            High,

            VeryHigh
        }

        #endregion

        #region Public Properties

        [Versioning("advertisements")]
        public Dictionary<AdvertisementType.CarrierAdvertisementType, AdvertisementType> Advertisements { get; set; }

        [Versioning("routefocus")]
        public Route.RouteType CarrierRouteFocus { get; set; }

        //0-100 with 0-9 as very_low, 10-30 as low, 31-70 as normal, 71-90 as high,91-100 as very_high 

        [Versioning("stations")]
        public List<Station> Stations { get; set; }

        [Versioning("alliances")]
        public List<Alliance> Alliances { get; set; }

        [Versioning("avgfleetvalue")]
        public long AvgFleetValue { get; set; }

        [Versioning("budget")]
        public CarrierBudget Budget { get; set; }

        [Versioning("budgethistory")]
        public IDictionary<DateTime, CarrierBudget> BudgetHistory { get; set; }

        [Versioning("codeshares", Version = 3)]
        public List<CodeshareAgreement> Codeshares { get; set; }

        /// <summary>Codeshare offers from computer carriers that the player has not answered yet.</summary>
        /// <summary>Advertising campaigns aimed at a country or an station.</summary>
        [Versioning("adcampaigns")]
        public List<AdCampaign> AdCampaigns { get; set; } = new List<AdCampaign>();

        [Versioning("codeshareoffers")]
        public List<CodeshareAgreement> CodeshareOffers { get; set; } = new List<CodeshareAgreement>();

        [Versioning("contract")]
        public ManufacturerContract Contract { get; set; }

        [Versioning("countedscores")]
        public int CountedScores { get; set; }

        public List<FleetCoach> DeliveredFleet => GetDeliveredFleet();

        [Versioning("eventlists")]
        public List<RandomEvent> EventList { get; set; }

        [Versioning("eventlog")]
        public List<RandomEvent> EventLog { get; set; }

        [Versioning("facilities")]
        public List<CarrierFacility> Facilities { get; set; }

        private CarrierFees _fees;

        // created on first use, so an carrier never has null fees
        [Versioning("fees")]
        public CarrierFees Fees
        {
            get { return _fees ?? (_fees = new CarrierFees()); }
            set { _fees = value; }
        }

        [Versioning("fleet")]
        public List<FleetCoach> Fleet { get; set; }

        [Versioning("fleetvalue")]
        public long FleetValue { get; set; }

        /// <summary>When on, pilots without an coach (new graduates, pilots back from training) are put on coach that need a pilot of their type.</summary>
        [Versioning("autoassignpilots")]
        public bool AutoAssignPilots { get; set; }

        /// <summary>When on, an idle coach that is scheduled to fly from another station gets a ferry trip there first (it costs fuel and takes time).</summary>
        [Versioning("autoferry")]
        public bool AutoFerry { get; set; } = true;

        /// <summary>What the carrier pays its pilots, as a percent of the market salary (100 = market).</summary>
        [Versioning("pilotpaypercent")]
        public double PilotPayPercent { get; set; } = 100;

        [Versioning("lastpilotbonus")]
        public DateTime LastPilotBonus { get; set; }

        [Versioning("tripschools")]
        public List<TripSchool> TripSchools { get; set; }

        [Versioning("futurecarriers")]
        public List<FutureSubsidiaryCarrier> FutureCarriers { get; set; }

        [Versioning("gamescores")]
        public Dictionary<DateTime, int> GameScores { get; set; }

        [Versioning("insuranceclaims")]
        public List<InsuranceClaim> InsuranceClaims { get; set; }

        [Versioning("insurancepolicies")]
        public List<CarrierInsurance> InsurancePolicies { get; set; }

        [Versioning("invoices")]
        public Invoices Invoices { get; set; }

        public bool IsHuman => isHuman();

        public bool IsSubsidiary => IsSubsidiaryCarrier();

        [Versioning("license")]
        public CarrierLicense License { get; set; }

        [Versioning("loans")]
        public List<Loan> Loans { get; set; }

        [Versioning("marketfocus")]
        public CarrierFocus MarketFocus { get; set; }

        [Versioning("mentality")]
        public CarrierMentality Mentality { get; set; }

        private double _money;

        [Versioning("money")]
        public double Money
        {
            get { return _money; }
            set
            {
                _money = value;
                // the player's money follows the human carrier's
                var game = GameObject.PeekInstance();
                if (game != null && ReferenceEquals(game.HumanCarrier, this)) game.RaiseHumanMoneyChanged();
            }
        }

        [Versioning("overallscore")]
        public int OverallScore { get; set; }

        [Versioning("pilots")]
        public List<Pilot> Pilots { get; set; }

        [Versioning("policies")]
        public List<CarrierPolicy> Policies { get; set; }

        [Versioning("profile")]
        public CarrierProfile Profile { get; set; }

        [Versioning("ratings")]
        public CarrierRatings Ratings { get; set; }

        [Versioning("reputation")]
        public int Reputation { get; set; }

        public List<Route> Routes
        {
            get { return GetRoutes(); }
            set { _Routes = value; }
        }

        [Versioning("scores")]
        public CarrierScores Scores { get; set; }

        // the old share list (10,000 objects an carrier), no longer used: Holdings has who owns what. Not saved (older
        // saves have it: it is skipped on loading), and no longer filled when a game starts.
        [NonSerialized]
        private List<CarrierShare> _shares;

        public List<CarrierShare> Shares
        {
            get { return _shares ?? (_shares = new List<CarrierShare>()); }
            set { _shares = value; }
        }

        /// <summary>Who owns the carrier's shares (see SharesService). Unlike Shares this is kept in the save.</summary>
        [Versioning("holdings")]
        public List<ShareHolding> Holdings { get; set; } = new List<ShareHolding>();

        [Versioning("startmoney")]
        public double StartMoney { get; set; }

        [Versioning("statistics")]
        public GeneralStatistics Statistics { get; set; }

        [Versioning("subsidiaries")]
        public List<SubsidiaryCarrier> Subsidiaries { get; set; }

        public IDictionary<DateTime, CarrierBudget> TestBudget { get; set; }

        [Versioning("routes")]
        public List<Route> _Routes { get; set; }

        [Versioning("scontracts", Version = 5)]
        public List<SpecialContract> SpecialContracts { get; set; }


        [Versioning("dailyoperatingbalancehistory")]
        public List<KeyValuePair<DateTime, KeyValuePair<double, double>>> DailyOperatingBalanceHistory
        {
            get { return _dailyOperatingBalanceHistory ?? (_dailyOperatingBalanceHistory = new List<KeyValuePair<DateTime, KeyValuePair<double, double>>>()); }
            protected set
            {
                if (value != null)
                {
                    _dailyOperatingBalanceHistory = value;
                }
            }
        }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 5);

            base.GetObjectData(info, context);
        }

        public void AddCarrierPolicy(CarrierPolicy policy)
        {
            Policies.Add(policy);
        }

        //adds an coach to the carriers fleet
        public void AddCoach(FleetCoach.PurchasedType type, Coach coach, Station homeBase)
        {
            AddCoach(new FleetCoach(type, GameObject.GetInstance().GameTime, this, coach, homeBase));
        }

        //adds a fleet coach to the carriers fleet
        public void AddCoach(FleetCoach coach)
        {
            lock (Fleet)
            {
                Fleet.Add(coach);
            }
        }

        //remove a fleet coach from the carriers fleet

        //adds an station to the carrier
        public void AddStation(Station station)
        {
            lock (Stations)
            {
                if (station != null)
                {
                    Stations.Add(station);
                }
            }
        }

        public void AddAlliance(Alliance alliance)
        {
            Alliances.Add(alliance);
        }

        public void AddCodeshareAgreement(CodeshareAgreement share)
        {
            Codeshares.Add(share);
        }

        //removes an station from the carrier

        //adds a facility to the carrier
        public void AddFacility(CarrierFacility facility)
        {
            Facilities.Add(facility);
        }

        public void AddTripSchool(TripSchool school)
        {
            TripSchools.Add(school);
        }

        public void AddInsurance(CarrierInsurance insurance)
        {
            InsurancePolicies.Add(insurance);
        }

        //removes a facility from the carrier

        //adds an invoice for the carrier - both incoming and expends - if updateMoney == true the money is updated as well
        public void AddInvoice(Invoice invoice, bool updateMoney = true)
        {
            // trips and the computer carriers' decisions run on several threads at once: keep every payment
            lock (Invoices)
            {
                Invoices.AddInvoice(invoice);

                if (updateMoney)
                {
                    Money += invoice.Amount;
                }
            }
        }

        /// <summary>Adds (or with a negative amount takes) money without an invoice, safely from any thread.</summary>
        public void AddMoney(double amount)
        {
            lock (Invoices)
            {
                Money += amount;
            }
        }

        //returns the reputation for the carrier

        //adds a loan to the carrier
        public void AddLoan(Loan loan)
        {
            lock (Loans)
            {
                Loans.Add(loan);
            }
        }

        public void AddPilot(Pilot pilot)
        {
            if (pilot == null)
            {
                throw new NullReferenceException("Pilot is null at Carrier.cs/addPilot");
            }

            lock (Pilots)
            {
                Pilots.Add(pilot);
                pilot.Carrier = this;
                pilot.Morale = Math.Max(pilot.Morale, 65);
            }
        }

        public void AddRoute(Route route)
        {
            lock (_Routes)
            {
                _Routes.Add(route);
                route.Carrier = this;
            }
            RouteIndex.Invalidate();
            /*
                foreach (string tripCode in route.TimeTable.Entries.Select(e => e.Destination.TripCode).Distinct())
                    this.TripCodes.Remove(tripCode);
          
         */
        }

        public void AddSubsidiaryCarrier(SubsidiaryCarrier subsidiary)
        {
            Subsidiaries.Add(subsidiary);
        }

        //removes a loan 

        //returns the advertisement for the carrier for a specific type
        public AdvertisementType GetCarrierAdvertisement(AdvertisementType.CarrierAdvertisementType type)
        {
            return Advertisements[type];
        }

        public List<AdvertisementType> GetCarrierAdvertisements()
        {
            return Advertisements.Values.ToList();
        }

        public CarrierPolicy GetCarrierPolicy(string name)
        {
            Policies ??= new List<CarrierPolicy>();
            var policy = Policies.Find(p => p.Name == name);
            if (policy == null && name == "Cancellation Minutes")
            {
                policy = new CarrierPolicy(name, 150);
                Policies.Add(policy);
            }

            return policy;
        }

        public CarrierValue GetCarrierValue()
        {
            double value = GeneralHelpers.GetInflationPrice(GetValue()*1000000);
            double startMoney = GeneralHelpers.GetInflationPrice(StartMoney);

            if (value <= startMoney)
            {
                return CarrierValue.VeryLow;
            }
            if (value > startMoney && value < startMoney*3)
            {
                return CarrierValue.Low;
            }
            if (value >= startMoney*3 && value < startMoney*9)
            {
                return CarrierValue.Normal;
            }
            if (value >= startMoney*9 && value < startMoney*18)
            {
                return CarrierValue.High;
            }
            if (value >= startMoney*18)
            {
                return CarrierValue.VeryHigh;
            }

            return CarrierValue.Normal;
        }

        //creates the standard Advertisement for the carrier

        /*! returns the average age for the fleet
         */

        public double GetAverageFleetAge()
        {
            // snapshot: carriers are simulated in parallel and the fleet can change while this is read
            var ages = Fleet.ToList().Where(f => f != null && f.Coach != null).Select(f => f.Coach.Age).ToList();
            return ages.Count > 0 ? ages.Average() : 0;
        }

        //returns total current value of fleet

        //returns the average value of an coach in the fleet
        public long GetAvgFleetValue()
        {
            var count = Fleet.Count();
            return count == 0 ? 0 : GetFleetValue()/count;
        }

        public List<Carrier> GetCodesharingCarriers()
        {
            return Codeshares.Select(c => c.Carrier1 == this ? c.Carrier2 : c.Carrier1).ToList();
        }

        public double GetFleetSize()
        {
            return Fleet.Count;
        }

        public long GetFleetValue()
        {
            return Fleet.Sum(coach => GetValue());
        }

        public List<string> GetTripCodes()
        {
            var codes = new List<string>();

            IEnumerable<string> rCodes =
                Routes.SelectMany(r => r.TimeTable.Entries).Select(e => e.Destination.TripCode).Distinct().ToList();

            for (int i = 0; i < 1000; i++)
            {
                string code = $"{Profile.IATACode}{i:0000}";

                if (!rCodes.Contains(code))
                {
                    codes.Add(code);
                }
            }

            return codes;

            /*
            var routes = new List<Route>(this.Routes);

            var entries = new List<RouteTimeTableEntry>(routes.SelectMany(r => r.TimeTable.Entries));
            
            foreach (RouteTimeTableEntry entry in entries)
            {
                if (codes.Contains(entry.Destination.TripCode))
                    codes.Remove(entry.Destination.TripCode);
                   
            }

            codes.Sort(delegate(string s1, string s2) { return s1.CompareTo(s2); });
            

            
            return codes;
         */
        }

        public List<Station> GetHubs()
        {
            var hubs = new List<Station>();
            lock (Stations)
            {
                hubs = (from a in Stations where a.GetHubs().Exists(h => h.Carrier == this) select a).ToList();
            }
            return hubs;
        }

        public Invoices GetInvoices()
        {
            return Invoices;
        }

        /*
        //returns all invoices with type
        public List<Invoice> getInvoices(DateTime start, DateTime end, Invoice.InvoiceType type)
        
        {
            return this.Invoices.FindAll(i=>i.Date>=start && i.Date <=end && i.Type == type);
        }
        //returns all the invoices in a specific period
        public List<Invoice> getInvoices(DateTime start, DateTime end)
        {
           return this.Invoices.FindAll(delegate(Invoice i) { return i.Date >= start && i.Date <= end; });

        }
       */
        //returns the amount of all the invoices in a specific period of a specific type
        public double GetInvoicesAmount(DateTime startTime, DateTime endTime, Invoice.InvoiceType type)
        {
            int startYear = startTime.Year;
            int endYear = endTime.Year;

            int startMonth = startTime.Month;
            int endMonth = endTime.Month;

            int totalMonths = (endMonth - startMonth) + 12*(endYear - startYear) + 1;

            double totalAmount = 0;

            var date = new DateTime(startYear, startMonth, 1);

            for (int i = 0; i < totalMonths; i++)
            {
                if (type == Invoice.InvoiceType.Total)
                {
                    totalAmount += Invoices.GetAmount(date.Year, date.Month);
                }
                else
                {
                    totalAmount += Invoices.GetAmount(type, date.Year, date.Month);
                }

                date = date.AddMonths(1);
            }

            return totalAmount;
        }

        public double GetInvoicesAmountMonth(int year, int month, Invoice.InvoiceType type)
        {
            if (type == Invoice.InvoiceType.Total)
            {
                return Invoices.GetAmount(year, month);
            }
            return Invoices.GetAmount(type, year, month);
        }

        public double GetInvoicesAmountYear(int year, Invoice.InvoiceType type)
        {
            if (type == Invoice.InvoiceType.Total)
            {
                return Invoices.GetYearlyAmount(year);
            }
            return Invoices.GetYearlyAmount(type, year);
        }

        public string GetNextTripCode(int n)
        {
            return GetTripCodes()[n];
        }

        public int GetNumberOfEmployees()
        {
            int instructors = TripSchools.Sum(f => f.NumberOfInstructors);

            int cockpitCrew = Pilots.Count;
            int cabinCrew =
                Routes.Where(r => r.Type == Route.RouteType.Passenger)
                      .Sum(r => ((PassengerRoute) r).GetTotalCabinCrew());

            int serviceCrew =
                Stations.SelectMany(a => a.GetCurrentStationFacilities(this))
                        .Where(a => a.EmployeeType == StationFacility.EmployeeTypes.Support)
                        .Sum(a => a.NumberOfEmployees);
            int maintenanceCrew =
                Stations.SelectMany(a => a.GetCurrentStationFacilities(this))
                        .Where(a => a.EmployeeType == StationFacility.EmployeeTypes.Maintenance)
                        .Sum(a => a.NumberOfEmployees);

            return cockpitCrew + cabinCrew + serviceCrew + maintenanceCrew + instructors;
        }

        public double GetProfit()
        {
            return Money - StartMoney;
        }

        public CarrierValue GetReputation()
        {
            //0-100 with 0-10 as very_low, 11-30 as low, 31-70 as normal, 71-90 as high,91-100 as very_high 
            if (Reputation < 11)
            {
                return CarrierValue.VeryLow;
            }
            if (Reputation > 10 && Reputation < 31)
            {
                return CarrierValue.Low;
            }
            if (Reputation > 30 && Reputation < 71)
            {
                return CarrierValue.Normal;
            }
            if (Reputation > 70 && Reputation < 91)
            {
                return CarrierValue.High;
            }
            if (Reputation > 90)
            {
                return CarrierValue.VeryHigh;
            }
            return CarrierValue.Normal;
        }

        //returns the value of the carrier in "money"
        public long GetValue()
        {
            double value = 0;
            value += Money;

            var fleet =
                new List<FleetCoach>(Fleet.FindAll(f => f != null && f.Purchased != FleetCoach.PurchasedType.Leased));

            value = fleet.Where(f => f.Coach != null).Aggregate(value, (current, coach) => current + coach.Coach.GetPrice());

            var facilities = new List<CarrierFacility>(Facilities);
            value += facilities.Sum(facility => facility.Price);

            var stations = new List<Station>(Stations);
            value += stations.SelectMany(station => new List<CarrierStationFacility>(station.GetStationFacilities(this))).Sum(facility => facility.Facility.Price);

            lock (Loans)
            {
                var loans = new List<Loan>(Loans);
                value = loans.Aggregate(value, (current, loan) => current - loan.Balance);
            }
            var subs = new List<SubsidiaryCarrier>(Subsidiaries);
            value = subs.Aggregate(value, (current, subCarrier) => current + subCarrier.GetValue());

            value = value/1000000;

            if (double.IsNaN(value))
            {
                return 0;
            }
            return Convert.ToInt64(value);
        }

        //returns if the carrier is a subsidiary carrier

        //returns if it is the human carrier
        public virtual bool isHuman()
        {
            return this == GameObject.GetInstance().HumanCarrier || this == GameObject.GetInstance().MainCarrier;
        }

        public virtual bool IsSubsidiaryCarrier()
        {
            return false;
        }

        public void RemoveCoach(FleetCoach coach)
        {
            Fleet.Remove(coach);

            coach.Coach.Carrier = null;
        }

        public void RemoveStation(Station station)
        {
            Stations.Remove(station);

            station.Cooperations.RemoveAll(r => r.Carrier == this);
        }

        public void RemoveAlliance(Alliance alliance)
        {
            lock (Alliances)
            {
                if (Alliances.Contains(alliance))
                {
                    Alliances.Remove(alliance);
                }
            }
        }

        //adds a subsidiary carrier to the carrier

        //removes a code share agreement from the carrier
        public void RemoveCodeshareAgreement(CodeshareAgreement share)
        {
            Codeshares.Remove(share);
        }

        public void RemoveFacility(CarrierFacility facility)
        {
            Facilities.Remove(facility);
        }

        public void RemoveTripSchool(TripSchool school)
        {
            TripSchools.Remove(school);

            foreach (Instructor instructor in school.Instructors)
            {
                instructor.TripSchool = null;
            }
        }

        public void RemoveInsurance(CarrierInsurance insurance)
        {
            InsurancePolicies.Remove(insurance);
        }

        public void RemoveLoan(Loan loan)
        {
            Loans.Remove(loan);
        }

        public void RemovePilot(Pilot pilot)
        {
            Pilots.Remove(pilot);
            pilot.Carrier = null;

            pilot.Coach?.RemovePilot(pilot);
        }

        public void RemoveRoute(Route route)
        {
            lock (_Routes)
            {
                _Routes.Remove(route);

                /*
                foreach (string tripCode in route.TimeTable.Entries.Select(e => e.Destination.TripCode).Distinct())
                    this.TripCodes.Add(tripCode);*/
            }
            RouteIndex.Invalidate();
        }

        public void RemoveSubsidiaryCarrier(SubsidiaryCarrier subsidiary)
        {
            Subsidiaries.Remove(subsidiary);
        }

        public void SetCarrierAdvertisement(AdvertisementType type)
        {
            if (!Advertisements.ContainsKey(type.Type))
            {
                Advertisements.Add(type.Type, type);
            }
            else
            {
                Advertisements[type.Type] = type;
            }
        }

        //adds a policy to the carrier

        //sets the policy for the carrier
        public void SetCarrierPolicy(string name, object value)
        {
            var policy = GetCarrierPolicy(name);
            if (policy == null)
            {
                policy = new CarrierPolicy(name, value);
                Policies.Add(policy);
            }
            else
            {
                policy.PolicyValue = value;
            }
        }

        public void SetInvoice(Invoice invoice)
        {
            Invoices.AddInvoice(invoice);
        }

        public void SetInvoice(Invoice.InvoiceType type, int year, int month, int day, double amount)
        {
            Invoices.AddInvoice(type, year, month, day, amount);
        }

        public void StoreBudget(CarrierBudget budget)
        {
            BudgetHistory.Add(GameObject.GetInstance().GameTime, budget);
        }

        #endregion

        #region Methods

        private void CreateStandardAdvertisement()
        {
            foreach (
                AdvertisementType.CarrierAdvertisementType type in
                    Enum.GetValues(typeof (AdvertisementType.CarrierAdvertisementType)))
            {
                SetCarrierAdvertisement(AdvertisementTypes.GetBasicType(type));
            }
        }

        private List<FleetCoach> GetDeliveredFleet()
        {
            return
                Fleet.FindAll(
                    (a => a.Coach.BuiltDate <= GameObject.GetInstance().GameTime));
        }

        private List<Route> GetRoutes()
        {
            var routes = new List<Route>();
            lock (_Routes)
            {
                routes = new List<Route>(_Routes);
            }

            return routes;
        }

        #endregion

        //returns a policy for the carrier
        public bool HasRouteTo(Station station)
        {
            return
                Routes.Any(
                    r =>
                    ((r.Destination1.Profile.IATACode == station.Profile.IATACode)
                     || (r.Destination2.Profile.IATACode == station.Profile.IATACode)));
        }

        //returns a policy for the carrier
        public bool HasAirplaneOnRouteTo(Station station)
        {
            return
                Routes.Any(
                    r =>
                    ((r.Destination1.Profile.IATACode == station.Profile.IATACode)
                     || (r.Destination2.Profile.IATACode == station.Profile.IATACode)) && r.HasCoach);
        }
    }

    //the list of carriers
    public class Carriers
    {
        #region Static Fields

        private static readonly List<Carrier> carriers = new List<Carrier>();

        #endregion

        #region Public Methods and Operators

        public static void AddCarrier(Carrier carrier)
        {
            carriers.Add(carrier);
        }

        public static void Clear()
        {
            carriers.Clear();
        }

        public static bool ContainsCarrier(Carrier carrier)
        {
            return carriers.Contains(carrier);
        }

        //returns an carrier
        public static Carrier GetCarrier(string iata)
        {
            return carriers.Find(a => a.Profile.IATACode == iata);
        }

        public static Carrier GetCarrier(string iata, int year)
        {
            return carriers.Find(a => a.Profile.IATACode == iata && a.Profile.Founded <= year && a.Profile.Folded >= year);
        }

        //returns all carriers

        //returns all carriers for a specific region
        public static List<Carrier> GetCarriers(Region region)
        {
            return carriers.FindAll(a => a.Profile.Country.Region == region);
        }

        //returns a list of carriers
        public static List<Carrier> GetCarriers(Predicate<Carrier> match)
        {
            List<Carrier> tCarriers;
            lock (carriers)
            {
                tCarriers = new List<Carrier>(carriers.FindAll(match));
            }
            return tCarriers;
        }

        public static List<Carrier> GetAllCarriers()
        {
            return carriers;
        }

        //returns all human carriers
        public static List<Carrier> GetHumanCarriers()
        {
            return carriers.FindAll(a => a.IsHuman);
        }

        public static int GetNumberOfCarriers()
        {
            return carriers.Count;
        }

        //removes an carrier from the list
        public static void RemoveCarrier(Carrier carrier)
        {
            carriers.Remove(carrier);
        }

        //removes carriers from the list
        public static void RemoveCarriers(Predicate<Carrier> match)
        {
            carriers.RemoveAll(match);
        }

        #endregion

        //clears the list

        //adds an carrier to the collection

        //returns if the list of carriers contains an carrier
    }
}