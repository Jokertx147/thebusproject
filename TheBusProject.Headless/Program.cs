using System;
using System.Linq;
using System.Threading;
using TheBusProject.Helpers;
using TheBusProject.Helpers.Workers;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Environment;

class P {
  static void Main(string[] args) {
    if (args.Length > 0 && args[0] == "vm") { VmTest.Run().GetAwaiter().GetResult(); return; }
    try {
      GameDataLoader.EnsureLoaded();
      Console.WriteLine("LOADED stations=" + TheBusProject.Models.Stations.Stations.GetAllStations().Count);
      var carrier = Carriers.GetAllCarriers().First(a => a.Profile.IATACode == "DL" || a.Profile.IATACode == "AA");
      var station = carrier.Stations.FirstOrDefault() ?? TheBusProject.Models.Stations.Stations.GetAllStations().First(a => a.Profile.IATACode == "JFK");
      var sd = new StartDataObject {
        Carrier = carrier, Station = station, CEO = "Test CEO",
        Continent = new Continent("100", "All continents"),
        Difficulty = DifficultyLevels.GetDifficultyLevel("Normal"),
        Focus = default(CarrierFocus), HomeCountry = station.Profile.Country,
        IsPaused = true, LocalCurrency = false, MajorStations = true, NumberOfOpponents = 3, RandomOpponents = true,
        RealData = false, Region = Regions.GetRegion("100"), SameRegion = true,
        TimeZone = TimeZones.GetTimeZones().First(), UseDayTurns = true, Year = 2007 };
      GameObjectHelpers.CreateGame(sd);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      while (!GameObjectWorker.GetInstance().IsBusy() && sw.Elapsed.TotalSeconds < 10) Thread.Sleep(200);
      Console.WriteLine("worker started after " + sw.Elapsed.TotalSeconds + "s (demand built)");
      GameObjectWorker.GetInstance().Pause();
      var g = GameObject.GetInstance();
      Console.WriteLine("human=" + g.HumanCarrier.Profile.Name + " start=" + g.GameTime.ToShortDateString() + " money=" + g.HumanCarrier.Money);
      for (int i = 0; i < 40; i++) GameObjectHelpers.SimulateTurn();
      Console.WriteLine("after 40 days: " + g.GameTime.ToShortDateString() + " money=" + g.HumanCarrier.Money);
      Console.WriteLine("SIMULATED OK");
      var before = g.GameTime; var money = g.HumanCarrier.Money;
      SerializedLoadSaveHelpers.SaveGame("headless-test");
      Console.WriteLine("SAVED: " + string.Join(", ", LoadSaveHelpers.GetSavedGameInfos().Select(x => x.Name)));
      SerializedLoadSaveHelpers.LoadGame("headless-test");
      var g2 = GameObject.GetInstance();
      Console.WriteLine("LOADED SAVE: " + g2.GameTime.ToShortDateString() + " money=" + g2.HumanCarrier.Money + " (expected " + before.ToShortDateString() + " / " + money + ")");
      GameObjectWorker.GetInstance().Pause();
      for (int i = 0; i < 10; i++) GameObjectHelpers.SimulateTurn();
      Console.WriteLine("10 more days after load: " + g2.GameTime.ToShortDateString());
      SerializedLoadSaveHelpers.DeleteSavedGame("headless-test");
      Console.WriteLine("ROUNDTRIP OK");
    } catch (Exception e) { Console.WriteLine("FAILED: " + e); }
  }
}
