using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ClashAutoFix.Revit.Models;

namespace ClashAutoFix.Revit.Detection
{
    /// <summary>
    /// The detection brain:
    /// 1) collect host and MEP elements,
    /// 2) find where they cross,
    /// 3) work out the size/position of each hole (plus the clearance tolerance).
    /// No model is changed here — we only read and calculate.
    /// </summary>
    public class IntersectionDetector
    {
        private readonly Document _doc;

        public IntersectionDetector(Document doc)
        {
            _doc = doc;
        }

        // ---- Collect the two lists: the hosts and the MEP services -----------------
        public List<HostElementInfo> CollectHosts(OpeningSettings s)
        {
            var cats = new List<BuiltInCategory>();
            if (s.IncludeWalls)    cats.Add(BuiltInCategory.OST_Walls);
            if (s.IncludeFloors)   cats.Add(BuiltInCategory.OST_Floors);
            if (s.IncludeBeams)    cats.Add(BuiltInCategory.OST_StructuralFraming);
            if (s.IncludeColumns)  cats.Add(BuiltInCategory.OST_StructuralColumns);
            if (s.IncludeCeilings) cats.Add(BuiltInCategory.OST_Ceilings);

            var result = new List<HostElementInfo>();
            foreach (var cat in cats)
            {
                var elems = new FilteredElementCollector(_doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var e in elems)
                {
                    var solid = GeometryHelper.GetSolid(e);
                    if (solid == null) continue;
                    result.Add(new HostElementInfo { Element = e, Category = cat , Solid = solid });
                }
            }
            return result;
        }

        public List<MepElementInfo> CollectMep(OpeningSettings s)
        {
            var map = new Dictionary<BuiltInCategory, OpeningShape>();
            if (s.IncludePipes)      map[BuiltInCategory.OST_PipeCurves] = OpeningShape.Round;
            if (s.IncludeConduits)   map[BuiltInCategory.OST_Conduit]    = OpeningShape.Round;
            if (s.IncludeDucts)      map[BuiltInCategory.OST_DuctCurves] = OpeningShape.Rectangular;
            if (s.IncludeCableTrays) map[BuiltInCategory.OST_CableTray]  = OpeningShape.Rectangular;

            var result = new List<MepElementInfo>();
            foreach (var kv in map)
            {
                var elems = new FilteredElementCollector(_doc)
                    .OfCategory(kv.Key)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var e in elems)
                {
                    var path = (e.Location as LocationCurve)?.Curve;
                    if (path == null) continue;

                    var info = new MepElementInfo
                    {
                        Element = e,
                        Category = kv.Key,
                        Path = path,
                        Shape = kv.Value
                    };
                    GeometryHelper.ReadMepSize(e, info);

                    // Read the service's own solid ONCE. Detection intersects solid against
                    // solid, and the scan loop tests every service against every host —
                    // re-extracting this geometry inside that loop would be costly.
                    info.Solid = GeometryHelper.GetSolid(e);

                    result.Add(info);
                }
            }
            return result;
        }

        // ---- Find the crossings and build each hole's "recipe" -------------

        public List<Penetration> Detect(OpeningSettings settings)
        {
            var hosts = CollectHosts(settings);
            var meps = CollectMep(settings);
            var penetrations = new List<Penetration>();

            foreach (var mep in meps)
            {
                foreach (var host in hosts)
                {
                    // Ask Revit's geometry: does the service overlap the host solid?
                    // Keep the WHOLE segment of the overlap, not just a midpoint. Its direction
                    // is the crossing angle and its length is how far the service travels
                    // through the host — both are needed to aim and stretch the void along the
                    // service instead of punching it straight through. Passing the service's
                    // SOLID (not just its centre line) is what catches a partially buried pipe,
                    // whose centre line never enters the host at all.
                    XYZ segStart, segEnd;
                    if (!GeometryHelper.TryFindPierce(host.Solid, mep.Solid, mep.Path,
                                                      out segStart, out segEnd))
                        continue;

                    // Apply the clearance tolerance: ToleranceFeet each side if enabled, else none.
                    double extra = settings.UseTolerance ? settings.ToleranceFeet : 0.0;

                    penetrations.Add(new Penetration
                    {
                        Host = host,
                        Mep = mep,
                        SegmentStart = segStart,
                        SegmentEnd = segEnd,
                        SegmentLength = segStart.DistanceTo(segEnd),
                        Point = (segStart + segEnd) / 2.0,              // middle of the crossing
                        Direction = (segEnd - segStart).Normalize(),    // the crossing angle
                        Shape = mep.Shape,
                        Width = mep.Width + 2 * extra,
                        Height = mep.Height + 2 * extra
                    });
                }
            }
            return penetrations;
        }
    }
}
