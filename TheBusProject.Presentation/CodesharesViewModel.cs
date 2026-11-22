using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    /// <summary>A codeshare you have.</summary>
    public class CodesharePartnerRow
    {
        public CodeshareAgreement Agreement { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Numbers { get; set; }
        public ICommand EndCommand { get; set; }
    }

    /// <summary>An offer from a computer carrier.</summary>
    public class CodeshareOfferRow
    {
        public CodeshareAgreement Agreement { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
        public ICommand AcceptCommand { get; set; }
        public ICommand DeclineCommand { get; set; }
    }

    /// <summary>An carrier you could ask.</summary>
    public class CodeshareCandidateRow
    {
        public Carrier Carrier { get; set; }
        public string LogoPath { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
    }

    /// <summary>Carriers > Codeshares: your partners, offers from other carriers and finding new partners.</summary>
    public class CodesharesViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _search = "";
        private CodeshareCandidateRow _selected;
        private string _status, _pendingText, _summary;
        private Action _pending;

        public CodesharesViewModel(Action changed)
        {
            _changed = changed;
            Partners = new ObservableCollection<CodesharePartnerRow>();
            Offers = new ObservableCollection<CodeshareOfferRow>();
            Candidates = new ObservableCollection<CodeshareCandidateRow>();
            AskBothCommand = new RelayCommand(() => Ask(CodeshareAgreement.CodeshareType.BothWays), () => _selected != null);
            AskOneCommand = new RelayCommand(() => Ask(CodeshareAgreement.CodeshareType.OneWay), () => _selected != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<CodesharePartnerRow> Partners { get; }
        public ObservableCollection<CodeshareOfferRow> Offers { get; }
        public ObservableCollection<CodeshareCandidateRow> Candidates { get; }
        public ICommand AskBothCommand { get; }
        public ICommand AskOneCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public bool HasPartners => Partners.Count > 0;
        public bool HasOffers => Offers.Count > 0;
        public bool HasSelection => _selected != null;

        public string Explanation =>
            "A codeshare lets two carriers sell seats on each other's trips. Each partner earns 1% of the ticket income " +
            "on the other's trips it sells, and both get more passengers at stations the partner runs to. " +
            "Both ways: you each sell the other's trips. One way: you sell the partner's trips only (you earn, it doesn't).";

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) LoadCandidates(); }
        }

        public CodeshareCandidateRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    SetPending(null, null);
                    (AskBothCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (AskOneCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(Evaluation));
                }
            }
        }

        /// <summary>How the selected carrier would answer, by the game's rules.</summary>
        public string Evaluation
        {
            get
            {
                if (_selected == null) return "";
                var me = GameObject.GetInstance().HumanCarrier;
                var a = _selected.Carrier;
                string blocker = CodeshareService.GetBlocker(me, a);
                if (blocker != null) return blocker;
                var both = CodeshareService.Reasons(a, me, CodeshareAgreement.CodeshareType.BothWays);
                var one = CodeshareService.Reasons(a, me, CodeshareAgreement.CodeshareType.OneWay);
                string fee = "Signing fee " + Money(CodeshareService.SigningFee(a, me)) + ".";
                return "Both ways: " + (both.Count == 0 ? "they would likely say yes." : string.Join(" ", both)) + Environment.NewLine
                       + "One way: " + (one.Count == 0 ? "they would likely say yes." : string.Join(" ", one)) + Environment.NewLine + fee;
            }
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string PendingText
        {
            get => _pendingText;
            private set => SetProperty(ref _pendingText, value);
        }

        public bool HasPending => _pending != null;

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
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

            Partners.Clear();
            double income = 0, cost = 0;
            foreach (var c in me.Codeshares.ToList())
            {
                var p = CodeshareService.Partner(c, me);
                double inc = CodeshareService.MonthlyIncome(me, c), co = CodeshareService.MonthlyCost(me, c);
                income += inc;
                cost += co;
                var agreement = c;
                Partners.Add(new CodesharePartnerRow
                {
                    Agreement = c,
                    LogoPath = CarrierProfileService.GetLogo(p),
                    Name = p.Profile.Name + " (" + p.Profile.IATACode + ")",
                    Kind = CodeshareService.Describe(c, me),
                    Numbers = string.Format(
                        "{0} of their routes touch your stations  |  {1} shared stations  |  last month: you got {2}, you paid {3}",
                        CodeshareService.PartnerRoutesAtMyStations(me, p), CodeshareService.SharedStations(me, p), Money(inc), Money(co)),
                    EndCommand = new RelayCommand(() => SetPending(
                        "End the codeshare with " + p.Profile.Name + "?",
                        () =>
                            {
                                CodeshareService.End(me, agreement);
                                Status = "The codeshare with " + p.Profile.Name + " has ended.";
                                Reload();
                                _changed?.Invoke();
                            }))
                });
            }

            Offers.Clear();
            foreach (var o in CodeshareService.Offers(me).ToList())
            {
                var p = CodeshareService.Partner(o, me);
                var offer = o;
                Offers.Add(new CodeshareOfferRow
                {
                    Agreement = o,
                    LogoPath = CarrierProfileService.GetLogo(p),
                    Name = p.Profile.Name + " (" + p.Profile.IATACode + ") asks for a codeshare both ways",
                    Numbers = string.Format(
                        "{0} routes  |  {1} of their routes touch your stations  |  {2} shared stations",
                        p.Routes.Count, CodeshareService.PartnerRoutesAtMyStations(me, p), CodeshareService.SharedStations(me, p)),
                    AcceptCommand = new RelayCommand(() => Answer(offer, true)),
                    DeclineCommand = new RelayCommand(() => Answer(offer, false))
                });
            }

            OnPropertyChanged(nameof(HasPartners));
            OnPropertyChanged(nameof(HasOffers));
            Summary = Partners.Count == 0
                ? "You have no codeshare partners yet."
                : string.Format("{0} partner(s)  |  last month you got {1} and paid {2}", Partners.Count, Money(income), Money(cost));

            LoadCandidates();
        }

        private void LoadCandidates()
        {
            var me = GameObject.GetInstance()?.HumanCarrier;
            if (me == null) return;
            var keep = _selected?.Carrier;
            string s = _search.Trim();
            var list = CodeshareService.Candidates(me)
                                       .Where(a => s.Length == 0
                                                   || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                                   || (a.Profile.IATACode ?? "").StartsWith(s, StringComparison.OrdinalIgnoreCase)
                                                   || (a.Profile.Country?.Name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                                       .Take(60);
            Candidates.Clear();
            foreach (var a in list)
            {
                Candidates.Add(new CodeshareCandidateRow
                {
                    Carrier = a,
                    LogoPath = CarrierProfileService.GetLogo(a),
                    Name = a.Profile.Name + " (" + a.Profile.IATACode + "), " + a.Profile.Country?.Name,
                    Numbers = string.Format(
                        "{0} routes  |  {1} touch your stations  |  {2} shared stations",
                        a.Routes.Count, CodeshareService.PartnerRoutesAtMyStations(me, a), CodeshareService.SharedStations(me, a))
                });
            }
            _selected = null;
            Selected = Candidates.FirstOrDefault(c => c.Carrier == keep);
            OnPropertyChanged(nameof(Selected));
        }

        private void Ask(CodeshareAgreement.CodeshareType type)
        {
            var row = _selected;
            if (row == null) return;
            var me = GameObject.GetInstance().HumanCarrier;
            string blocker = CodeshareService.GetBlocker(me, row.Carrier);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            string kind = type == CodeshareAgreement.CodeshareType.BothWays ? "both ways" : "one way";
            SetPending(
                "Ask " + row.Carrier.Profile.Name + " for a codeshare " + kind + "? If they agree, the signing fee is "
                + Money(CodeshareService.SigningFee(row.Carrier, me)) + ". If they say no, you can ask again after "
                + CodeshareService.RefusalDays + " days.",
                () =>
                    {
                        string m;
                        bool ok = CodeshareService.Propose(me, row.Carrier, type, out m);
                        Reload();
                        Status = m;
                        if (ok) _changed?.Invoke();
                    });
        }

        private void Answer(CodeshareAgreement offer, bool accept)
        {
            string m;
            CodeshareService.AnswerOffer(GameObject.GetInstance().HumanCarrier, offer, accept, out m);
            Reload();
            Status = m;
            if (accept) _changed?.Invoke();
        }
    }
}
