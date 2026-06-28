// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public partial class CBoundary
    {
        public List<vec3> bndBeingMadePts = new List<vec3>(128);

        public double createBndOffset;
        public bool isBndBeingMade;

        public bool isDrawRightSide = true, isDrawAtPivot = true, isOkToAddPoints = false;
        public bool isRecBoundaryWhenSectionOn = false;

        public bool IsPointInsideFenceArea(vec3 testPoint)
        {
            //first where are we, must be inside outer and outside of inner geofence non drive thru turn borders
            if (bndList[0].fenceLineEar.IsPointInPolygon(testPoint))
            {
                for (int i = 1; i < bndList.Count; i++)
                {
                    //make sure not inside a non drivethru boundary
                    //if (bndList[i].isDriveThru) continue;
                    if (bndList[i].fenceLineEar.IsPointInPolygon(testPoint))
                    {
                        return false;
                    }
                }
                //we are safely inside outer, outside inner boundaries
                return true;
            }
            return false;
        }

        public bool IsPointInsideFenceArea(vec2 testPoint)
        {
            //first where are we, must be inside outer and outside of inner geofence non drive thru turn borders
            if (bndList[0].fenceLineEar.IsPointInPolygon(testPoint))
            {
                for (int i = 1; i < bndList.Count; i++)
                {
                    //make sure not inside a non drivethru boundary
                    //if (bndList[i].isDriveThru) continue;
                    if (bndList[i].fenceLineEar.IsPointInPolygon(testPoint))
                    {
                        return false;
                    }
                }
                //we are safely inside outer, outside inner boundaries
                return true;
            }
            return false;
        }

        public void DrawFenceLines()
        {
            // [XPLAT] Decoupled from the WinForms host shell: mf.mc -> injected mc (CModuleComm);
            // mf.ABLine -> injected ABLine (CABLine); mf.section/mf.tool -> injected section[]/tool;
            // mf.pivotAxlePos -> appModel.PivotAxlePos + appModel.FixHeading; mf.bnd -> this. All
            // collaborators are held by the CBoundary root (CBoundary.cs); drawing/geometry is
            // unchanged. See MIGRATION_DOCS/TRANSITION_MAP.md.
            if (!mc.isOutOfBounds)
            {
                GL.Color4(0, 0, 0, 0.8);
                GL.LineWidth(6);

                for (int i = 0; i < bndList.Count; i++)
                {
                    bndList[i].fenceLineEar.DrawPolygon();
                }

                GL.Color4(0.95f, 0.44f, 0.350f, 0.8f);
                GL.LineWidth(2);

                for (int i = 0; i < bndList.Count; i++)
                {
                    bndList[i].fenceLineEar.DrawPolygon();
                }
            }
            else
            {
                GL.LineWidth(ABLine.lineWidth * 3);
                GL.Color3(0.95f, 0.25f, 0.250f);

                for (int i = 0; i < bndList.Count; i++)
                {
                    bndList[i].fenceLineEar.DrawPolygon();
                }
            }

            ////closest points  TooDoo
            //GL.Color3(0.70f, 0.95f, 0.95f);
            //GL.PointSize(6.0f);
            //GL.Begin(PrimitiveType.Points);
            //GL.Vertex3(this.closestTurnPt.easting, this.closestTurnPt.northing, 0);
            //GL.End();

            if (bndBeingMadePts.Count > 0)
            {
                //the boundary so far
                // [XPLAT] mf.pivotAxlePos -> recomposed from appModel.PivotAxlePos (easting/northing) +
                // appModel.FixHeading (heading). The original set pivotAxlePos.heading = fixHeading, and
                // GeoDir's [0, 2pi) normalization is rotation-neutral for the Sin/Cos uses below, so the
                // drawn pivot line is value-identical to the WinForms original.
                vec3 pivot = new vec3(appModel.PivotAxlePos.Easting, appModel.PivotAxlePos.Northing, appModel.FixHeading.AngleInRadians);
                GL.LineWidth(ABLine.lineWidth);
                GL.Color3(0.825f, 0.22f, 0.90f);
                GL.Begin(PrimitiveType.LineStrip);
                for (int h = 0; h < bndBeingMadePts.Count; h++) GL.Vertex3(bndBeingMadePts[h].easting, bndBeingMadePts[h].northing, 0);
                GL.Color3(0.295f, 0.972f, 0.290f);
                GL.Vertex3(bndBeingMadePts[0].easting, bndBeingMadePts[0].northing, 0);
                GL.End();

                //line from last point to pivot marker
                GL.Color3(0.825f, 0.842f, 0.0f);
                GL.Enable(EnableCap.LineStipple);
                GL.LineStipple(1, 0x0700);
                GL.Begin(PrimitiveType.LineStrip);

                if (isDrawAtPivot)
                {
                    if (isDrawRightSide)
                    {
                        GL.Vertex3(bndBeingMadePts[0].easting, bndBeingMadePts[0].northing, 0);

                        GL.Vertex3(pivot.easting + (Math.Sin(pivot.heading - glm.PIBy2) * -createBndOffset),
                                pivot.northing + (Math.Cos(pivot.heading - glm.PIBy2) * -createBndOffset), 0);
                        GL.Vertex3(bndBeingMadePts[bndBeingMadePts.Count - 1].easting, bndBeingMadePts[bndBeingMadePts.Count - 1].northing, 0);
                    }
                    else
                    {
                        GL.Vertex3(bndBeingMadePts[0].easting, bndBeingMadePts[0].northing, 0);

                        GL.Vertex3(pivot.easting + (Math.Sin(pivot.heading - glm.PIBy2) * createBndOffset),
                                pivot.northing + (Math.Cos(pivot.heading - glm.PIBy2) * createBndOffset), 0);
                        GL.Vertex3(bndBeingMadePts[bndBeingMadePts.Count - 1].easting, bndBeingMadePts[bndBeingMadePts.Count - 1].northing, 0);
                    }
                }
                else //draw from tool
                {
                    if (isDrawRightSide)
                    {
                        GL.Vertex3(bndBeingMadePts[0].easting, bndBeingMadePts[0].northing, 0);
                        GL.Vertex3(section[tool.numOfSections - 1].rightPoint.easting, section[tool.numOfSections - 1].rightPoint.northing, 0);
                        GL.Vertex3(bndBeingMadePts[bndBeingMadePts.Count - 1].easting, bndBeingMadePts[bndBeingMadePts.Count - 1].northing, 0);
                    }
                    else
                    {
                        GL.Vertex3(bndBeingMadePts[0].easting, bndBeingMadePts[0].northing, 0);
                        GL.Vertex3(section[0].leftPoint.easting, section[0].leftPoint.northing, 0);
                        GL.Vertex3(bndBeingMadePts[bndBeingMadePts.Count - 1].easting, bndBeingMadePts[bndBeingMadePts.Count - 1].northing, 0);
                    }
                }
                GL.End();
                GL.Disable(EnableCap.LineStipple);

                //boundary points
                GL.Color3(0.0f, 0.95f, 0.95f);
                GL.PointSize(6.0f);
                GL.Begin(PrimitiveType.Points);
                for (int h = 0; h < bndBeingMadePts.Count; h++) GL.Vertex3(bndBeingMadePts[h].easting, bndBeingMadePts[h].northing, 0);
                GL.End();
            }
        }
    }
}