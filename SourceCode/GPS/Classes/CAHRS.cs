// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
namespace AgOpenGPS
{
    public class CAHRS
    {
        //Roll and heading from the IMU
        public double imuHeading = 99999, imuRoll = 0, imuPitch = 0, imuYawRate = 0;

        public System.Int16 angVel;

        //actual value in degrees
        public double rollZero;

        //Roll Filter Value
        public double rollFilter;

        //is the auto steer in auto turn on mode or not
        public bool isAutoSteerAuto, isRollInvert, isDualAsIMU, isReverseOn;

        // AutoswitchDualFix 
        public bool autoSwitchDualFixOn;
        public double autoSwitchDualFixSpeed;

        //the factor for fusion of GPS and IMU
        public double forwardComp, reverseComp, fusionWeight;

        //constructor
        public CAHRS()
        {
            rollZero = Properties.VehicleSettings.Default.setIMU_rollZero;

            rollFilter = Properties.VehicleSettings.Default.setIMU_rollFilter;

            fusionWeight = Properties.VehicleSettings.Default.setIMU_fusionWeight2;

            //isAutoSteerAuto = Properties.Settings.Default.setAS_isAutoSteerAutoOn;
            isAutoSteerAuto = true;

            forwardComp = Properties.VehicleSettings.Default.setGPS_forwardComp;

            reverseComp = Properties.VehicleSettings.Default.setGPS_reverseComp;

            isRollInvert = Properties.VehicleSettings.Default.setIMU_invertRoll;

            isReverseOn = Properties.Settings.Default.setIMU_isReverseOn;

            autoSwitchDualFixOn = Properties.Settings.Default.setAutoSwitchDualFixOn;

            autoSwitchDualFixSpeed = Properties.Settings.Default.setAutoSwitchDualFixSpeed;
        }

        // [XPLAT] QA F5 M3: pure, side-effect-free seam for the IMU/GPS heading-fusion arithmetic so the
        // fused-heading math can be unit-tested in isolation (it previously lived inline inside the
        // FormGPS-coupled PositionService.UpdateFixPosition "Fix" branch and could not be instantiated for
        // a representative IMU+GPS input). This is a DECOUPLING extraction only (AAP §0.7.1: "logic may
        // move for decoupling but outputs may not change"): the body is the VERBATIM net48 fusion block
        // from Position.designer.cs (the "#region IMU Fusion"), so the fused heading is byte-for-byte
        // identical to the baseline. PositionService now delegates to this method. See
        // MIGRATION_DOCS/TRANSITION_MAP.md and PARITY_REPORT.md.
        /// <summary>
        /// Fuses the IMU heading with the GPS heading by advancing the running <paramref name="imuGPS_Offset"/>
        /// toward the GPS heading and returning the corrected (fused) heading in radians, exactly as the net48
        /// "Fix" heading branch did. The offset is updated in place (it is persistent loop state).
        /// </summary>
        /// <param name="imuHeadingDegrees">Raw IMU heading in degrees (ahrs.imuHeading).</param>
        /// <param name="gpsHeading">Antenna-corrected GPS heading in radians.</param>
        /// <param name="fusionWeight">Forward fusion weight (ahrs.fusionWeight); the reverse path uses the frozen 0.02.</param>
        /// <param name="isReverseWithIMU">True when reversing with the IMU engaged (selects the 0.02 reverse weight).</param>
        /// <param name="imuGPS_Offset">Persistent IMU↔GPS offset (radians); updated in place by this call.</param>
        /// <returns>The corrected/fused heading in radians, normalized to [0, 2π).</returns>
        public static double FuseImuGpsHeading(
            double imuHeadingDegrees, double gpsHeading, double fusionWeight, bool isReverseWithIMU, ref double imuGPS_Offset)
        {
            // IMU Fusion with heading correction, add the correction
            //current gyro angle in radians
            double imuHeading = (glm.toRadians(imuHeadingDegrees));

            //Difference between the IMU heading and the GPS heading
            double gyroDelta = 0;

            //if (!isReverseWithIMU)
            gyroDelta = (imuHeading + imuGPS_Offset) - gpsHeading;

            if (gyroDelta < 0) gyroDelta += glm.twoPI;
            else if (gyroDelta >= glm.twoPI) gyroDelta -= glm.twoPI;

            //calculate delta based on circular data problem 0 to 360 to 0, clamp to +- 2 Pi
            if (gyroDelta >= -glm.PIBy2 && gyroDelta <= glm.PIBy2) gyroDelta *= -1.0;
            else
            {
                if (gyroDelta > glm.PIBy2) { gyroDelta = glm.twoPI - gyroDelta; }
                else { gyroDelta = (glm.twoPI + gyroDelta) * -1.0; }
            }
            if (gyroDelta > glm.twoPI) gyroDelta -= glm.twoPI;
            else if (gyroDelta < -glm.twoPI) gyroDelta += glm.twoPI;

            //moe the offset to line up imu with gps
            if (!isReverseWithIMU)
                imuGPS_Offset += (gyroDelta * (fusionWeight));
            else
                imuGPS_Offset += (gyroDelta * (0.02));

            if (imuGPS_Offset > glm.twoPI) imuGPS_Offset -= glm.twoPI;
            else if (imuGPS_Offset < 0) imuGPS_Offset += glm.twoPI;

            //determine the Corrected heading based on gyro and GPS
            double imuCorrected = imuHeading + imuGPS_Offset;
            if (imuCorrected >= glm.twoPI) imuCorrected -= glm.twoPI;
            else if (imuCorrected < 0) imuCorrected += glm.twoPI;

            return imuCorrected;
        }
    }
}