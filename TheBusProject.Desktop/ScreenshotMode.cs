using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TheBusProject.Desktop.Views;
using TheBusProject.Helpers;
using TheBusProject.Models.General;
using TheBusProject.Presentation;

namespace TheBusProject.Desktop
{
    /// <summary>
    ///     "--screenshots &lt;folder&gt;": starts a game, gives the player a few flying routes, plays ten weeks, then
    ///     opens the newer screens one by one and saves a picture of each (and of what is further down a screen that
    ///     scrolls), and exits without saving the game. For checking the layout on a real display; progress.txt in the
    ///     folder says how far it got, error.txt what went wrong.
    /// </summary>
    public static class ScreenshotMode
    {
        public static string FolderFrom(string[] args)
        {
            if (args == null) return null;
            int i = Array.IndexOf(args, "--screenshots");
            if (i < 0) return null;
            return i + 1 < args.Length ? args[i + 1] : "screenshots";
        }

        private static string _dir;

        private static void Log(string line)
        {
            try { File.AppendAllText(Path.Combine(_dir, "progress.txt"), DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine); }
            catch (Exception) { }
        }

        private static async Task Wait(Func<bool> until, string what, int seconds = 180)
        {
            DateTime end = DateTime.Now.AddSeconds(seconds);
            while (!until())
            {
                if (DateTime.Now > end) throw new TimeoutException("waited too long for " + what);
                await Task.Delay(100);
            }
        }

