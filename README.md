# AgOpenGPS - Guidance software

<!-- [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md -->

AgOpenGPS is now **cross-platform**. This repository **is** the cross-platform AgOpenGPS: it has been
re-platformed from .NET Framework 4.8 + Windows Forms (Windows-only) onto modern **.NET 8/9 +
[Avalonia UI](https://avaloniaui.net/)**, and runs natively on **Windows**, **macOS**, and **Linux**
while preserving **100% functional parity** with the previous Windows-only product. The migration is
documented under [`MIGRATION_DOCS/`](MIGRATION_DOCS/) — see the
[Changelog](MIGRATION_DOCS/CHANGELOG.md) for the change narrative and the
[Transition Map](MIGRATION_DOCS/TRANSITION_MAP.md) for the old → new file-by-file mapping.

[![GitHub Release](https://img.shields.io/github/v/release/agopengps-official/AgOpenGPS)](https://github.com/agopengps-official/AgOpenGPS/releases/latest)
[![Translation status](https://hosted.weblate.org/widget/agopengps/language-badge.svg)](https://hosted.weblate.org/engage/agopengps/)

Ag Precision Mapping and Section Control Software

AgOpenGPS is 2 programs. AgIO is the communication hub to the outside world and AgOpenGPS is the
application. You can run either and within each, you can run the other.

You only need to run AgOpenGPS if you are using the simulator.

The software reads NMEA strings for the purpose of recording and mapping position information
for Agricultural use. Also it has up to 16 sections of Section Control that can have unique widths
or up to 64 same width sections to control implements application of product preventing
over-application.

Also ouputs Pure pursuit steer angles from reference line for AB line, AB Curve and Contour guidance.
Auto Headland called UTurn on Curve and AB Line with loops for narrow equipment.
Mapping as a background can also be added.

The application now runs **natively on Windows, macOS, and Linux** (previously Windows-only). Core
guidance, Section Control, field I/O, and the PGN communication fabric behave identically on every
platform. A few non-essential conveniences — online background map imagery and webcam — are
**optional features available where the platform supports them** and are gracefully disabled
elsewhere, so they never affect startup or core guidance.

Included in this repository is an application, and source folders.

See the PCB repo for PCB layouts, firmware for steering and rate control, machine control, GPS and simulator.

## Installation

1. Download the [Most Stable AgOpenGPS Release](https://github.com/agopengps-official/AgOpenGPS/releases).
   Releases now ship **per-OS self-contained** artifacts — one archive per runtime identifier
   (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`) produced by the CI matrix — so no separate .NET
   runtime install is required. Pick the archive that matches your operating system.
2. Extract the contents to a folder that is writable by your user (not the root of `C:\`, and not a
   system-protected location). Your desktop or home folder is fine.
3. The extracted archive contains one subfolder per program — `AgOpenGPS/`, `AgIO/`, `ModSim/`,
   `GPS_Out/`, `AgDiag/`, and `Updater/` — alongside the `LICENSE` file. Start the main application from
   inside the `AgOpenGPS/` subfolder:
   - **Windows (win-x64):** run `AgOpenGPS\AgOpenGPS.exe`.
   - **Linux (linux-x64):** make the binary executable once with `chmod +x AgOpenGPS/AgOpenGPS`, then run `./AgOpenGPS/AgOpenGPS`.
   - **macOS (osx-x64 / osx-arm64):** run the `AgOpenGPS/AgOpenGPS` executable. Because the build is unsigned,
     Gatekeeper may ask you to allow it the first time (right-click → **Open**, or allow it from
     *System Settings → Privacy & Security*).

AgIO (the communication hub) is **auto-started and stopped by AgOpenGPS**, so you normally only launch
AgOpenGPS; the two-program model is preserved.

## Building

You no longer need Windows or Visual Studio to build AgOpenGPS — it builds on **Windows, macOS, or
Linux** using the cross-platform `dotnet` CLI.

1. Install the **.NET SDK 9.0.300** (pinned in [`global.json`](global.json)).
2. Clone this repository.
3. Open the solution (`SourceCode/AgOpenGPS.sln`) in your editor of choice — Visual Studio 2022+,
   Visual Studio Code, or JetBrains Rider — or just use the `dotnet` CLI.
4. Add your code and (re)build.
5. Build the whole solution with the `dotnet` CLI:
   ```sh
   dotnet build SourceCode/AgOpenGPS.sln -c Release
   ```
   To produce runnable, distributable apps, use the per-project **publish** commands below.

To produce a **per-OS self-contained** distribution (the .NET runtime is bundled, so the target machine
needs no SDK installed), publish **each executable project** for the runtime identifier (RID) you want.
A solution-level `dotnet publish SourceCode/AgOpenGPS.sln` is **not** used: the solution also contains
class libraries and test projects (which must not be published with a RID), and the `AgOpenGPS`/`AgIO`
apps multi-target `net8.0;net8.0-windows`, so the framework must be paired to the RID. These are the
exact commands the release CI ([`.github/workflows/release.yml`](.github/workflows/release.yml)) runs.
The example below targets `linux-x64`; for another OS, substitute the RID (`win-x64`, `linux-x64`,
`osx-x64`, or `osx-arm64`) and, for `AgOpenGPS`/`AgIO`, pair the framework — `-f net8.0-windows` for
`win-x64`, `-f net8.0` for the others:

```sh
# Multi-targeted apps — framework paired to the RID with -f (win-x64 -> net8.0-windows):
dotnet publish SourceCode/GPS/AgOpenGPS.csproj    -c Release -r linux-x64 -f net8.0 --self-contained true -o publish/linux-x64/AgOpenGPS
dotnet publish SourceCode/AgIO/Source/AgIO.csproj -c Release -r linux-x64 -f net8.0 --self-contained true -o publish/linux-x64/AgIO

# Single-target (net8.0) executables — no -f:
dotnet publish SourceCode/ModSim/Source/ModSim.csproj      -c Release -r linux-x64 --self-contained true -o publish/linux-x64/ModSim
dotnet publish SourceCode/GPS_Out/Source/GPS_Out.csproj    -c Release -r linux-x64 --self-contained true -o publish/linux-x64/GPS_Out
dotnet publish SourceCode/AgDiag/AgDiag.csproj             -c Release -r linux-x64 --self-contained true -o publish/linux-x64/AgDiag
dotnet publish SourceCode/Updater/AgOpenGPS.Updater.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64/Updater
```

This produces a `publish/linux-x64/` tree with `AgOpenGPS/`, `AgIO/`, `ModSim/`, `GPS_Out/`, `AgDiag/`,
and `Updater/` subfolders — the same layout the release archives use — so the main executable lands at
`AgOpenGPS/AgOpenGPS` (or `AgOpenGPS\AgOpenGPS.exe` on Windows).

`dotnet build` and `dotnet test` also work on all three operating systems, and continuous integration
now validates the solution on a **windows / ubuntu / macos** matrix.

Settings and profiles are stored under a per-OS configuration root — `%AppData%\AgOpenGPS` on Windows,
`~/.config/AgOpenGPS` on Linux, and `~/Library/Application Support/AgOpenGPS` on macOS. On Windows,
existing settings are migrated one time from the legacy Windows Registry / `%AppData%` location; the
settings XML schema itself is unchanged.

## Contributing

The `master` branch contains the most stable version of AgOpenGPS, while the `develop` branch
is actively being worked on and may not be ready for production use.

In order to contribute to AgOpenGPS, follow these steps:

1. Checkout the `develop` branch
2. Create a new branch named after your feature
3. Make your changes and commit to this branch
4. Create a PR targeting the `develop` branch

## Translation

We use [Weblate](https://weblate.org) to manage translations for this project.

If you want to help translate AgOpenGPS, follow these steps:

1. Create (or log in to) your free account on [Weblate](https://hosted.weblate.org)
2. Go to the [AgOpenGPS Project on Weblate](https://hosted.weblate.org/engage/agopengps)
3. Select your language (or add a new one if it's missing)
4. Translate strings directly in the web interface

### Translation Status

[![Translation status](https://hosted.weblate.org/widget/agopengps/multi-auto.svg)](https://hosted.weblate.org/engage/agopengps/)

## Links

- [AgOpenGPS Documentation](https://docs.agopengps.com/)
- [AgOpenGPS Forum](https://discourse.agopengps.com/)
- [PCB and Firmware Repository](https://github.com/agopengps-official/Boards)
- [SK21 Rate Control Repository](https://github.com/agopengps-official/Rate_Control)
- [Migration Documentation (`MIGRATION_DOCS/`)](MIGRATION_DOCS/)

## License

AgOpenGPS is distributed under two licenses. The repository root is licensed under the
**Apache License 2.0** (see [`LICENSE`](LICENSE)), while the **GPS** application and the **Updater**
are licensed under the **GNU GPLv3** (see `SourceCode/GPS/License.txt` and
`SourceCode/Updater/License.txt`). These license artifacts are retained unchanged.

If you distribute copies of such a program, whether
gratis or for a fee, you must pass on to the recipients the same
freedoms that you received.  You must make sure that they, too, receive
or can get the source code.  And you must show them these terms so they
know their rights as Outlined in the GPLv3 License.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
