// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Models;
using System;
using System.IO;

namespace AgOpenGPS.Core.Streamers
{
    public class RecordedPathStreamer : FieldAspectStreamer
    {
        public RecordedPathStreamer(
            IFieldStreamerPresenter presenter
        ) :
            base("RecPath.txt", presenter)
        {
        }

        public RecordedPath TryRead(DirectoryInfo fieldDirectory, string fileName)
        {
            RecordedPath recordedPath = null;
            try
            {
                recordedPath = Read(fieldDirectory, fileName);
            }
            catch (Exception e)
            {
                _presenter.PresentRecordedPathFileCorrupt();
                Log.EventWriter("Load Recorded Path" + e.ToString());
            }
            return recordedPath;
        }

        private RecordedPath Read(DirectoryInfo fieldDirectory, string fileName)
        {
            FileInfo fileInfo = GetFileInfo(fieldDirectory, fileName);
            if (!fileInfo.Exists)
            {
                return null;
            }
            RecordedPath recordedPath = new RecordedPath();
            using (GeoStreamReader reader = new GeoStreamReader(fileInfo))
            {
                reader.ReadLine(); // skip header
                int numPoints = reader.ReadInt();

                while (!reader.EndOfStream)
                {
                    for (int v = 0; v < numPoints; v++)
                    {
                        string line = reader.ReadLine();
                        string[] words = line.Split(',');
                        RecordedPoint point = new RecordedPoint(
                            reader.ParseGeoCoordDir(words[0], words[1], words[2]),
                            reader.ParseDouble(words[3]),
                            reader.ParseBool(words[4]));
                        recordedPath.Add(point);
                    }
                }
            }
            return recordedPath;
        }

        public void Write(RecordedPath path, DirectoryInfo fieldDirectory, string fileName)
        {
            fieldDirectory.Create();
            using (GeoStreamWriter writer = new GeoStreamWriter(GetFileInfo(fieldDirectory, fileName)))
            {
                writer.WriteLine("$RecPath");
                writer.WriteInt(path.Count);
                foreach (var point in path.PointList)
                {
                    writer.WriteLine(
                        writer.GeoCoordDirStringENH(point.GeoCoordDir.Coord, point.GeoCoordDir.Direction)
                        + "," + writer.DoubleString(point.Speed, "N1")
                        + "," + writer.BoolString(point.AutoButtonState));
                }
            }
        }

        public void CreateFile(DirectoryInfo fieldDirectory)
        {
            fieldDirectory.Create();
            // [XPLAT] Pin CRLF so the seeded RecPath.txt is byte-identical to the net48/Windows golden on every OS.
            using (StreamWriter writer = new StreamWriter(GetFileInfo(fieldDirectory).FullName) { NewLine = "\r\n" })
            {
                //write paths # of sections
                writer.WriteLine("$RecPath");
                writer.WriteLine("0");
            }
        }
    }
}
