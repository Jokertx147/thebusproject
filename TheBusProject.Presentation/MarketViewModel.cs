using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Presentation
{
    public class CoachItem
    {
        public CoachItem(Coach coach)
        {
            Coach = coach;
            var f = new ValueCurrencyFormatter();
            Name = coach.Type.Name;
            Year = coach.BuiltDate.Year;
            Range = coach.Type.Range;
            Seats = (coach.Type as CoachPassengerType)?.MaxSeatingCapacity ?? 0;
            PriceText = Convert.ToString(f.Convert(coach.GetPrice()));
            LeaseText = Convert.ToString(f.Convert(coach.GetLeasingPrice())) + " / month";
            Details = string.Format("built {0}  |  {1}  |  range {2}  |  {3}", Year, Seats + " seats", DistanceFormatter.Km(Range), PriceText);
        }

        public Coach Coach { get; }
        public string Name { get; }
        public int Year { get; }
        public long Range { get; }
        public int Seats { get; }
        public string PriceText { get; }
        public string LeaseText { get; }
        public string Details { get; }
    }

    /// <summary>Used coach market: buy an coach or lease it, placed at one of the carrier's home stations.</summary>
    public class MarketViewModel : ObservableObject
    {
        private CoachItem _selected;
        private Station _homebase;
        private List<Station> _homebases = new List<Station>();
        private string _status;
        private string _filter = "";
        private List<CoachItem> _all = new List<CoachItem>();

        private readonly Action _changed;

        public MarketViewModel(Action changed = null)
        {
            _changed = changed;
            Coach = new ObservableCollection<CoachItem>();
            BuyCommand = new RelayCommand(() => Place(false), () => _selected != null);
            LeaseCommand = new RelayCommand(() => Place(true), () => _selected != null);
            Reload();
        }

        public ObservableCollection<CoachItem> Coach { get; }
        public ICommand BuyCommand { get; }
        public ICommand LeaseCommand { get; }

        public CoachItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    LoadHomebases();
                    (BuyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (LeaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public List<Station> Homebases
        {
            get { return _homebases; }
            private set { SetProperty(ref _homebases, value); }
        }

        public Station Homebase
        {
            get { return _homebase; }
            set { SetProperty(ref _homebase, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>Text filter on the coach name.</summary>
        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value ?? ""))
                {
                    ApplyFilter();
                }
            }
        }

        public void Reload()
        {
            _all = Coaches.GetCoachesForSale()
                            .Where(
                                a => a.Type.TypeCoach == CoachType.TypeOfCoach.Passenger)
                            .OrderByDescending(a => a.BuiltDate.Year)
                            .ThenBy(a => a.Type.Name)
                            .Select(a => new CoachItem(a))
                            .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var keep = _selected?.Coach;
            Coach.Clear();
            foreach (var a in _all.Where(a => a.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).Take(400))
            {
                Coach.Add(a);
            }
            _selected = Coach.FirstOrDefault(a => a.Coach == keep);
            OnPropertyChanged(nameof(Selected));
        }

        private void LoadHomebases()
        {
            if (_selected == null)
            {
                Homebases = new List<Station>();
                Homebase = null;
                return;
            }

            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            long minBay = _selected.Coach.Type.MinBayLength;
            Homebases = CarrierHelpers.GetHomebases(carrier, minBay);
            Homebase = Homebases.FirstOrDefault();
            Status = Homebases.Count == 0
                ? "None of your stations can take this coach (bay, or no free service capacity). Get a contract at a bigger station first."
                : null;
        }

        private void Place(bool lease)
        {
            if (_selected == null)
            {
                return;
            }

            if (_homebase == null)
            {
                Status = "Pick a home station for the coach first.";
                return;
            }

            Carrier carrier = GameObject.GetInstance().HumanCarrier;
            Coach coach = _selected.Coach;

            if (carrier.Contract != null && carrier.Contract.Manufacturer != coach.Type.Manufacturer)
            {
                Status = "You have a contract with " + carrier.Contract.Manufacturer.Name
                         + ". End it first, or pick a coach from that manufacturer.";
                return;
            }

            double discount = carrier.Contract?.Discount ?? 0;

            if (lease)
            {
                double deposit = coach.GetLeasingPrice() * 2;
                if (deposit > carrier.Money)
                {
                    Status = "You can't afford the leasing deposit (two months).";
                    return;
                }

                if (CarrierHelpers.NeedsNewRegistration(carrier, coach.TailNumber))
                {
                    coach.TailNumber = carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier);
                }

                carrier.AddCoach(FleetCoach.PurchasedType.Leased, coach, _homebase);
                CarrierHelpers.AddCarrierInvoice(
                    carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Rents, -deposit);
                Status = "Leased " + _selected.Name + ".";
            }
            else
            {
                double price = coach.GetPrice() * ((100 - discount) / 100);
                if (price > carrier.Money)
                {
                    Status = "You can't afford this coach.";
                    return;
                }

                // it may have aged past the limit while the list stood
                if (Coaches.TooOldToSell(coach))
                {
                    string gone = _selected.Name;
                    _all.Remove(_selected);
                    Selected = null;
                    ApplyFilter();
                    Status = gone + " is too old to be sold now and has come off the market.";
                    return;
                }

                CarrierHelpers.BuyCoach(carrier, coach, _homebase, discount);
                Status = "Bought " + _selected.Name + ".";
            }

            if (carrier.Contract != null)
            {
                carrier.Contract.PurchasedCoaches++;
            }

            string status = Status;
            _all.Remove(_selected);
            Selected = null;
            ApplyFilter();
            Status = status;
            _changed?.Invoke();
        }
    }
}
