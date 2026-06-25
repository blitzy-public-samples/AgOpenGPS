// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
// WinForms/GDI+ purged: System.Windows.Forms, System.Drawing(.Printing), System.Media and the
// user32.dll Form-drag P/Invoke are gone. Window geometry persistence now flows through the
// internal IWindowState contract (Avalonia PixelPoint), and ShowHelp opens the Avalonia
// Views.HelpWindow. File/settings/CRC logic is unchanged (behaviour frozen).
using System;
using System.Collections;
using System.IO;
using Avalonia;          // [XPLAT] PixelPoint for IWindowState-based geometry persistence
using GPS_Out.Views;     // [XPLAT] back-reference retyped frmStart -> Avalonia MainWindow

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
        private MainWindow mf;
        private int SentenceCount = 0;

        public clsTools(MainWindow CallingForm)
        {
            mf = CallingForm;
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

        // [XPLAT] geometry persistence retargeted from System.Windows.Forms.Form to the internal
        // IWindowState contract (Avalonia PixelPoint). The persisted keys remain Name + ".Left"/
        // ".Top" so saved positions round-trip unchanged. The old IsOnScreen multi-monitor reset is
        // replaced by a simple non-negative clamp (the original effect was to pull an off-screen
        // window back to 0,0).
        // [XPLAT] internal: takes the internal IWindowState (R7-scoped). Only MainWindow (same
        // assembly) calls it, so internal accessibility resolves CS0051 without widening surface.
        internal void LoadFormData(IWindowState w)
        {
            int Leftloc = 0;
            int.TryParse(LoadAppProperty(w.Name + ".Left"), out Leftloc);

            int Toploc = 0;
            int.TryParse(LoadAppProperty(w.Name + ".Top"), out Toploc);

            if (Leftloc < 0) Leftloc = 0;
            if (Toploc < 0) Toploc = 0;

            w.Position = new PixelPoint(Leftloc, Toploc);
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

                cPropertiesFile = Properties.Settings.Default.FilesDir + "\\" + FileName;
                if (!File.Exists(cPropertiesFile)) File.Create(cPropertiesFile).Dispose();
                LoadFilesData(cPropertiesFile);
                Properties.Settings.Default.FileName = FileName;
                Properties.Settings.Default.Save();

                cPropertiesApp = Properties.Settings.Default.FilesDir + "\\AppData.txt";
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

        // [XPLAT] internal: takes the internal IWindowState (R7-scoped); only MainWindow calls it.
        internal void SaveFormData(IWindowState w)
        {
            try
            {
                SaveAppProperty(w.Name + ".Left", w.Position.X.ToString());
                SaveAppProperty(w.Name + ".Top", w.Position.Y.ToString());
            }
            catch (Exception)
            {
            }
        }

        public string SettingsDir()
        {
            return cSettingsDir;
        }

        // [XPLAT] opens the Avalonia Views.HelpWindow (replaces the deleted WinForms frmHelp). The
        // modal path uses ShowDialog(owner) discarded fire-and-forget to preserve the non-blocking
        // call semantics; the only caller uses Modal=false (auto-dismissing popup). PlayErrorSound
        // is a cross-platform no-op (System.Media.SystemSounds is Windows-only).
        public void ShowHelp(string Message, string Title = "Help",
            int timeInMsec = 30000, bool LogError = false, bool Modal = false, bool PlayErrorSound = false)
        {
            var Hlp = new HelpWindow(Message, Title, timeInMsec);
            if (Modal)
            {
                _ = Hlp.ShowDialog(mf);
            }
            else
            {
                Hlp.Show();
            }

            if (LogError) WriteErrorLog(Message);
        }

        public void WriteByteFile(byte[] Data, string DataName)
        {
            string FileName = cSettingsDir + "\\" + DataName;
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
                string FileName = cSettingsDir + "\\Error Log.txt";
                TrimFile(FileName);
                File.AppendAllText(FileName, DateTime.Now.ToString("MMM-dd hh:mm:ss") + "  -  " + strErrorText + "\r\n\r\n");
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
                cSettingsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\" + cAppName;

                if (!Directory.Exists(cSettingsDir)) Directory.CreateDirectory(cSettingsDir);
                //if (!File.Exists(cSettingsDir + "\\Example.rcs")) File.WriteAllBytes(cSettingsDir + "\\Example.rcs", Properties.Resources.Example);

                string FilesDir = Properties.Settings.Default.FilesDir;
                if (!Directory.Exists(FilesDir)) Properties.Settings.Default.FilesDir = cSettingsDir;

                // erase old debug file
                string FileName = cSettingsDir + "\\" + "AGIOdata.txt";
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