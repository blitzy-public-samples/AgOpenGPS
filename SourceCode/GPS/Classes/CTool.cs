// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Classes;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using OpenTK.Graphics.OpenGL;
using System;
using System.Globalization;

namespace AgOpenGPS
{
    public class CTool
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;` + `CTool(FormGPS _f)`). The collaborators this class read
        // through `mf` are now constructor-injected (or, for the cyclic CTram peer, wired
        // post-construct via SetTram) and used live-by-reference, mirroring the established
        // CVehicle(ApplicationModel, ...) / CSim(ApplicationModel, ...) decoupling. No FormGPS
        // reference remains, so the class is portable across Windows, Linux and macOS. The tool
        // width / section-layout / offset math, the section-count semantics (1–16 unique / up to 64
        // same-width) and the GL tool draw (vertex order and values) are FROZEN — outputs are
        // unchanged. Field-by-field decoupling map (was mf.X):
        //   _appModel        - shared AgOpenGPS.Core runtime model. Supplies the vehicle heading
        //                      (FixHeading.AngleInRadians, was mf.fixHeading), the tool/tank hitch
        //                      headings (ToolPivotHeading / TankHeading, were mf.toolPivotPos.heading /
        //                      mf.tankPos.heading) and the field-job gate (isJobStarted, was
        //                      mf.isJobStarted). Read live-by-reference; the fix/position pipeline
        //                      writes them.
        //   _section         - per-section state array (was mf.section): on/mapping flags, button
        //                      tri-state and left/right edge positions used by the section draw.
        //   _vehicle         - vehicle config + hydraulic-lift look-ahead (was mf.vehicle).
        //   _camera          - render camera (was mf.camera); read-only camSetDistance for LOD/overlays.
        //   _vehicleTextures - tool-axle + tire textures (was mf.VehicleTextures); identical Texture2D draws.
        //   _sim             - GPS simulator (was mf.sim). Its migrated IsActive flag replaces the old
        //                      WinForms `mf.timerSim.Enabled` gate (NOT a System.Windows.Forms.Timer).
        //   _mc              - module-comm state (was mf.mc); actualSteerAngleDegrees for articulation.
        //   _tram            - tramline overlay (was mf.tram). Cyclic peer (CTram's constructor reads
        //                      tool.width), so it is created AFTER this CTool and wired via SetTram.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel _appModel;
        private readonly CSection[] _section;
        private readonly CVehicle _vehicle;
        private readonly Camera _camera;
        private readonly VehicleTextures _vehicleTextures;
        private readonly CSim _sim;
        private readonly CModuleComm _mc;

        // [XPLAT] Cyclic collaborator wired post-construct (see SetTram). Non-readonly because it is
        // assigned after the constructor runs, exactly like CVehicle's late-wired _tool/_bnd.
        private CTram _tram;

        public double width, halfWidth, contourWidth;
        public double farLeftPosition = 0;
        public double farLeftSpeed = 0;
        public double farRightPosition = 0;
        public double farRightSpeed = 0;

        public double overlap;
        public double trailingHitchLength, tankTrailingHitchLength, trailingToolToPivotLength;
        public double offset;

        public double lookAheadOffSetting, lookAheadOnSetting;
        public double turnOffDelay;

        public double lookAheadDistanceOnPixelsLeft, lookAheadDistanceOnPixelsRight;
        public double lookAheadDistanceOffPixelsLeft, lookAheadDistanceOffPixelsRight;

        public bool isToolTrailing, isToolTBT;
        public bool isToolRearFixed, isToolFrontFixed;

        public bool isMultiColoredSections, isSectionOffWhenOut;

        public double hitchLength;

        //how many individual sections
        public int numOfSections;

        //used for super section off on
        public int minCoverage;

        public bool isLeftSideInHeadland = true, isRightSideInHeadland = true, isSectionsNotZones;

        //read pixel values
        public int rpXPosition;

        public int rpWidth;

        private double textRotate;

        // [XPLAT] Was System.Drawing.Color[]. Stored as the portable Core ColorRgba (same RGBA byte
        // values); the System.Drawing.Color settings values are converted via ColorRgba's explicit
        // operator below, so the section colors are byte-for-byte identical and System.Drawing is
        // no longer referenced.
        public ColorRgba[] secColors = new ColorRgba[16];

        public int zones;
        public int[] zoneRanges = new int[9];

        public bool isDisplayTramControl;

        // [XPLAT] Was CTool(FormGPS _f). The shared runtime model and the leaf collaborators are
        // injected here; the cyclic tram peer is supplied later via SetTram. Every settings read below
        // is preserved verbatim from the net48 original, so the tool geometry, the section layout and
        // the section colors are unchanged (frozen).
        public CTool(
            ApplicationModel appModel,
            CSection[] section,
            CVehicle vehicle,
            Camera camera,
            VehicleTextures vehicleTextures,
            CSim sim,
            CModuleComm mc)
        {
            _appModel = appModel;
            _section = section;
            _vehicle = vehicle;
            _camera = camera;
            _vehicleTextures = vehicleTextures;
            _sim = sim;
            _mc = mc;

            //from settings grab the vehicle specifics

            trailingToolToPivotLength = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength;
            width = Properties.ToolSettings.Default.setVehicle_toolWidth;
            overlap = Properties.ToolSettings.Default.setVehicle_toolOverlap;

            offset = Properties.ToolSettings.Default.setVehicle_toolOffset;

            trailingHitchLength = Properties.ToolSettings.Default.setVehicle_toolTrailingHitchLength;
            tankTrailingHitchLength = Properties.ToolSettings.Default.setVehicle_tankTrailingHitchLength;
            hitchLength = Properties.ToolSettings.Default.setVehicle_hitchLength;

            isToolRearFixed = Properties.ToolSettings.Default.setTool_isToolRearFixed;
            isToolTrailing = Properties.ToolSettings.Default.setTool_isToolTrailing;
            isToolTBT = Properties.ToolSettings.Default.setTool_isToolTBT;
            isToolFrontFixed = Properties.ToolSettings.Default.setTool_isToolFront;

            lookAheadOnSetting = Properties.ToolSettings.Default.setVehicle_toolLookAheadOn;
            lookAheadOffSetting = Properties.ToolSettings.Default.setVehicle_toolLookAheadOff;
            turnOffDelay = Properties.ToolSettings.Default.setVehicle_toolOffDelay;

            isSectionOffWhenOut = Properties.ToolSettings.Default.setTool_isSectionOffWhenOut;

            isSectionsNotZones = Properties.ToolSettings.Default.setTool_isSectionsNotZones;

            if (isSectionsNotZones)
                numOfSections = Properties.ToolSettings.Default.setVehicle_numSections;
            else
                numOfSections = Properties.ToolSettings.Default.setTool_numSectionsMulti;

            minCoverage = (int)Properties.ToolSettings.Default.setVehicle_minCoverage;
            isMultiColoredSections = Properties.ToolSettings.Default.setColor_isMultiColorSections;

            // [XPLAT] setColor_secNN remain System.Drawing.Color in the settings; the explicit
            // ColorRgba conversion preserves the RGBA bytes exactly while keeping this class free of a
            // System.Drawing using-directive. CheckColorFor255() (AgOpenGPS extension) is unchanged.
            secColors[0] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec01.CheckColorFor255();
            secColors[1] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec02.CheckColorFor255();
            secColors[2] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec03.CheckColorFor255();
            secColors[3] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec04.CheckColorFor255();
            secColors[4] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec05.CheckColorFor255();
            secColors[5] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec06.CheckColorFor255();
            secColors[6] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec07.CheckColorFor255();
            secColors[7] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec08.CheckColorFor255();
            secColors[8] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec09.CheckColorFor255();
            secColors[9] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec10.CheckColorFor255();
            secColors[10] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec11.CheckColorFor255();
            secColors[11] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec12.CheckColorFor255();
            secColors[12] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec13.CheckColorFor255();
            secColors[13] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec14.CheckColorFor255();
            secColors[14] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec15.CheckColorFor255();
            secColors[15] = (ColorRgba)Properties.ToolSettings.Default.setColor_sec16.CheckColorFor255();

            string[] words = Properties.ToolSettings.Default.setTool_zones.Split(',');
            // [XPLAT] Pinned to InvariantCulture so zone parsing is locale-independent (a comma/period
            // decimal locale must not change how these integer tokens are read). Numeric semantics
            // unchanged.
            zones = int.Parse(words[0], CultureInfo.InvariantCulture);

            for (int i = 0; i < Math.Min(words.Length, zoneRanges.Length); i++)
            {
                zoneRanges[i] = int.Parse(words[i], CultureInfo.InvariantCulture);
            }

            isDisplayTramControl = Properties.ToolSettings.Default.setTool_isDisplayTramControl;
        }

        // [XPLAT] Post-construct wiring for the tramline collaborator (was mf.tram). CTram's
        // constructor reads tool.width, so the tram is created after this CTool and supplied here,
        // breaking the construction cycle with no new abstraction (mirrors CVehicle.SetTool).
        public void SetTram(CTram tram)
        {
            _tram = tram;
        }

        public double GetHitchLengthFromVehiclePivot()
        {
            double pivotToHitch = hitchLength;

            if (_vehicle.VehicleConfig.Type == VehicleType.Articulated && !glm.IsZero(pivotToHitch))
            {
                double halfWheelbase = 0.5 * _vehicle.VehicleConfig.Wheelbase;

                if (!glm.IsZero(halfWheelbase))
                {
                    pivotToHitch += Math.Sign(pivotToHitch) * halfWheelbase;
                }
            }

            return pivotToHitch;
        }

        public double GetHitchHeadingFromVehiclePivot(double pivotToHitchLength)
        {
            // [XPLAT] mf.fixHeading -> _appModel.FixHeading.AngleInRadians (exactly what the former
            // FormGPS.fixHeading getter returned); guidance/draw heading is unchanged.
            double hitchHeading = _appModel.FixHeading.AngleInRadians;

            if (_vehicle.VehicleConfig.Type == VehicleType.Articulated && !glm.IsZero(pivotToHitchLength))
            {
                // [XPLAT] mf.timerSim.Enabled -> _sim.IsActive (CSim's portable run-state flag, NOT a
                // WinForms Timer); mf.sim/mf.mc -> injected _sim/_mc. Selection and sign are unchanged.
                double steerAngleDegrees = _sim.IsActive ? _sim.steerAngle : _mc.actualSteerAngleDegrees;
                double articulationRadians = glm.toRadians(steerAngleDegrees);

                // The hitch translation already starts from the averaged vehicle heading
                // (fixHeading). Applying the full rear-frame deflection on top of that
                // over-rotates the hitch, so scale the articulation once more to keep the
                // lateral movement aligned with the rear frame.
                double rearHeadingOffset = 0.25 * articulationRadians;

                if (pivotToHitchLength > 0)
                {
                    hitchHeading += rearHeadingOffset;
                }
                else
                {
                    hitchHeading -= rearHeadingOffset;
                }

                hitchHeading = NormalizeAngle(hitchHeading);
            }

            return hitchHeading;
        }

        private static double NormalizeAngle(double angle)
        {
            if (angle < 0)
            {
                angle = (angle % glm.twoPI) + glm.twoPI;
            }
            else if (angle >= glm.twoPI)
            {
                angle %= glm.twoPI;
            }

            return angle;
        }

        private static void DrawHitch(double trailingTank)
        {
            XyCoord[] vertices = {
                new XyCoord(-0.57, trailingTank),
                new XyCoord(0.0, 0.0),
                new XyCoord(0.57, trailingTank)
            };
            LineStyle backgroundLineStyle = new LineStyle(6.0f, Colors.Black);
            LineStyle foregroundLineStyle = new LineStyle(1.0f, Colors.HitchColor);
            GLW.DrawLineLoopPrimitiveLayered(vertices, backgroundLineStyle, foregroundLineStyle);
        }

        private void DrawTrailingHitch(double trailingTool)
        {
            XyCoord[] vertices = {
                new XyCoord(-0.65 + offset, trailingTool),
                new XyCoord(0.0, 0.0),
                new XyCoord(0.65 + offset, trailingTool)
            };
            LineStyle backgroundLineStyle = new LineStyle(6.0f, Colors.Black);
            LineStyle foregroundLineStyle = new LineStyle(1.0f, Colors.HitchTrailingColor);
            GLW.DrawLineLoopPrimitiveLayered(vertices, backgroundLineStyle, foregroundLineStyle);
        }

        public void DrawTool()
        {
            // [XPLAT] The pivot-axle frame is now established by the caller (the render coordinator).
            // In the net48 original the call site (OpenGL.Designer.cs) ran, inside one outer
            // PushMatrix/PopMatrix, `tool.DrawTool(); vehicle.DrawVehicle();` — and DrawTool's leading
            // GL.Translate(pivotAxlePos), placed BEFORE its own PushMatrix, persisted past DrawTool's
            // PopMatrix to set up the pivot frame that DrawVehicle (which starts with GL.Rotate and has
            // no translate of its own) then relied on. The already-decoupled CVehicle.DrawVehicle keeps
            // that convention (it still requires the caller to pre-translate to the pivot), so the render
            // coordinator now performs that single GL.Translate(pivotAxlePos) once before invoking both
            // draws. Keeping the translate here would double-translate the tool relative to DrawVehicle's
            // contract; removing it reproduces the original geometry exactly. The internal
            // PushMatrix/PopMatrix below remain balanced, so on return the matrix is still at the pivot
            // frame for DrawVehicle. (Was: GL.Translate(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing, 0).)
            GL.PushMatrix();

            //translate down to the hitch pin
            double pivotToHitchLength = GetHitchLengthFromVehiclePivot();
            double hitchHeading = GetHitchHeadingFromVehiclePivot(pivotToHitchLength);
            GL.Translate(
                Math.Sin(hitchHeading) * pivotToHitchLength,
                Math.Cos(hitchHeading) * pivotToHitchLength,
                0);

            //settings doesn't change trailing hitch length if set to rigid, so do it here
            double trailingTank, trailingTool;
            if (isToolTrailing)
            {
                trailingTank = tankTrailingHitchLength;
                trailingTool = trailingHitchLength;
            }
            else { trailingTank = 0; trailingTool = 0; }

            // if there is a trailing tow between hitch
            if (isToolTBT && isToolTrailing)
            {
                //rotate to tank heading
                // [XPLAT] mf.tankPos.heading -> _appModel.TankHeading.AngleInRadians (relocated runtime
                // state); the rotation result is unchanged.
                GL.Rotate(glm.toDegrees(-_appModel.TankHeading.AngleInRadians), 0.0, 0.0, 1.0);

                DrawHitch(trailingTank);

                GL.Color4(1, 1, 1, 0.75);
                XyCoord toolAxleCenter = new XyCoord(0.0, trailingTank);
                XyDelta deltaToU1V1 = new XyDelta(1.5, 1.0);
                _vehicleTextures.ToolAxle.DrawCentered(toolAxleCenter, deltaToU1V1);

                //move down the tank hitch, unwind, rotate to section heading
                GL.Translate(0.0, trailingTank, 0.0);
                GL.Rotate(glm.toDegrees(_appModel.TankHeading.AngleInRadians), 0.0, 0.0, 1.0);
            }
            // [XPLAT] mf.toolPivotPos.heading -> _appModel.ToolPivotHeading.AngleInRadians (relocated
            // runtime state); the rotation result is unchanged.
            GL.Rotate(glm.toDegrees(-_appModel.ToolPivotHeading.AngleInRadians), 0.0, 0.0, 1.0);

            //draw the hitch if trailing
            if (isToolTrailing)
            {
                DrawTrailingHitch(trailingTool);

                if (Math.Abs(trailingToolToPivotLength) > 1 && _camera.camSetDistance > -100)
                {
                    textRotate += (_sim.stepDistance);
                    GL.Color4(1, 1, 1, 0.75);
                    XyCoord rightTire00 = new XyCoord(0.75 + offset, trailingTool + 0.51);
                    XyCoord rightTire11 = new XyCoord(1.4 + offset, trailingTool - 0.51);
                    XyCoord leftTire00 = new XyCoord(-0.75 + offset, trailingTool + 0.51);
                    XyCoord lefttTire11 = new XyCoord(-1.4 + offset, trailingTool - 0.51);
                    _vehicleTextures.Tire.Draw(rightTire00, rightTire11);
                    _vehicleTextures.Tire.Draw(leftTire00, lefttTire11);
                }
                trailingTool -= trailingToolToPivotLength;
            }

            if (_appModel.isJobStarted)
            {
                //look ahead lines
                GL.LineWidth(3);
                GL.Begin(PrimitiveType.Lines);

                //lookahead section on
                GL.Color3(0.20f, 0.7f, 0.2f);
                GL.Vertex3(farLeftPosition, (lookAheadDistanceOnPixelsLeft) * 0.1 + trailingTool, 0);
                GL.Vertex3(farRightPosition, (lookAheadDistanceOnPixelsRight) * 0.1 + trailingTool, 0);

                //lookahead section off
                GL.Color3(0.70f, 0.2f, 0.2f);
                GL.Vertex3(farLeftPosition, (lookAheadDistanceOffPixelsLeft) * 0.1 + trailingTool, 0);
                GL.Vertex3(farRightPosition, (lookAheadDistanceOffPixelsRight) * 0.1 + trailingTool, 0);

                if (_vehicle.isHydLiftOn)
                {
                    GL.Color3(0.70f, 0.2f, 0.72f);
                    GL.Vertex3(_section[0].positionLeft, (_vehicle.hydLiftLookAheadDistanceLeft * 0.1) + trailingTool, 0);
                    GL.Vertex3(_section[numOfSections - 1].positionRight, (_vehicle.hydLiftLookAheadDistanceRight * 0.1) + trailingTool, 0);
                }

                GL.End();
            }

            //draw the sections
            GL.LineWidth(2);

            double hite = _camera.camSetDistance / -250;
            if (hite > 4) hite = 4;
            if (hite < 1) hite = 1;

            //TooDoo
            //hite = 0.2;

            for (int j = 0; j < numOfSections; j++)
            {
                //if section is on, green, if off, red color
                if (_section[j].isSectionOn)
                {
                    if (_section[j].sectionBtnState == btnStates.Auto)
                    {
                        //GL.Color3(0.0f, 0.9f, 0.0f);
                        if (_section[j].isMappingOn) GL.Color3(0.0f, 0.95f, 0.0f);
                        else GL.Color3(0.970f, 0.30f, 0.970f);
                    }
                    else GL.Color3(0.97, 0.97, 0);
                }
                else
                {
                    if (!_section[j].isMappingOn) GL.Color3(0.950f, 0.2f, 0.2f);
                    else GL.Color3(0.00f, 0.250f, 0.97f);
                    //GL.Color3(0.7f, 0.2f, 0.2f);
                }

                double mid = (_section[j].positionRight - _section[j].positionLeft) / 2 + _section[j].positionLeft;
                XyCoord[] vertices = {
                    new XyCoord(_section[j].positionLeft, trailingTool),
                    new XyCoord(_section[j].positionLeft, trailingTool - hite),
                    new XyCoord(mid, trailingTool - hite * 1.5),
                    new XyCoord(_section[j].positionRight, trailingTool - hite),
                    new XyCoord(_section[j].positionRight, trailingTool),
                };
                GLW.DrawTriangleFanPrimitive(vertices);

                if (_camera.camSetDistance > -width * 200)
                {
                    GLW.SetColor(Colors.Black);
                    GLW.DrawLineLoopPrimitive(vertices);
                }
            }

            //zones
            if (!isSectionsNotZones && zones > 0 && _camera.camSetDistance > -150)
            {
                //GL.PointSize(8);

                GL.Begin(PrimitiveType.Lines);
                for (int i = 1; i < zones; i++)
                {
                    GL.Color3(0.5f, 0.80f, 0.950f);
                    GL.Vertex3(_section[zoneRanges[i]].positionLeft, trailingTool - 0.4, 0);
                    GL.Vertex3(_section[zoneRanges[i]].positionLeft, trailingTool + 0.2, 0);
                }

                GL.End();
            }

            //tram Dots
            if (isDisplayTramControl && _tram.displayMode != 0)
            {
                if (_camera.camSetDistance > -300)
                {
                    if (_camera.camSetDistance > -100)
                        GL.PointSize(12);
                    else GL.PointSize(8);

                    ColorRgba rightMarkerColor = ((_tram.controlByte) & 1) != 0 ? Colors.TramMarkerOnColor : Colors.Black;
                    ColorRgba leftMarkerColor = ((_tram.controlByte) & 2) != 0 ? Colors.TramMarkerOnColor : Colors.Black;
                    double rightX = _tram.isOuter ? farRightPosition - _tram.halfWheelTrack : _tram.halfWheelTrack;
                    double leftX = _tram.isOuter ? farLeftPosition + _tram.halfWheelTrack : -_tram.halfWheelTrack;
                    // section markers
                    GL.Begin(PrimitiveType.Points);
                    GLW.SetColor(rightMarkerColor);
                    GL.Vertex3(rightX, trailingTool, 0);
                    GLW.SetColor(leftMarkerColor);
                    GL.Vertex3(leftX, trailingTool, 0);
                    GL.End();
                }
            }

            GL.PopMatrix();
        }
    }
}
