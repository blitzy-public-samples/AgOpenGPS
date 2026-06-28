// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;

namespace AgOpenGPS
{
    // [XPLAT] Headland-build + headland-control partial of CBoundary. Decoupled from the WinForms
    // FormGPS host god-object: every former `mf.X` access is replaced by a collaborator held by the
    // CBoundary root (this.tool / this.section / this.vehicle / this.sounds — constructor-injected in
    // CBoundary.cs) or by the shared AgOpenGPS.Core runtime model (this.appModel). The headland
    // geometry, the headland-distance proximity gating, the machine PGN 0xEF (p_239) hydraulic-lift
    // byte semantics and which sound fires on which headland event are all FROZEN — outputs are
    // byte-for-byte identical to the net48/WinForms original. No FormGPS reference remains, so this
    // logic is portable across Windows, Linux and macOS. See MIGRATION_DOCS/TRANSITION_MAP.md.
    public partial class CBoundary
    {
        public bool isHeadlandOn;

        public bool isToolInHeadland,
            isToolOuterPointsInHeadland, isSectionControlledByHeadland;

        public vec2? HeadlandNearestPoint { get; private set; } = null;
        public double? HeadlandDistance { get; private set; } = null;

        public void SetHydPosition()
        {
            // [XPLAT] mf.vehicle -> injected vehicle; mf.avgSpeed / mf.isReverse -> appModel runtime
            // state (written by the fix/position pipeline); mf.p_239 -> appModel.machinePgnEF (machine
            // PGN 0xEF, hydraulic-lift command byte at index 7); mf.sounds -> injected cross-platform
            // CSound. The forward-only gating, the hydLift byte values (2 = raise in headland, 1 = lower)
            // and which clip plays on which event (up on entering, down on leaving) are unchanged.
            if (vehicle.isHydLiftOn && appModel.avgSpeed > 0.2 && !appModel.isReverse)
            {
                if (isToolInHeadland)
                {
                    appModel.machinePgnEF[appModel.machinePgnEFHydLift] = 2;
                    if (sounds.isHydLiftChange != isToolInHeadland)
                    {
                        if (sounds.isHydLiftSoundOn) sounds.sndHydLiftUp.Play();
                        sounds.isHydLiftChange = isToolInHeadland;
                    }
                }
                else
                {
                    appModel.machinePgnEF[appModel.machinePgnEFHydLift] = 1;
                    if (sounds.isHydLiftChange != isToolInHeadland)
                    {
                        if (sounds.isHydLiftSoundOn) sounds.sndHydLiftDn.Play();
                        sounds.isHydLiftChange = isToolInHeadland;
                    }
                }
            }
        }

        public void WhereAreToolCorners()
        {
            if (bndList.Count > 0 && bndList[0].hdLine.Count > 0)
            {
                bool isLeftInWk, isRightInWk = true;

                // [XPLAT] mf.tool -> injected tool; mf.section -> injected section[]. Geometry unchanged.
                for (int j = 0; j < tool.numOfSections; j++)
                {
                    isLeftInWk = j == 0 ? IsPointInsideHeadArea(section[j].leftPoint) : isRightInWk;
                    isRightInWk = IsPointInsideHeadArea(section[j].rightPoint);

                    //save left side
                    if (j == 0)
                        tool.isLeftSideInHeadland = !isLeftInWk;

                    //merge the two sides into in or out
                    section[j].isInHeadlandArea = !isLeftInWk && !isRightInWk;
                }

                //save right side
                tool.isRightSideInHeadland = !isRightInWk;

                //is the tool in or out based on endpoints
                isToolOuterPointsInHeadland = tool.isLeftSideInHeadland && tool.isRightSideInHeadland;
            }
        }

