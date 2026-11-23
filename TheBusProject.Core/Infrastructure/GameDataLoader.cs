using System.Globalization;
using System.Threading;

namespace TheBusProject.Infrastructure
{
    /// <summary>
    ///     Loads the game's XML data (stations, carriers, coaches ...) once, using the older static setup code.
    ///     It is called on the thread that owns the UI, because the UI hooks it triggers may need it.
    /// </summary>
    public static class GameDataLoader
    {
        private static bool _loaded;

        //the game changes the loaded data while it runs, so it is read again the next time a game starts or loads
        public static void Invalidate()
        {
            _loaded = false;
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            var previousCulture = Thread.CurrentThread.CurrentCulture;
            UserInterface.SetBusy?.Invoke(true);
            try
            {
                // the data files use US number and date formats
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US", false);
                Setup.SetupGame();
                // a fresh game state for the fresh data: the old one held the previous game's contract offers, news and
                // scenario (which kept that whole world in memory, and made a new game check the old scenario's goals)
                Models.General.GameObject.RestartInstance();
                // caches from an earlier game point into its world: start empty
                Helpers.ConnectionService.Invalidate();
                Helpers.RouteIndex.Invalidate();
                Helpers.AIPricingService.Invalidate();
                Helpers.AIPricingService.ForgetReviews();
                Helpers.CompetitorService.Clear();
                Helpers.TripRecords.Clear();
                Helpers.CarrierReportService.Clear();
                if (Setup.SetupError != null)
                {
                    throw new System.InvalidOperationException("Loading the game data failed: " + Setup.SetupError.Message,
                        Setup.SetupError);
                }
                StartupLog.Write(string.Format(
                    "Game data loaded: {0} stations, {1} carriers, {2} coaches types, {3} standard coach configurations, {4} coaches facilities",
                    Models.Stations.Stations.GetAllStations().Count,
                    Models.Carriers.Carriers.GetAllCarriers().Count,
                    Models.Coaches.CoachTypes.GetAllTypes().Count,
                    Configurations.GetConfigurations(Configuration.ConfigurationType.Coach).Count,
                    Models.Coaches.CoachFacilities.GetFacilities(Models.Coaches.CoachFacility.FacilityType.Seat).Count));
                _loaded = true;
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                UserInterface.SetBusy?.Invoke(false);
            }
        }
    }
}
