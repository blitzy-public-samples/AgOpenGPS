// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public class CTram
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;` + `CTram(FormGPS _f)`). The collaborators this class used to
        // read through `mf` are now constructor-injected and used live-by-reference, mirroring the
        // established CTool(...) / CVehicle(ApplicationModel, ...) decoupling. No FormGPS reference
        // remains, so the class is portable across Windows, Linux and macOS. The tramline geometry
        // (spacing / offset / passes), the boundary-track "Build Around" algorithm and the GL tram
        // draw (vertex order and values) are FROZEN — outputs are unchanged. Field decoupling map
        // (was mf.X):
        //   self (was mf.tram)      - the FormGPS field `CTram tram` referred back to this very
        //                             instance, so mf.tram.displayMode is now simply displayMode.
        //   _bnd   (was mf.bnd)     - field boundary; bndList[0].fenceLine supplies the source ring
        //                             the inner/outer boundary tracks are offset from.
        //   _tool  (was mf.tool)    - implement/tool config; tool.width drives the outer/inner parity
        //                             in IsTramOuterOrInner. Cyclic peer: CTool late-wires this CTram
        //                             via CTool.SetTram after construction (CTram's ctor reads
        //                             tool.width, so this CTram is created AFTER CTool).
        //   _camera (was mf.camera) - render camera (AgOpenGPS.Core.Camera); read-only camSetDistance
        //                             selects the GL line-width level-of-detail in DrawTram.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly CBoundary _bnd;
        private readonly CTool _tool;
        private readonly Camera _camera;

        public List<vec2> tramBndOuterArr = new List<vec2>();
        public List<vec2> tramBndInnerArr = new List<vec2>();

        //tram settings
        //public double wheelTrack;
        public double tramWidth;

        public double halfWheelTrack, alpha;
        public int passes;
        public bool isOuter;

        public bool isLeftManualOn, isRightManualOn;


        //tramlines
        public List<vec2> tramArr = new List<vec2>();

        public List<List<vec2>> tramList = new List<List<vec2>>();

        // 0 off, 1 All, 2, Lines, 3 Outer
        public int displayMode, generateMode = 0;

        internal int controlByte;

        // [XPLAT] Was CTram(FormGPS _f). The field boundary, tool and render camera are injected here;
        // the cyclic CTool peer late-wires this instance via CTool.SetTram. Every settings read below
        // is preserved verbatim from the net48 original, so the tram width / passes / alpha and the
        // outer-or-inner parity are unchanged (frozen).
        public CTram(CBoundary bnd, CTool tool, Camera camera)
        {
            //constructor
            _bnd = bnd;
            _tool = tool;
            _camera = camera;

            tramWidth = Properties.Settings.Default.setTram_tramWidth;
            //halfTramWidth = (Math.Round((Properties.Settings.Default.setTram_tramWidth) / 2.0, 3));

            halfWheelTrack = Properties.VehicleSettings.Default.setVehicle_trackWidth * 0.5;

            IsTramOuterOrInner();

            passes = Properties.Settings.Default.setTram_passes;
            displayMode = 0;

            alpha = Properties.Settings.Default.setTram_alpha;
        }

        public void IsTramOuterOrInner()
        {
            isOuter = ((int)(tramWidth / _tool.width + 0.5)) % 2 == 0;
            if (Properties.ToolSettings.Default.setTool_isTramOuterInverted) isOuter = !isOuter;
        }

        public void DrawTram()
        {
            if (_camera.camSetDistance > -500) GL.LineWidth(10);
            else GL.LineWidth(6);

            GL.Color4(0, 0, 0, alpha);

            if (displayMode == 1 || displayMode == 2)
            {
                if (tramList.Count > 0)
                {
                    for (int i = 0; i < tramList.Count; i++)
                    {
                        GL.Begin(PrimitiveType.LineStrip);
                        for (int h = 0; h < tramList[i].Count; h++)
                            GL.Vertex3(tramList[i][h].easting, tramList[i][h].northing, 0);
                        GL.End();
                    }
                }
            }

            if (displayMode == 1 || displayMode == 3)
            {
                if (tramBndOuterArr.Count > 0)
                {
                    GL.Begin(PrimitiveType.LineLoop);
                    for (int h = 0; h < tramBndOuterArr.Count; h++) GL.Vertex3(tramBndOuterArr[h].easting, tramBndOuterArr[h].northing, 0);
                    GL.End();
                    GL.Begin(PrimitiveType.LineLoop);
                    for (int h = 0; h < tramBndInnerArr.Count; h++) GL.Vertex3(tramBndInnerArr[h].easting, tramBndInnerArr[h].northing, 0);
                    GL.End();
                }
            }

            if (_camera.camSetDistance > -500) GL.LineWidth(4);
            else GL.LineWidth(2);

            GL.Color4(0.930f, 0.72f, 0.73530f, alpha);

            if (displayMode == 1 || displayMode == 2)
            {
                if (tramList.Count > 0)
                {
                    for (int i = 0; i < tramList.Count; i++)
                    {
                        GL.Begin(PrimitiveType.LineStrip);
                        for (int h = 0; h < tramList[i].Count; h++)
                            GL.Vertex3(tramList[i][h].easting, tramList[i][h].northing, 0);
                        GL.End();
                    }
                }
            }

            if (displayMode == 1 || displayMode == 3)
            {
                if (tramBndOuterArr.Count > 0)
                {
                    GL.Begin(PrimitiveType.LineLoop);
                    for (int h = 0; h < tramBndOuterArr.Count; h++) GL.Vertex3(tramBndOuterArr[h].easting, tramBndOuterArr[h].northing, 0);
                    GL.End();
                    GL.Begin(PrimitiveType.LineLoop);
                    for (int h = 0; h < tramBndInnerArr.Count; h++) GL.Vertex3(tramBndInnerArr[h].easting, tramBndInnerArr[h].northing, 0);
                    GL.End();
                }
            }
        }

        public void BuildTramBnd()
        {
            bool isBndExist = _bnd.bndList.Count != 0;

            if (isBndExist)
            {
                CreateBoundaryOuterTrack();
                CreateBoundaryInnerTrack();
            }
            else
            {
                tramBndOuterArr?.Clear();
                tramBndInnerArr?.Clear();
            }
        }

        public void CreateBoundaryOuterTrack()
        {
            tramBndOuterArr = CreateBoundaryTrack(0.5 * tramWidth - halfWheelTrack);
        }

        public void CreateBoundaryInnerTrack()
        {
            tramBndInnerArr = CreateBoundaryTrack(0.5 * tramWidth + halfWheelTrack);
        }

        private List<vec2> CreateBoundaryTrack(double distance)
        {
            List<vec2> newTrack = new List<vec2>();

            int ptCount = _bnd.bndList[0].fenceLine.Count;
            if (ptCount < 2) return newTrack;

            // Identical to the headland "Build Around" algorithm (btnBndLoop_Click):
            // 1. Shift each boundary point inward along its perpendicular by 'distance'.
            // 2. Reject any shifted point closer than 'distance' to any original boundary
            //    point — removes self-intersecting loops at concave corners naturally.
            // 3. Reject consecutive points less than 1 m apart.
            // 4. Close the loop by appending the first accepted point twice,
            //    then MakePointMinimumSpacing fills gaps (e.g. at sharp convex corners)
            //    and CalculateHeadings recalculates all bisector headings.

            double distSq = distance * distance * 0.999;

            List<vec3> rawList = new List<vec3>();

            for (int i = 0; i < ptCount; i++)
            {
                double heading = _bnd.bndList[0].fenceLine[i].heading;

                vec3 pt = new vec3(
                    _bnd.bndList[0].fenceLine[i].easting - (Math.Sin(glm.PIBy2 + heading) * distance),
                    _bnd.bndList[0].fenceLine[i].northing - (Math.Cos(glm.PIBy2 + heading) * distance),
                    heading);

                bool add = true;

                for (int j = 0; j < ptCount; j++)
                {
                    double check = glm.DistanceSquared(
                        pt.northing, pt.easting,
                        _bnd.bndList[0].fenceLine[j].northing,
                        _bnd.bndList[0].fenceLine[j].easting);

                    if (check < distSq)
                    {
                        add = false;
                        break;
                    }
                }

                if (!add) continue;

                if (rawList.Count > 0)
                {
                    double spacingSq = (pt.easting - rawList[rawList.Count - 1].easting) * (pt.easting - rawList[rawList.Count - 1].easting)
                                     + (pt.northing - rawList[rawList.Count - 1].northing) * (pt.northing - rawList[rawList.Count - 1].northing);
                    if (spacingSq > 1.0)
                        rawList.Add(pt);
                }
                else
                {
                    rawList.Add(pt);
                }
            }

            if (rawList.Count < 4)
            {
                foreach (var p in rawList) newTrack.Add(new vec2(p.easting, p.northing));
                return newTrack;
            }

            // Close the loop and smooth, exactly as the headland algorithm does.
            rawList.Add(new vec3(rawList[0]));
            rawList.Add(new vec3(rawList[0]));

            CABCurve.MakePointMinimumSpacing(ref rawList, 1.2);
            CABCurve.CalculateHeadings(ref rawList);

            foreach (var p in rawList)
                newTrack.Add(new vec2(p.easting, p.northing));

            return newTrack;
        }

    }
}