        // lets bindings, layout and rendering catch up
        private static async Task Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(150);
            }
        }

        private static void Save(Window window, string name)
        {
            double scale = window.RenderScaling;
            Size size = window.ClientSize;
            var pixels = new PixelSize(Math.Max(1, (int) (size.Width*scale)), Math.Max(1, (int) (size.Height*scale)));
            using (var bitmap = new RenderTargetBitmap(pixels, new Vector(96*scale, 96*scale)))
            {
                bitmap.Render(window);
                bitmap.Save(Path.Combine(_dir, name + ".png"));
            }
            Log("saved " + name + ".png (" + pixels.Width + "x" + pixels.Height + ")");
        }

        // a picture of the screen, and of each further screenful of its tallest scrolling area
        private static async Task Shoot(Window window, string name)
        {
            await Settle();
            Save(window, name);
            ScrollViewer scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
                                          .Where(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height + 40 && s.Viewport.Height > 200)
                                          .OrderByDescending(s => s.Viewport.Width*s.Viewport.Height).FirstOrDefault();
            if (scroller == null) return;
            for (int page = 2; page <= 4; page++)
            {
                double next = scroller.Offset.Y + scroller.Viewport.Height - 60;
                double max = scroller.Extent.Height - scroller.Viewport.Height;
                if (scroller.Offset.Y >= max - 1) break;
                scroller.Offset = new Vector(scroller.Offset.X, Math.Min(next, max));
                await Settle();
                Save(window, name + "-" + page);
            }
            scroller.Offset = new Vector(scroller.Offset.X, 0);
        }

        private static int PageOf(string name) => Array.IndexOf(CarrierViewModel.PageNames, name);

        // an coach from the market the player can base somewhere (as the tests buy theirs)
        /// <summary>
        ///     Buys one coach from a place in the market, given as a share of what is on offer rather than as a fixed
        ///     position: in 1960 there are a few dozen types and the fortieth does not exist, which used to leave the
        ///     screenshots a carrier with an empty garage and a map with nothing on it.
        /// </summary>
        private static void Buy(CarrierViewModel av, double through)
        {
            var mk = av.Market;
            var offered = mk.Coach.OrderBy(x => x.Coach.GetPrice()).ToList();
            if (offered.Count == 0) return;
            int skip = Math.Min(offered.Count - 1, (int) (offered.Count*through));
            foreach (var item in offered.Skip(skip))
            {
                mk.Selected = item;
                if (mk.Homebase != null) break;
            }

            mk.BuyCommand.Execute(null);
            av.Refresh();
        }

        public static async Task Run(ShellViewModel shell, Window window, string dir)
        {
            _dir = Path.GetFullPath(dir);
            try
            {
                Directory.CreateDirectory(_dir);
                foreach (string old in Directory.GetFiles(_dir, "*.png").Concat(Directory.GetFiles(_dir, "*.txt"))) File.Delete(old);
                Log("started; window " + window.ClientSize.Width + "x" + window.ClientSize.Height + " at scale " + window.RenderScaling);
                window.Width = 1440;
                window.Height = 900;
                await Settle();

                // a new game as Delta with eight computer carriers
                ((MainMenuViewModel) shell.Current).NewGameCommand.Execute(null);
                var ng = (NewGameViewModel) shell.Current;
                await Wait(() => !ng.IsBusy, "the new game screen");
                var america = ng.Continents.FirstOrDefault(c => c.Name == "North America");
                if (america != null) ng.SelectedContinent = america;
                await Wait(() => !ng.IsBusy, "the carriers");
                ng.SelectedCarrier = ng.Carriers.FirstOrDefault(a => a.Profile.IATACode == "DL") ?? ng.Carriers.First();
                ng.Opponents = 8;
                await Shoot(window, "00-new-game");
                ng.CreateCommand.Execute(null);
                await Wait(() => shell.Current is CarrierViewModel, "the game", 600);
                GameSession.StopClock();
                var av = (CarrierViewModel) shell.Current;
                var me = GameObject.GetInstance().HumanCarrier;
                Log("game created: " + me.Profile.Name + ", " + GameObject.GetInstance().GameTime.ToString("yyyy-MM-dd"));

                // four coach: three get a route, the fourth is delivered during the weeks played and left idle
                foreach (double through in new[] {0.3, 0.6, 0.9, 0.45}) Buy(av, through);
                Log("bought " + me.Fleet.Count + " coaches out of " + av.Market.Coach.Count + " on offer");
                if (me.Fleet.Count == 0) throw new Exception("no coach could be bought, so the pictures would show an empty carrier");
                foreach (var f in me.Fleet.ToList())
                    if (f.Drivers.Count < f.Coach.Type.DriverCrew) CarrierHelpers.HireCoachDrivers(f);
                int give = Math.Max(1, me.Fleet.Count - 1);
                for (int i = 0; i < give; i++)
                {
                    await av.Suggestions.Reload();
                    var row = av.Suggestions.Coach.FirstOrDefault(a => a.Suggestions.Count > 0);
                    if (row == null) break;
                    bool opened = av.Suggestions.Open(row.Suggestions[0]);
                    Log("route " + row.Suggestions[0].Name + " with " + row.Title + ": " + (opened ? "opened" : av.Suggestions.Status));
                    await Wait(() => !av.Suggestions.IsBusy, "the suggestions");
                }
                foreach (var f in me.Fleet.ToList())
                    if (f.Routes.Count > 0 && f.Status == TheBusProject.Models.Coaches.FleetCoach.CoachStatus.Stopped)
                        f.Status = TheBusProject.Models.Coaches.FleetCoach.CoachStatus.OnRoute;

                // ten weeks of play: two full months and part of a third
                await Task.Run(() =>
                {
                    for (int d = 0; d < 72; d++) GameObjectHelpers.SimulateTurn();
                });
                av.Refresh();
                Log("played to " + GameObject.GetInstance().GameTime.ToString("yyyy-MM-dd") + "; routes " + me.Routes.Count
                    + ", fleet " + me.Fleet.Count + ", idle " + RouteSuggestionService.IdleCoach(me).Count);

                // My Carrier > the map: the states, the interstates and the routes that run along them
                av.Page = PageOf("My Carrier");
                av.MyCarrierTab = 0;
                av.Map.Reload();
                await Settle();
                Save(window, "00b-map-network");
                Log("map: " + av.Map.Coach.Count + " coaches, " + av.Map.Lines.Count + " lines drawn");
                if (av.Map.Lines.Count == 0) throw new Exception("the map has no routes on it, so it cannot show how a route is drawn");

                // and closer in, where the US routes come in and a route can be seen running along the interstate
                var map = window.GetVisualDescendants().OfType<WorldMapControl>().FirstOrDefault();
                if (map != null)
                {
                    // on the network first, so the closer view is over the routes rather than over whatever happened
                    // to be in the middle of the country
                    map.FitView();
                    map.ZoomBy(4);
                    await Settle();
                    Save(window, "00c-map-closer");
                }

                // My Carrier > Statistics
                av.Page = PageOf("My Carrier");
                av.MyCarrierTab = CarrierViewModel.StatisticsTabIndex;
                await Shoot(window, "01-statistics-passengers");
                av.Statistics.Figure = "Operating result";
                await Shoot(window, "02-statistics-result");
                Log("statistics: log scale " + av.Statistics.LogScale);
                av.Statistics.LogScale = !av.Statistics.LogScale;
                await Settle();
                Save(window, "02b-statistics-result-other-scale");
                av.Statistics.LogScale = !av.Statistics.LogScale;
                av.Statistics.Figure = "Seats filled";
                await Settle();
                Save(window, "03-statistics-seats-filled");
                av.Statistics.Figure = "Cash";
                av.Statistics.Period = StatisticsViewModel.ByYear;
                await Settle();
                Save(window, "04-statistics-cash-by-year");
                av.Statistics.Period = StatisticsViewModel.ByMonth;
                av.Statistics.Figure = "Passengers";

                // My Carrier > Monthly report
                av.MyCarrierTab = CarrierViewModel.ReportTabIndex;
                await Shoot(window, "05-monthly-report");

                // Routes > Suggestions
                av.Page = PageOf("Routes");
                av.RoutesTab = CarrierViewModel.SuggestionsTabIndex;
                await Wait(() => !av.Suggestions.IsBusy, "the suggestions");
                await Shoot(window, "06-route-suggestions");

                // Stations > Manage: the list sorted by distance from an station
                av.Page = PageOf("Stations");
                av.StationsTab = 0;
                av.StationManager.OnlyMine = false;
                av.StationManager.Search = "Texas";
                av.StationManager.SortBy = "Distance";
                av.StationManager.Near = "DAL";
                await Settle();
                Save(window, "06b-stations-near");
                av.StationManager.SortBy = "Name";
                av.StationManager.Near = "";
                av.StationManager.Search = "";

                // Carriers > Competitors
                av.Page = PageOf("Carriers");
                av.CarriersTab = 1;
                await Shoot(window, "07-competitors");

                // News
                av.Page = PageOf("News");
                av.News.Reload();
                var report = av.News.Rows.FirstOrDefault(r => r.News.ActionObject is ReportLink) ?? av.News.Rows.FirstOrDefault();
                if (report != null) av.News.Selected = report;
                await Shoot(window, "08-news-report-link");

                // Trips > Timetable: the week, with the gate time after each trip
                var withTrips = me.Fleet.FirstOrDefault(x => x.Routes.Count > 0);
                if (withTrips != null)
                {
                    av.OpenSchedule(withTrips);
                    await Shoot(window, "11-timetable-gate-time");
                    Log("timetable: " + av.Timetable.Summary);
                }

                // My Carrier > Best and worst: the routes, then the coach
                av.Page = PageOf("My Carrier");
                av.MyCarrierTab = CarrierViewModel.RankingTabIndex;
                av.Ranking.What = TheBusProject.Presentation.ProfitRankingViewModel.Routes;
                // the most it will show either way, so the shot catches the list being taller than the screen:
                // at the smaller settings it fits, which is why nothing ever showed whether it scrolls
                av.Ranking.HowMany = av.Ranking.HowManyChoices.Max();
                av.Ranking.Reload();
                await Shoot(window, "10-best-and-worst-routes");
                av.Ranking.What = TheBusProject.Presentation.ProfitRankingViewModel.Coach;
                av.Ranking.Reload();
                await Shoot(window, "10b-best-and-worst-coach");
                Log("best and worst: " + av.Ranking.Summary);

                // Whether that list can scroll, which a screenshot cannot show: the carrier here has three
                // routes, so it never overflows however many rows are asked for. What the numbers settle is the
                // part that was in doubt. A finite extent the width of the viewport means the chart is being
                // offered a real width and is not laying itself out endlessly wide, which would leave the
                // scroller nothing to fit and so no bar to show. A height that tracks the chart's own desired
                // height means the extent follows the content, so the bar appears as soon as there is more of it
                // than fits.
                var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
                                     .FirstOrDefault(s => s.GetVisualDescendants().OfType<RankBarChart>().Any());
                if (scroller == null)
                {
                    Log("best and worst: no scroller found around the chart");
                }
                else
                {
                    var chart = scroller.GetVisualDescendants().OfType<RankBarChart>().First();
                    Log(string.Format(
                            "best and worst scrolling: extent {0:0}x{1:0}, viewport {2:0}x{3:0}, the chart wants {4:0}x{5:0}; scrolls {6}",
                            scroller.Extent.Width, scroller.Extent.Height,
                            scroller.Viewport.Width, scroller.Viewport.Height,
                            chart.DesiredSize.Width, chart.DesiredSize.Height,
                            scroller.Extent.Height > scroller.Viewport.Height ? "yes" : "not needed, it fits"));

                    if (double.IsInfinity(scroller.Extent.Width) || scroller.Extent.Width > scroller.Viewport.Width + 1)
                        Log("best and worst scrolling: FAULT the chart is wider than the viewport, so it was offered an endless width");
                    if (Math.Abs(scroller.Extent.Height - chart.DesiredSize.Height) > 1)
                        Log("best and worst scrolling: FAULT the extent does not follow the chart's height, so the bar will not appear when the list overflows");

                    // The carrier here has three routes, so the list fits and no bar is called for. Squeezing the
                    // panel for one shot is the only way to see what a player with a full list sees, which is the
                    // whole question: the bar is kept from hiding itself, so it should be there to look at.
                    scroller.MaxHeight = 90;
                    await Shoot(window, "10c-best-and-worst-squeezed");
                    var bar = scroller.GetVisualDescendants().OfType<ScrollBar>()
                                      .FirstOrDefault(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
                    Log(string.Format("best and worst squeezed to {0:0}: extent {1:0}, a vertical bar {2}",
                                      scroller.Viewport.Height, scroller.Extent.Height,
                                      bar == null ? "does not exist" : bar.IsEffectivelyVisible ? "is showing" : "exists but is hidden"));
                    if (bar == null || !bar.IsEffectivelyVisible)
                        Log("best and worst scrolling: FAULT the list overflows and there is still no bar to drag");
                    scroller.MaxHeight = double.PositiveInfinity;
                }

                // Coaches > New coach: a type with a choice of engines, ordered ahead of its first deliveries
                av.Page = PageOf("Coaches");
                av.NewCoach.Reload();
                var boeing = av.NewCoach.Manufacturers.FirstOrDefault(m => m.Name == "The Boeing Company");
                if (boeing != null)
                {
                    av.NewCoach.Manufacturer = boeing;
                    var type = av.NewCoach.Types.FirstOrDefault(t => t.Name == "Boeing 787-8") ?? av.NewCoach.Types.FirstOrDefault(t => t.Name.StartsWith("Boeing 777"));
                    if (type != null) av.NewCoach.Selected = type;
                }
                await Shoot(window, "09-new-coach-engines");

                Log("done");
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(_dir, "error.txt"), ex.ToString()); }
                catch (Exception) { }
                Log("failed: " + ex.Message);
            }
            // without the game's save on exit: this was a throwaway game
            Environment.Exit(0);
        }
    }
}
