using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Coaches
{
    [Serializable]
    public class FleetCoach : BaseModel
    {
        #region Constructors and Destructors

        public FleetCoach(
            PurchasedType purchased,
            DateTime purchasedDate,
            Carrier carrier,
            Coach coach,
            Station homebase)
        {
            Coach = coach;
            Purchased = purchased;
            PurchasedDate = purchasedDate;
            Coach.Carrier = carrier;
            Homebase = homebase;
            Name = coach.TailNumber;
            Statistics = new CoachStatistics(this);
            LastCMaintenance = Coach.BuiltDate;
            LastAMaintenance = Coach.BuiltDate;
            LastBMaintenance = Coach.BuiltDate;
            LastDMaintenance = Coach.BuiltDate;
            Status = CoachStatus.Stopped;
            MaintRoutes = new List<Route>();

            CurrentPosition = Homebase;
            //new GeoCoordinate(this.Homebase.Profile.Coordinates.Latitude,this.Homebase.Profile.Coordinates.Longitude);

            Routes = new List<Route>();
            Drivers = new List<Driver>();
            InsurancePolicies = new List<CoachInsurance>();
            MaintenanceHistory = new Dictionary<Invoice, string>();

            Data = new OperatingData();

            if (Purchased == PurchasedType.Bought || Purchased == PurchasedType.BoughtDownPayment)
                Coach.Owner = Coach.Carrier;

            // a leased coach is off the leasing market (it used to stay there, and could be leased again by another
            // carrier, or flown by its owner at the same time)
            if (Purchased == PurchasedType.Leased && Coach.Status == Coach.StatusTypes.Leasing)
                Coach.Status = Coach.StatusTypes.Normal;
        }

        /// <summary>
        ///     Whether the carrier can fly this coach: it is the carrier's (not put up for lease, and not leased out to
        ///     another carrier that flies it now).
        /// </summary>
        public bool UsableBy(Carrier carrier)
        {
            return Coach.Status != Coach.StatusTypes.Leasing && Coach.Carrier == carrier;
        }

        private FleetCoach(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
                Data = new OperatingData();
        }

        #endregion

        #region Enums

        public enum CoachStatus
        {
            Stopped,

            OnRoute,

            OnService,

            OnCharter,

            Resting,

            ToHomebase,

            ToRouteStart
        }

        public enum PurchasedType
        {
            Bought,

            Leased,

            BoughtDownPayment
        }

        #endregion

        #region Public Properties

        [Versioning("ainterval")]
        public int AMaintenanceInterval { get; set; }

        /// <summary>While a spare coach covers this one during a heavy check, its trips carry on until this date.</summary>
        [Versioning("covereduntil")]
        public DateTime CoveredUntil { get; set; }

        /// <summary>A spare coach is tied up covering another one until this date.</summary>
        [Versioning("coveruntil")]
        public DateTime CoverUntil { get; set; }

        /// <summary>Registration of the other coach in a cover arrangement (the spare, or the one being covered).</summary>
        [Versioning("covername")]
        public string CoverName { get; set; }

        /// <summary>
        ///     When on, the checks that fall due are done automatically. On by default, because a real operator runs a
        ///     maintenance programme and does not decide coach by coach whether to have one. It used to default to off,
        ///     and nothing outside the maintenance screen ever turned it on, so a computer carrier never serviced a
        ///     coach in its life: every fleet sat at the condition floor of 20% and broke down for ever.
        /// </summary>
        [Versioning("automaintenance")]
        public bool AutoMaintenance { get; set; } = true;

        /// <summary>The coach is out on a charter trip until this date.</summary>
        [Versioning("charteruntil")]
        public DateTime CharterUntil { get; set; }

        [Versioning("charterpay")]
        public double CharterPay { get; set; }

        [Versioning("chartercost")]
        public double CharterCost { get; set; }

        [Versioning("charterdestination")]
        public Station CharterDestination { get; set; }

        [Versioning("chartertext")]
        public string CharterText { get; set; }

        /// <summary>The carrier this coach is leased out to, until LeaseOutUntil.</summary>
        [Versioning("leaseoutto")]
        public Carrier LeaseOutTo { get; set; }

        [Versioning("leaseoutuntil")]
        public DateTime LeaseOutUntil { get; set; }

        [Versioning("leaseoutrent")]
        public double LeaseOutRent { get; set; }

        [Versioning("leaseoutstart")]
        public DateTime LeaseOutStart { get; set; }

        /// <summary>Asking price when the coach is listed for sale (0 when not listed).</summary>
        [Versioning("saleask")]
        public double SaleAsk { get; set; }

        [Versioning("salesince")]
        public DateTime SaleSince { get; set; }

        /// <summary>An automatic ferry trip is under way to this station until FerryUntil.</summary>
        [Versioning("ferryto")]
        public Station FerryTo { get; set; }

        [Versioning("ferryuntil")]
        public DateTime FerryUntil { get; set; }

        /// <summary>The last day this coach flew a scheduled trip.</summary>
        [Versioning("lasttripdate")]
        public DateTime LastTripDate { get; set; }

        [Versioning("coach")]
        public Coach Coach { get; set; }

        [Versioning("binterval")]
        public int BMaintenanceInterval { get; set; }

        [Versioning("cinterval")]
        public int CMaintenanceInterval { get; set; }

        [Versioning("currenttrip")]
        public Trip CurrentTrip { get; set; }

        [Versioning("currentposition")]
        public Station CurrentPosition { get; set; }

        [Versioning("dinterval")]
        public int DMaintenanceInterval { get; set; }

        [Versioning("duecmaintenance")]
        public DateTime DueCMaintenance { get; set; }

        [Versioning("duedmaintenance")]
        public DateTime DueDMaintenance { get; set; }

        [Versioning("groundedto")]
        public DateTime GroundedToDate { get; set; }

        [Versioning("data", Version = 2)]
        public OperatingData Data { get; set; }

        public bool HasRoute => Routes.Count > 0;

        [Versioning("homebase")]
        public Station Homebase { get; set; }

        [Versioning("insurancepolicies")]
        public List<CoachInsurance> InsurancePolicies { get; set; }

        [Versioning("lastamaintenance")]
        public DateTime LastAMaintenance { get; set; }

        [Versioning("lastbmaintenance")]
        public DateTime LastBMaintenance { get; set; }

        [Versioning("lastcmaintenance")]
        public DateTime LastCMaintenance { get; set; }

        [Versioning("lastdmaintenance")]
        public DateTime LastDMaintenance { get; set; }

        [Versioning("maintroutes")]
        public List<Route> MaintRoutes { get; set; }

        [Versioning("maintenancehistory")]
        public IDictionary<Invoice, string> MaintenanceHistory { get; set; }

        [Versioning("name")]
        private string _name;

        //the name of a fleet coach is its registration, so it follows registration changes
        public string Name
        {
            get { return Coach?.TailNumber ?? _name; }
            set { _name = value; }
        }

        public int NumberOfDrivers => Drivers.Count;

        [Versioning("oosdate")]
        public DateTime OOSDate { get; set; }

        [Versioning("drivers")]
        public List<Driver> Drivers { get; set; }

        [Versioning("purchased")]
        public PurchasedType Purchased { get; set; }

        //what is still to be paid on delivery day for an coach ordered with a down payment (0 in older saves: then the list price is charged)
        [Versioning("remainingpayment")]
        public double RemainingPayment { get; set; }

        /// <summary>
        ///     For an coach ordered in a fleet renewal: the coach whose routes it takes over when it is delivered
        ///     (null otherwise), how (a ReplaceMode) and what then happens to the old one (an OldCoachChoice).
        /// </summary>
        [Versioning("replacesondelivery")]
        public FleetCoach ReplacesOnDelivery { get; set; }

        [Versioning("renewalmode")]
        public int RenewalMode { get; set; }

        [Versioning("renewalafterold")]
        public int RenewalAfterOld { get; set; }

        [Versioning("date")]
        public DateTime PurchasedDate { get; set; }

        [Versioning("routes")]
        private List<Route> _routes;
        public List<Route> Routes
        {
            get { return _routes ?? (_routes = new List<Route>()); }
            set { _routes = value; }
        }

        [Versioning("schedamaintenance")]
        public DateTime SchedAMaintenance { get; set; }

        [Versioning("schedbmaintenance")]
        public DateTime SchedBMaintenance { get; set; }

        [Versioning("schedcmaintenance")]
        public DateTime SchedCMaintenance { get; set; }

        [Versioning("scheddmaintenance")]
        public DateTime SchedDMaintenance { get; set; }

        [Versioning("statistics")]
        public CoachStatistics Statistics { get; set; }

        [Versioning("status")]
        public CoachStatus Status { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        //adds a driver to the coach
        public void AddDriver(Driver driver)
        {
            lock (Drivers)
            {
                Drivers.Add(driver);
                driver.Coach = this;
            }
        }

        //removes a driver from the coach

        //adds a route to the coach
        public void AddRoute(Route route)
        {
            Routes.Add(route);
        }

        public void RemoveDriver(Driver driver)
        {
            lock (Drivers)
            {
                Drivers.Remove(driver);
                driver.Coach = null;
            }
        }

        //removes a route from the coach
        public void RemoveRoute(Route route)
        {
            Routes.Remove(route);
            route.TimeTable.Entries.RemoveAll(e => e.Coach == this);
        }

        #endregion
    }
}