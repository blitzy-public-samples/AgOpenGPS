// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core;
using System.Windows.Input;

namespace AgOpenGPS
{
    public class CModuleComm
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host (the former `mf` back-reference is gone).
        // The section-master button tri-state (manual/auto) and the autosteer-engaged flag now live
        // in the shared Core ApplicationModel — the SAME state the Avalonia view-models and the
        // section/guidance logic read/write — the AHRS sensor state is injected directly, and the
        // three on-screen button actions are invoked through view-model commands (RelayCommand via
        // ICommand) rather than WinForms button clicks. No new abstraction is introduced beyond the
        // existing Core view-model command surface. Module-comm state, timeout/heartbeat handling and
        // switch-decoding behavior are unchanged. See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel _appModel;
        private readonly CAHRS _ahrs;
        private readonly ICommand _autoSteerToggleCommand;
        private readonly ICommand _sectionMasterManualCommand;
        private readonly ICommand _sectionMasterAutoCommand;

        //Critical Safety Properties
        public bool isOutOfBounds = true;

        // ---- Section control switches to AOG  ---------------------------------------------------------
        //PGN - 32736 - 127.249 0x7FF9
        public byte[] ss = new byte[9];

        public byte[] ssP = new byte[9];

        public int
            swHeader = 0,
            swMain = 1,
            swAutoGr0 = 2,
            swAutoGr1 = 3,
            swNumSections = 4,
            swOnGr0 = 5,
            swOffGr0 = 6,
            swOnGr1 = 7,
            swOffGr1 = 8;

        public int pwmDisplay = 0;
        public double actualSteerAngleDegrees = 0;
        public int actualSteerAngleChart = 0, sensorData = -1;

        //for the workswitch
        public bool isWorkSwitchActiveLow, isRemoteWorkSystemOn, isWorkSwitchEnabled,
            isWorkSwitchManualSections, isSteerWorkSwitchManualSections, isSteerWorkSwitchEnabled;

        public bool workSwitchHigh, oldWorkSwitchHigh, steerSwitchHigh, oldSteerSwitchHigh, oldSteerSwitchRemote;

        // [XPLAT] Constructor now receives its collaborators by injection instead of the WinForms host:
        //   appModel                    - shared Core state (manualBtnState / autoBtnState / isBtnAutoSteerOn).
        //   ahrs                        - injected AHRS sensor state (was mf.ahrs).
        //   autoSteerToggleCommand      - command bound to the autosteer button (was mf.btnAutoSteer).
        //   sectionMasterManualCommand  - command bound to the section-master-manual button.
        //   sectionMasterAutoCommand    - command bound to the section-master-auto button.
        public CModuleComm(
            ApplicationModel appModel,
            CAHRS ahrs,
            ICommand autoSteerToggleCommand,
            ICommand sectionMasterManualCommand,
            ICommand sectionMasterAutoCommand)
        {
            _appModel = appModel;
            _ahrs = ahrs;
            _autoSteerToggleCommand = autoSteerToggleCommand;
            _sectionMasterManualCommand = sectionMasterManualCommand;
            _sectionMasterAutoCommand = sectionMasterAutoCommand;

            //WorkSwitch logic
            isRemoteWorkSystemOn = false;

            //does a low, grounded out, mean on
            isWorkSwitchActiveLow = true;
        }

        //Called from "OpenGL.Designer.cs" when requied
        public void CheckWorkAndSteerSwitch()
        {
            //AutoSteerAuto button enable - Ray Bear inspired code - Thx Ray!
            if (_ahrs.isAutoSteerAuto && steerSwitchHigh != oldSteerSwitchRemote)
            {
                oldSteerSwitchRemote = steerSwitchHigh;
                //steerSwith is active low
                if (steerSwitchHigh == _appModel.isBtnAutoSteerOn)
                {
                    _autoSteerToggleCommand.Execute(null);
                }
            }

            if (isRemoteWorkSystemOn)
            {
                if (isWorkSwitchEnabled && (oldWorkSwitchHigh != workSwitchHigh))
                {
                    oldWorkSwitchHigh = workSwitchHigh;

                    if (workSwitchHigh != isWorkSwitchActiveLow)
                    {
                        if (isWorkSwitchManualSections)
                        {
                            if (_appModel.manualBtnState != btnStates.On)
                                _sectionMasterManualCommand.Execute(null);
                        }
                        else
                        {
                            if (_appModel.autoBtnState != btnStates.Auto)
                                _sectionMasterAutoCommand.Execute(null);
                        }
                    }

                    else//Checks both on-screen buttons, performs click if button is not off
                    {
                        if (_appModel.autoBtnState != btnStates.Off)
                            _sectionMasterAutoCommand.Execute(null);
                        if (_appModel.manualBtnState != btnStates.Off)
                            _sectionMasterManualCommand.Execute(null);
                    }
                }

                if (isSteerWorkSwitchEnabled && (oldSteerSwitchHigh != steerSwitchHigh))
                {
                    oldSteerSwitchHigh = steerSwitchHigh;

                    if ((_appModel.isBtnAutoSteerOn && _ahrs.isAutoSteerAuto)
                        || !_ahrs.isAutoSteerAuto && !steerSwitchHigh)
                    {
                        if (isSteerWorkSwitchManualSections)
                        {
                            if (_appModel.manualBtnState != btnStates.On)
                                _sectionMasterManualCommand.Execute(null);
                        }
                        else
                        {
                            if (_appModel.autoBtnState != btnStates.Auto)
                                _sectionMasterAutoCommand.Execute(null);
                        }
                    }

                    else//Checks both on-screen buttons, performs click if button is not off
                    {
                        if (_appModel.autoBtnState != btnStates.Off)
                            _sectionMasterAutoCommand.Execute(null);
                        if (_appModel.manualBtnState != btnStates.Off)
                            _sectionMasterManualCommand.Execute(null);
                    }
                }
            }
        }
    }
}
