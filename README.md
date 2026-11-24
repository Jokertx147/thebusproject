# The Bus Project

An intercity coach operator simulation for the United States. You buy, lease or
pick up second-hand coaches, put them on routes between cities, set the fares
and the timetable, and try to make the thing pay against the operators already
running those roads.

It is in early development, and it plays: you can buy a coach new or
second-hand, lease one, hire drivers, open a route with a timetable and fares,
run it and read the books.

## What is in it

**The coaches.** 39 passenger types and four parcel coaches, from Van Hool,
Temsa, MCI, Prevost and Mercedes-Benz (Setra), and from the decades before
them: GM's PD-4104 through the PD-4903 Buffalo, Flxible's Clipper and
VistaLiner, Eagle's Models 05 to 20, the MCI MC-5A, MC-7 Challenger and MC-8.
Each with the years it was built, so a game started in 1962 sells you what was
on sale in 1962. Fourteen diesels to choose between, from the Detroit Diesel
8V-71 to the Cummins ISX12, with a Volvo in the Prevosts and a Mercedes in the
Setras.

**The map.** 52 US cities, with their bays, their passengers a day and the
demand between them. A coach market, not an airline's: demand peaks between
about 200 and 450 km, halves by 900 and is all but gone past 2,000.

**The competition.** Twelve carriers, from Greyhound and Trailways down to
Vonlane and RedCoach, plus a parcel line, each with a home city, a fleet of its
era and a mind of its own. Two interline alliances. Three charter contracts.

**Two scenarios.** *Deregulation, 1982*: the Bus Regulatory Reform Act has just
handed you your own routes and fares, and you have eight years to still be
standing. *The Curbside Boom, 2008*: four-dollar diesel, dollar fares and
double-deckers.

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

### On Linux, from a clean clone

```bash
git clone https://github.com/Jokertx147/thebusproject.git
cd thebusproject
dotnet run -c Release --project TheBusProject.Desktop
```

The build copies `Data/Data` next to the binary itself, so there is nothing to
install or point at: the game finds its own stations, coaches and map.

Needs the .NET 10 SDK. On Ubuntu 24.04 and anything built on it, including
Pop!_OS 24.04, it is in the archive:

```bash
sudo apt install dotnet10
```

On 22.04 it comes from the backports PPA:

```bash
sudo add-apt-repository ppa:dotnet/backports
sudo apt install dotnet10
```

Anywhere else, or without root, Microsoft's script puts it under your home
directory:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export PATH="$HOME/.dotnet:$PATH"
```

`dotnet --version` should say 10 before you build.

### Without building it at all

Every green push publishes a self-contained Linux build that needs no .NET
installed:

```bash
gh run download -n TheBusProject-Linux-x64
tar xzf TheBusProject-Linux-x64.tar.gz
./TheBusProject/TheBusProject
```

### If the window does not open

Avalonia wants the usual X libraries. On a desktop install they are already
there; on a bare one:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
```

## Tests

The tests are modes of the headless runner rather than unit tests: each one
starts a real game, plays it, and checks what happened. They run on every push.

```bash
dotnet run --no-build -c Release --project TheBusProject.Headless -- vm quick
```

`.github/workflows/build.yml` lists them all.
