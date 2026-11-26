using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    public class SubsidiaryRow
    {
        public SubsidiaryCarrier Carrier { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
    }

    /// <summary>Carriers > Subsidiaries: start one, move money in and out, sell it or fold it into your carrier.</summary>
    public class SubsidiariesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private SubsidiaryRow _selected;
        private string _status, _pendingText, _transfer = "1000000", _name = "", _code = "", _money = "", _summary;
        private Action _pending;
        private StationItem _homebase;
        private NamedChoice<Carrier.CarrierMentality> _mentality;
        private NamedChoice<CarrierFocus> _market;
        private NamedChoice<Route.RouteType> _focus;

        public SubsidiariesViewModel(Action changed)
        {
            _changed = changed;
            Rows = new ObservableCollection<SubsidiaryRow>();
            Homebases = new ObservableCollection<StationItem>();
            Mentalities = new List<NamedChoice<Carrier.CarrierMentality>>
            {
                new NamedChoice<Carrier.CarrierMentality> { Name = "Careful", Value = Carrier.CarrierMentality.Safe },
                new NamedChoice<Carrier.CarrierMentality> { Name = "Balanced", Value = Carrier.CarrierMentality.Moderate },
                new NamedChoice<Carrier.CarrierMentality> { Name = "Fast growing", Value = Carrier.CarrierMentality.Aggressive }
            };
            Markets = new List<NamedChoice<CarrierFocus>>
            {
                new NamedChoice<CarrierFocus> { Name = "Local", Value = CarrierFocus.Local },
                new NamedChoice<CarrierFocus> { Name = "Domestic", Value = CarrierFocus.Domestic },
                new NamedChoice<CarrierFocus> { Name = "Regional", Value = CarrierFocus.Regional },
                new NamedChoice<CarrierFocus> { Name = "Global", Value = CarrierFocus.Global }
            };
            Focuses = new List<NamedChoice<Route.RouteType>>
            {
                new NamedChoice<Route.RouteType> { Name = "Passengers", Value = Route.RouteType.Passenger }
            };
            _mentality = Mentalities[1];
            _market = Markets[1];
            _focus = Focuses[0];
            _money = ((long) SubsidiaryService.MinStartMoney*5).ToString();
            CreateCommand = new RelayCommand(AskCreate);
            ToSubCommand = new RelayCommand(() => AskTransfer(true), () => _selected != null);
            FromSubCommand = new RelayCommand(() => AskTransfer(false), () => _selected != null);
            SellCommand = new RelayCommand(AskSell, () => _selected != null);
            FoldCommand = new RelayCommand(AskFold, () => _selected != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<SubsidiaryRow> Rows { get; }
        public ObservableCollection<StationItem> Homebases { get; }
        public List<NamedChoice<Carrier.CarrierMentality>> Mentalities { get; }
        public List<NamedChoice<CarrierFocus>> Markets { get; }
        public List<NamedChoice<Route.RouteType>> Focuses { get; }
        public ICommand CreateCommand { get; }
        public ICommand ToSubCommand { get; }
        public ICommand FromSubCommand { get; }
        public ICommand SellCommand { get; }
        public ICommand FoldCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Explanation =>
            "A subsidiary is a second carrier you own. It runs under its own name, code and money and is run by its own " +
            "management: it buys coach, opens routes and keeps what it earns. You decide how much money it has, and you can " +
            "sell it or fold it into your carrier at any time. Its value counts toward yours.";

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string PendingText { get => _pendingText; private set => SetProperty(ref _pendingText, value); }
        public bool HasPending => _pending != null;
        public bool HasSelection => _selected != null;

        public string Transfer { get => _transfer; set => SetProperty(ref _transfer, value ?? ""); }
        public string NewName { get => _name; set => SetProperty(ref _name, value ?? ""); }
        public string NewCode { get => _code; set => SetProperty(ref _code, value ?? ""); }
        public string NewMoney { get => _money; set => SetProperty(ref _money, value ?? ""); }
        public StationItem Homebase { get => _homebase; set => SetProperty(ref _homebase, value); }
        public NamedChoice<Carrier.CarrierMentality> Mentality { get => _mentality; set => SetProperty(ref _mentality, value); }
        public NamedChoice<CarrierFocus> Market { get => _market; set => SetProperty(ref _market, value); }
        public NamedChoice<Route.RouteType> Focus { get => _focus; set => SetProperty(ref _focus, value); }

        public string MinMoneyText => "At least " + Money(SubsidiaryService.MinStartMoney) + ". It comes out of your carrier's cash.";

        public SubsidiaryRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    SetPending(null, null);
                    foreach (var c in new[] { ToSubCommand, FromSubCommand, SellCommand, FoldCommand }) (c as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasSelection));
                }
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static double Parse(string text)
        {
            double v;
            return double.TryParse((text ?? "").Replace(",", "").Replace("$", "").Trim(), out v) ? v : 0;
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        public void Reload()
        {
            var me = GameObject.GetInstance()?.HumanCarrier;
            if (me == null) return;
            var keep = _selected?.Carrier;
            Rows.Clear();
            foreach (var s in SubsidiaryService.Mine(me))
            {
                Rows.Add(new SubsidiaryRow
                {
                    Carrier = s,
                    LogoPath = CarrierProfileService.GetLogo(s),
                    Name = s.Profile.Name + " (" + s.Profile.IATACode + ")",
                    Numbers = string.Format(
                        "cash {0}  |  {1} coaches, {2} routes, {3} stations  |  worth {4}  |  last month {5}",
                        Money(s.Money), s.Fleet.Count, s.Routes.Count, s.Stations.Count, Money(SharesService.Value(s)), Money(SubsidiaryService.LastMonthProfit(s)))
                });
            }
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Carrier == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            Summary = Rows.Count == 0
                ? "You have no subsidiaries."
                : string.Format("{0} subsidiar{1}, worth {2} together.", Rows.Count, Rows.Count == 1 ? "y" : "ies", Money(Rows.Sum(r => SharesService.Value(r.Carrier))));

            var home = _homebase?.Station;
            Homebases.Clear();
            foreach (var a in me.Stations.OrderBy(a => a.Profile.Name)) Homebases.Add(new StationItem(a, true));
            Homebase = Homebases.FirstOrDefault(h => h.Station == home) ?? Homebases.FirstOrDefault();
        }

        private void Done(bool ok, string message)
        {
            Reload();
            Status = message;
            if (ok) _changed?.Invoke();
        }

        private void AskCreate()
        {
            var me = GameObject.GetInstance().HumanCarrier;
            double money = Parse(_money);
            string blocker = SubsidiaryService.GetCreateBlocker(me, _name, _code, _homebase?.Station, money);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            string name = _name, code = _code;
            var home = _homebase.Station;
            var mentality = _mentality.Value;
            var market = _market.Value;
            var focus = _focus.Value;
            SetPending(
                string.Format("Start {0} ({1}) at {2} with {3} of your money?", name.Trim(), code.Trim().ToUpperInvariant(), home.Profile.IATACode, Money(money)),
                () =>
                    {
                        string m;
                        var sub = SubsidiaryService.Create(me, name, code, home, money, mentality, market, focus, out m);
                        if (sub != null)
                        {
                            NewName = "";
                            NewCode = "";
                        }
                        Done(sub != null, m);
                    });
        }

        private void AskTransfer(bool toSub)
        {
            var s = _selected?.Carrier;
            if (s == null) return;
            double amount = Parse(_transfer);
            if (amount <= 0)
            {
                Status = "Type an amount.";
                return;
            }
            var me = GameObject.GetInstance().HumanCarrier;
            SetPending(
                toSub ? "Move " + Money(amount) + " to " + s.Profile.Name + "?" : "Take " + Money(amount) + " out of " + s.Profile.Name + "?",
                () => { string m; Done(SubsidiaryService.Transfer(me, s, toSub ? amount : -amount, out m), m); });
        }

        private void AskSell()
        {
            var s = _selected?.Carrier;
            if (s == null) return;
            var me = GameObject.GetInstance().HumanCarrier;
            SetPending(
                "Sell " + s.Profile.Name + " for " + Money(SubsidiaryService.SalePrice(s)) + " (90% of its value)? It becomes an independent carrier.",
                () => { string m; SubsidiaryService.Sell(me, s, out m); Done(true, m); });
        }

        private void AskFold()
        {
            var s = _selected?.Carrier;
            if (s == null) return;
            var me = GameObject.GetInstance().HumanCarrier;
            string blocker = null;
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            SetPending(
                "Fold " + s.Profile.Name + " into your carrier? Its coaches, routes, gates, drivers and " + Money(s.Money) + " come over and its name disappears.",
                () => { string m; Done(SubsidiaryService.FoldIn(me, s, out m), m); });
        }
    }
}
