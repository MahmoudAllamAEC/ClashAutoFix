# Clash Auto Fix

A Revit 2024 add-in that finds where MEP services (pipes, ducts, cable trays,
conduits) pass through structure and architecture (walls, floors, beams, columns,
ceilings) and automatically cuts a correctly sized, correctly angled opening —
a "sleeve" void — through each one.

Unlike a straight-through punch, it follows the service: a pipe crossing a slab at
45°, or one only partially buried in it, gets an opening aligned to the *actual*
line of the service, at the *actual* angle, sized to the *actual* run through the
host.

## What it does

- **Detects real overlaps, not just centre-line hits.** Detection intersects the
  host's solid with the *service's* solid, so a service that only clips or grazes
  the host — one whose centre line never enters — is still caught.
- **Follows the crossing angle.** The opening is placed unhosted, aimed along the
  crossing segment, and stretched to its length, so angled penetrations get an
  angled hole instead of a square one that still clashes.
- **Squares rectangular sleeves to the duct.** Duct/tray openings are rolled so the
  void's cross-section lines up with the service's own up direction.
- **Optional clearance tolerance.** Add an even gap (in mm) around every opening.
- **Handles services running *along* a host** as a deliberate opt-in — off by
  default, since cutting one carves a trench rather than a hole.
- **Preview before you commit.** *Scan* counts the openings without touching the
  model; *Create Openings* makes them, all inside a single transaction (one Undo).
- **Explains itself.** If a run places nothing, it says why — missing sleeve family,
  a host that can't be cut, a family missing its `Length` parameter, and so on.

## Requirements

- Autodesk Revit 2024
- .NET Framework 4.8.1, x64
- The two sleeve families (below), loaded into your project.

## The sleeve families

The tool places one of two work-plane-based void families. Make them once and keep
them loaded (or in your template):

| Family | For | Required instance parameters |
|--------|-----|------------------------------|
| `CAF_Sleeve_Round` | pipes, conduits | `Diameter`, `Length` |
| `CAF_Sleeve_Rect`  | ducts, cable trays | `Width`, `Height`, `Length` |

Each family must:

- be a **work-plane-based** family whose form is a **Void**;
- have "Cut with Voids When Loaded" enabled and "Always Vertical" **disabled**;
- drive the void's extrusion length from a **`Length` instance parameter** (no
  formula) — `Depth` is accepted as an alias;
- extrude along the family's local Z, with the profile sketched in plan and the
  origin at the centre of the cross-section.

## Build & install

1. Open `ClashAutoFix.csproj` in Visual Studio (build as **x64**).
2. Point the two Revit `HintPath`s in the `.csproj` at your Revit 2024
   `RevitAPI.dll` and `RevitAPIUI.dll`, and keep **Copy Local = False**.
3. Build. Copy `ClashAutoFix.addin` (edited to point at your built
   `ClashAutoFix.dll`) into `%AppData%\Autodesk\Revit\Addins\2024\`.
4. Start Revit → **AACD Architect** tab → **General** panel → **Clash Fix**.

## Using it

1. Click **Clash Fix** to open the window.
2. Tick the host categories and service categories to check, set a tolerance if you
   want one, then click **Scan** to see how many openings would be made.
3. Click **Create Openings** to cut them. Re-running is safe — an opening already at
   a crossing is recognised and skipped, not duplicated.

## Project layout

| File | Job |
|------|-----|
| `ClashAutoFix.addin` | Tells Revit to load the add-in. |
| `Revit/Entry/ExtApp.cs` | Runs at startup; builds the ribbon button. |
| `Revit/Entry/ExtCmd.cs` | Runs on click; opens the window. |
| `Revit/Entry/DependencyResolver.cs` | Finds helper DLLs at runtime. |
| `Revit/Models/Models.cs` | Data classes (host, MEP, penetration, settings, report). |
| `Revit/Detection/IntersectionDetector.cs` | Collects elements, finds crossings, sizes holes. |
| `Revit/Geometry/GeometryHelper.cs` | Solids, MEP sizes, and the pierce segment. |
| `Revit/Services/OpeningService.cs` | Places and cuts the sleeves inside a Transaction. |
| `UI/ViewModels/MainViewModel.cs` | Settings + Scan (preview) + Create. |
| `UI/Views/MainWindow.xaml(.cs)` | The window the user sees. |
| `UI/Commands/`, `UI/ViewModels/ViewModelBase.cs` | Small MVVM plumbing. |

## Known limitations

- The scan compares every service against every host, so very large models take a
  while; a bounding-box pre-filter is the natural next step.
- Pipe/duct insulation is not added to the opening size.
- Round/oval ducts are not yet sized (rectangular ducts and round pipes are).

## Credits

Ribbon icon from [Icons8](https://icons8.com).

## License

_Add the license of your choice here before publishing._
