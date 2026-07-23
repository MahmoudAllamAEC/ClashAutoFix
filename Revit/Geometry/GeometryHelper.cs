using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;   // Pipe
using Autodesk.Revit.DB.Mechanical; // Duct
using Autodesk.Revit.DB.Electrical; // Conduit, CableTray
using ClashAutoFix.Revit.Models;

namespace ClashAutoFix.Revit.Detection
{
    /// <summary>
    /// The "fiddly maths" bits — solids, sizes, and the pierce segment — kept in
    /// one place so the detector reads cleanly.
    /// </summary>
    public static class GeometryHelper
    {
        // Fine detail so we get the true, most-detailed solid; ComputeReferences so its
        // faces and edges carry usable references.
        private static readonly Options GeoOpt = new Options
        {
            ComputeReferences = true,
            DetailLevel = ViewDetailLevel.Fine
        };

        /// <summary>Pull the biggest solid out of an element (the host's shape).</summary>
        public static Solid GetSolid(Element e)
        {
            GeometryElement geo = e.get_Geometry(GeoOpt);
            if (geo == null) return null;

            // Keep the largest solid we find — that is the element's real body.
            Solid best = null;
            foreach (GeometryObject go in geo)
            {
                var s = go as Solid;
                // Volume > 1e-6 skips tiny slivers that are really just a face or an edge.
                if (s != null && s.Volume > 1e-6 && (best == null || s.Volume > best.Volume))
                    best = s;

                // Some elements (beams/columns) nest their solids in a GeometryInstance.
                var inst = go as GeometryInstance;
                if (inst != null)
                {
                    foreach (GeometryObject io in inst.GetInstanceGeometry())
                    {
                        var s2 = io as Solid;
                        if (s2 != null && s2.Volume > 1e-6 && (best == null || s2.Volume > best.Volume))
                            best = s2;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Read the real size of the MEP element into the model.
        /// Round services -> diameter in Width & Height. Rectangular -> width/height.
        /// </summary>
        public static void ReadMepSize(Element e, MepElementInfo info)
        {
            switch (e)
            {
                case Pipe pipe:
                    double d = pipe.Diameter;
                    info.Width = info.Height = d;
                    break;
                case Conduit conduit:
                    double cd = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM)?.AsDouble() ?? 0;
                    info.Width = info.Height = cd;
                    break;
                case Duct duct:
                    info.Width  = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0;
                    info.Height = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;
                    break;
                case CableTray tray:
                    info.Width  = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0;
                    info.Height = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0;
                    break;
            }
        }

        /// <summary>
        /// Where does the service actually overlap the host? Returns the START and END of
        /// the piece the opening must span.
        ///
        /// The segment IS the answer: its direction is the angle the service crosses at, so
        /// the opening can be aligned ALONG the service instead of being forced perpendicular
        /// to the host.
        ///
        /// Detection intersects the host with the service's real SOLID, not just its centre
        /// line, so a pipe lying a quarter buried in a slab — or one clipping the corner of a
        /// beam — still counts, even though its centre line never enters the host.
        ///
        /// The returned segment still lies ON the centre line (the sleeve has to stay
        /// concentric with the service) but spans the FULL reach of the overlap. That extra
        /// reach is exactly the "radius x tan(angle)" an angled crossing needs at each end,
        /// so the correction falls out of the geometry rather than being calculated — there
        /// is no trigonometry anywhere in here.
        /// </summary>
        public static bool TryFindPierce(Solid hostSolid, Solid mepSolid, Curve mepPath,
                                         out XYZ start, out XYZ end)
        {
            start = null;
            end = null;

            // A bend has no single axis to project onto, and some elements yield no solid.
            // Both fall back to the old centre-line test — less accurate, but far better
            // than dropping the clash entirely.
            Line axis = mepPath as Line;
            if (axis == null || mepSolid == null)
                return TryFindPierceOnCentreLine(hostSolid, mepPath, out start, out end);

            Solid overlap;
            try
            {
                overlap = BooleanOperationsUtils.ExecuteBooleanOperation(
                    hostSolid, mepSolid, BooleanOperationsType.Intersect);
            }
            catch
            {
                // Booleans occasionally fail on awkward geometry. Degrade, don't lose it.
                return TryFindPierceOnCentreLine(hostSolid, mepPath, out start, out end);
            }

            if (overlap == null || overlap.Volume < 1e-9) return false;

            // Project the overlap onto the service's axis and take its full reach: the
            // smallest and largest distance along the axis that any part of it touches.
            XYZ origin = axis.GetEndPoint(0);
            XYZ dir = axis.Direction;

            double min = double.MaxValue;
            double max = double.MinValue;

            foreach (Edge e in overlap.Edges)
            {
                // Tessellate so curved edges are sampled along their length, not just at
                // their endpoints — the extreme point of a cylinder cut is mid-edge.
                foreach (XYZ v in e.Tessellate())
                {
                    double t = (v - origin).DotProduct(dir);
                    if (t < min) min = t;
                    if (t > max) max = t;
                }
            }

            // A zero-length graze gives us no direction to aim along.
            if (max - min < 1e-6) return false;

            start = origin + dir * min;
            end   = origin + dir * max;
            return true;
        }

        /// <summary>
        /// The original centre-line test. Kept as the fallback for curved services and for
        /// the rare case where a boolean intersection fails.
        /// </summary>
        private static bool TryFindPierceOnCentreLine(Solid hostSolid, Curve mepPath,
                                                      out XYZ start, out XYZ end)
        {
            start = null;
            end = null;

            SolidCurveIntersection sci =
                hostSolid.IntersectWithCurve(mepPath, new SolidCurveIntersectionOptions());

            if (sci == null || sci.SegmentCount == 0) return false;

            // Take the first inside-the-solid segment.
            Curve inside = sci.GetCurveSegment(0);
            start = inside.GetEndPoint(0);
            end   = inside.GetEndPoint(1);

            if (start.DistanceTo(end) < 1e-6) return false;

            return true;
        }
    }
}
