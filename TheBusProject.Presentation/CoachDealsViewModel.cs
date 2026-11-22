using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    public class DealButton
    {
        public string Text { get; set; }
        public ICommand Command { get; set; }
    }

    public class DealCoachItem
    {
        public string Name { get; set; }
        public string Info { get; set; }
        public List<DealButton> LeaseButtons { get; set; }
        public List<DealButton> SaleButtons { get; set; }
    }

    public class DealLineItem
    {
        public string Text { get; set; }
        public string ButtonText { get; set; }
        public ICommand Command { get; set; }
    }

    /// <summary>Lease coach out to other carriers for rent, or list them for sale at a price you choose.</summary>
    public class CoachDealsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status, _pendingText;
        private Action _pending;

        public CoachDealsViewModel(Action changed)
        {
            _changed = changed;
            Available = new ObservableCollection<DealCoachItem>();
            LeasedOut = new ObservableCollection<DealLineItem>();
            Listed = new ObservableCollection<DealLineItem>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<DealCoachItem> Available { get; }
        public ObservableCollection<DealLineItem> LeasedOut { get; }
        public ObservableCollection<DealLineItem> Listed { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public string AvailableTitle { get; private set; }
        public string LeasedTitle { get; private set; }
        public string ListedTitle { get; private set; }
        public bool HasPending => _pending != null;

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

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

        private void Confirm()
        {
            var action = _pending;
            SetPending(null, null);
            action?.Invoke();
        }

        private void Done(string message)
        {
            Status = message;
            Reload();
            _changed();
        }

        public void Reload()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null) return;

            Available.Clear();
            foreach (var f in carrier.Fleet.Where(x => CoachDealService.GetBlocker(x) == null).OrderBy(x => x.Name).ToList())
            {
                var ff = f;
                var item = new DealCoachItem
                {
                    Name = f.Name + " (" + f.Coach.Type.Name + ")",
                    Info = string.Format("{0} years old, condition {1:0}%, worth {2}, at {3}", f.Coach.Age, f.Coach.Condition, Money(f.Coach.GetPrice()), f.CurrentPosition?.Profile.IATACode),
                    LeaseButtons = new List<DealButton>(),
                    SaleButtons = new List<DealButton>()
                };
                foreach (int months in CoachDealService.LeaseMonths)
                {
                    int m = months;
                    double rent = CoachDealService.RentFor(ff, m);
                    item.LeaseButtons.Add(
                        new DealButton
                        {
                            Text = string.Format("Lease out {0} months: {1} / month", m, Money(rent)),
                            Command = new RelayCommand(() => SetPending(
                                string.Format("Lease {0} to another carrier for {1} months? You get {2} a month ({3} in total) and cannot run it meanwhile. Ending early costs three months of rent.", ff.Name, m, Money(rent), Money(rent*m)),
                                () => { string msg; CoachDealService.LeaseOut(carrier, ff, m, out msg); Done(msg); }))
                        });
                }
                foreach (double ratio in CoachDealService.SalePrices)
                {
                    double r = ratio;
                    double ask = Math.Round(ff.Coach.GetPrice()*r);
                    item.SaleButtons.Add(
                        new DealButton
                        {
                            Text = string.Format("Ask {0}: {1}", r < 1 ? "less (" + (int) (r*100) + "%)" : r == 1 ? "market (100%)" : "more (" + (int) (r*100) + "%)", Money(ask)),
                            Command = new RelayCommand(() => SetPending(
                                string.Format("List {0} for sale at {1}? A lower price sells faster. You can withdraw it any time.", ff.Name, Money(ask)),
                                () => { string msg; CoachDealService.List(ff, r, out msg); Done(msg); }))
                        });
                }
                Available.Add(item);
            }

            LeasedOut.Clear();
            foreach (var f in carrier.Fleet.Where(x => CoachDealService.IsLeasedOut(x)).OrderBy(x => x.Name).ToList())
            {
                var ff = f;
                LeasedOut.Add(
                    new DealLineItem
                    {
                        Text = string.Format("{0} ({1}) leased to {2} until {3}, {4} / month", f.Name, f.Coach.Type.Name, f.LeaseOutTo.Profile.Name, DateFormatter.Date(f.LeaseOutUntil), Money(f.LeaseOutRent)),
                        ButtonText = "End early (" + Money(CoachDealService.EarlyEndFee(f)) + ")",
                        Command = new RelayCommand(() => SetPending(
                            string.Format("End the lease of {0} now? It costs {1} and the coach comes back worn from its time away.", ff.Name, Money(CoachDealService.EarlyEndFee(ff))),
                            () => Done(CoachDealService.EndLease(carrier, ff, true))))
                    });
            }

            Listed.Clear();
            foreach (var f in carrier.Fleet.Where(x => CoachDealService.IsListed(x)).OrderBy(x => x.Name).ToList())
            {
                var ff = f;
                Listed.Add(
                    new DealLineItem
                    {
                        Text = string.Format("{0} ({1}) for sale at {2} since {3}, about {4:0.0}% chance of a buyer each day", f.Name, f.Coach.Type.Name, Money(f.SaleAsk), DateFormatter.Date(f.SaleSince), CoachDealService.BuyerChance(f)*100),
                        ButtonText = "Withdraw",
                        Command = new RelayCommand(() => { CoachDealService.Withdraw(ff); Done(ff.Name + " is off the market."); })
                    });
            }

            AvailableTitle = Available.Count == 0 ? "No coach to lease out or sell. It must be owned outright, delivered, and have no routes." : "Coach you can lease out or sell";
            LeasedTitle = LeasedOut.Count == 0 ? "None of your coach is leased out." : "Leased out";
            ListedTitle = Listed.Count == 0 ? "None of your coach is for sale." : "For sale";
            OnPropertyChanged(nameof(AvailableTitle));
            OnPropertyChanged(nameof(LeasedTitle));
            OnPropertyChanged(nameof(ListedTitle));
        }
    }
}
