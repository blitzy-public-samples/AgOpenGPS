// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    // [XPLAT] TrackMode and the CTrk track-line model were moved to Classes/CTrk.cs (Extract-Class) so
    // the cross-platform track I/O / ISOXML / Views can build while this track manager stays gated. The
    // manager itself is now decoupled from the WinForms host form (see the class below), but remains gated
    // in AgOpenGPS.csproj until its guidance collaborators (CABCurve/CABLine/CYouTurn) are likewise
    // decoupled. See Classes/CTrk.cs and MIGRATION_DOCS/TRANSITION_MAP.md.

    public class CTrack
    {
        // [XPLAT] Decoupled from the WinForms host-form god-object (formerly `private readonly FormGPS mf;`).
        // The collaborators this track manager needs are now injected (tool, ApplicationModel) or late-wired
        // (curve/ABLine/yt), and the vehicle steer-axle position is read live-by-reference from the shared
        // AgOpenGPS.Core ApplicationModel — exactly as the already-decoupled CSmartWAS does — so this manager
        // runs free of any WinForms coupling on Windows, Linux and macOS. See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly CTool tool;
        private readonly ApplicationModel _appModel;

        // [XPLAT] Guidance-line peers (formerly mf.curve / mf.ABLine / mf.yt). They form construction cycles
        // with CTrack, so they are late-wired after construction via SetGuidanceReferences, mirroring the
        // established late-wire setters (CVehicle.SetTool/SetBoundary, CTool.SetTram).
        private CABCurve curve;
        private CABLine ABLine;
        private CYouTurn yt;

        public List<CTrk> gArr = new List<CTrk>();

        public int idx, autoTrack3SecTimer;

        public bool isAutoTrack = false, isAutoSnapToPivot = false, isAutoSnapped;

        // [XPLAT] Inject the non-cyclic collaborators directly: the tool geometry (was mf.tool) and the
        // shared ApplicationModel carrying the live steer-axle position (was mf.steerAxlePos). The cyclic
        // guidance peers are late-wired via SetGuidanceReferences once all guidance objects are constructed.
        public CTrack(CTool tool, ApplicationModel appModel)
        {
            //constructor
            this.tool = tool;
            _appModel = appModel;
            idx = -1;
        }

        // [XPLAT] Post-construct wiring for the curve/ABLine/yt construction cycle (was mf.curve / mf.ABLine /
        // mf.yt), mirroring the established late-wire setters (e.g. CVehicle.SetTool/SetBoundary). Call once
        // after the guidance objects are constructed.
        public void SetGuidanceReferences(CABCurve curve, CABLine ABLine, CYouTurn yt)
        {
            this.curve = curve;
            this.ABLine = ABLine;
            this.yt = yt;
        }

        public int FindClosestRefTrack(vec3 pivot)
        {
            if (idx < 0 || gArr.Count == 0) return -1;

            //only 1 track
            if (gArr.Count == 1) return idx;

            int trak = -1;
            int cntr = 0;

            //Count visible
            for (int i = 0; i < gArr.Count; i++)
            {
                if (gArr[i].isVisible)
                {
                    cntr++;
                    trak = i;
                }
            }

            //only 1 track visible of the group
            if (cntr == 1) return trak;

            //no visible tracks
            if (cntr == 0) return -1;

            //determine if any aligned reasonably close
            bool[] isAlignedArr = new bool[gArr.Count];
            for (int i = 0; i < gArr.Count; i++)
            {
                if (gArr[i].mode == TrackMode.Curve) isAlignedArr[i] = true;
                else
                {
                    double diff = Math.PI - Math.Abs(Math.Abs(pivot.heading - gArr[i].heading) - Math.PI);
                    if (diff < 1 || diff > 2.14)
                        isAlignedArr[i] = true;
                    else
                        isAlignedArr[i] = false;
                }
            }

            double minDistA = double.MaxValue;
            double dist;

            vec2 endPtA, endPtB;

            for (int i = 0; i < gArr.Count; i++)
            {
                if (!isAlignedArr[i]) continue;
                if (!gArr[i].isVisible) continue;

                if (gArr[i].mode == TrackMode.AB)
                {
                    double abHeading = gArr[i].heading;

                    endPtA.easting = gArr[i].ptA.easting - (Math.Sin(abHeading) * 2000);
                    endPtA.northing = gArr[i].ptA.northing - (Math.Cos(abHeading) * 2000);

                    endPtB.easting = gArr[i].ptB.easting + (Math.Sin(abHeading) * 2000);
                    endPtB.northing = gArr[i].ptB.northing + (Math.Cos(abHeading) * 2000);

                    //x2-x1
                    double dx = endPtB.easting - endPtA.easting;
                    //z2-z1
                    double dy = endPtB.northing - endPtA.northing;

                    dist = ((dy * _appModel.SteerAxlePos.Easting) - (dx * _appModel.SteerAxlePos.Northing) + (endPtB.easting
                                            * endPtA.northing) - (endPtB.northing * endPtA.easting))
                                                / Math.Sqrt((dy * dy) + (dx * dx));

                    dist *= dist;

                    if (dist < minDistA)
                    {
                        minDistA = dist;
                        trak = i;
                    }
                }
                else
                {
                    for (int j = 0; j < gArr[i].curvePts.Count; j++)
                    {

                        dist = glm.DistanceSquared(gArr[i].curvePts[j], pivot);

                        if (dist < minDistA)
                        {
                            minDistA = dist;
                            trak = i;
                        }
                    }
                }
            }

            return trak;
        }

        public void NudgeTrack(double dist)
        {
            if (idx > -1)
            {
                if (gArr[idx].mode == TrackMode.AB)
                {
                    ABLine.isABValid = false;
                    gArr[idx].nudgeDistance += ABLine.isHeadingSameWay ? dist : -dist;
                }
                else
                {
                    curve.isCurveValid = false;
                    gArr[idx].nudgeDistance += curve.isHeadingSameWay ? dist : -dist;

                }

                // Rebuild uturn after nudge to reflect new track position
                yt.RebuildAfterNudge();

                //if (gArr[idx].nudgeDistance > 0.5 * tool.width) gArr[idx].nudgeDistance -= tool.width;
                //else if (gArr[idx].nudgeDistance < -0.5 * tool.width) gArr[idx].nudgeDistance += tool.width;
            }
        }

        public void NudgeDistanceReset()
        {
            if (idx > -1 && gArr.Count > 0)
            {
                if (gArr[idx].mode == TrackMode.AB)
                {
                    ABLine.isABValid = false;
                }
                else
                {
                    curve.isCurveValid = false;
                }

                gArr[idx].nudgeDistance = 0;

                // Rebuild uturn after reset to reflect new track position
                yt.RebuildAfterNudge();
            }
        }

        public void SnapToPivot()
        {
            if (idx > -1)
            {
                NudgeTrack(gArr[idx].mode == TrackMode.AB ? ABLine.distanceFromCurrentLinePivot : curve.distanceFromCurrentLinePivot);
            }
        }

        public void NudgeRefTrack(double dist)
        {
            if (idx > -1)
            {
                if (gArr[idx].mode == TrackMode.AB)
                {
                    ABLine.isABValid = false;
                    NudgeRefABLine(ABLine.isHeadingSameWay ? dist : -dist);
                }
                else
                {
                    curve.isCurveValid = false;
                    NudgeRefCurve(curve.isHeadingSameWay ? dist : -dist);
                }
            }
        }

        public void NudgeRefABLine(double dist)
        {
            double head = gArr[idx].heading;

            gArr[idx].ptA.easting += (Math.Sin(head + glm.PIBy2) * (dist));
            gArr[idx].ptA.northing += (Math.Cos(head + glm.PIBy2) * (dist));

            gArr[idx].ptB.easting += (Math.Sin(head + glm.PIBy2) * (dist));
            gArr[idx].ptB.northing += (Math.Cos(head + glm.PIBy2) * (dist));
        }

        public void NudgeRefCurve(double distAway)
        {
            curve.isCurveValid = false;

            List<vec3> curList = new List<vec3>();

            double distSqAway = (distAway * distAway) - 0.01;
            vec3 point;

            for (int i = 0; i < gArr[idx].curvePts.Count; i++)
            {
                point = new vec3(
                gArr[idx].curvePts[i].easting + (Math.Sin(glm.PIBy2 + gArr[idx].curvePts[i].heading) * distAway),
                gArr[idx].curvePts[i].northing + (Math.Cos(glm.PIBy2 + gArr[idx].curvePts[i].heading) * distAway),
                gArr[idx].curvePts[i].heading);
                bool Add = true;

                for (int t = 0; t < gArr[idx].curvePts.Count; t++)
                {
                    double dist = ((point.easting - gArr[idx].curvePts[t].easting) * (point.easting - gArr[idx].curvePts[t].easting))
                        + ((point.northing - gArr[idx].curvePts[t].northing) * (point.northing - gArr[idx].curvePts[t].northing));
                    if (dist < distSqAway)
                    {
                        Add = false;
                        break;
                    }
                }

                if (Add)
                {
                    if (curList.Count > 0)
                    {
                        double dist = ((point.easting - curList[curList.Count - 1].easting) * (point.easting - curList[curList.Count - 1].easting))
                            + ((point.northing - curList[curList.Count - 1].northing) * (point.northing - curList[curList.Count - 1].northing));
                        if (dist > 1.0)
                            curList.Add(point);
                    }
                    else curList.Add(point);
                }
            }

            int cnt = curList.Count;
            if (cnt > 6)
            {
                // Set ptA and ptB from the shifted raw points before Catmull-Rom extension
                gArr[idx].ptA = new vec2(curList[0].easting, curList[0].northing);
                gArr[idx].ptB = new vec2(curList[cnt - 1].easting, curList[cnt - 1].northing);

                vec3[] arr = new vec3[cnt];
                curList.CopyTo(arr);

                curList.Clear();

                for (int i = 0; i < (arr.Length - 1); i++)
                {
                    arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                    if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                    if (arr[i].heading >= glm.twoPI) arr[i].heading -= glm.twoPI;
                }

                arr[arr.Length - 1].heading = arr[arr.Length - 2].heading;

                //replace the array
                cnt = arr.Length;
                double distance;
                double spacing = 1.2;

                //add the first point of loop - it will be p1
                curList.Add(arr[0]);

                for (int i = 0; i < cnt - 3; i++)
                {
                    // add p2
                    curList.Add(arr[i + 1]);

                    distance = glm.Distance(arr[i + 1], arr[i + 2]);

                    if (distance > spacing)
                    {
                        int loopTimes = (int)(distance / spacing + 1);
                        for (int j = 1; j < loopTimes; j++)
                        {
                            vec3 pos = new vec3(glm.Catmull(j / (double)(loopTimes), arr[i], arr[i + 1], arr[i + 2], arr[i + 3]));
                            curList.Add(pos);
                        }
                    }
                }

                curList.Add(arr[cnt - 2]);
                curList.Add(arr[cnt - 1]);

                CABCurve.CalculateHeadings(ref curList);

                gArr[idx].curvePts.Clear();

                foreach (var item in curList)
                {
                    gArr[idx].curvePts.Add(new vec3(item));
                }

                //for (int i = 0; i < cnt; i++)
                //{
                //    arr[i].easting += Math.Cos(arr[i].heading) * (dist);
                //    arr[i].northing -= Math.Sin(arr[i].heading) * (dist);
                //    gArr[idx].curvePts.Add(arr[i]);
                //}
            }
        }
    }

    // [XPLAT] The CTrk track-line model that lived here was moved to Classes/CTrk.cs (Extract-Class);
    // see the note at the top of this namespace.
}
