# The Bus Project

An intercity coach operator simulation for the United States. You buy, lease or
pick up second-hand coaches, put them on routes between cities, set the fares
and the timetable, and try to make the thing pay against the operators already
running those roads.

It is in early development. Nothing here is playable yet.

## Where it came from

The engine began as [The Airline Project](https://github.com/TheAirlineProject/tap-desktop),
a C# airline simulation, by way of a port to modern .NET. Almost everything an
airline simulation does, an intercity coach operator needs as well: a clock that
advances a day at a time, routes with timetables, demand between places,
fares, vehicles that are owned or leased and cost money either way, rival
operators that make their own decisions, and books that have to balance. What
changes is the vocabulary, the vehicles, the geography and the rules of the
road.

The airline's own vocabulary has been renamed throughout:

| Was | Is | Why |
| --- | --- | --- |
| Airline | Carrier | US law calls an intercity bus company a motor carrier. `operator` is a C# keyword, so that word was out. |
| Airport | Station | A bus station. `Terminal` and `Gate` are kept for a building and a bay within a station. |
| Airliner, Aircraft | Coach | |
| Flight | Trip | One run of a route in one direction. |

## Layout

| Project | What it is |
| --- | --- |
| `TheBusProject.Core` | The simulation: the clock, the model, the money, the rival carriers. No user interface. |
| `TheBusProject.Presentation` | View models. Everything the screens need, with no drawing in it. |
| `TheBusProject.Desktop` | The game itself, in Avalonia, so it runs on Linux and Windows. |
| `TheBusProject.Headless` | Runs the simulation with no screen at all. This is what the tests use. |
| `Data/Data` | The game's data as XML: places, vehicles, carriers. |

## Building and running

Needs the .NET 10 SDK.

```bash
dotnet run -c Release --project TheBusProject.Desktop
```

## Tests

The tests are modes of the headless runner rather than unit tests: each one
starts a real game, plays it, and checks what happened. They run on every push.

```bash
dotnet run --no-build -c Release --project TheBusProject.Headless -- vm quick
```

`.github/workflows/build.yml` lists them all.
