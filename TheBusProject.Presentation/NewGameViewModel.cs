using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Presentation
{
    /// <summary>Choose an carrier, a start year and a difficulty, then start a new game.</summary>
    /// <summary>A region with the name shown to the player (from the region translations).</summary>
    public class RegionChoice
    {
        public RegionChoice(Region region, string name)
        {
            Region = region;
            Name = name ?? TranslatedName(region);
        }

        public Region Region { get; }
        public string Name { get; }

        private static string TranslatedName(Region region)
        {
            return Regions.GetDisplayName(region);
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class NewGameViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private bool _isLoading = true;
        private bool _isCreating;
        private string _status = "Loading the game data...";
        private int _startYear = 2007;
        private Carrier _selectedCarrier;
        private string _ceo = "";
        private DifficultyLevel _selectedDifficulty;
        private int _opponents = 12;
        private bool _useDayTurns = true;
        private bool _majorStations, _sameRegion = true;
        private List<Continent> _continents = new List<Continent>();
        private List<RegionChoice> _regions = new List<RegionChoice>();
        private Continent _continent;
        private RegionChoice _region;
        private List<Carrier> _carriers = new List<Carrier>();
        private List<DifficultyLevel> _difficulties = new List<DifficultyLevel>();

        public NewGameViewModel(ShellViewModel shell)
        {
            _shell = shell;

            StartYears = Enumerable.Range(1960, 2030 - 1960 + 1).ToList();
            // more than about 50 makes setting up a game and every game day very slow
            OpponentChoices = new List<int> { 1, 3, 5, 8, 12, 20, 30, 50 };
            OpponentOptions = OpponentChoices.Select(n => new NamedChoice<int> { Name = n.ToString(), Value = n }).ToList();

            CreateCommand = new AsyncCommand(CreateAsync, () => !_isLoading && !_isCreating && SelectedCarrier != null);
            BackCommand = new RelayCommand(shell.ShowMainMenu, () => !_isCreating);

            var ignore = LoadAsync();
        }

        public List<int> StartYears { get; }

        public List<int> OpponentChoices { get; }

        public List<NamedChoice<int>> OpponentOptions { get; }

        public NamedChoice<int> SelectedOpponentOption
        {
            get { return OpponentOptions.FirstOrDefault(o => o.Value == _opponents) ?? OpponentOptions[0]; }
            set { if (value != null) Opponents = value.Value; }
        }

        /// <summary>How many carriers there are to compete with in the chosen year.</summary>
        public string OpponentsText
        {
            get
            {
                if (_isLoading) return null;
                int year = StartYear;
                int n = Models.Carriers.Carriers.GetCarriers(a => a.Profile.Founded <= year && a.Profile.Folded > year).Count - 1;
                int playing = Math.Min(n, _opponents);
                return string.Format(
                    "{0} other carriers ran in {1}; {2} of them will compete with you{3}. More carriers mean more competition and higher-fare routes get crowded; each game day also takes longer.",
                    Math.Max(0, n), year, Math.Max(0, playing), SameRegion ? " (the ones nearest to you)" : " (picked at random)");
            }
        }

        public ICommand CreateCommand { get; }

        public ICommand BackCommand { get; }

        public bool IsBusy => _isLoading || _isCreating;

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public int StartYear
        {
            get { return _startYear; }
            set
            {
                if (SetProperty(ref _startYear, value))
                {
                    RefreshCarriers();
                }
            }
        }

        public List<Carrier> Carriers
        {
            get { return _carriers; }
            private set { SetProperty(ref _carriers, value); }
        }

        /// <summary>What the chosen carrier can fly: passenger carriers fly passenger routes, cargo carriers cargo routes.</summary>
        public string SelectedCarrierKind
        {
            get
            {
                var a = _selectedCarrier;
                if (a == null) return null;
                bool cargo = Helpers.CarrierTypeRules.IsCargoCarrier(a);
                return a.Profile.Name + " is a " + Helpers.CarrierTypeRules.Kind(a) + ": it runs " + (cargo ? "cargo" : "passenger") +
                       " routes only. You can start a " + (cargo ? "passenger" : "cargo") + " subsidiary later to run the other kind.";
            }
        }

        public Carrier SelectedCarrier
        {
            get { return _selectedCarrier; }
            set
            {
                if (SetProperty(ref _selectedCarrier, value))
                {
                    if (value != null)
                    {
                        Ceo = value.Profile.CEO ?? "";
                        OnPropertyChanged(nameof(Narrative));
                    }
                    OnPropertyChanged(nameof(SelectedCarrierKind));

                    (CreateCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Narrative => _selectedCarrier?.Profile.Narrative ?? "";

        public string Ceo
        {
            get { return _ceo; }
            set { SetProperty(ref _ceo, value); }
        }

        public List<DifficultyLevel> Difficulties
        {
            get { return _difficulties; }
            private set { SetProperty(ref _difficulties, value); }
        }

        public DifficultyLevel SelectedDifficulty
        {
            get { return _selectedDifficulty; }
            set { SetProperty(ref _selectedDifficulty, value); }
        }

        public int Opponents
        {
            get { return _opponents; }
            set
            {
                if (SetProperty(ref _opponents, value))
                {
                    OnPropertyChanged(nameof(SelectedOpponentOption));
                    OnPropertyChanged(nameof(OpponentsText));
                }
            }
        }

        public List<Continent> Continents
        {
            get { return _continents; }
            private set { SetProperty(ref _continents, value); }
        }

        public Continent SelectedContinent
        {
            get { return _continent; }
            set
            {
                if (value != null && SetProperty(ref _continent, value))
                {
                    var list = new List<RegionChoice> { new RegionChoice(Models.General.Countries.Regions.GetRegion("100"), "All regions") };
                    if (value.Uid != "100")
                    {
                        list.AddRange(value.Regions.Select(r => new RegionChoice(r, null)));
                    }
                    _region = list[0];
                    Regions = list;
                    OnPropertyChanged(nameof(SelectedRegion));
                    RefreshCarriers();
                }
            }
        }

        public List<RegionChoice> Regions
        {
            get { return _regions; }
            private set { SetProperty(ref _regions, value); }
        }

        public RegionChoice SelectedRegion
        {
            get { return _region; }
            set
            {
                if (value != null && SetProperty(ref _region, value))
                {
                    RefreshCarriers();
                }
            }
        }

        /// <summary>Start with the major stations only.</summary>
        public bool MajorStations
        {
            get { return _majorStations; }
            set { SetProperty(ref _majorStations, value); }
        }

        /// <summary>Opponents are carriers from the same region as the player.</summary>
        public bool SameRegion
        {
            get { return _sameRegion; }
            set { SetProperty(ref _sameRegion, value); }
        }

        public bool UseDayTurns
        {
            get { return _useDayTurns; }
            set { SetProperty(ref _useDayTurns, value); }
        }

        private async Task LoadAsync()
        {
            try
            {
                await GameSession.EnsureDataLoadedAsync();

                var allContinents = new Continent("100", "All continents");
                allContinents.Regions = Models.General.Countries.Regions.GetRegions();
                Continents = new List<Continent> { allContinents };
                Continents.AddRange(Models.General.Countries.Continents.GetContinents());
                _isLoading = true;
                SelectedContinent = Continents[0];

                Difficulties = DifficultyLevels.GetDifficultyLevels();
                SelectedDifficulty = Difficulties.FirstOrDefault(d => d.Name == "Normal") ?? Difficulties.FirstOrDefault();

                _isLoading = false;
                RefreshCarriers();
            }
            catch (Exception ex)
            {
                StartupLog.Write("New game: loading the data failed: " + ex);
                Status = "Loading the game data failed: " + ex.Message;
                return;
            }
            finally
            {
                OnPropertyChanged(nameof(IsBusy));
                (CreateCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }

        // the carriers that exist in the chosen year
        private void RefreshCarriers()
        {
            if (_isLoading)
            {
                return;
            }

            int year = StartYear;
            Carriers = Models.Carriers.Carriers.GetCarriers(a => a.Profile.Founded <= year && a.Profile.Folded > year && InSelectedArea(a))
                                               .OrderBy(a => a.Profile.Name)
                                               .ToList();
            SelectedCarrier = Carriers.FirstOrDefault();
            Status = Carriers.Count + " carriers in " + year;
            OnPropertyChanged(nameof(OpponentsText));
        }

        private bool InSelectedArea(Carrier a)
        {
            var region = a.Profile.Country?.Region;
            if (_region == null || _region.Region.Uid == "100")
            {
                return _continent == null || _continent.Uid == "100" || _continent.HasRegion(region);
            }

            return region != null && region.Uid == _region.Region.Uid;
        }

        private async Task CreateAsync()
        {
            Carrier carrier = SelectedCarrier;
            _isCreating = true;
            OnPropertyChanged(nameof(IsBusy));
            (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Status = "Creating the game, this takes a little while...";

            try
            {
                // stations are only active relative to the game clock, so set it to the start year first
                GameObject.GetInstance().GameTime = new DateTime(StartYear, 1, 1);

                Station station = FindHomeStation(carrier);
                if (station == null)
                {
                    Status = "No station found for " + carrier.Profile.Name + " in " + StartYear;
                    return;
                }

                var startData = new StartDataObject
                    {
                        Carrier = carrier,
                        Station = station,
                        CEO = string.IsNullOrWhiteSpace(Ceo) ? "CEO" : Ceo.Trim(),
                        // uid "100" is the "all" entry the game setup code checks for
                        Continent = _continent ?? new Continent("100", "All continents"),
                        Difficulty = SelectedDifficulty,
                        Focus = default(Infrastructure.Enums.CarrierFocus),
                        HomeCountry = station.Profile.Country ?? carrier.Profile.Country,
                        IsPaused = true,
                        LocalCurrency = false,
                        MajorStations = MajorStations,
                        NumberOfOpponents = Opponents,
                        RandomOpponents = true,
                        RealData = false,
                        Region = _region?.Region ?? Models.General.Countries.Regions.GetRegion("100"),
                        SameRegion = SameRegion,
                        TimeZone = station.Profile.TimeZone ?? TimeZones.GetTimeZones().First(),
                        UseDayTurns = UseDayTurns,
                        Year = StartYear
                    };

                await GameSession.CreateGameAsync(startData);

                _shell.ShowCarrier();
            }
            catch (Exception ex)
            {
                StartupLog.Write("New game: creating the game failed: " + ex);
                Status = "Creating the game failed: " + ex.Message;
            }
            finally
            {
                _isCreating = false;
                OnPropertyChanged(nameof(IsBusy));
                (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private static Station FindHomeStation(Carrier carrier)
        {
            Station preferred = carrier.Profile.PreferedStation;
            if (preferred != null)
            {
                return preferred;
            }

            Station own = carrier.Stations.FirstOrDefault();
            if (own != null)
            {
                return own;
            }

            return Models.Stations.Stations.GetAllActiveStations()
                         .Where(a => a.Profile.Country != null && carrier.Profile.Country != null
                                     && a.Profile.Country.Uid == carrier.Profile.Country.Uid)
                         .OrderByDescending(a => a.Profile.Size)
                         .FirstOrDefault();
        }
    }
}
