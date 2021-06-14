// COPYRIGHT 2012 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Reflection;

using Microsoft.Xna.Framework;

using Orts.Common.Xna;
using Orts.Formats.Msts.Parsers;

namespace Orts.Simulation.RollingStocks
{
    /*
    [ORTSPhysicsFile(".orts", "ORTSElectricLocomotive")]
    [ORTSPhysicsFile(".orcvf", "ORTSExtendendCVF", "CVF")]
    public partial class MSTSElectricLocomotive
    {
        [ORTSPhysics("Extended Name", "ExtName", "The extended name of the Locomotive", "<None>")]
        public string ExtendedName;

        [ORTSPhysics("Extended Size", "ExtSize", "The extended size of the Locomotive", 42.42)]
        public double ExtendedSize;

        [ORTSPhysics("Second light RGB", "LightColorRGB", "The color of the second cab light", "255 255 255", "CVF")]
        public Vector3 LightColorRGB;
    }
    */
    public partial class MSTSLocomotive
    {
        internal virtual void InitializeFromORTSSpecific(string wagFilePath, object initObject)
        {
            object[] fattrs;
            initObject ??= this;
            MemberInfo info = initObject.GetType();
            fattrs = Attribute.GetCustomAttributes(info, typeof(OrtsPhysicsFileAttribute), true);

            bool setdef = true;

            foreach (object fattr in fattrs)
            {
                OrtsPhysicsFileAttribute opfa = fattr as OrtsPhysicsFileAttribute;
                using (STFReader stf = opfa.OpenSTF(wagFilePath))
                {
                    bool hasFile = stf != null;

                    object[] attrs;
                    STFReader.TokenProcessor tp;
                    List<STFReader.TokenProcessor> result = new List<STFReader.TokenProcessor>();

                    OrtsPhysicsAttribute attr;
                    FieldInfo[] fields = initObject.GetType().GetFields();
                    foreach (FieldInfo fi in fields)
                    {
                        attrs = fi.GetCustomAttributes(typeof(OrtsPhysicsAttribute), false);
                        if (attrs.Length > 0)
                        {
                            attr = attrs[0] as OrtsPhysicsAttribute;

                            if (setdef)
                                fi.SetValue2(initObject, attr.DefaultValue);

                            if (hasFile && opfa.FileID == attr.FileID)
                            {
                                AttributeProcessor ap = new AttributeProcessor(initObject, fi, stf, attr.DefaultValue);
                                tp = new STFReader.TokenProcessor(attr.Token, ap.Processor);

                                result.Add(tp);
                            }
                        }
                    }

                    setdef = false;

                    if (hasFile)
                    {
                        stf.MustMatch(opfa.Token);
                        stf.MustMatch("(");
                        stf.ParseBlock(result.ToArray());
                        stf.Dispose();
                    }
                }
            }
        }

        internal virtual void InitializeFromORTSSpecificCopy(MSTSLocomotive locoFrom)
        {
            FieldInfo[] fields = GetType().GetFields();
            object[] attrs;
            foreach (FieldInfo fi in fields)
            {
                attrs = fi.GetCustomAttributes(typeof(OrtsPhysicsAttribute), false);
                if (attrs.Length > 0)
                {
                    fi.SetValue(this, fi.GetValue(locoFrom));
                }
            }
        }
    }

