using System;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;

namespace TheBusProject.Infrastructure
{
    /// <summary>
    ///     The few things the game logic needs from whatever user interface hosts it. The UI project sets these
    ///     hooks at startup; when a hook is not set the logic simply carries on (tests, a headless run).
    /// </summary>
    public static class UserInterface
    {
        /// <summary>Shows a message to the player (title, text).</summary>
        public static Action<string, string> ShowMessage { get; set; }

        /// <summary>Lets the player pick a new homebase for an coach, or returns null.</summary>
        public static Func<FleetCoach, Station> ChooseHomebase { get; set; }

        /// <summary>Called when a scenario has been set up and the game should show the player's carrier.</summary>
        public static Action<Carrier> ScenarioStarted { get; set; }

        /// <summary>Builds the map images; called while the game data is loaded.</summary>
        public static Action PrepareMaps { get; set; }

        /// <summary>Shows or clears a busy indicator (true while the game data loads).</summary>
        public static Action<bool> SetBusy { get; set; }
    }
}
