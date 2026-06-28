// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
// [XPLAT] WinForms/GDI+/Win32 plus the legacy help-popup and audio APIs are purged. Window geometry
// persistence now flows through the internal IWindowState contract (Avalonia PixelPoint); ShowHelp
// opens the Avalonia Views.HelpWindow; the constructor is parameterless (no view back-reference).
// All file/settings logic and the additive CRC primitives are unchanged (behaviour frozen).
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using GPS_Out.Views;

namespace GPS_Out
{
    public class clsTools
    {
        private static Hashtable HTapp;
        private static Hashtable HTfiles;
        private string cAppName = "GPS_Out";
        private string cAppVersion = "1.2.2";
        private string cPropertiesApp;
        private string cPropertiesFile;
        private string cSettingsDir;
        private int SentenceCount = 0;

        // [XPLAT] parameterless: the former view back-reference was removed. Views/MainWindow now
        // constructs this as 'new clsTools();' and the help-window owner is resolved on demand from
        // the Avalonia application lifetime (see ShowHelp).
        public clsTools()
        {
            CheckFolders();
            OpenFile(Properties.Settings.Default.FileName);
        }

        public string AppVersion()
        {
            return cAppVersion;
        }

        public byte CRC(byte[] Data, int Length, byte Start = 0)
        {
            byte Result = 0;
            if (Length <= Data.Length)
            {
                int CK = 0;
                for (int i = Start; i < Length; i++)
                {
                    CK += Data[i];
                }
                Result = (byte)CK;
            }
            return Result;
        }

        // [XPLAT] DragForm (user32.dll P/Invoke) and DrawGroupBox (GDI+ Graphics) were WinForms-only
        // and had zero surviving callers: the Avalonia Views/*.axaml reproduce the group-box frames
        // declaratively (Border + header TextBlock), and borderless drag is not used. Both removed.

        public bool GoodCRC(byte[] Data, byte Start = 0)
        {
            bool Result = false;
            int Length = Data.Length;
            byte cr = CRC(Data, Length - 1, Start);
            Result = cr == Data[Length - 1];
            return Result;
        }

        public string LoadAppProperty(string Key)
        {
            string Prop = "";
            if (HTapp.Contains(Key)) Prop = HTapp[Key].ToString();
            return Prop;
        }

        // [XPLAT] geometry persistence retargeted from the WinForms Form to the internal IWindowState
        // contract (Avalonia PixelPoint). The persisted keys remain Name + ".Left" / ".Top" and the
        // values are parsed with InvariantCulture so saved positions round-trip identically across
        // locales. Off-screen recovery is now owned by the Avalonia view (window placement).
        // [XPLAT] internal: the parameter type IWindowState is internal, so this method is internal
        // too (a public signature would raise CS0051). Only MainWindow (same assembly) calls it.
        internal void LoadFormData(IWindowState Frm)
        {
            int Leftloc = 0;
            int.TryParse(LoadAppProperty(Frm.Name + ".Left"), NumberStyles.Integer, CultureInfo.InvariantCulture, out Leftloc);

            int Toploc = 0;
            int.TryParse(LoadAppProperty(Frm.Name + ".Top"), NumberStyles.Integer, CultureInfo.InvariantCulture, out Toploc);

            Frm.Position = new PixelPoint(Leftloc, Toploc);
        }

        public string LoadProperty(string Key)
        {
            string Prop = "";
            if (HTfiles.Contains(Key)) Prop = HTfiles[Key].ToString();
            return Prop;
        }

        public void OpenFile(string NewFile)
        {
            try
            {
                string PathName = Path.GetDirectoryName(NewFile); // only works if file name present
                string FileName = Path.GetFileName(NewFile);
                if (FileName == "") PathName = NewFile;     // no file name present, fix path name
                if (Directory.Exists(PathName)) Properties.Settings.Default.FilesDir = PathName; // set the new files dir

                cPropertiesFile = Path.Combine(Properties.Settings.Default.FilesDir, FileName);
                if (!File.Exists(cPropertiesFile)) File.Create(cPropertiesFile).Dispose();
                LoadFilesData(cPropertiesFile);
                Properties.Settings.Default.FileName = FileName;
                Properties.Settings.Default.Save();

                cPropertiesApp = Path.Combine(Properties.Settings.Default.FilesDir, "AppData.txt");
                if (!File.Exists(cPropertiesApp)) File.Create(cPropertiesApp).Dispose();
                LoadAppData(cPropertiesApp);
            }
            catch (Exception ex)
            {
                WriteErrorLog("Tools: OpenFile: " + ex.Message);
            }
        }

        public void SaveAppProperty(string Key, string Value)
        {
            bool Changed = false;
            if (HTapp.Contains(Key))
            {
                if (!HTapp[Key].ToString().Equals(Value))
                {
                    HTapp[Key] = Value;
                    Changed = true;
                }
            }
            else
            {
                HTapp.Add(Key, Value);
                Changed = true;
            }
            if (Changed) SaveAppProperties();
        }