    #region Attributes
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class OrtsPhysicsFileAttribute : Attribute
    {
        public string NamePattern { get; private set; }
        public string Token { get; private set; }
        public string FileID { get; private set; }

        /// <summary>
        /// Constructs an ORTS custom physics Attribute class
        /// <para>Used to specify the STF file extension and initial token.</para>
        /// <para>If multiple physics files are used, other than default, specify the FileID also.</para>
        /// </summary>
        /// <param name="namePattern">Extension of STF file</param>
        /// <param name="token">Token in STF file</param>
        public OrtsPhysicsFileAttribute(string namePattern, string token)
        {
            NamePattern = namePattern;
#pragma warning disable CA1308 // Normalize strings to uppercase
            Token = token.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
            FileID = "default";
        }

        /// <summary>
        /// Constructs an ORTS custom physics Attribute class
        /// <para>Used to specify the STF file extension and initial token.</para>
        /// <para>If multiple physics files are used, other than default, specify the FileID also.</para>
        /// </summary>
        /// <param name="namePattern">Extension of STF file</param>
        /// <param name="token">Token in STF file</param>
        /// <param name="fileID">ID of the file, used to separate attributes specified in different files. Default value is 'default'.</param>
        public OrtsPhysicsFileAttribute(string namePattern, string token, string fileID)
        {
            NamePattern = namePattern;
#pragma warning disable CA1308 // Normalize strings to uppercase
            Token = token.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
            FileID = fileID;
        }

        public STFReader OpenSTF(string engFile)
        {
            try
            {
                string name = engFile;

                if (!NamePattern.StartsWith(".", StringComparison.Ordinal))
                {
                    int lp = name.LastIndexOf('.');
                    name = name.Substring(0, lp + 1);
                }
                name += NamePattern;

                return new STFReader(name, false);
            }
            catch
            {
                return null;
            }
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class OrtsPhysicsAttribute : Attribute
    {
        public string Title { get; private set; }
        public string Token { get; private set; }
        public string Description { get; private set; }
        public object DefaultValue { get; private set; }
        public string FileID { get; private set; }

        /// <summary>
        /// Constructs an ORTS custom physics Attribute class
        /// <para>Parsed types are string, bool, int, float, double, Vector3</para>
        /// </summary>
        /// <param name="title">Short, meaningful title of the physics Attribute, displayed in the Editor progam</param>
        /// <param name="token">Token in STF of the physics Attribute</param>
        /// <param name="description">Longer description of the physics Attribute, displayed in the Editor progam</param>
        /// <param name="defaultvalue">Default value of the physics Attribute -- BE CAUTIOUS of the given value's type, it is checked at runtime ONLY!
        /// <para>Vector3 values must be specified as string, numeric values separated by space, ',' or ':'</para></param>
        public OrtsPhysicsAttribute(string title, string token, string description, object defaultvalue)
        {
            Title = title;
#pragma warning disable CA1308 // Normalize strings to uppercase
            Token = token.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
            Description = description;
            DefaultValue = defaultvalue;
            FileID = "default";
        }

        /// <summary>
        /// Constructs an ORTS custom physics Attribute class
        /// <para>Parsed types are string, bool, int, float, double, Vector3</para>
        /// </summary>
        /// <param name="title">Short, meaningful title of the physics Attribute, displayed in the Editor progam</param>
        /// <param name="token">Token in STF of the physics Attribute</param>
        /// <param name="description">Longer description of the physics Attribute, displayed in the Editor progam</param>
        /// <param name="defaultvalue">Default value of the physics Attribute -- BE CAUTIOUS of the given value's type, it is checked at runtime ONLY!
        /// <para>Vector3 values must be specified as string, numeric values separated by space, ',' or ':'</para></param>
        /// <param name="fileID">Optional, string ID of the file containing the Attribute. The ID is specified at ORTSPhysicsFileAttribute on the class. Default value is 'default'.</param>
        public OrtsPhysicsAttribute(string title, string token, string description, object defaultvalue, string fileID)
        {
            Title = title;
#pragma warning disable CA1308 // Normalize strings to uppercase
            Token = token.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
            Description = description;
            DefaultValue = defaultvalue;
            FileID = fileID;
        }
    }

    internal class AttributeProcessor
    {
        public STFReader.Processor Processor { get; }
        private readonly FieldInfo fieldInfo;

        public AttributeProcessor(object setWhom, FieldInfo fi, STFReader stf, object defaultValue)
        {
            fieldInfo = fi;

            Processor = () =>
            {
                switch (fieldInfo.FieldType.Name.ToUpperInvariant())
                {
                    case "INT":
                    case "INT32":
                        {
                            int? i = defaultValue as int?;
                            fieldInfo.SetValue(setWhom,
                                stf.ReadIntBlock(i));
                            break;
                        }
                    case "BOOL":
                    case "BOOLEAN":
                        {
                            bool? b = defaultValue as bool?;
                            fieldInfo.SetValue(setWhom,
                                stf.ReadBoolBlock(b.Value));
                            break;
                        }
                    case "STRING":
                        {
                            string s = defaultValue as string;
                            fieldInfo.SetValue(setWhom,
                                stf.ReadStringBlock(s));
                            break;
                        }
                    case "FLOAT":
                    case "SINGLE":
                        {
                            float? f = defaultValue as float?;
                            fieldInfo.SetValue(setWhom,
                                stf.ReadFloatBlock(STFReader.Units.Any, f));
                            break;
                        }
                    case "DOUBLE":
                        {
                            double? d = defaultValue as double?;
                            fieldInfo.SetValue(setWhom,
                                stf.ReadDoubleBlock(d));
                            break;
                        }
                    case "VECTOR3":
                        {
                            Vector3 v3 = (defaultValue as string).ParseVector3();
                            {
                                fieldInfo.SetValue(setWhom,
                                    stf.ReadVector3Block(STFReader.Units.Any, v3));
                            }
                            break;
                        }
                    case "VECTOR4":
                        {
                            Vector4 v4 = (defaultValue as string).ParseVector4();
                            {
                                stf.ReadVector4Block(STFReader.Units.Any, ref v4);
                                fieldInfo.SetValue(setWhom, v4);
                            }
                            break;
                        }
                    case "COLOR":
                        {
                            Color c = (defaultValue as string).ParseColor();
                            {
                                Vector4 v4 = new Vector4(-1);
                                stf.ReadVector4Block(STFReader.Units.Any, ref v4);
                                if (v4.W == -1)
                                {
                                    c.A = 255;
                                    c.R = v4.X == -1 ? c.R : (byte)v4.X;
                                    c.G = v4.Y == -1 ? c.G : (byte)v4.Y;
                                    c.B = v4.Z == -1 ? c.B : (byte)v4.Z;
                                }
                                else
                                {
                                    c.A = v4.X == -1 ? c.A : (byte)v4.X;
                                    c.R = v4.Y == -1 ? c.R : (byte)v4.Y;
                                    c.G = v4.Z == -1 ? c.G : (byte)v4.Z;
                                    c.B = v4.W == -1 ? c.B : (byte)v4.W;
                                }
                                fieldInfo.SetValue(setWhom, c);
                            }
                            break;
                        }
                }
            };
        }
    }

    internal static class VectorExt
    {

        public static void SetValue2(this FieldInfo fi, object obj, object value)
        {
            switch (fi.FieldType.Name)
            {
                case "Vector3":
                    {
                        Vector3 v3 = (value as string).ParseVector3();
                        fi.SetValue(obj, v3);
                        break;
                    }

                case "Vector4":
                    {
                        Vector4 v4 = (value as string).ParseVector4();
                        fi.SetValue(obj, v4);
                        break;
                    }

                case "Color":
                    {
                        Color c = (value as string).ParseColor();
                        fi.SetValue(obj, c);
                        break;
                    }

                default:
                    fi.SetValue(obj, value);
                    break;
            }
        }
    }
    #endregion
}
