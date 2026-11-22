using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Presentation
{
    /// <summary>Changes the cabin of an coach the carrier owns. Only an coach that is not flying can be refitted.</summary>
    public class CabinRefitViewModel : ObservableObject
    {
        private readonly FleetCoach _coach;
        private readonly Action _done;
        private readonly double _oldPrice;
        private string _status;

        public CabinRefitViewModel(FleetCoach coach, Action done)
        {
            _coach = coach;
            _done = done;
            _oldPrice = CabinEditorViewModel.PriceOf(coach.Coach.Classes);
            Title = "Cabin of " + coach.Name + " (" + coach.Coach.Type.Name + ")";
            Editor = new CabinEditorViewModel(
                (CoachPassengerType) coach.Coach.Type,
                coach.Coach.Classes,
                () => { OnPropertyChanged(nameof(CostText)); OnPropertyChanged(nameof(AllText)); });
            ApplyCommand = new RelayCommand(Apply);
            ApplyAllCommand = new RelayCommand(ApplyAll, () => SameType().Count > 1);
            CancelCommand = new RelayCommand(() => _done?.Invoke());
        }

        public string Title { get; }
        public CabinEditorViewModel Editor { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ApplyAllCommand { get; }

        /// <summary>The carrier's other coach of this type, and this one.</summary>
        private List<FleetCoach> SameType()
        {
            var carrier = _coach.Coach.Carrier ?? GameObject.GetInstance().HumanCarrier;
            return carrier.Fleet.Where(f => f.Coach.Type == _coach.Coach.Type).ToList();
        }

        private double CostFor(FleetCoach f)
        {
            return Math.Max(0, Editor.Price - CabinEditorViewModel.PriceOf(f.Coach.Classes));
        }

        public bool HasOthers => SameType().Count > 1;

        public string AllText
        {
            get
            {
                var all = SameType();
                if (all.Count <= 1) return null;
                var stopped = all.Where(f => f.Status == FleetCoach.CoachStatus.Stopped).ToList();
                return string.Format(
                    "You have {0} {1}. {2} of them stopped and can be refitted now, for {3} together. Flying ones are left as they are.",
                    all.Count, _coach.Coach.Type.Name, stopped.Count,
                    Convert.ToString(new ValueCurrencyFormatter().Convert(stopped.Sum(CostFor))));
            }
        }

        private double Cost => Editor == null ? 0 : Math.Max(0, Editor.Price - _oldPrice);

        public string CostText
        {
            get
            {
                return "Refit cost " + Convert.ToString(new ValueCurrencyFormatter().Convert(Cost))
                       + " (you pay for the extras you add, nothing is paid back for what you take out).";
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private void ApplyAll()
        {
            var stopped = SameType().Where(f => f.Status == FleetCoach.CoachStatus.Stopped).ToList();
            if (stopped.Count == 0)
            {
                Status = "None of them is stopped: stop the coach you want refitted first.";
                return;
            }

            var carrier = GameObject.GetInstance().HumanCarrier;
            double cost = stopped.Sum(CostFor);
            if (cost > carrier.Money)
            {
                Status = "You can't afford to refit all " + stopped.Count + ".";
                return;
            }

            if (cost > 0)
            {
                CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -cost);
            }

            foreach (var f in stopped)
            {
                f.Coach.ClearCoachClasses();
                foreach (CoachClass c in Editor.BuildClasses())
                {
                    f.Coach.AddCoachClass(c);
                }
            }

            _done?.Invoke();
        }

        private void Apply()
        {
            if (_coach.Status != FleetCoach.CoachStatus.Stopped)
            {
                Status = "Stop the coach first: a running coach can't be refitted.";
                return;
            }

            var carrier = GameObject.GetInstance().HumanCarrier;
            double cost = Cost;
            if (cost > carrier.Money)
            {
                Status = "You can't afford the refit.";
                return;
            }

            if (cost > 0)
            {
                CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -cost);
            }

            _coach.Coach.ClearCoachClasses();
            foreach (CoachClass c in Editor.BuildClasses())
            {
                _coach.Coach.AddCoachClass(c);
            }

            _done?.Invoke();
        }
    }
}
