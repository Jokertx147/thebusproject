using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    /// <summary>One thing that can be done to the coach, as the screen shows it.</summary>
    public class UpgradeRow
    {
        public CoachUpgrade Upgrade { get; set; }

        public string Name { get; set; }

        public string Text { get; set; }

        public bool CanDo { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    ///     Fleet &gt; a coach &gt; Upgrades: what can be done to a coach you already own. Today that is a re-power,
    ///     fitting one of the engines on sale now in place of the one it left the factory with. The interior is the
    ///     refit screen's business.
    /// </summary>
    public class CoachUpgradeViewModel : ObservableObject
    {
        private readonly Action _done;
        private readonly FleetCoach _coach;
        private UpgradeRow _selected;
        private string _status;

        public CoachUpgradeViewModel(FleetCoach coach, Action done = null)
        {
            _coach = coach;
            _done = done;
            Rows = new List<UpgradeRow>();
            DoCommand = new RelayCommand(Apply);
            Reload();
        }

        public List<UpgradeRow> Rows { get; private set; }

        public ICommand DoCommand { get; }

        public string Title => _coach == null
                                   ? "No coach"
                                   : _coach.Name + "  " + _coach.Coach.Type.Name + ", "
                                     + (_coach.Coach.EngineType == null
                                            ? "the engine it was built with"
                                            : EngineTypes.FullName(_coach.Coach.EngineType));

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public bool HasAny => Rows.Count > 0;

        public UpgradeRow Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        public void Reload()
        {
            var money = new ValueCurrencyFormatter();
            Rows = CoachUpgradeService.Repowers(_coach)
                                      .Select(
                                          u => new UpgradeRow
                                          {
                                              Upgrade = u,
                                              Name = u.Name,
                                              Text = string.Format(
                                                  "{0} for {1}, {2} day{3} in the shop: {4}",
                                                  u.Name,
                                                  Convert.ToString(money.Convert(u.Price)),
                                                  u.Days,
                                                  u.Days == 1 ? "" : "s",
                                                  u.Effect),
                                              CanDo = CoachUpgradeService.WhyNot(_coach, u) == null,
                                              Reason = CoachUpgradeService.WhyNot(_coach, u)
                                          })
                                      .ToList();
            _selected = Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasAny));
            OnPropertyChanged(nameof(Title));
        }

        private void Apply()
        {
            if (_selected == null)
            {
                Status = "Pick an engine first.";
                return;
            }

            Status = CoachUpgradeService.Repower(_coach, _selected.Upgrade);
            Reload();
            _done?.Invoke();
        }
    }
}
