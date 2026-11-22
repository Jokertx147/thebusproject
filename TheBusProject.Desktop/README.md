# TheBusProject.Desktop (Avalonia, Linux / macOS / Windows)

Minimal cross-platform UI: main menu, new game, load/save, and the airline screen with the clock, money, fleet and routes.
It uses `TheBusProject.Core` (game logic) and `TheBusProject.Presentation` (view models, no UI framework).

Requirements: .NET 10 SDK. From the repository folder:

    dotnet run --project TheBusProject.Desktop

The first start loads the game data (a few seconds). Saves go to the `data/saves` folder next to the program.
Headless check of the view models without a window: `dotnet run --project TheBusProject.Headless -- vm`.
