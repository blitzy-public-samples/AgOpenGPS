// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace AgLibrary.Settings
{
    public enum LoadResult { Ok, MissingFile, Failed };

    public static class XmlSettingsHandler
    {
        public static LoadResult LoadXMLFile(string filePath, object obj)
        {
            bool Errors = false;
            try
            {
                if (!File.Exists(filePath))
                {
                    return LoadResult.MissingFile;
                }

                using (XmlTextReader reader = new XmlTextReader(filePath))
                {
                    string name = "";
                    while (reader.Read())
                    {
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                if (reader.Name == "setting")
                                {
                                    name = reader.GetAttribute("name");
                                }
                                else if (reader.Name == "value")
                                {
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        var pinfo = obj.GetType().GetField(name);
                                        if (pinfo != null)
                                        {
                                            try
                                            {
                                                SetFieldValue(pinfo, reader, obj);
                                            }
                                            catch (Exception)
                                            {
                                                if (Debugger.IsAttached)
                                                    throw;// Re-throws the original exception
                                                Errors = true;
                                            }
                                        }
                                    }
                                }
                                break;

                            case XmlNodeType.EndElement:
                                break;
                        }
                    }
                    reader.Close();
                }
            }
            catch (Exception)
            {
                if (Debugger.IsAttached)
                    throw;// Re-throws the original exception
                Errors = true;
            }
            return Errors ? LoadResult.Failed : LoadResult.Ok;
        }

        private static bool SetFieldValue(FieldInfo pinfo, XmlTextReader reader, object obj)
        {
            Type fieldType = pinfo.FieldType;
            // Read string values
            string value = reader.ReadString();

            if (fieldType == typeof(string))
            {
                pinfo.SetValue(obj, value);
            }
            else if (fieldType.IsEnum) // Handle Enums
            {
                var enumValue = Enum.Parse(fieldType, value, ignoreCase: true);
                pinfo.SetValue(obj, enumValue);
            }
            else if (fieldType == typeof(double) || fieldType == typeof(float) || fieldType == typeof(decimal))
            {
                // [XPLAT] Cross-platform/culture hardening (QA Issue 8) — see MIGRATION_DOCS/TRANSITION_MAP.md.
                // Parse floating-point / decimal scalars with a STRICT, culture-invariant policy instead of
                // Convert.ChangeType. Convert.ToDouble/ToSingle/ToDecimal apply NumberStyles.Float |
                // NumberStyles.AllowThousands, so under a comma-decimal locale (e.g. de-DE) a CORRUPT value
                // such as "0,64" was silently re-interpreted as the thousands-grouped "064" => 64 — a
                // different magnitude for a safety-sensitive setting (maxAngularVelocity=0.64°/s). NumberStyles.Float
                // deliberately EXCLUDES AllowThousands, so a stray comma is rejected; on failure we throw so the
                // caller (LoadXMLFile) records LoadResult.Failed and the field keeps its existing default
                // (fail closed). Files this writer produces always use the invariant '.' separator
                // (SaveXMLFile -> Convert.ToString(value, InvariantCulture)), so valid round-trips are
                // unaffected and the frozen settings-XML round-trip contract (AAP §0.2.2) is preserved.
                object parsedValue;
                if (fieldType == typeof(double))
                {
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
                        throw new FormatException($"Invalid invariant Double value '{value}'.");
                    parsedValue = doubleValue;
                }
                else if (fieldType == typeof(float))
                {
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                        throw new FormatException($"Invalid invariant Single value '{value}'.");
                    parsedValue = floatValue;
                }
                else // decimal
                {
                    if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue))
                        throw new FormatException($"Invalid invariant Decimal value '{value}'.");
                    parsedValue = decimalValue;
                }

                pinfo.SetValue(obj, parsedValue);
            }
            else if (fieldType.IsPrimitive)
            {
                // [XPLAT] Non-floating primitives (int/long/short/byte/bool/char/…). Convert.ChangeType with
                // InvariantCulture already fails closed for these on a corrupt comma value: the integer
                // converters use NumberStyles.Integer (no decimal point / no thousands), so "0,64" throws
                // here too and LoadXMLFile records LoadResult.Failed — behaviour preserved exactly.
                object parsedValue = Convert.ChangeType(value, fieldType, CultureInfo.InvariantCulture);
                pinfo.SetValue(obj, parsedValue);
            }
            else if (fieldType == typeof(Color))
            {
                var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && parseInvariantCulture(parts[0], out int r) && parseInvariantCulture(parts[1], out int g) && parseInvariantCulture(parts[2], out int b))
                {
                    pinfo.SetValue(obj, Color.FromArgb(r, g, b));
                }
            }
            else if (fieldType == typeof(Point))
            {
                var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parseInvariantCulture(parts[0], out int x) && parseInvariantCulture(parts[1], out int y))
                {
                    pinfo.SetValue(obj, new Point(x, y));
                }
            }
            else if (fieldType == typeof(Size))
            {
                var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parseInvariantCulture(parts[0], out int width) && parseInvariantCulture(parts[1], out int height))
                {
                    pinfo.SetValue(obj, new Size(width, height));
                }
            }
            else if (typeof(IEnumerable).IsAssignableFrom(fieldType) && (fieldType.IsGenericType || fieldType.IsArray))
            {
                Type itemType;

                if (fieldType.IsGenericType) // For generic collections like List<T>
                    itemType = fieldType.GetGenericArguments()[0];
                else if (fieldType.IsArray) // For arrays like T[]
                    itemType = fieldType.GetElementType();
                else
                {
                    throw new NotSupportedException($"Unsupported collection type: {fieldType}");
                }

                // Deserialize XML into the custom object
                var serializer = new XmlSerializer(typeof(List<>).MakeGenericType(itemType));
                var list = serializer.Deserialize(reader);

                if (fieldType.IsArray) // Convert List<T> to T[] for arrays
                {
                    var array = ((IEnumerable)list).Cast<object>().ToArray();
                    pinfo.SetValue(obj, Array.CreateInstance(itemType, array.Length));
                    Array.Copy(array, (Array)pinfo.GetValue(obj), array.Length);
                }
                else // Directly assign the List<T>
                {
                    pinfo.SetValue(obj, list);
                }
            }
            else if (fieldType.IsClass)
            {
                try
                {
                    if (reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement)
                    {
                        string innerXml = reader.ReadOuterXml().Trim();
                        innerXml = innerXml.Replace(">True<", ">true<").Replace(">False<", ">false<");

                        if (!string.IsNullOrEmpty(innerXml))
                        {
                            using (StringReader stringReader = new StringReader(innerXml))
                            {
                                var serializer = new XmlSerializer(fieldType);
                                object nestedObj = serializer.Deserialize(stringReader);
                                pinfo.SetValue(obj, nestedObj);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            else
            {
                if (Debugger.IsAttached)
                    throw new ArgumentException("type not found");
                return false;
            }
            return true;
        }

        private static bool parseInvariantCulture(string value, out int outValue)
        {
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out outValue);
        }

        public static void SaveXMLFile(string filePath, object obj)
        {
            try
            {
                var dirName = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                {
                    Directory.CreateDirectory(dirName);
                }

                using (XmlTextWriter xml = new XmlTextWriter(filePath + ".tmp", Encoding.UTF8)
                {
                    Formatting = Formatting.Indented,
                    Indentation = 4
                })
                {
                    xml.WriteStartDocument();

                    // Start the root element
                    xml.WriteStartElement("configuration");
                    xml.WriteStartElement("userSettings");
                    xml.WriteStartElement(obj.ToString());

                    foreach (var fld in obj.GetType().GetFields())
                    {
                        var value = fld.GetValue(obj);
                        var fieldType = value.GetType();

                        // Start a "setting" element
                        xml.WriteStartElement("setting");

                        // Add attributes to the "setting" element
                        xml.WriteAttributeString("name", fld.Name);

                        if ((fieldType.IsClass && fieldType != typeof(string)) || (typeof(IEnumerable).IsAssignableFrom(fieldType) && (fieldType.IsGenericType || fieldType.IsArray)))
                        {
                            //classes, arrays and lists
                            xml.WriteAttributeString("serializeAs", "Xml");

                            // Write the serialized object to a nested "value" element
                            xml.WriteStartElement("value");

                            var serializer = new XmlSerializer(fieldType);

                            // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
                            // Preserve the .NET Framework XmlSerializer namespace-declaration order
                            // (xmlns:xsd BEFORE xmlns:xsi). On net8.0+ the serializer's default order is
                            // reversed (xsi before xsd), which would change the bytes of every saved
                            // settings file. Supplying an explicit, ordered XmlSerializerNamespaces keeps
                            // the output byte-for-byte identical to the net48 baseline, honoring the frozen
                            // settings-XML round-trip contract (AAP §0.2.2). Namespaces are added in the
                            // legacy order; the serializer emits them in insertion order.
                            var legacyNamespaceOrder = new XmlSerializerNamespaces();
                            legacyNamespaceOrder.Add("xsd", "http://www.w3.org/2001/XMLSchema");
                            legacyNamespaceOrder.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
                            serializer.Serialize(xml, value, legacyNamespaceOrder);

                            xml.WriteEndElement(); // value
                        }
                        else
                        {
                            xml.WriteAttributeString("serializeAs", "String");

                            if (value is Point pointValue)
                            {
                                xml.WriteElementString("value", $"{pointValue.X.ToString(CultureInfo.InvariantCulture)}, {pointValue.Y.ToString(CultureInfo.InvariantCulture)}");
                            }
                            else if (value is Size sizeValue)
                            {
                                xml.WriteElementString("value", $"{sizeValue.Width.ToString(CultureInfo.InvariantCulture)}, {sizeValue.Height.ToString(CultureInfo.InvariantCulture)}");
                            }
                            else if (value is Color dd)
                            {
                                xml.WriteElementString("value", $"{dd.R.ToString(CultureInfo.InvariantCulture)}, {dd.G.ToString(CultureInfo.InvariantCulture)}, {dd.B.ToString(CultureInfo.InvariantCulture)}");
                            }
                            else
                            {
                                // Write primitive types or strings
                                string stringValue = Convert.ToString(value, CultureInfo.InvariantCulture);
                                xml.WriteElementString("value", stringValue);
                            }
                        }

                        xml.WriteEndElement(); // setting
                    }

                    // Close all open elements
                    xml.WriteEndElement(); // AgOpenGPS.Properties.Settings
                    xml.WriteEndElement(); // userSettings
                    xml.WriteEndElement(); // configuration

                    // End the document
                    xml.WriteEndDocument();
                    xml.Flush();
                }

                if (File.Exists(filePath))
                    File.Delete(filePath);

                if (File.Exists(filePath + ".tmp"))
                    File.Move(filePath + ".tmp", filePath);
            }
            catch (Exception ex)
            {
                Log.EventWriter("Exception saving XML file" + ex.ToString());
            }
        }
    }
}
