using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace ClashAutoFix.Revit.Models
{
    // Simple "labelled boxes" that carry data between the detection and cutting
    // steps. Wrapping the raw Revit objects here keeps the rest of the code tidy.

    /// <summary>What shape of hole to cut — round (pipes/conduit) or rectangular (ducts/tray).</summary>
    public enum OpeningShape { Round, Rectangular }

    /// <summary>A host we can cut a hole IN (wall / floor / beam / column / ceiling).</summary>
    public class HostElementInfo
    {
        public Element Element { get; set; }
        public BuiltInCategory Category { get; set; }
        public Solid Solid { get; set; }   // the host's 3D shape, used for intersection tests
    }

    /// <summary>An MEP service that must pass through (pipe / duct / tray / conduit).</summary>
    public class MepElementInfo
    {
        public Element Element { get; set; }
        public BuiltInCategory Category { get; set; }
        public Curve Path { get; set; }            // centre line of the service
        public OpeningShape Shape { get; set; }    // round for pipes/conduit, rect for ducts/tray

        // The service's own 3D shape. Detection intersects SOLID against SOLID rather than
        // centre-line against solid, so this is read ONCE here instead of being re-extracted
        // for every host inside the scan loop.
        public Solid Solid { get; set; }

        // Raw size in Revit internal units (feet). For round: Width == Height == diameter.
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>
    /// One detected crossing = one hole to make. Produced by the detector and
    /// consumed by the opening service.
    /// </summary>
    public class Penetration
    {
        public HostElementInfo Host { get; set; }
        public MepElementInfo Mep { get; set; }

        // The piece of the service that actually lies INSIDE the host. This is what the
        // opening gets aligned to. Its direction is the angle the service crosses at and its
        // length is how far the service travels through the host — so an angled crossing is
        // handled automatically, with no trigonometry anywhere.
        public XYZ SegmentStart { get; set; }
        public XYZ SegmentEnd { get; set; }
        public double SegmentLength { get; set; }

        public XYZ Point { get; set; }             // middle of that segment (where the void goes)
        public XYZ Direction { get; set; }         // direction of that segment (what the void aims along)

        public OpeningShape Shape { get; set; }
        public double Width { get; set; }          // final size AFTER tolerance (feet)
        public double Height { get; set; }

        // Filled in after the cut so the summary can report it.
        public ElementId CreatedOpeningId { get; set; }
    }

    /// <summary>
    /// Everything the user chose in the window. Tolerance is stored in feet
    /// internally; the UI shows millimetres.
    /// </summary>
    public class OpeningSettings
    {
        // These must be auto-properties, not fields: WPF data binding can only read/write
        // PROPERTIES, so a "{Binding Settings.IncludeWalls}" against a field binds to
        // nothing and the checkbox never writes back here.

        // Which host categories to consider.
        public bool IncludeWalls { get; set; } = false;
        public bool IncludeFloors { get; set; } = false;
        public bool IncludeBeams { get; set; } = false;
        public bool IncludeColumns { get; set; } = false;
        public bool IncludeCeilings { get; set; } = false;

        // Which MEP categories to detect.
        public bool IncludePipes { get; set; } = false;
        public bool IncludeDucts { get; set; } = false;
        public bool IncludeCableTrays { get; set; } = false;
        public bool IncludeConduits { get; set; } = false;

        // The clearance tolerance option.
        public bool UseTolerance { get; set; } = true;
        public double ToleranceFeet { get; set; } = 0.0;   // set from the UI (mm -> feet)

        // A service running ALONG a host (a pipe buried in a slab) is normally a routing
        // mistake, and cutting it carves a continuous trench through the structure rather
        // than a hole — so it is off unless the user deliberately asks for it.
        public bool CutServicesAlongHosts { get; set; } = false;
    }

    /// <summary>
    /// What actually happened during one "Create Openings" run. Each skip is counted
    /// against its reason, so a run that places nothing can say WHY — whether the scan
    /// was empty, the sleeve families were missing, or the holes already existed —
    /// instead of just reporting "0 openings created".
    /// </summary>
    public class CreationReport
    {
        public int Created { get; set; }           // sleeve placed AND host successfully cut
        public int SkippedExisting { get; set; }   // re-run guard: a sleeve was already at that point
        public int SkippedNoFamily { get; set; }   // no sleeve family of that shape is loaded

        // The cut can fail for reasons other than a missing family. Each is counted
        // separately so the window can explain exactly why a hole didn't appear.
        public int SkippedHostNotCuttable { get; set; }  // host category cannot take a void cut
        public int FailedCut { get; set; }               // the cut operation threw for this clash

        // The service runs ALONG the host instead of through it (e.g. a pipe buried in a
        // slab). That isn't a penetration — cutting a trench the length of the run would be
        // wrong — so it is skipped and reported instead.
        public int SkippedRunsAlongHost { get; set; }

        // The sleeve family has no "Length" instance parameter, so the void cannot be
        // stretched to span the crossing. Reported so the family can be fixed.
        public int SkippedNoLengthParam { get; set; }

        /// <summary>Family names this run searched for but could not find.</summary>
        public List<string> MissingFamilies { get; } = new List<string>();
    }
}
