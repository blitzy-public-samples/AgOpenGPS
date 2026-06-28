// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public class CContour
    {
        // [XPLAT] Decoupled from the FormGPS god-object: instead of a single mf back-reference, the
        // collaborators this contour-follow guidance needs are injected. Guidance/steering output is
        // FROZEN (GuidanceEquivalenceTests) — only the source of each value changes, never the math.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        //   _appModel - shared AgOpenGPS.Core runtime model: supplies the relocated FormGPS scan-loop
        //               state (secondsSinceStart, isBtnAutoSteerOn, avgSpeed, isReverse, FixHeading) and
        //               the canonical autosteer output (guidanceLineDistanceOff/guidanceLineSteerAngle),
        //               read and written live-by-reference exactly as the originals were.
        //   vehicle   - CVehicle (was mf.vehicle): steer gains, maxSteerAngle, pure-pursuit integral
        //               gain, goal-point distance, wheelbase and modeActualXTE.
        //   tool      - CTool (was mf.tool): width/halfWidth/overlap/offset for the contour geometry.
        //   pn        - CNMEA (was mf.pn): the current GPS fix used by the Stanley cross-track distance.
        //   ahrs      - CAHRS (was mf.ahrs): IMU roll for the side-hill steer compensation.
        //   ABLine    - CABLine (was mf.ABLine): on-screen line/point width for DrawContourLine.
        private readonly ApplicationModel _appModel;
        private readonly CVehicle vehicle;
        private readonly CTool tool;
        private readonly CNMEA pn;
        private readonly CAHRS ahrs;
        private readonly CABLine ABLine;

        // [XPLAT] Late-wired cyclic guidance peers (constructed after CContour, so they cannot be
        // ctor-injected). Set once via SetGuidanceReferences, mirroring the established CABLine pattern.
        //   yt  - CYouTurn (was mf.yt): isYouTurnTriggered gate on the pure-pursuit integral term.
        //   gyd - CGuidance (was mf.gyd): sideHillCompFactor for the IMU-roll steer compensation.
        private CYouTurn yt;
        private CGuidance gyd;

        public bool isContourOn, isContourBtnOn, isRightPriority = true;

        // for closest line point to current fix
        public double minDistance = 99999.0, refX, refZ;

        public double distanceFromCurrentLinePivot;

        private int A, B, C, stripNum, lastLockPt = int.MaxValue;

        public double abFixHeadingDelta, abHeading;

        public vec2 boxA = new vec2(0, 0), boxB = new vec2(0, 2);

        public bool isHeadingSameWay = true;

        public vec2 goalPointCT = new vec2(0, 0);
        public double steerAngleCT;
        public double rEastCT, rNorthCT;
        public double ppRadiusCT;

        public double pivotDistanceError, pivotDistanceErrorLast, pivotDerivative;

        //derivative counters
        private int counter2;

        public double inty;
        public double pivotErrorTotal;

        //list of strip data individual points
        public List<vec3> ptList = new List<vec3>();

        //list of the list of individual Lines for entire field
        public List<List<vec3>> stripList = new List<List<vec3>>();

        //list of points for the new contour line
        public List<vec3> ctList = new List<vec3>();

        // [XPLAT] contourSaveList relocated here from the deleted FormGPS partial (SaveOpen.Designer.cs:
        // public List<List<vec3>> contourSaveList) into the contour manager that owns it. It accumulates
        // finished contour strips for appending to the field's contour file. It stays in the GPS layer
        // (not the Core ApplicationModel) because vec3 is a GPS type that must not leak into Core; the
        // cross-platform field-save / field-close logic accesses it via this contour instance
        // (ct.contourSaveList) exactly as it previously did through the host form. Type and semantics are
        // unchanged. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public List<List<vec3>> contourSaveList = new List<List<vec3>>();

        // [XPLAT] ctor now injects the domain collaborators + the Core ApplicationModel instead of
        // FormGPS (was CContour(FormGPS _f)). No FormGPS back-reference remains. The composition root
        // builds vehicle/tool/pn/ahrs/ABLine before CContour; the cyclic guidance peers (yt/gyd) are
        // wired afterwards via SetGuidanceReferences.
        public CContour(ApplicationModel appModel, CVehicle vehicle, CTool tool, CNMEA pn, CAHRS ahrs, CABLine ABLine)
        {
            //constructor
            _appModel = appModel;
            this.vehicle = vehicle;
            this.tool = tool;
            this.pn = pn;
            this.ahrs = ahrs;
            this.ABLine = ABLine;
            ctList.Capacity = 128;
            ptList.Capacity = 128;
        }

        // [XPLAT] Post-construct wiring for the cyclic guidance peers (CYouTurn/CGuidance) that do not
        // yet exist when CContour is constructed. Mirrors the established CABLine.SetGuidanceReferences
        // pattern; introduces no new abstraction.
        public void SetGuidanceReferences(CYouTurn yt, CGuidance gyd)
        {
            this.yt = yt;
            this.gyd = gyd;
        }

        public bool isLocked = false;

        // [XPLAT] View-model-bindable projection of the contour-lock indicator, replacing the former
        // direct WinForms write to the host form's btnContourLock.Image. The original
        // SetContourLockImage(bool isOn) did exactly:
        //     btnContourLock.Image = isOn ? Resources.ColorLocked : Resources.ColorUnlocked;
        // The bound Avalonia view-model now reads IsContourLocked and refreshes the lock/unlock icon when
        // ContourLockChanged is raised. This mirrors the established CISOBUS SectionControlButtonState +
        // SectionControlButtonChanged view-model projection; no WinForms image/button reference is kept
        // and no new abstraction is introduced. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool IsContourLocked { get; private set; }

        public event Action ContourLockChanged;

        //determine closest point on left side

        // [XPLAT] Replaces the host form's SetContourLockImage(bool). Invoked at the SAME 7 sites with
        // the SAME boolean as the original, so the contour-lock indicator toggles identically; it updates
        // the bound IsContourLocked state and raises ContourLockChanged so the view refreshes the lock
        // icon. The change-guard only avoids redundant per-fix refreshes; it never alters the indicator's
        // value at any site.
        private void SetContourLockImage(bool isOn)
        {
            if (IsContourLocked == isOn) return;
            IsContourLocked = isOn;
            ContourLockChanged?.Invoke();
        }

        //hitting the cycle lines buttons lock to current line
        public bool SetLockToLine()
        {
            if (ctList.Count > 5) isLocked = !isLocked;
            SetContourLockImage(isLocked);
            return isLocked;
        }

        private double lastSecond;
        private int pt = 0;

        public void BuildContourGuidanceLine(vec3 pivot)
        {
            if (ctList.Count == 0)
            {
                if ((_appModel.secondsSinceStart - lastSecond) < 0.3) return;
            }
            else
            {
                if ((_appModel.secondsSinceStart - lastSecond) < 2) return;
            }

            lastSecond = _appModel.secondsSinceStart;
            int ptCount;
            minDistance = double.MaxValue;
            int start, stop;

            double toolContourDistance = (tool.width * 3 + Math.Abs(tool.offset));

            //check if no strips yet, return
            int stripCount = stripList.Count;

            if (stripCount < 1) return;

            double sinH = Math.Sin(pivot.heading) * 0.2;
            double cosH = Math.Cos(pivot.heading) * 0.2;

            double sin2HL = Math.Sin(pivot.heading + glm.PIBy2);
            double cos2HL = Math.Cos(pivot.heading + glm.PIBy2);

            boxA.easting = pivot.easting - sin2HL + sinH;
            boxA.northing = pivot.northing - cos2HL + cosH;

            boxB.easting = pivot.easting + sin2HL + sinH;
            boxB.northing = pivot.northing + cos2HL + cosH;

            if (!isLocked && !_appModel.isBtnAutoSteerOn)
            {
                stripNum = -1;
                for (int s = 0; s < stripCount; s++)
                {
                    int p;
                    ptCount = stripList[s].Count;
                    if (ptCount == 0) continue;
                    double dist;
                    for (p = 0; p < ptCount; p += 3)
                    {
                        if ((((boxA.easting - boxB.easting) * (stripList[s][p].northing - boxB.northing))
                                - ((boxA.northing - boxB.northing) * (stripList[s][p].easting - boxB.easting))) > 0)
                        {
                            continue;
                        }

                        dist = ((pivot.easting - stripList[s][p].easting) * (pivot.easting - stripList[s][p].easting))
                            + ((pivot.northing - stripList[s][p].northing) * (pivot.northing - stripList[s][p].northing));
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            stripNum = s;
                            pt = lastLockPt = p;
                        }
                    }
                }
                minDistance = Math.Sqrt(minDistance);

                if (stripNum < 0 || minDistance > toolContourDistance || stripList[stripNum].Count < 4)
                {
                    //no points in the box, exit
                    ctList.Clear();
                    isLocked = false;
                    SetContourLockImage(isLocked);
                    return;
                }
            }

            //locked to this stripNum so find closest within a range
            else
            {
                //no points in the box, exit
                ptCount = stripList[stripNum].Count;

                if (ptCount < 2)
                {
                    ctList.Clear();
                    isLocked = false;
                    SetContourLockImage(isLocked);
                    return;
                }

                start = lastLockPt - 20; if (start < 0) start = 0;
                stop = lastLockPt + 20; if (stop > ptCount) stop = ptCount;

                //determine closest point
                minDistance = double.MaxValue;

                for (int i = start; i < stop; i += 3)
                {
                    double dist = ((pivot.easting - stripList[stripNum][i].easting) * (pivot.easting - stripList[stripNum][i].easting))
                        + ((pivot.northing - stripList[stripNum][i].northing) * (pivot.northing - stripList[stripNum][i].northing));

                    if (minDistance >= dist)
                    {
                        minDistance = dist;
                        pt = lastLockPt = i;
                    }
                }

                minDistance = Math.Sqrt(minDistance);

                if (minDistance > toolContourDistance)
                {
                    ctList.Clear();
                    isLocked = false;
                    SetContourLockImage(isLocked);
                    return;
                }
            }

            //now we have closest point, the distance squared from it, and which patch and point its from
            refX = stripList[stripNum][pt].easting;
            refZ = stripList[stripNum][pt].northing;

            double dx, dz, distanceFromRefLine;

            if (pt < stripList[stripNum].Count - 1)
            {
                dx = stripList[stripNum][pt + 1].easting - refX;
                dz = stripList[stripNum][pt + 1].northing - refZ;

                //how far are we away from the reference line at 90 degrees - 2D cross product and distance
                distanceFromRefLine = ((dz * pivot.easting) - (dx * pivot.northing) + (stripList[stripNum][pt + 1].easting
                                        * refZ) - (stripList[stripNum][pt + 1].northing * refX))
                                        / Math.Sqrt((dz * dz) + (dx * dx));
            }
            else if (pt > 0)
            {
                dx = refX - stripList[stripNum][pt - 1].easting;
                dz = refZ - stripList[stripNum][pt - 1].northing;

                //how far are we away from the reference line at 90 degrees - 2D cross product and distance
                distanceFromRefLine = ((dz * pivot.easting) - (dx * pivot.northing) + (refX
                                        * stripList[stripNum][pt - 1].northing) - (refZ * stripList[stripNum][pt - 1].easting))
                                        / Math.Sqrt((dz * dz) + (dx * dx));
            }
            else return;

            //are we going same direction as stripList was created?
            bool isSameWay = Math.PI - Math.Abs(Math.Abs(_appModel.FixHeading.AngleInRadians - stripList[stripNum][pt].heading) - Math.PI) < 1.57;

            double RefDist = (distanceFromRefLine + (isSameWay ? tool.offset : -tool.offset))
                                / (tool.width - tool.overlap);

            double howManyPathsAway;

            if (Math.Abs(distanceFromRefLine) > tool.halfWidth
                || Math.Abs(tool.offset) > tool.halfWidth)
            {
                //beside what is done
                if (RefDist < 0) howManyPathsAway = -1;
                else howManyPathsAway = 1;
            }
            else
            {
                //driving on what is done
                howManyPathsAway = 0;
            }

            if (howManyPathsAway >= -1 && howManyPathsAway <= 1)
            {
                ctList.Clear();

                //make the new guidance line list called guideList
                ptCount = stripList[stripNum].Count;

                //shorter behind you
                if (isSameWay)
                {
                    start = pt - 20; if (start < 0) start = 0;
                    stop = pt + 70; if (stop > ptCount) stop = ptCount;
                }
                else
                {
                    start = pt - 70; if (start < 0) start = 0;
                    stop = pt + 20; if (stop > ptCount) stop = ptCount;
                }

                double distAway = (tool.width - tool.overlap) * howManyPathsAway
                    + (isSameWay ? -tool.offset : tool.offset);
                double distSqAway = (distAway * distAway) * 0.97;

                for (int i = start; i < stop; i++)
                {
                    vec3 point = new vec3(
                        stripList[stripNum][i].easting + (Math.Cos(stripList[stripNum][i].heading) * distAway),
                        stripList[stripNum][i].northing - (Math.Sin(stripList[stripNum][i].heading) * distAway),
                        stripList[stripNum][i].heading);

                    bool isOkToAdd = true;
                    //make sure its not closer then 1 eq width
                    for (int j = start; j < stop; j++)
                    {
                        double check = glm.DistanceSquared(point.northing, point.easting,
                            stripList[stripNum][j].northing, stripList[stripNum][j].easting);
                        if (check < distSqAway)
                        {
                            isOkToAdd = false;
                            break;
                        }
                    }

                    if (isOkToAdd)
                    {
                        if (ctList.Count > 0)
                        {
                            double dist =
                                ((point.easting - ctList[ctList.Count - 1].easting) * (point.easting - ctList[ctList.Count - 1].easting))
                                + ((point.northing - ctList[ctList.Count - 1].northing) * (point.northing - ctList[ctList.Count - 1].northing));
                            if (dist > 0.2)
                                ctList.Add(point);
                        }
                        else ctList.Add(point);
                    }
                }

                int ptc = ctList.Count;
                if (ptc < 5)
                {
                    ctList.Clear();
                    isLocked = false;
                    SetContourLockImage(isLocked);
                    return;
                }
            }
            else
            {
                ctList.Clear();
                isLocked = false;
                SetContourLockImage(isLocked);
                return;
            }
        }

        //determine distance from contour guidance line
        public void DistanceFromContourLine(vec3 pivot, vec3 steer)
        {
            double minDistA = 1000000, minDistB = 1000000;
            int ptCount = ctList.Count;
            if (ptCount > 8)
            {
                if (Properties.ToolSettings.Default.setVehicle_isStanleyUsed)
                {
                    //find the closest 2 points to current fix
                    for (int t = 0; t < ptCount; t++)
                    {
                        double dist = ((steer.easting - ctList[t].easting) * (steer.easting - ctList[t].easting))
                                        + ((steer.northing - ctList[t].northing) * (steer.northing - ctList[t].northing));
                        if (dist < minDistA)
                        {
                            minDistB = minDistA;
                            B = A;
                            minDistA = dist;
                            A = t;
                        }
                        else if (dist < minDistB)
                        {
                            minDistB = dist;
                            B = t;
                        }
                    }

                    //just need to make sure the points continue ascending in list order or heading switches all over the place
                    if (A > B) { C = A; A = B; B = C; }

                    //get the distance from currently active AB line
                    //x2-x1
                    double dx = ctList[B].easting - ctList[A].easting;
                    //z2-z1
                    double dy = ctList[B].northing - ctList[A].northing;

                    if (Math.Abs(dx) < Double.Epsilon && Math.Abs(dy) < Double.Epsilon) return;

                    //how far from current AB Line is fix
                    distanceFromCurrentLinePivot = ((dy * steer.easting) - (dx * steer.northing) + (ctList[B].easting
                                * ctList[A].northing) - (ctList[B].northing * ctList[A].easting))
                                    / Math.Sqrt((dy * dy) + (dx * dx));

                    abHeading = Math.Atan2(dx, dy);
                    if (abHeading < 0) abHeading += glm.twoPI;

                    isHeadingSameWay = Math.PI - Math.Abs(Math.Abs(pivot.heading - abHeading) - Math.PI) < glm.PIBy2;

                    // calc point on ABLine closest to current position
                    double U = (((steer.easting - ctList[A].easting) * dx) + ((steer.northing - ctList[A].northing) * dy))
                                / ((dx * dx) + (dy * dy));

                    rEastCT = ctList[A].easting + (U * dx);
                    rNorthCT = ctList[A].northing + (U * dy);

                    //distance is negative if on left, positive if on right
                    if (isHeadingSameWay)
                    {
                        abFixHeadingDelta = (steer.heading - abHeading);
                    }
                    else
                    {
                        distanceFromCurrentLinePivot *= -1.0;
                        abFixHeadingDelta = (steer.heading - abHeading + Math.PI);
                    }

                    //Fix the circular error
                    if (abFixHeadingDelta > Math.PI) abFixHeadingDelta -= Math.PI;
                    else if (abFixHeadingDelta < Math.PI) abFixHeadingDelta += Math.PI;

                    if (abFixHeadingDelta > glm.PIBy2) abFixHeadingDelta -= Math.PI;
                    else if (abFixHeadingDelta < -glm.PIBy2) abFixHeadingDelta += Math.PI;

                    if (_appModel.isReverse) abFixHeadingDelta *= -1;

                    abFixHeadingDelta *= vehicle.stanleyHeadingErrorGain;
                    if (abFixHeadingDelta > 0.74) abFixHeadingDelta = 0.74;
                    if (abFixHeadingDelta < -0.74) abFixHeadingDelta = -0.74;

                    steerAngleCT = Math.Atan((distanceFromCurrentLinePivot * vehicle.stanleyDistanceErrorGain)
                        / ((Math.Abs(_appModel.avgSpeed) * 0.277777) + 1));

                    if (steerAngleCT > 0.74) steerAngleCT = 0.74;
                    if (steerAngleCT < -0.74) steerAngleCT = -0.74;

                    steerAngleCT = glm.toDegrees((steerAngleCT + abFixHeadingDelta) * -1.0);

                    if (steerAngleCT < -vehicle.maxSteerAngle) steerAngleCT = -vehicle.maxSteerAngle;
                    if (steerAngleCT > vehicle.maxSteerAngle) steerAngleCT = vehicle.maxSteerAngle;
                }
                else
                {
                    //find the closest 2 points to current fix
                    for (int t = 0; t < ptCount; t++)
                    {
                        double dist = ((pivot.easting - ctList[t].easting) * (pivot.easting - ctList[t].easting))
                                        + ((pivot.northing - ctList[t].northing) * (pivot.northing - ctList[t].northing));
                        if (dist < minDistA)
                        {
                            minDistB = minDistA;
                            B = A;
                            minDistA = dist;
                            A = t;
                        }
                        else if (dist < minDistB)
                        {
                            minDistB = dist;
                            B = t;
                        }
                    }

                    //just need to make sure the points continue ascending in list order or heading switches all over the place
                    if (A > B) { C = A; A = B; B = C; }

                    if (isLocked && (A < 2 || B > ptCount - 3))
                    {
                        isLocked = false;
                        SetContourLockImage(isLocked);
                        lastLockPt = int.MaxValue;
                        return;
                    }

                    //get the distance from currently active AB line
                    //x2-x1
                    double dx = ctList[B].easting - ctList[A].easting;
                    //z2-z1
                    double dy = ctList[B].northing - ctList[A].northing;

                    if (Math.Abs(dx) < Double.Epsilon && Math.Abs(dy) < Double.Epsilon) return;

                    //how far from current AB Line is fix
                    distanceFromCurrentLinePivot = ((dy * pn.fix.easting) - (dx * pn.fix.northing) + (ctList[B].easting
                                * ctList[A].northing) - (ctList[B].northing * ctList[A].easting))
                                    / Math.Sqrt((dy * dy) + (dx * dx));

                    //integral slider is set to 0
                    if (vehicle.purePursuitIntegralGain != 0)
                    {
                        pivotDistanceError = distanceFromCurrentLinePivot * 0.2 + pivotDistanceError * 0.8;

                        if (counter2++ > 4)
                        {
                            pivotDerivative = pivotDistanceError - pivotDistanceErrorLast;
                            pivotDistanceErrorLast = pivotDistanceError;
                            counter2 = 0;
                            pivotDerivative *= 2;
                        }

                        if (_appModel.isBtnAutoSteerOn
                            && Math.Abs(pivotDerivative) < (0.1)
                            && _appModel.avgSpeed > 2.5
                            && !yt.isYouTurnTriggered)
                        {
                            //if over the line heading wrong way, rapidly decrease integral
                            if ((inty < 0 && distanceFromCurrentLinePivot < 0) || (inty > 0 && distanceFromCurrentLinePivot > 0))
                            {
                                inty += pivotDistanceError * vehicle.purePursuitIntegralGain * -0.06;
                            }
                            else
                            {
                                if (Math.Abs(distanceFromCurrentLinePivot) > 0.02)
                                {
                                    inty += pivotDistanceError * vehicle.purePursuitIntegralGain * -0.02;
                                    if (inty > 0.2) inty = 0.2;
                                    else if (inty < -0.2) inty = -0.2;
                                }
                            }
                        }
                        else inty *= 0.95;
                    }
                    else inty = 0;

                    if (_appModel.isReverse) inty = 0;

                    isHeadingSameWay = Math.PI - Math.Abs(Math.Abs(pivot.heading - ctList[A].heading) - Math.PI) < glm.PIBy2;

                    if (!isHeadingSameWay)
                        distanceFromCurrentLinePivot *= -1.0;

                    // ** Pure pursuit ** - calc point on ABLine closest to current position
                    double U = (((pivot.easting - ctList[A].easting) * dx) + ((pivot.northing - ctList[A].northing) * dy))
                            / ((dx * dx) + (dy * dy));

                    rEastCT = ctList[A].easting + (U * dx);
                    rNorthCT = ctList[A].northing + (U * dy);

                    //update base on autosteer settings and distance from line
                    double goalPointDistance = vehicle.UpdateGoalPointDistance();

                    bool ReverseHeading = _appModel.isReverse ? !isHeadingSameWay : isHeadingSameWay;

                    int count = ReverseHeading ? 1 : -1;
                    vec3 start = new vec3(rEastCT, rNorthCT, 0);
                    double distSoFar = 0;

                    for (int i = ReverseHeading ? B : A; i < ptCount && i >= 0; i += count)
                    {
                        // used for calculating the length squared of next segment.
                        double tempDist = glm.Distance(start, ctList[i]);

                        //will we go too far?
                        if ((tempDist + distSoFar) > goalPointDistance)
                        {
                            double j = (goalPointDistance - distSoFar) / tempDist; // the remainder to yet travel

                            goalPointCT.easting = (((1 - j) * start.easting) + (j * ctList[i].easting));
                            goalPointCT.northing = (((1 - j) * start.northing) + (j * ctList[i].northing));
                            break;
                        }
                        else distSoFar += tempDist;
                        start = ctList[i];
                    }

                    //calc "D" the distance from pivot axle to lookahead point
                    double goalPointDistanceSquared = glm.DistanceSquared(goalPointCT.northing, goalPointCT.easting, pivot.northing, pivot.easting);

                    //calculate the the delta x in local coordinates and steering angle degrees based on wheelbase
                    double localHeading;

                    if (isHeadingSameWay) localHeading = glm.twoPI - _appModel.FixHeading.AngleInRadians + inty;
                    else localHeading = glm.twoPI - _appModel.FixHeading.AngleInRadians - inty;

                    steerAngleCT = glm.toDegrees(Math.Atan(2 * (((goalPointCT.easting - pivot.easting) * Math.Cos(localHeading))
                        + ((goalPointCT.northing - pivot.northing) * Math.Sin(localHeading))) * vehicle.VehicleConfig.Wheelbase / goalPointDistanceSquared));

                    if (ahrs.imuRoll != 88888)
                        steerAngleCT += ahrs.imuRoll * -gyd.sideHillCompFactor;

                    if (steerAngleCT < -vehicle.maxSteerAngle) steerAngleCT = -vehicle.maxSteerAngle;
                    if (steerAngleCT > vehicle.maxSteerAngle) steerAngleCT = vehicle.maxSteerAngle;
                }

                //used for smooth mode
                vehicle.modeActualXTE = (distanceFromCurrentLinePivot);

                //fill in the autosteer variables
                _appModel.guidanceLineDistanceOff = (short)Math.Round(distanceFromCurrentLinePivot * 1000.0, MidpointRounding.AwayFromZero);
                _appModel.guidanceLineSteerAngle = (short)(steerAngleCT * 100);
            }
            else
            {
                //invalid distance so tell AS module
                distanceFromCurrentLinePivot = 0;
                _appModel.guidanceLineDistanceOff = 0;
            }
        }

        //start stop and add points to list
        public void StartContourLine()
        {
            //make new ptList
            ptList = new List<vec3>(16);
            stripList.Add(ptList);
            isContourOn = true;
            return;
        }

        //Add current position to stripList
        public void AddPoint(vec3 pivot)
        {
            ptList.Add(new vec3(pivot.easting + Math.Cos(pivot.heading) * tool.offset,
                pivot.northing - Math.Sin(pivot.heading) * tool.offset,
                pivot.heading));
        }

        //End the strip
        public void StopContourLine()
        {
            //make sure its long enough to bother
            if (ptList.Count > 5)
            {
                //add the point list to the save list for appending to contour file
                contourSaveList.Add(ptList);
            }
            //delete ptList
            else
            {
                ptList.Clear();
            }

            //turn it off
            isContourOn = false;
        }

        //draw the red follow me line
        public void DrawContourLine()
        {
            int ptCount = ctList.Count;
            if (ptCount < 2) return;
            GL.LineWidth(ABLine.lineWidth);
            GL.Color3(0.98f, 0.2f, 0.980f);
            GL.Begin(PrimitiveType.LineStrip);
            for (int h = 0; h < ptCount; h++) GL.Vertex3(ctList[h].easting, ctList[h].northing, 0);
            GL.End();

            GL.PointSize(ABLine.lineWidth);
            GL.Begin(PrimitiveType.Points);

            GL.Color3(0.87f, 08.7f, 0.25f);
            for (int h = 0; h < ptCount; h++) GL.Vertex3(ctList[h].easting, ctList[h].northing, 0);

            GL.End();

            //Draw the captured ref strip, red if locked
            if (isLocked)
            {
                GL.Color3(0.983f, 0.92f, 0.420f);
                GL.LineWidth(4);
            }
            else
            {
                GL.Color3(0.3f, 0.982f, 0.0f);
                GL.LineWidth(ABLine.lineWidth);
            }

            if (stripNum > -1)
            {
                GL.Begin(PrimitiveType.Points);
                for (int h = 0; h < stripList[stripNum].Count; h++) GL.Vertex3(stripList[stripNum][h].easting, stripList[stripNum][h].northing, 0);
                GL.End();
            }

            GL.Color3(0.35f, 0.30f, 0.90f);
            GL.PointSize(6.0f);
            GL.Begin(PrimitiveType.Points);
            GL.Vertex3(stripList[stripNum][pt].easting, stripList[stripNum][pt].northing, 0);
            GL.End();

            if (Properties.Settings.Default.setMenu_isPureOn && distanceFromCurrentLinePivot != 32000 && !Properties.ToolSettings.Default.setVehicle_isStanleyUsed)
            {
                //Draw lookahead Point
                GL.PointSize(6.0f);
                GL.Begin(PrimitiveType.Points);

                GL.Color3(1.0f, 0.95f, 0.095f);
                GL.Vertex3(goalPointCT.easting, goalPointCT.northing, 0.0);
                GL.End();
                GL.PointSize(1.0f);
            }
        }

        //Reset the contour to zip
        public void ResetContour()
        {
            stripList.Clear();
            ptList?.Clear();
            ctList?.Clear();
        }
    }//class
}//namespace