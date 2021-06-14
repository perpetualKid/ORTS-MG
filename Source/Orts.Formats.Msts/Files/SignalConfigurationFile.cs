// COPYRIGHT 2010, 2011, 2012, 2013 by the Open Rails project.
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

// This module parses the sigcfg file and builds an object model based on signal details
// 
// Author: Laurie Heath
// Updates : Rob Roeterdink
// 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Orts.Common;
using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Files
{
    /// <summary>
    /// Object containing a representation of everything in the MSTS sigcfg.dat file
    /// Not everythin of the representation will be used by OpenRails
    /// </summary>
    public class SignalConfigurationFile
    {
        /// <summary>Name-indexed list of available light textures</summary>
        public Dictionary<string, LightTexture> LightTextures { get; private set; }
        /// <summary>Name-indexed list of available colours for lights</summary>
        public Dictionary<string, LightTableEntry> LightsTable { get; private set; }
        /// <summary>Name-indexed list of available signal types</summary>
        public Dictionary<string, SignalType> SignalTypes { get; private set; }
        /// <summary>Name-indexed list of available signal shapes (including heads and other sub-objects)</summary>
        public Dictionary<string, SignalShape> SignalShapes { get; private set; }
        /// <summary>list of names of script files</summary>
        public List<string> ScriptFiles { get; private set; }
        /// <summary>Full file name and path of the signal config file</summary>
        public string ScriptPath { get; private set; }

        /// <summary>
        /// Constructor from file
        /// </summary>
        /// <param name="fileName">Full file name of the sigcfg.dat file</param>
        /// <param name="orMode">Read file in OR Mode (set NumClearAhead_ORTS only)</param>
        public SignalConfigurationFile(string fileName, bool orMode)
        {
            ScriptPath = Path.GetDirectoryName(fileName);

            OrSignalTypes.Reset();
            // preset OR function types and related MSTS function types for default types
            OrSignalTypes.Instance.FunctionTypes.AddRange(EnumExtension.GetNames<SignalFunction>());

            if (orMode)
            {
                using (STFReader stf = new STFReader(fileName, false))
                    stf.ParseFile(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("lighttextures", ()=>{ LightTextures = ReadLightTextures(stf); }),
                    new STFReader.TokenProcessor("lightstab", ()=>{ LightsTable = ReadLightsTable(stf); }),
                    new STFReader.TokenProcessor("ortssignalfunctions", ()=>{ ReadOrtsSignalFunctionTypes(stf); }),
                    new STFReader.TokenProcessor("ortsnormalsubtypes", ()=>{ ReadOrtsNormalSubtypes(stf); }),
                    new STFReader.TokenProcessor("signaltypes", ()=>{ SignalTypes = ReadSignalTypes(stf, orMode); }),
                    new STFReader.TokenProcessor("signalshapes", ()=>{ SignalShapes = ReadSignalShapes(stf); }),
                    new STFReader.TokenProcessor("scriptfiles", ()=>{ ScriptFiles = ReadScriptFiles(stf); }),
                });
            }
            else
            {
                using (STFReader stf = new STFReader(fileName, false))
                    stf.ParseFile(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("lighttextures", ()=>{ LightTextures = ReadLightTextures(stf); }),
                    new STFReader.TokenProcessor("lightstab", ()=>{ LightsTable = ReadLightsTable(stf); }),
                    new STFReader.TokenProcessor("signaltypes", ()=>{ SignalTypes = ReadSignalTypes(stf, orMode); }),
                    new STFReader.TokenProcessor("signalshapes", ()=>{ SignalShapes = ReadSignalShapes(stf); }),
                    new STFReader.TokenProcessor("scriptfiles", ()=>{ ScriptFiles = ReadScriptFiles(stf); }),
                });
            }

            LightTextures = CheckAndInitialize(LightTextures, nameof(LightTextures), fileName);
            LightsTable = CheckAndInitialize(LightsTable, nameof(LightsTable), fileName);
            SignalTypes = CheckAndInitialize(SignalTypes, nameof(SignalTypes), fileName);
            SignalShapes = CheckAndInitialize(SignalShapes, nameof(SignalShapes), fileName);
            ScriptFiles = CheckAndInitialize(ScriptFiles, nameof(ScriptFiles), fileName);
        }

        private static T CheckAndInitialize<T>(T obj, string name, string fileName) where T: new()
        {
            if (obj == null)
            {
                Trace.TraceWarning($"Ignored missing {name} in {fileName}");
                return new T();
            }
            return obj;
        }

        private static void ReadOrtsSignalFunctionTypes(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            int tokensRead = 0;

            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortssignalfunctiontype", ()=> {
                    stf.MustMatchBlockStart();
                    if (tokensRead >= count)
                    {
                        STFException.TraceWarning(stf, "Skipped extra ORTSFunctionType");
                    }
                    else
                    {
                        string functionType = stf.ReadString();
                        // check agains default types
                        if (EnumExtension.GetValue(functionType, out SignalFunction result))
                        {
                            STFException.TraceWarning(stf, "Invalid definition of ORTSFunctionType, type is equal to MSTS defined type : " + functionType);
                        }
                        else if (functionType.StartsWith("OR_", StringComparison.OrdinalIgnoreCase) || functionType.StartsWith("ORTS", StringComparison.OrdinalIgnoreCase))
                        {
                            STFException.TraceWarning(stf, "Invalid definition of ORTSFunctionType, using reserved type name : " + functionType);
                        }
                        else
                        {
                            if (OrSignalTypes.Instance.FunctionTypes.Contains(functionType, StringComparer.OrdinalIgnoreCase))
                                STFException.TraceWarning(stf, "Skipped duplicate ORTSSignalFunction definition : " + functionType);
                            else
                            {
                                OrSignalTypes.Instance.FunctionTypes.Add(functionType);
                                tokensRead ++;
                            }
                        }
                    }
                    stf.SkipRestOfBlock();
                }),
            });
        }

        private static void ReadOrtsNormalSubtypes(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            int tokensRead = 0;

            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("ortsnormalsubtype", ()=> {
                    stf.MustMatchBlockStart();
                    if (tokensRead >= count)
                    {
                        STFException.TraceWarning(stf, "Skipped extra ORTSNormalSubtype");
                    }
                    else
                    {
                        string subType = stf.ReadString().ToUpperInvariant();
                        if (OrSignalTypes.Instance.NormalSubTypes.Contains(subType, StringComparer.OrdinalIgnoreCase))
                        {
                            STFException.TraceWarning(stf, "Skipped duplicate ORTSNormalSubtype definition : " + subType);
                        }
                        else
                        {
                            OrSignalTypes.Instance.NormalSubTypes.Add(subType);
                            tokensRead ++;
                        }
                    }
                    stf.SkipRestOfBlock();
                }),
            });
        }

        private static Dictionary<string, LightTexture> ReadLightTextures(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            Dictionary<string, LightTexture> lightTextures = new Dictionary<string, LightTexture>(count, StringComparer.OrdinalIgnoreCase);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("lighttex", ()=>{
                    if (lightTextures.Count >= count)
                        STFException.TraceWarning(stf, "Skipped extra LightTex");
                    else
                    {
                        LightTexture lightTexture = new LightTexture(stf);
                        if (lightTextures.ContainsKey(lightTexture.Name))
                            STFException.TraceWarning(stf, "Skipped duplicate LightTex " + lightTexture.Name);
                        else
                            lightTextures.Add(lightTexture.Name, lightTexture);
                    }
                }),
            });
            if (lightTextures.Count < count)
                STFException.TraceWarning(stf, $"{count - lightTextures.Count} missing LightTex(s)");
            return lightTextures;
        }

        private static Dictionary<string, LightTableEntry> ReadLightsTable(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            Dictionary<string, LightTableEntry> lightsTable = new Dictionary<string, LightTableEntry>(count, StringComparer.OrdinalIgnoreCase);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("lightstabentry", ()=>{
                    if (lightsTable.Count >= count)
                        STFException.TraceWarning(stf, "Skipped extra LightsTabEntry");
                    else
                    {
                        LightTableEntry lightsTableEntry = new LightTableEntry(stf);
                        if (lightsTable.ContainsKey(lightsTableEntry.Name))
                            STFException.TraceWarning(stf, "Skipped duplicate LightsTabEntry " + lightsTableEntry.Name);
                        else
                            lightsTable.Add(lightsTableEntry.Name, lightsTableEntry);
                    }
                }),
            });
            if (lightsTable.Count < count)
                STFException.TraceWarning(stf, $"{count - lightsTable.Count} missing LightsTabEntry(s)");
            return lightsTable;
        }

        private static Dictionary<string, SignalType> ReadSignalTypes(STFReader stf, bool orMode)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            Dictionary<string, SignalType> signalTypes = new Dictionary<string, SignalType>(count, StringComparer.OrdinalIgnoreCase);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signaltype", ()=>{
                    if (signalTypes.Count >= count)
                        STFException.TraceWarning(stf, "Skipped extra SignalType");
                    else
                    {
                        SignalType signalType = new SignalType(stf, orMode);
                        if (signalTypes.ContainsKey(signalType.Name))
                            STFException.TraceWarning(stf, "Skipped duplicate SignalType " + signalType.Name);
                        else
                            signalTypes.Add(signalType.Name, signalType);
                    }
                }),
            });
            if (signalTypes.Count < count)
                STFException.TraceWarning(stf, $"{count - signalTypes.Count} missing SignalType(s)");
            return signalTypes;
        }

        private static Dictionary<string, SignalShape> ReadSignalShapes(STFReader stf)
        {
            stf.MustMatchBlockStart();
            int count = stf.ReadInt(null);
            Dictionary<string, SignalShape> signalShapes = new Dictionary<string, SignalShape>(count, StringComparer.OrdinalIgnoreCase);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("signalshape", ()=>{
                        if (signalShapes.Count >= count)
                            STFException.TraceWarning(stf, "Skipped extra SignalShape");
                        else
                        {
                            SignalShape signalShape = new SignalShape(stf);
                            if (signalShapes.ContainsKey(signalShape.ShapeFileName))
                                STFException.TraceWarning(stf, "Skipped duplicate SignalShape " + signalShape.ShapeFileName);
                            else
                                signalShapes.Add(signalShape.ShapeFileName, signalShape);
                        }
                }),
            });
            if (signalShapes.Count < count)
                STFException.TraceWarning(stf, $"{count - signalShapes.Count} missing SignalShape(s)");
            return signalShapes;
        }

        private static List<string> ReadScriptFiles(STFReader stf)
        {
            stf.MustMatchBlockStart();
            List<string> scriptFiles = new List<string>();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("scriptfile", ()=>{ scriptFiles.Add(stf.ReadStringBlock(null)); }),
            });
            return scriptFiles;
        }
    }
}