        public void WhereAreToolLookOnPoints()
        {
            if (bndList.Count > 0 && bndList[0].hdLine.Count > 0)
            {
                bool isLookRightIn = false;

                // [XPLAT] mf.toolPivotPos -> recomposed from appModel.ToolPivotPosition (easting/
                // northing) + appModel.ToolPivotHeading (heading; GeoDir keeps [0,2pi), identical to the
                // original toolPivotPos.heading). Only the heading feeds Sin/Cos here, so the look-on
                // box is unchanged. mf.tool -> injected tool; mf.section -> injected section[].
                vec3 toolFix = new vec3(appModel.ToolPivotPosition.Easting, appModel.ToolPivotPosition.Northing, appModel.ToolPivotHeading.AngleInRadians);
                double sinAB = Math.Sin(toolFix.heading);
                double cosAB = Math.Cos(toolFix.heading);

                //generated box for finding closest point
                double pos = 0;
                double mOn = (tool.lookAheadDistanceOnPixelsRight - tool.lookAheadDistanceOnPixelsLeft) / tool.rpWidth;

                for (int j = 0; j < tool.numOfSections; j++)
                {
                    bool isLookLeftIn = j == 0 ? IsPointInsideHeadArea(new vec2(
                        section[j].leftPoint.easting + (sinAB * tool.lookAheadDistanceOnPixelsLeft * 0.1),
                        section[j].leftPoint.northing + (cosAB * tool.lookAheadDistanceOnPixelsLeft * 0.1))) : isLookRightIn;

                    pos += section[j].rpSectionWidth;
                    double endHeight = (tool.lookAheadDistanceOnPixelsLeft + (mOn * pos)) * 0.1;

                    isLookRightIn = IsPointInsideHeadArea(new vec2(
                        section[j].rightPoint.easting + (sinAB * endHeight),
                        section[j].rightPoint.northing + (cosAB * endHeight)));

                    section[j].isLookOnInHeadland = !isLookLeftIn && !isLookRightIn;
                }
            }
        }

        public bool IsPointInsideHeadArea(vec2 pt)
        {
            //if inside outer boundary, then potentially add
            if (bndList[0].hdLine.IsPointInPolygon(pt))
            {
                for (int i = 1; i < bndList.Count; i++)
                {
                    if (bndList[i].hdLine.IsPointInPolygon(pt))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }
        public void CheckHeadlandProximity()
        {
            if (!isHeadlandOn || bndList.Count == 0 || bndList[0].hdLine.Count < 2)
            {
                HeadlandNearestPoint = null;
                HeadlandDistance = null;
                return;
            }

            // [XPLAT] mf.toolPivotPos -> recomposed from appModel.ToolPivotPosition (easting/northing) +
            // appModel.ToolPivotHeading (heading). The recomposed vec3 is value-identical to the former
            // toolPivotPos, so the raycast, distance and AngleDiff results are unchanged.
            vec3 vehiclePos = new vec3(appModel.ToolPivotPosition.Easting, appModel.ToolPivotPosition.Northing, appModel.ToolPivotHeading.AngleInRadians);

            vec2? nearest = glm.RaycastToPolygon(vehiclePos, bndList[0].hdLine);
            if (!nearest.HasValue)
            {
                HeadlandNearestPoint = null;
                HeadlandDistance = null;
                return;
            }

            vec2 nearestVal = nearest.Value;
            double distance = glm.Distance(vehiclePos.ToVec2(), nearestVal);

            HeadlandNearestPoint = nearestVal;
            HeadlandDistance = distance;

            bool isInside = bndList[0].hdLine.IsPointInPolygon(vehiclePos.ToVec2());

            double dx = nearestVal.easting - vehiclePos.easting;
            double dy = nearestVal.northing - vehiclePos.northing;
            double angleToPolygon = Math.Atan2(dx, dy);
            double headingDiff = glm.AngleDiff(vehiclePos.heading, angleToPolygon);
            bool headingOk = headingDiff < glm.toRadians(60); // eventueel verwijderen: zit al in GetClosestPointInFront

            // Warning Logic
            bool shouldPlay =
                (isInside && headingOk && distance < 20.0) ||
                (!isInside && headingOk && distance < 5.0);

            // [XPLAT] mf.isHeadlandDistanceOn -> appModel runtime state; mf.sounds -> injected
            // cross-platform CSound. The alarm-trigger condition and the one-shot latch
            // (isBoundAlarming) are unchanged, so the headland proximity sound fires identically.
            if (shouldPlay && appModel.isHeadlandDistanceOn)
            {
                if (!sounds.isBoundAlarming)
                {
                    sounds.sndHeadland.Play();
                    sounds.isBoundAlarming = true;
                }
            }
            else
            {
                sounds.isBoundAlarming = false;
            }
        }

    }
}