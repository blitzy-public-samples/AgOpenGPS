// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Text;
using AgOpenGPS.Core;

namespace AgOpenGPS
{
    public class CFieldData
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;` + `CFieldData(FormGPS _f)`). The collaborators this class
        // read through `mf` are now constructor-injected and used live-by-reference, mirroring the
        // established CVehicle(ApplicationModel, ...) / CTool(ApplicationModel, ...) /
        // CBoundary(ApplicationModel, ...) decoupling — no DI container and no new abstraction
        // (AAP §0.7.1). No FormGPS reference remains, so the field-statistics math is portable across
        // Windows, Linux and macOS. The area / distance / application-rate formulas, the
        // metric/imperial unit conversions and the rounding are FROZEN — outputs are unchanged.
        // Field-by-field decoupling map (was mf.X):
        //   appModel - shared AgOpenGPS.Core runtime model. Supplies the smoothed vehicle speed in
        //              km/h (avgSpeed, was mf.avgSpeed; written by the fix/position pipeline, read
        //              live-by-reference here) and the current field name
        //              (Fields.CurrentFieldName, was mf.displayFieldName) for the field summary.
        //   tool     - implement/tool config (was mf.tool): width, numOfSections and overlap, used by
        //              the work-rate / time-till-finished math and the field summary.
        //   bnd      - boundary set (was mf.bnd): bndList outer/inner areas used to compute the field
        //              area. Cyclic peer of this class (CBoundary wires this CFieldData back via
        //              CBoundary.SetFieldData), so CBoundary is constructed first and injected here.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel appModel;
        private readonly CTool tool;
        private readonly CBoundary bnd;

        //all the section area added up;
        public double workedAreaTotal;

        //just a cumulative tally based on distance and eq width.
        public double workedAreaTotalUser;

        //accumulated user distance
        public double distanceUser;

        public double barPercent = 0;

        public double overlapPercent = 0;

        //Outside area minus inner boundaries areas (m)
        public double areaBoundaryOuterLessInner;

        //used for overlap calcs - total done minus overlap
        public double actualAreaCovered;

        //Inner area of outer boundary(m)
        public double areaOuterBoundary;

        //not really used - but if needed
        public double userSquareMetersAlarm;

        // [XPLAT] All numeric ToString sites below pass CultureInfo.InvariantCulture so the
        // field-statistics text (used on-screen and in the GetDescription field summary) is
        // byte-stable across locales — a Linux/macOS comma-decimal locale would otherwise corrupt any
        // persisted/exported summary (AAP §0.6.5). The format specifiers and precision are unchanged,
        // so on the invariant/en-US reference build the output is identical.

        //Area inside Boundary less inside boundary areas
        public string AreaBoundaryLessInnersHectares => (areaBoundaryOuterLessInner * glm.m2ha).ToString("N2", CultureInfo.InvariantCulture);

        public string AreaBoundaryLessInnersAcres => (areaBoundaryOuterLessInner * glm.m2ac).ToString("N2", CultureInfo.InvariantCulture);

        //USer tally string
        public string WorkedUserHectares => (workedAreaTotalUser * glm.m2ha).ToString("N2", CultureInfo.InvariantCulture);

        //user tally string
        public string WorkedUserAcres => (workedAreaTotalUser * glm.m2ac).ToString("N2", CultureInfo.InvariantCulture);

        //String of Area worked
        public string WorkedAcres => (workedAreaTotal * 0.000247105).ToString("N2", CultureInfo.InvariantCulture);

        public string WorkedHectares => (workedAreaTotal * 0.0001).ToString("N2", CultureInfo.InvariantCulture);

        //User Distance strings
        public string DistanceUserMeters => Math.Round(distanceUser, 1).ToString(CultureInfo.InvariantCulture);

        public string DistanceUserFeet => Math.Round((distanceUser * glm.m2ft), 1).ToString(CultureInfo.InvariantCulture);

        //remaining area to be worked
        public string WorkedAreaRemainHectares => ((areaBoundaryOuterLessInner - workedAreaTotal) * glm.m2ha).ToString("N2", CultureInfo.InvariantCulture);

        public string WorkedAreaRemainAcres => ((areaBoundaryOuterLessInner - workedAreaTotal) * glm.m2ac).ToString("N2", CultureInfo.InvariantCulture);

        public string WorkedAreaRemainPercentage
        {
            get
            {
                if (areaBoundaryOuterLessInner > 10)
                {
                    barPercent = ((areaBoundaryOuterLessInner - workedAreaTotal) * 100 / areaBoundaryOuterLessInner);
                    return barPercent.ToString("N1", CultureInfo.InvariantCulture) + "%";
                }
                else
                {
                    barPercent = 0;
                    return "0%";
                }
            }
        }

        //overlap strings
        public string ActualAreaWorkedHectares => (actualAreaCovered * glm.m2ha).ToString("N2", CultureInfo.InvariantCulture);
        public string ActualAreaWorkedAcres => (actualAreaCovered * glm.m2ac).ToString("N2", CultureInfo.InvariantCulture);

        public string ActualRemainHectares => ((areaBoundaryOuterLessInner - actualAreaCovered) * glm.m2ha).ToString("N2", CultureInfo.InvariantCulture);
        public string ActualRemainAcres => ((areaBoundaryOuterLessInner - actualAreaCovered) * glm.m2ac).ToString("N2", CultureInfo.InvariantCulture);

        public string ActualOverlapPercent => overlapPercent.ToString("N1", CultureInfo.InvariantCulture) + "% ";

        public string TimeTillFinished
        {
            get
            {
                if (appModel.avgSpeed > 2)
                {
                    TimeSpan timeSpan = TimeSpan.FromHours(((areaBoundaryOuterLessInner - workedAreaTotal) * glm.m2ha
                        / (tool.width * appModel.avgSpeed * 0.1)));
                    return timeSpan.Hours.ToString("00:", CultureInfo.InvariantCulture) + timeSpan.Minutes.ToString("00", CultureInfo.InvariantCulture) + '"';
                }
                else return "\u221E Hrs";
            }
        }

        public string WorkRateHectares => (tool.width * appModel.avgSpeed * 0.1).ToString("N1", CultureInfo.InvariantCulture) + " ha/hr";
        public string WorkRateAcres => (tool.width * appModel.avgSpeed * 0.2471).ToString("N1", CultureInfo.InvariantCulture) + " ac/hr";

        //constructor
        // [XPLAT] Was CFieldData(FormGPS _f). The shared ApplicationModel and the tool/boundary
        // collaborators are constructor-injected (no DI container, no new abstraction — AAP §0.7.1).
        // bnd is a cyclic peer wired back via CBoundary.SetFieldData, so CBoundary is constructed
        // before this CFieldData and passed in here. The initialization body is unchanged.
        public CFieldData(ApplicationModel appModel, CTool tool, CBoundary bnd)
        {
            this.appModel = appModel;
            this.tool = tool;
            this.bnd = bnd;
            workedAreaTotal = 0;
            workedAreaTotalUser = 0;
            userSquareMetersAlarm = 0;
        }

        public void UpdateFieldBoundaryGUIAreas()
        {
            if (bnd.bndList.Count > 0)
            {
                areaOuterBoundary = bnd.bndList[0].area;
                areaBoundaryOuterLessInner = areaOuterBoundary;

                for (int i = 1; i < bnd.bndList.Count; i++)
                {
                    areaBoundaryOuterLessInner -= bnd.bndList[i].area;
                }
            }
            else
            {
                areaOuterBoundary = 0;
                areaBoundaryOuterLessInner = 0;
            }
            // [XPLAT] The manual-on/off button label (formerly set here by the already-disabled
            // `if (isMetric) btnManualOffOn.Text = AreaBoundaryLessInnersHectares; else ...Acres;`
            // that read the WinForms button via the FormGPS host) is now produced by the bound
            // Avalonia view-model from its units state and these area properties. Behavior is
            // unchanged: the assignment was commented out in the original, so nothing is emitted here.
        }

        public String GetDescription()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Field: {0}", appModel.Fields.CurrentFieldName);
            sb.AppendLine();
            sb.AppendFormat("Total Hectares: {0}", AreaBoundaryLessInnersHectares);
            sb.AppendLine();
            sb.AppendFormat("Worked Hectares: {0}", WorkedHectares);
            sb.AppendLine();
            sb.AppendFormat("Missing Hectares: {0}", WorkedAreaRemainHectares);
            sb.AppendLine();
            sb.AppendFormat("Total Acres: {0}", AreaBoundaryLessInnersAcres);
            sb.AppendLine();
            sb.AppendFormat("Worked Acres: {0}", WorkedAcres);
            sb.AppendLine();
            sb.AppendFormat("Missing Acres: {0}", WorkedAreaRemainAcres);
            sb.AppendLine();
            // [XPLAT] Numeric inserts use InvariantCulture so this field summary is byte-stable across
            // locales (AAP §0.6.5); values/precision are unchanged from the originals.
            sb.AppendFormat(CultureInfo.InvariantCulture, "Tool Width: {0}", tool.width);
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Sections: {0}", tool.numOfSections);
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Section Overlap: {0}", tool.overlap);
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
