// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//Please, if you use this, share the improvements

using AgOpenGPS.Classes;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using OpenTK.Graphics.OpenGL;
using System;

namespace AgOpenGPS
{
    public class CVehicle
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;` + `CVehicle(FormGPS _f)`). The collaborators this class read
        // through `mf` are now constructor-injected (or, for the two cyclic peers, wired post-construct)
        // and used live-by-reference, exactly mirroring the established CSim(ApplicationModel, ...) /
        // CModuleComm(ApplicationModel, ...) decoupling. No FormGPS reference remains, so the class is
        // portable across Windows, Linux and macOS. The vehicle geometry/config, the autosteer guard
        // values (maxSteerAngle 30°, maxAngularVelocity 0.64°/s), the Stanley/Pure-Pursuit gains and the
        // GL tractor/brand draw are FROZEN — values, math and GL vertex order are unchanged, so guidance
        // output and the rendered vehicle stay byte-for-byte identical. Field-by-field decoupling map:
        //   _appModel        - shared AgOpenGPS.Core runtime model (was mf.AppModel). Supplies the
        //                      vehicle heading (FixHeading.AngleInRadians, was mf.fixHeading), the
        //                      smoothed speed (avgSpeed, was mf.avgSpeed) and the relocated fix-state
        //                      flags isFirstHeadingSet / headingFromSource (were FormGPS fields). Read
        //                      live-by-reference; the fix/position pipeline writes them.
        //   _vehicleTextures - tractor/harvester/articulated body + wheel textures (was mf.VehicleTextures).
        //                      Cross-platform texture holder; same Texture2D identities and draw calls.
        //   _screenTextures  - on-screen overlay textures (was mf.ScreenTextures); QuestionMark only.
        //   _camera          - render camera (was mf.camera); read-only camSetDistance for LOD/overlays.
        //   _mc              - module-comm state (was mf.mc); actualSteerAngleDegrees for the drawn wheels.
        //   _sim             - GPS simulator (was mf.sim). Its migrated IsActive flag replaces the old
        //                      WinForms `mf.timerSim.Enabled` gate (NOT a System.Windows.Forms.Timer),
        //                      selecting the simulated vs. module steer angle exactly as before.
        //   _bnd             - boundary recorder (was mf.bnd); the in-progress boundary-offset overlay.
        //   _tool            - implement/tool (was mf.tool); rigid-hitch geometry. _bnd and _tool form
        //                      construction cycles with this class, so they are wired AFTER construction
        //                      via SetBoundary(...) / SetTool(...) (mirroring CSim.SetNmea), introducing
        //                      no DI container and no new abstraction.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel _appModel;
        private readonly VehicleTextures _vehicleTextures;
        private readonly ScreenTextures _screenTextures;
        private readonly Camera _camera;
        private readonly CModuleComm _mc;
        private readonly CSim _sim;

        // [XPLAT] Cyclic collaborators wired post-construct (see SetBoundary/SetTool). Non-readonly
        // because they are assigned after the constructor runs, exactly like CSim's late-wired _pn.
        private CBoundary _bnd;
        private CTool _tool;

        public int deadZoneHeading, deadZoneDelay;
        public int deadZoneDelayCounter;
        public bool isInDeadZone;

        //min vehicle speed allowed before turning shit off
        public double slowSpeedCutoff = 0;

        //autosteer values
        public double goalPointLookAheadHold, goalPointLookAheadMult, goalPointAcquireFactor, uturnCompensation;

        public double stanleyDistanceErrorGain, stanleyHeadingErrorGain;
        public double maxSteerAngle, maxSteerSpeed, minSteerSpeed;
        public double maxAngularVelocity;
        public double hydLiftLookAheadTime;

        public double hydLiftLookAheadDistanceLeft, hydLiftLookAheadDistanceRight;

        public bool isHydLiftOn;
        public double stanleyIntegralGainAB, purePursuitIntegralGain;

        //flag for free drive window to control autosteer
        public bool isInFreeDriveMode;

        //the trackbar angle for free drive
        public double driveFreeSteerAngle = 0;

        public double modeXTE, modeActualXTE = 0, modeActualHeadingError = 0;
        public int modeTime = 0;

        public double functionSpeedLimit;

        // [XPLAT] Was CVehicle(FormGPS _f). The shared model + leaf collaborators are injected here;
        // the cyclic peers (boundary, tool) are supplied later via SetBoundary/SetTool. Every settings
        // read below is preserved verbatim from the net48 original, so the vehicle geometry, the
        // autosteer guard values and the guidance gains are unchanged (frozen).
        public CVehicle(
            ApplicationModel appModel,
            VehicleTextures vehicleTextures,
            ScreenTextures screenTextures,
            Camera camera,
            CModuleComm mc,
            CSim sim)
        {
            //constructor
            _appModel = appModel;
            _vehicleTextures = vehicleTextures;
            _screenTextures = screenTextures;
            _camera = camera;
            _mc = mc;
            _sim = sim;

            VehicleConfig = new VehicleConfig();

            VehicleConfig.AntennaHeight = Properties.VehicleSettings.Default.setVehicle_antennaHeight;
            VehicleConfig.AntennaPivot = Properties.VehicleSettings.Default.setVehicle_antennaPivot;
            VehicleConfig.AntennaOffset = Properties.VehicleSettings.Default.setVehicle_antennaOffset;

            VehicleConfig.Wheelbase = Properties.VehicleSettings.Default.setVehicle_wheelbase;

            slowSpeedCutoff = Properties.ToolSettings.Default.setVehicle_slowSpeedCutoff;

            goalPointLookAheadHold = Properties.ToolSettings.Default.setVehicle_goalPointLookAheadHold;
            goalPointLookAheadMult = Properties.ToolSettings.Default.setVehicle_goalPointLookAheadMult;
            goalPointAcquireFactor = Properties.ToolSettings.Default.setVehicle_goalPointAcquireFactor;

            stanleyDistanceErrorGain = Properties.ToolSettings.Default.stanleyDistanceErrorGain;
            stanleyHeadingErrorGain = Properties.ToolSettings.Default.stanleyHeadingErrorGain;

            maxAngularVelocity = Properties.VehicleSettings.Default.setVehicle_maxAngularVelocity;
            maxSteerAngle = Properties.VehicleSettings.Default.setVehicle_maxSteerAngle;

            isHydLiftOn = false;

            VehicleConfig.TrackWidth = Properties.VehicleSettings.Default.setVehicle_trackWidth;

            stanleyIntegralGainAB = Properties.ToolSettings.Default.stanleyIntegralGainAB;

            purePursuitIntegralGain = Properties.ToolSettings.Default.purePursuitIntegralGainAB;
            VehicleConfig.Type = (VehicleType)Properties.VehicleSettings.Default.setVehicle_vehicleType;

            hydLiftLookAheadTime = Properties.ToolSettings.Default.setVehicle_hydraulicLiftLookAhead;

            deadZoneHeading = Properties.ToolSettings.Default.setAS_deadZoneHeading;
            deadZoneDelay = Properties.ToolSettings.Default.setAS_deadZoneDelay;

            isInFreeDriveMode = false;

            //how far from line before it becomes Hold
            modeXTE = 0.2;

            //how long before hold is activated
            modeTime = 1;

            functionSpeedLimit = Properties.VehicleSettings.Default.setAS_functionSpeedLimit;
            maxSteerSpeed = Properties.VehicleSettings.Default.setAS_maxSteerSpeed;
            minSteerSpeed = Properties.VehicleSettings.Default.setAS_minSteerSpeed;

            uturnCompensation = Properties.Settings.Default.setAS_uTurnCompensation;
        }

        // [XPLAT] Post-construct wiring for the boundary collaborator (was mf.bnd). Called by the
        // composition root after both objects exist, breaking the construction cycle with no new
        // abstraction (mirrors CSim.SetNmea).
        public void SetBoundary(CBoundary bnd)
        {
            _bnd = bnd;
        }

        // [XPLAT] Post-construct wiring for the tool collaborator (was mf.tool). CTool reads back into
        // this vehicle, so it is created after CVehicle and supplied here.
        public void SetTool(CTool tool)
        {
            _tool = tool;
        }

        public int modeTimeCounter = 0;
        public double goalDistance = 0;

        public VehicleConfig VehicleConfig { get; }

        public double UpdateGoalPointDistance()
        {
            double xTE = Math.Abs(modeActualXTE);
            // [XPLAT] mf.avgSpeed -> _appModel.avgSpeed (relocated to the shared Core model); value unchanged.
            double goalPointDistance = _appModel.avgSpeed * 0.05 * goalPointLookAheadMult;

            double LoekiAheadHold = goalPointLookAheadHold;
            double LoekiAheadAcquire = goalPointLookAheadHold * goalPointAcquireFactor;

            if (xTE <= 0.1)
            {
                goalPointDistance *= LoekiAheadHold;
                goalPointDistance += LoekiAheadHold;
            }

            else if (xTE > 0.1 && xTE < 0.4)
            {
                xTE -= 0.1;

                LoekiAheadHold = (1 - (xTE / 0.3)) * (LoekiAheadHold - LoekiAheadAcquire);
                LoekiAheadHold += LoekiAheadAcquire;

                goalPointDistance *= LoekiAheadHold;
                goalPointDistance += LoekiAheadHold;
            }
            else
            {
                goalPointDistance *= LoekiAheadAcquire;
                goalPointDistance += LoekiAheadAcquire;
            }

            if (goalPointDistance < 2) goalPointDistance = 2;
            goalDistance = goalPointDistance;

            return goalPointDistance;
        }

        public void DrawVehicle()
        {
            // [XPLAT] mf.fixHeading -> _appModel.FixHeading.AngleInRadians (exactly what the former
            // FormGPS.fixHeading getter returned); the rendered heading rotation is unchanged.
            GL.Rotate(glm.toDegrees(-_appModel.FixHeading.AngleInRadians), 0.0, 0.0, 1.0);
            if (_appModel.isFirstHeadingSet && !_tool.isToolFrontFixed)
            {
                // Draw the rigid hitch
                double hitchLengthFromPivot = _tool.GetHitchLengthFromVehiclePivot();
                double hitchHeading = _tool.GetHitchHeadingFromVehiclePivot(hitchLengthFromPivot);
                double hitchAngleOffset = hitchHeading - _appModel.FixHeading.AngleInRadians;
                double sinOffset = Math.Sin(hitchAngleOffset);
                double cosOffset = Math.Cos(hitchAngleOffset);

                XyCoord TransformVertex(double lateral, double longitudinal)
                {
                    double x = lateral * cosOffset + longitudinal * sinOffset;
                    double y = longitudinal * cosOffset - lateral * sinOffset;
                    return new XyCoord(x, y);
                }

                XyCoord[] vertices;
                if (!_tool.isToolRearFixed)
                {
                    vertices = new XyCoord[]
                    {
                        TransformVertex(0, hitchLengthFromPivot), TransformVertex(0, 0)
                    };
                }
                else
                {
                    vertices = new XyCoord[]
                    {
                        TransformVertex(-0.35, hitchLengthFromPivot), TransformVertex(-0.35, 0),
                        TransformVertex( 0.35, hitchLengthFromPivot), TransformVertex( 0.35, 0)
                    };
                }
                LineStyle backgroundLineStyle = new LineStyle(4, Colors.Black);
                LineStyle foregroundLineStyle = new LineStyle(1, Colors.HitchRigidColor);
                GLW.DrawLinesPrimitiveLayered(vertices, backgroundLineStyle, foregroundLineStyle);
            }

            //draw the vehicle Body
            // [XPLAT] mf.isFirstHeadingSet / mf.headingFromSource -> relocated _appModel fix-state flags.
            if (!_appModel.isFirstHeadingSet && _appModel.headingFromSource != "Dual")
            {
                GL.Color4(1, 1, 1, 0.75);
                // [XPLAT] mf.ScreenTextures -> injected _screenTextures; same Texture2D draw.
                _screenTextures.QuestionMark.Draw(new XyCoord(1.0, 5.0), new XyCoord(5.0, 1.0));
            }

            //3 vehicle types  tractor=0 harvestor=1 Articulated=2
            ColorRgba vehicleColor = new ColorRgba(
                VehicleConfig.Color.Red,
                VehicleConfig.Color.Green,
                VehicleConfig.Color.Blue,
                (byte)(255.0 * VehicleConfig.Opacity));

            if (VehicleConfig.IsImage)
            {
                if (VehicleConfig.Type == VehicleType.Tractor)
                {
                    //vehicle body
                    GLW.SetColor(vehicleColor);

                    // [XPLAT] mf.timerSim.Enabled -> _sim.IsActive (CSim's portable run-state flag);
                    // mf.sim/mf.mc -> injected _sim/_mc. Selection and sign are unchanged.
                    AckermannAngles(
                        -(_sim.IsActive ? _sim.steerangleAve : _mc.actualSteerAngleDegrees),
                        out double leftAckermann,
                        out double rightAckermann);
                    XyCoord tractorCenter = new XyCoord(0.0, 0.5 * VehicleConfig.Wheelbase);
                    // [XPLAT] mf.VehicleTextures -> injected _vehicleTextures (cross-platform brand-image
                    // textures uploaded via the Core DrawLib Texture2D); identical centered draw.
                    _vehicleTextures.Tractor.DrawCentered(
                        tractorCenter,
                        new XyDelta(VehicleConfig.TrackWidth, -1.0 * VehicleConfig.Wheelbase));

                    //right wheel
                    GL.PushMatrix();
                    GL.Translate(0.5 * VehicleConfig.TrackWidth, VehicleConfig.Wheelbase, 0);
                    GL.Rotate(rightAckermann, 0, 0, 1);

                    XyDelta frontWheelDelta = new XyDelta(0.5 * VehicleConfig.TrackWidth, -0.75 * VehicleConfig.Wheelbase);
                    _vehicleTextures.FrontWheel.DrawCenteredAroundOrigin(frontWheelDelta);

                    GL.PopMatrix();

                    //Left Wheel
                    GL.PushMatrix();

                    GL.Translate(-VehicleConfig.TrackWidth * 0.5, VehicleConfig.Wheelbase, 0);
                    GL.Rotate(leftAckermann, 0, 0, 1);

                    _vehicleTextures.FrontWheel.DrawCenteredAroundOrigin(frontWheelDelta);

                    GL.PopMatrix();
                    //disable, straight color
                }
                else if (VehicleConfig.Type == VehicleType.Harvester)
                {
                    //vehicle body

                    // [XPLAT] mf.timerSim.Enabled -> _sim.IsActive; mf.sim/mf.mc -> injected _sim/_mc.
                    AckermannAngles(
                        _sim.IsActive ? _sim.steerAngle : _mc.actualSteerAngleDegrees,
                        out double leftAckermannAngle,
                        out double rightAckermannAngle);
                    ColorRgba harvesterWheelColor = new ColorRgba(
                        Colors.HarvesterWheelColor.Red,
                        Colors.HarvesterWheelColor.Green,
                        Colors.HarvesterWheelColor.Blue,
                        (byte)(255.0 * VehicleConfig.Opacity));
                    GLW.SetColor(harvesterWheelColor);
                    //right wheel
                    GL.PushMatrix();
                    GL.Translate(VehicleConfig.TrackWidth * 0.5, -VehicleConfig.Wheelbase, 0);
                    GL.Rotate(rightAckermannAngle, 0, 0, 1);
                    XyDelta forntWheelDelta = new XyDelta(0.25 * VehicleConfig.TrackWidth, 0.5 * VehicleConfig.Wheelbase);
                    _vehicleTextures.FrontWheel.DrawCenteredAroundOrigin(forntWheelDelta);
                    GL.PopMatrix();

                    //Left Wheel
                    GL.PushMatrix();
                    GL.Translate(-VehicleConfig.TrackWidth * 0.5, -VehicleConfig.Wheelbase, 0);
                    GL.Rotate(leftAckermannAngle, 0, 0, 1);
                    _vehicleTextures.FrontWheel.DrawCenteredAroundOrigin(forntWheelDelta);
                    GL.PopMatrix();

                    GLW.SetColor(vehicleColor);
                    _vehicleTextures.Harvester.DrawCenteredAroundOrigin(
                        new XyDelta(VehicleConfig.TrackWidth, -1.5 * VehicleConfig.Wheelbase));
                    //disable, straight color
                }
                else if (VehicleConfig.Type == VehicleType.Articulated)
                {
                    // [XPLAT] mf.timerSim.Enabled -> _sim.IsActive; mf.sim/mf.mc -> injected _sim/_mc.
                    double modelSteerAngle = 0.5 * (_sim.IsActive ? _sim.steerAngle : _mc.actualSteerAngleDegrees);
                    GLW.SetColor(vehicleColor);

                    XyDelta articulated = new XyDelta(VehicleConfig.TrackWidth, -0.65 * VehicleConfig.Wheelbase);
                    GL.PushMatrix();
                    GL.Translate(0, -VehicleConfig.Wheelbase * 0.5, 0);
                    GL.Rotate(modelSteerAngle, 0, 0, 1);
                    _vehicleTextures.ArticulatedRear.DrawCenteredAroundOrigin(articulated);
                    GL.PopMatrix();

                    GL.PushMatrix();
                    GL.Translate(0, VehicleConfig.Wheelbase * 0.5, 0);
                    GL.Rotate(-modelSteerAngle, 0, 0, 1);
                    _vehicleTextures.ArticulatedFront.DrawCenteredAroundOrigin(articulated);
                    GL.PopMatrix();
                }
            }
            else
            {
                GL.Color4(1.2, 1.20, 0.0, VehicleConfig.Opacity);
                GL.Begin(PrimitiveType.TriangleFan);
                GL.Vertex3(0, VehicleConfig.AntennaPivot, -0.0);
                GL.Vertex3(1.0, -0, 0.0);
                GL.Color4(0.0, 1.20, 1.22, VehicleConfig.Opacity);
                GL.Vertex3(0, VehicleConfig.Wheelbase, 0.0);
                GL.Color4(1.220, 0.0, 1.2, VehicleConfig.Opacity);
                GL.Vertex3(-1.0, -0, 0.0);
                GL.Vertex3(1.0, -0, 0.0);
                GL.End();

                GL.LineWidth(3);
                GL.Color3(0.12, 0.12, 0.12);
                GL.Begin(PrimitiveType.LineLoop);
                {
                    GL.Vertex3(-1.0, 0, 0);
                    GL.Vertex3(1.0, 0, 0);
                    GL.Vertex3(0, VehicleConfig.Wheelbase, 0);
                }
                GL.End();
            }
            // [XPLAT] mf.camera -> injected _camera (AgOpenGPS.Core.Camera); mf.isFirstHeadingSet ->
            // _appModel.isFirstHeadingSet. camSetDistance value and threshold unchanged.
            if (_camera.camSetDistance > -75 && _appModel.isFirstHeadingSet)
            {
                //draw the bright antenna dot
                // background layer
                GLW.SetPointSize(16.0f);
                GLW.SetColor(Colors.Black);
                GLW.DrawPoint(-VehicleConfig.AntennaOffset, VehicleConfig.AntennaPivot, 0.1);
                // foreground layer
                GLW.SetPointSize(10.0f);
                GLW.SetColor(Colors.AntennaColor);
                GLW.DrawPoint(-VehicleConfig.AntennaOffset, VehicleConfig.AntennaPivot, 0.1);
            }

            // [XPLAT] mf.bnd -> injected _bnd (boundary recorder, wired via SetBoundary). All members,
            // values and GL vertex order are unchanged.
            if (_bnd.isBndBeingMade && _bnd.isDrawAtPivot)
            {
                if (_bnd.isDrawRightSide)
                {
                    GL.LineWidth(2);
                    GL.Color3(0.0, 1.270, 0.0);
                    GL.Begin(PrimitiveType.LineStrip);
                    {
                        GL.Vertex3(0.0, 0, 0);
                        GL.Color3(1.270, 1.220, 0.20);
                        GL.Vertex3(_bnd.createBndOffset, 0, 0);
                        GL.Vertex3(_bnd.createBndOffset * 0.75, 0.25, 0);
                    }
                    GL.End();
                }
                //draw on left side
                else
                {
                    GL.LineWidth(2);
                    GL.Color3(0.0, 1.270, 0.0);
                    GL.Begin(PrimitiveType.LineStrip);
                    {
                        GL.Vertex3(0.0, 0, 0);
                        GL.Color3(1.270, 1.220, 0.20);
                        GL.Vertex3(-_bnd.createBndOffset, 0, 0);
                        GL.Vertex3(-_bnd.createBndOffset * 0.75, 0.25, 0);
                    }
                    GL.End();
                }
            }

            //Svenn Arrow
            // [XPLAT] mf.isSvennArrowOn -> Properties.Settings.Default.setDisplay_isSvennArrowOn (the
            // canonical persisted display flag the migrated config dialog binds to); mf.camera ->
            // _camera. The arrow geometry, threshold and value are unchanged.
            if (Properties.Settings.Default.setDisplay_isSvennArrowOn && _camera.camSetDistance > -1000)
            {
                double svennDist = _camera.camSetDistance * -0.07;
                double svennWidth = svennDist * 0.22;
                // [XPLAT] mf.ABLine.lineWidth -> Properties.Settings.Default.setDisplay_lineWidth (the
                // same persisted line-width CABLine.lineWidth itself is loaded from); value unchanged.
                GLW.SetLineWidth(Properties.Settings.Default.setDisplay_lineWidth);
                GLW.SetColor(Colors.SvenArrowColor);
                XyCoord[] vertices = {
                    new XyCoord(svennWidth, VehicleConfig.Wheelbase + svennDist),
                    new XyCoord(0, VehicleConfig.Wheelbase + svennWidth + 0.5 + svennDist),
                    new XyCoord(-svennWidth, VehicleConfig.Wheelbase + svennDist)
                };
                GLW.DrawLineStripPrimitive(vertices);
            }
            GL.LineWidth(1);
        }

        private void AckermannAngles(double wheelAngle, out double leftAckermannAngle, out double rightAckermannAngle)
        {
            leftAckermannAngle = wheelAngle;
            rightAckermannAngle = wheelAngle;
            if (wheelAngle > 0.0)
            {
                leftAckermannAngle *= 1.25;
            }
            else
            {
                rightAckermannAngle *= 1.25;
            }
        }

    }
}