        // [XPLAT] internal: the parameter type IWindowState is internal, so this method is internal too;
        // only MainWindow (same assembly) calls it. Values are written with InvariantCulture so the
        // persisted Name + ".Left" / ".Top" keys round-trip identically across locales.
        internal void SaveFormData(IWindowState Frm)
        {
            try
            {
                SaveAppProperty(Frm.Name + ".Left", Frm.Position.X.ToString(CultureInfo.InvariantCulture));
                SaveAppProperty(Frm.Name + ".Top", Frm.Position.Y.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
            }
        }

        public string SettingsDir()
        {
            return cSettingsDir;
        }

        // [XPLAT] opens the Avalonia Views.HelpWindow (replaces the deleted WinForms help popup). The
        // owner is resolved from the Avalonia application lifetime (no view back-reference); the modal
        // path uses ShowDialog(owner) discarded fire-and-forget to preserve the non-blocking call
        // semantics, falling back to a non-owned Show() when no main window is available. The only
        // caller uses Modal=false (auto-dismissing popup). Error-sound playback is a cross-platform
        // no-op (the Windows-only system-sounds API was dropped).
        public void ShowHelp(string Message, string Title = "Help",
            int timeInMsec = 30000, bool LogError = false, bool Modal = false, bool PlayErrorSound = false)
        {
            var Hlp = new HelpWindow(Message, Title, timeInMsec);
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (Modal && owner != null)
            {
                _ = Hlp.ShowDialog(owner);   // fire-and-forget; ShowDialog returns Task (discarded to stay warning-clean)
            }
            else
            {
                Hlp.Show();
            }

            if (LogError) WriteErrorLog(Message);

            // [XPLAT] the Windows-only system-sounds API is gone; audio is non-essential -> no-op on all platforms.
            if (PlayErrorSound)
            {
                // intentional no-op (audio not portable); PlayErrorSound retained for behavioral/signature parity
            }
        }

        public void WriteByteFile(byte[] Data, string DataName)
        {
            string FileName = Path.Combine(cSettingsDir, DataName);
            if (SentenceCount < 20)
            {
                SentenceCount++;
                using (var stream = new FileStream(FileName, FileMode.Append))
                {
                    stream.Write(Data, 0, Data.Length);
                }
            }
        }

        public void WriteErrorLog(string strErrorText)
        {
            try
            {
                string FileName = Path.Combine(cSettingsDir, "Error Log.txt");
                TrimFile(FileName);
                File.AppendAllText(FileName, DateTime.Now.ToString("MMM-dd hh:mm:ss", CultureInfo.InvariantCulture) + "  -  " + strErrorText + "\r\n\r\n");
            }
            catch (Exception)
            {
            }
        }

        private void CheckFolders()
        {
            try
            {
                // SettingsDir
                cSettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), cAppName);

                if (!Directory.Exists(cSettingsDir)) Directory.CreateDirectory(cSettingsDir);
                //if (!File.Exists(Path.Combine(cSettingsDir, "Example.rcs"))) File.WriteAllBytes(Path.Combine(cSettingsDir, "Example.rcs"), Properties.Resources.Example);

                string FilesDir = Properties.Settings.Default.FilesDir;
                if (!Directory.Exists(FilesDir)) Properties.Settings.Default.FilesDir = cSettingsDir;

                // erase old debug file
                string FileName = Path.Combine(cSettingsDir, "AGIOdata.txt");
                if (File.Exists(FileName)) File.Delete(FileName);
            }
            catch (Exception)
            {
            }
        }

        private void LoadAppData(string path)
        {
            // property:  key=value  ex: "LastFile=Main.mdb"
            try
            {
                HTapp = new Hashtable();
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (line.Contains("=") && !string.IsNullOrEmpty(line.Split('=')[0]) && !string.IsNullOrEmpty(line.Split('=')[1]))
                    {
                        string[] splitText = line.Split('=');
                        HTapp.Add(splitText[0], splitText[1]);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteErrorLog("Tools: LoadProperties: " + ex.Message);
            }
        }

        private void LoadFilesData(string path)
        {
            // property:  key=value  ex: "LastFile=Main.mdb"
            try
            {
                HTfiles = new Hashtable();
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (line.Contains("=") && !string.IsNullOrEmpty(line.Split('=')[0]) && !string.IsNullOrEmpty(line.Split('=')[1]))
                    {
                        string[] splitText = line.Split('=');
                        HTfiles.Add(splitText[0], splitText[1]);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteErrorLog("Tools: LoadProperties: " + ex.Message);
            }
        }

        private void SaveAppProperties()
        {
            try
            {
                string[] NewLines = new string[HTapp.Count];
                int i = -1;
                foreach (DictionaryEntry Pair in HTapp)
                {
                    i++;
                    NewLines[i] = Pair.Key.ToString() + "=" + Pair.Value.ToString();
                }
                if (i > -1) File.WriteAllLines(cPropertiesApp, NewLines);
            }
            catch (Exception)
            {
            }
        }

        private void SaveProperties()
        {
            try
            {
                string[] NewLines = new string[HTfiles.Count];
                int i = -1;
                foreach (DictionaryEntry Pair in HTfiles)
                {
                    i++;
                    NewLines[i] = Pair.Key.ToString() + "=" + Pair.Value.ToString();
                }
                if (i > -1) File.WriteAllLines(cPropertiesFile, NewLines);
            }
            catch (Exception)
            {
            }
        }

        private void TrimFile(string FileName, int MaxSize = 100000)
        {
            try
            {
                if (File.Exists(FileName))
                {
                    long FileSize = new FileInfo(FileName).Length;
                    if (FileSize > MaxSize)
                    {
                        // trim file
                        string[] Lines = File.ReadAllLines(FileName);
                        int Len = Lines.Length;
                        int St = (int)(Len * .1); // skip first 10% of old lines
                        string[] NewLines = new string[Len - St];
                        Array.Copy(Lines, St, NewLines, 0, Len - St);
                        File.Delete(FileName);
                        File.AppendAllLines(FileName, NewLines);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}