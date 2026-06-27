// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//Please, if you use this, share the improvements

using System;
using System.Collections.Generic;
using AgOpenGPS.Core;

namespace AgOpenGPS
{
    public class CPatches
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;` + `CPatches(FormGPS _f)`). The collaborators this class read
        // through `mf` are now constructor-injected, and the shared cross-platform runtime state is read
        // from the AgOpenGPS.Core ApplicationModel — mirroring the established
        // CTool(ApplicationModel, ...) / CFieldData(ApplicationModel, ...) decoupling. No DI container and
        // no new abstraction (AAP §0.7.1). No FormGPS reference remains, so the applied-area coverage
        // builder is portable across Windows, Linux and macOS. The patch triangle-strip geometry, the
        // per-patch RGB colour bytes and the Sections.txt coverage-save format are FROZEN — outputs are
        // byte/value-identical (FieldRoundTripTests + render parity). Field-by-field decoupling map
        // (was mf.X):
        //   appModel      - shared AgOpenGPS.Core runtime model. Supplies the section day colour
        //                   (SectionColorDay, was mf.sectionColorDay, System.Drawing.Color — its R/G/B
        //                   bytes are written verbatim into the patch colour vertex) and the diagnostic
        //                   applied-patch tally (patchCounter, was mf.patchCounter, int). Both are read/
        //                   written live-by-reference; the settings loader and field life-cycle write them.
        //   tool          - implement/tool config (was mf.tool): isMultiColoredSections / isSectionsNotZones
        //                   gate the colour source, and secColors[j] (Core ColorRgba) supplies the per-
        //                   section colour. ColorRgba's .Red/.Green/.Blue expose the SAME bytes the old
        //                   System.Drawing.Color .R/.G/.B did, so the written colour vertex is unchanged.
        //   section       - per-section state array (was mf.section): leftPoint/rightPoint world-space
        //                   edges that become the triangle-strip vertices.
        //   fd            - field data (was mf.fd): the worked-area accumulators (workedAreaTotal /
        //                   workedAreaTotalUser) tallied per two-triangle step.
        //   patchSaveList - shared coverage save-list (was mf.patchSaveList, List<List<vec3>>). It stays in
        //                   the GPS layer and is injected live-by-reference because vec3 is a GPS type that
        //                   must not leak into AgOpenGPS.Core (exactly as ApplicationModel keeps vehicle
        //                   positions as Core GeoCoord rather than vec3); completed/cutoff patches are
        //                   appended here and the saved Sections.txt format is frozen (SectionsFiles.Append).
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel appModel;
        private readonly CTool tool;
        private readonly CSection[] section;
        private readonly CFieldData fd;
        private readonly List<List<vec3>> patchSaveList;

        //list of patch data individual triangles
        public List<vec3> triangleList = new List<vec3>();

        //list of the list of patch data individual triangles for that entire section activity
        public List<List<vec3>> patchList = new List<List<vec3>>();

        //mapping
        public bool isDrawing = false;

        //points in world space that start and end of section are in
        public vec2 leftPoint, rightPoint;

        public int numTriangles = 0;
        public int currentStartSectionNum, currentEndSectionNum;
        public int newStartSectionNum, newEndSectionNum;

        //simple constructor, collaborators are injected by the composition root when creating the object
        public CPatches(ApplicationModel appModel, CTool tool, CSection[] section, CFieldData fd, List<List<vec3>> patchSaveList)
        {
            //constructor - collaborators injected (replaces the former FormGPS `mf` back-reference)
            this.appModel = appModel;
            this.tool = tool;
            this.section = section;
            this.fd = fd;
            this.patchSaveList = patchSaveList;
            patchList.Capacity = 2048;
            //triangleList.Capacity =
        }

        public void TurnMappingOn(int j)
        {
            numTriangles = 0;

            //do not tally square meters on inital point, that would be silly
            if (!isDrawing)
            {
                //set the section bool to on
                isDrawing = true;

                //starting a new patch chunk so create a new triangle list
                triangleList = new List<vec3>(64);

                patchList.Add(triangleList);

                if (!tool.isMultiColoredSections)
                {
                    triangleList.Add(new vec3(appModel.SectionColorDay.R, appModel.SectionColorDay.G, appModel.SectionColorDay.B));
                }
                else
                {
                    if (tool.isSectionsNotZones)
                        triangleList.Add(new vec3(tool.secColors[j].Red, tool.secColors[j].Green, tool.secColors[j].Blue));
                    else
                        triangleList.Add(new vec3(appModel.SectionColorDay.R, appModel.SectionColorDay.G, appModel.SectionColorDay.B));
                }

                leftPoint = section[currentStartSectionNum].leftPoint;
                rightPoint = section[currentEndSectionNum].rightPoint;

                //left side of triangle
                triangleList.Add(new vec3(leftPoint.easting, leftPoint.northing, 0));

                //Right side of triangle
                triangleList.Add(new vec3(rightPoint.easting, rightPoint.northing, 0));

                appModel.patchCounter++;
            }
        }

        public void TurnMappingOff()
        {
            AddMappingPoint(0);

            isDrawing = false;
            numTriangles = 0;

            if (triangleList.Count > 4)
            {
                //save the triangle list in a patch list to add to saving file
                patchSaveList.Add(triangleList);
            }
            else
            {
                triangleList.Clear();
                if (patchList.Count > 0) patchList.RemoveAt(patchList.Count - 1);
            }
        }

        //every time a new fix, a new patch point from last point to this point
        //only need prev point on the first points of triangle strip that makes a box (2 triangles)

        public void AddMappingPoint(int j)
        {
            leftPoint = section[currentStartSectionNum].leftPoint;
            rightPoint = section[currentEndSectionNum].rightPoint;

            //add two triangles for next step.
            //left side

            //add the point to List
            triangleList.Add(new vec3(leftPoint.easting, leftPoint.northing, 0));

            //Right side
            triangleList.Add(new vec3(rightPoint.easting, rightPoint.northing, 0));

            //countExit the triangle pairs
            numTriangles++;

            //quick countExit
            int c = triangleList.Count - 1;

            //when closing a job the triangle patches all are emptied but the section delay keeps going.
            //Prevented by quick check. 4 points plus colour
            //if (c >= 5)
            {
                //calculate area of these 2 new triangles - AbsoluteValue of (Ax(By-Cy) + Bx(Cy-Ay) + Cx(Ay-By)/2)
                {
                    double temp = Math.Abs((triangleList[c].easting * (triangleList[c - 1].northing - triangleList[c - 2].northing))
                              + (triangleList[c - 1].easting * (triangleList[c - 2].northing - triangleList[c].northing))
                                  + (triangleList[c - 2].easting * (triangleList[c].northing - triangleList[c - 1].northing)));

                    temp += Math.Abs((triangleList[c - 1].easting * (triangleList[c - 2].northing - triangleList[c - 3].northing))
                              + (triangleList[c - 2].easting * (triangleList[c - 3].northing - triangleList[c - 1].northing))
                                  + (triangleList[c - 3].easting * (triangleList[c - 1].northing - triangleList[c - 2].northing)));

                    temp *= 0.5;
                    fd.workedAreaTotal += temp;
                    fd.workedAreaTotalUser += temp;
                }
            }

            if (numTriangles > 61)
            {
                numTriangles = 0;

                //save the cutoff patch to be saved later
                patchSaveList.Add(triangleList);

                triangleList = new List<vec3>(64);

                patchList.Add(triangleList);

                //Add Patch colour
                if (!tool.isMultiColoredSections)
                    triangleList.Add(new vec3(appModel.SectionColorDay.R, appModel.SectionColorDay.G, appModel.SectionColorDay.B));
                else
                    triangleList.Add(new vec3(tool.secColors[j].Red, tool.secColors[j].Green, tool.secColors[j].Blue));

                //add the points to List, yes its more points, but breaks up patches for culling
                triangleList.Add(new vec3(leftPoint.easting, leftPoint.northing, 0));
                triangleList.Add(new vec3(rightPoint.easting, rightPoint.northing, 0));
            }
        }
    }
}