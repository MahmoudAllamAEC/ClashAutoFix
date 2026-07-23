# 🕳️ Clash Auto Fix — Revit Add-in

<div align="center">

[![Revit API](https://img.shields.io/badge/Revit%20API-2024–2025-blue?style=for-the-badge&logo=autodesk)](https://www.autodesk.com/developer-network/platform-technologies/revit)
[![Language](https://img.shields.io/badge/C%23-.NET-purple?style=for-the-badge&logo=csharp)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/Version-1.0-green?style=for-the-badge)]()
[![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)]()

**A Revit API plugin that finds where MEP services pass through structure and architecture — and automatically cuts a correctly sized, correctly angled opening through every clash. No more manual sleeve placement, no more square holes that still collide.**

[▶️ Watch Demo Video](https://drive.google.com/file/d/13zZ-h9qhlQ8iFjx4V8-Qj_aX-fviT-f6/view?usp=drive_link) • [📥 Download Installer](https://github.com/MahmoudAllamAEC/ClashAutoFix/releases/download/RevitAPI/ClashAutoFix.exe)

</div>

---

## 🎬 Demo

> Click the thumbnail below to watch the full walkthrough

[![Clash Auto Fix Demo](https://img.shields.io/badge/▶_Watch_Full_Demo-Google_Drive-red?style=for-the-badge&logo=google-drive)](https://drive.google.com/file/d/13zZ-h9qhlQ8iFjx4V8-Qj_aX-fviT-f6/view?usp=drive_link)
<!-- Replace the line below with an actual screenshot of your plugin UI -->
<!-- ![Clash Auto Fix UI](docs/screenshots/ui-preview.png) -->

---

## 🧩 What Problem Does It Solve?

Cutting openings for MEP penetrations is one of the most repetitive, error-prone jobs in coordination. Done by hand it means:

- Hunting through the model for every pipe, duct, tray and conduit that crosses a wall or slab
- Placing a sleeve at each one, then rotating and resizing it to match the service
- Getting the angle wrong on sloped or skewed runs — leaving a hole that still clashes
- Doing it all again every time the MEP model changes

**Clash Auto Fix eliminates all of that.** Pick which hosts and services to check, hit **Scan** to preview, then **Create Openings** — the add-in detects every real overlap, places a void sleeve aligned to the *actual* line of the service, sizes it to the *actual* run through the host, and cuts a clean hole. All in a single undoable transaction.

---

## ✨ Features

| Feature | Description |
|---|---|
| 🎯 **Solid-based detection** | Intersects the host's solid with the *service's* solid — so a service that only clips or grazes the host, whose centre line never enters, is still caught |
| 📐 **Follows the crossing angle** | Openings are placed aimed along the crossing segment and stretched to its length, so a pipe crossing a slab at 45° gets an angled hole, not a square one that still clashes |
| 🔲 **Squares rectangular sleeves** | Duct and cable-tray openings are rolled so the void's cross-section lines up with the service's own up direction |
| 📦 **Self-contained families** | The two void sleeve families ship *inside* the add-in DLL and load themselves into your project automatically — nothing extra to install or keep in your template |
| 📏 **Adjustable clearance** | Add an even tolerance gap (in mm) around every opening |
| ↔️ **Along-host opt-in** | Services running *along* a host are ignored by default (cutting one carves a trench, not a hole) — enable it deliberately when you need it |
| 👁️ **Preview before you commit** | *Scan* counts the openings without touching the model; *Create Openings* makes them all inside one transaction (one Undo) |
| ♻️ **Safe to re-run** | An opening already at a crossing is recognised and skipped, never duplicated — re-run after every MEP change |
| 💬 **Explains itself** | If a run places nothing, it says why — missing sleeve family, a host that can't be cut, a family missing its `Length` parameter, and so on |
| 🪟 **Modeless-friendly UI** | A clean WPF window with category filters, tolerance and status — pick, scan, cut |

---

## 🖥️ Screenshots

<div align="center">

<!-- Add your actual screenshots below -->
<!-- Drag screenshots into the repo under docs/screenshots/ then update these paths -->

| Plugin UI | Scan Preview | Openings Cut |
|---|---|---|
| ![UI](https://github.com/MahmoudAllamAEC/ClashAutoFix/blob/master/Snipaste_2026-07-23_23-02-58.png) | ![Before](https://github.com/MahmoudAllamAEC/ClashAutoFix/blob/master/Snipaste_2026-07-23_23-05-01.png) | ![Result](https://github.com/MahmoudAllamAEC/ClashAutoFix/blob/master/Snipaste_2026-07-23_23-04-14.png) |

</div>

---

## 🔧 Tech Stack

- **Language:** C# (LangVersion 7.3, shared across both builds)
- **API:** Autodesk Revit API
- **UI:** WPF (MVVM pattern)
- **Targets:** .NET Framework 4.8.1 (Revit 2024) · .NET 8 (Revit 2025), x64
- **Geometry:** Solid–solid intersection for true clash detection; face-hosted void cuts via `InstanceVoidCutUtils`
- **Packaging:** Sleeve families + icons embedded as assembly resources — no external dependencies

---

## 📦 Installation

### Option 1 — Installer (Recommended)

1. Download the latest release from the [Releases page](REPLACE_WITH_YOUR_RELEASE_LINK)
2. Run the setup file — it installs for your detected Revit version(s)
3. Launch Revit and go to the **AACD Architect** tab → **General** panel
4. Click **Clash Fix** to launch

### Option 2 — Manual (from source)

1. Clone this repository
2. Point the two Revit `HintPath`s in the `.csproj` at your Revit `RevitAPI.dll` and `RevitAPIUI.dll` (keep **Copy Local = False**)
3. Build the matching project:
   - **Revit 2024:** open `ClashAutoFix.csproj` in Visual Studio, build as **x64**
   - **Revit 2025:** `dotnet build ClashAutoFix.2025.csproj -c Release`
4. Copy `ClashAutoFix.addin` (edited to point at your built `ClashAutoFix.dll`) and the DLL to:
   ```
   %APPDATA%\Autodesk\Revit\Addins\{RevitVersion}\
   ```

> **No family setup required.** The `CAF_Sleeve_Round` and `CAF_Sleeve_Rect` void families are embedded in the DLL and loaded into your document automatically the first time you cut openings.

---

## ✅ Compatibility

| Revit Version | Runtime | Status |
|---|---|---|
| Revit 2024 | .NET Framework 4.8.1 | ✅ Supported |
| Revit 2025 | .NET 8 | ✅ Supported |

---

## 🚀 How to Use

1. Open a Revit project containing MEP services (pipes, ducts, cable trays, conduits) that penetrate structure or architecture
2. Go to **AACD Architect tab → General → Clash Fix**
3. In the window, tick the **host categories** to cut into (Walls, Floors, Beams, Columns, Ceilings) and the **service categories** to check (Pipes, Ducts, Cable Trays, Conduits)
4. *(Optional)* Enable **Tolerance** and enter a clearance in mm to leave an even gap around each opening
5. *(Optional)* Enable **Cut services running along hosts** if you deliberately want openings for parallel runs
6. Click **Scan** to see how many openings would be made — nothing is changed yet
7. Click **Create Openings** to cut them all in one undoable step
8. Edit the MEP model as it evolves and re-run — existing openings are recognised and skipped, so only new clashes get cut

---

## 🎨 Understanding the Status

The **Status** panel reports the outcome of every run and, when nothing is placed, explains why:

| Message type | Meaning |
|---|---|
| ✅ Openings created | Each detected clash got a sleeve void cut through its host |
| ⏭️ Skipped (already cut) | An opening already exists at that crossing — no duplicate made |
| ⚠️ No host face | The host (e.g. a beam or column) has no cuttable face for that penetration |
| ⚠️ No matching family | The sleeve family for that service couldn't be loaded |
| ⚠️ Missing `Length` | The sleeve family has no `Length` (or `Depth`) instance parameter to drive the cut depth |

> **Pipes cut a round sleeve; ducts and trays cut a rectangular one** — each aligned and sized to the service that pierces the host.

---

## 📁 Project Structure

```
ClashAutoFix/
├── Revit/
│   ├── Entry/
│   │   ├── ExtApp.cs                 # Startup: builds the AACD Architect ribbon button
│   │   ├── ExtCmd.cs                 # On click: opens the tool window
│   │   └── DependencyResolver.cs     # Resolves helper DLLs at runtime
│   ├── Models/
│   │   └── Models.cs                 # Host, MEP, penetration, settings & report data classes
│   ├── Detection/
│   │   └── IntersectionDetector.cs   # Collects elements, finds crossings, sizes holes
│   ├── Geometry/
│   │   └── GeometryHelper.cs         # Solids, MEP sizes, and the pierce segment
│   └── Services/
│       ├── OpeningService.cs         # Places & cuts sleeves inside one Transaction
│       └── SleeveFamilyProvider.cs   # Auto-loads the embedded sleeve families
├── UI/
│   ├── Commands/                     # RelayCommand
│   ├── ViewModels/                   # MainViewModel (Scan + Create), ViewModelBase
│   └── Views/                        # MainWindow.xaml (+ .cs)
├── Resources/                        # Sleeve families (.rfa) + ribbon icons (embedded)
├── ClashAutoFix.csproj               # Revit 2024 build (.NET Framework 4.8.1)
├── ClashAutoFix.2025.csproj          # Revit 2025 build (.NET 8) — same source
├── ClashAutoFix.addin
└── README.md
```

---

## 🗺️ Roadmap

- [ ] Bounding-box pre-filter for faster scans on very large models
- [ ] Add pipe/duct insulation thickness to the opening size
- [ ] Size round & oval ducts (rectangular ducts and round pipes are supported today)
- [ ] Full beam / column / ceiling hosting (walls & floors are verified)
- [ ] Revit 2022 / 2023 back-support and Revit 2026 support
- [ ] Write penetration data back to Revit parameters / schedules
- [ ] Provision-for-void workflow (MEP proposes → structural approves → cut) via IFC/BCF

---

## 👤 Author

**Mahmoud Amr Allam**
Architect & BIM Software Developer

[![LinkedIn](https://img.shields.io/badge/LinkedIn-Connect-blue?style=flat-square&logo=linkedin)](https://www.linkedin.com/in/mahmoud-allam-4a25b4172/)
[![Email](https://img.shields.io/badge/Email-mahmoud.amr55@gmail.com-red?style=flat-square&logo=gmail)](mailto:mahmoud.amr55@gmail.com)
[![GitHub](https://img.shields.io/badge/GitHub-MahmoudAllamAEC-black?style=flat-square&logo=github)](https://github.com/MahmoudAllamAEC)

---

## 🙏 Credits

Ribbon icon from [Icons8](https://icons8.com).

---

<div align="center">

⭐ **If this project helped you, please give it a star!** ⭐

</div>
