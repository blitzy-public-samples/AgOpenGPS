// This file centralizes all coordinate conversion previously handled in CNMEA
// Now uses LocalPlane, Wgs84, GeoCoord, and related structs (C# 7.1 compatible)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgOpenGPS.Core.AgShare;
using AgOpenGPS.Core.AgShare.Models;
using AgOpenGPS.Core.Models;

namespace AgOpenGPS
{
    public class AgShareUploader
    {
        private readonly AgShareClient _client;

        public AgShareUploader(AgShareClient client)
        {
            _client = client;
        }

        // [XPLAT] Removed the static CreateSnapshot(FormGPS gps) factory — see MIGRATION_DOCS/TRANSITION_MAP.md.
        // It reached through the WinForms FormGPS god-object (gps.currentFieldDirectory / gps.bnd.bndList /
        // gps.trk.gArr / gps.AppModel.LocalPlane / gps.displayFieldName), none of which survive the Avalonia
        // migration, and it had zero callers. Field snapshots are now assembled by the caller from the field
        // directory (see FormAgShareUploaderView.LoadFieldSnapshot), so this FormGPS-coupled overload is dropped.

        // Upload snapshot to AgShare using boundary with holes
        // [XPLAT] The trailing FormGPS gps parameter is replaced with an unused object gps = null — see
        // MIGRATION_DOCS/TRANSITION_MAP.md. The method body never referenced gps; the parameter is retained
        // (now optional/typeless) purely so the behaviour-frozen call sites keep their exact shape.
        public async Task UploadAsync(FieldSnapshot snapshot, object gps = null)
        {
            try
            {
                // Allow upload even without boundary - boundary is optional
                List<CoordinateDto> outer = new List<CoordinateDto>();
                List<List<CoordinateDto>> holes = new List<List<CoordinateDto>>();

                if (snapshot.Boundaries != null && snapshot.Boundaries.Count > 0)
                {
                    // Convert first boundary as outer boundary
                    var firstBoundary = snapshot.Boundaries.FirstOrDefault();
                    if (firstBoundary != null)
                    {
                        outer = ConvertBoundary(firstBoundary, snapshot.Converter);
                        if (outer == null) outer = new List<CoordinateDto>();
                    }

                    // Convert remaining boundaries as holes
                    foreach (var innerBoundary in snapshot.Boundaries.Skip(1))
                    {
                        List<CoordinateDto> hole = ConvertBoundary(innerBoundary, snapshot.Converter);
                        if (hole != null && hole.Count >= 4) holes.Add(hole);
                    }
                }

                List<GuidanceTrackDto> abLines = ConvertGuidanceTracks(snapshot.Tracks, snapshot.Converter);

                bool isPublic = false;
                try
                {
                    var downloadResult = await _client.DownloadFieldAsync(snapshot.FieldId);
                    if (downloadResult.IsSuccessful)
                    {
                        var field = downloadResult.Data;
                        isPublic = field.IsPublic;
                    }
                }
                catch (Exception)
                {
                    Log.EventWriter("Failed to check field visibility on AgShare, defaulting to private.");
                }

                var boundary = new PolygonDto
                {
                    Outer = outer,
                    Holes = holes
                };

                var payload = new UploadFieldDto
                {
                    Name = snapshot.FieldName,
                    IsPublic = isPublic,
                    Origin = new CoordinateDto { Latitude = snapshot.OriginLat, Longitude = snapshot.OriginLon },
                    Boundary = boundary,
                    AbLines = abLines
                };

                var result = await _client.UploadFieldAsync(snapshot.FieldId, payload);

                if (result.IsSuccessful)
                {
                    string txtPath = Path.Combine(snapshot.FieldDirectory, "agshare.txt");
                    File.WriteAllText(txtPath, snapshot.FieldId.ToString());
                    Log.EventWriter("Field uploaded to AgShare: " + snapshot.FieldName + " (" + snapshot.FieldId + ")");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Error uploading field to AgShare: " + ex.Message);
            }
        }

        // Convert local NE boundary to WGS84
        private static List<CoordinateDto> ConvertBoundary(List<vec3> localFence, LocalPlane converter)
        {
            List<CoordinateDto> coords = new List<CoordinateDto>();
            for (int i = 0; i < localFence.Count; i++)
            {
                GeoCoord geo = new GeoCoord(localFence[i].northing, localFence[i].easting);
                Wgs84 wgs = converter.ConvertGeoCoordToWgs84(geo);
                coords.Add(new CoordinateDto { Latitude = wgs.Latitude, Longitude = wgs.Longitude });
            }

            if (coords.Count > 1)
            {
                CoordinateDto first = coords[0];
                CoordinateDto last = coords[coords.Count - 1];
                if (first.Latitude != last.Latitude || first.Longitude != last.Longitude)
                {
                    coords.Add(first);
                }
            }

            return coords;
        }

        // Convert track lines from local NE to WGS84 format
        private static List<GuidanceTrackDto> ConvertGuidanceTracks(List<CTrk> tracks, LocalPlane converter)
        {
            List<GuidanceTrackDto> result = new List<GuidanceTrackDto>();

            foreach (CTrk ab in tracks)
            {
                if (ab.mode == TrackMode.AB)
                {
                    GeoCoord a = new GeoCoord(ab.ptA.northing, ab.ptA.easting);
                    GeoCoord b = new GeoCoord(ab.ptB.northing, ab.ptB.easting);
                    Wgs84 wgsA = converter.ConvertGeoCoordToWgs84(a);
                    Wgs84 wgsB = converter.ConvertGeoCoordToWgs84(b);

                    result.Add(new GuidanceTrackDto
                    {
                        Name = ab.name,
                        Type = "AB",
                        Coords = new List<CoordinateDto>
                        {
                            new CoordinateDto { Latitude = wgsA.Latitude, Longitude = wgsA.Longitude },
                            new CoordinateDto { Latitude = wgsB.Latitude, Longitude = wgsB.Longitude }
                        }
                    });
                }
                else if (ab.mode == TrackMode.Curve && ab.curvePts.Count >= 2)
                {
                    List<CoordinateDto> coords = new List<CoordinateDto>();
                    foreach (vec3 pt in ab.curvePts)
                    {
                        GeoCoord geo = new GeoCoord(pt.northing, pt.easting);
                        Wgs84 wgs = converter.ConvertGeoCoordToWgs84(geo);
                        coords.Add(new CoordinateDto { Latitude = wgs.Latitude, Longitude = wgs.Longitude });
                    }

                    result.Add(new GuidanceTrackDto
                    {
                        Name = ab.name,
                        Type = "Curve",
                        Coords = coords
                    });
                }
            }

            return result;
        }
    }
}
