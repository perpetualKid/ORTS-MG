// COPYRIGHT 2018 by the Open Rails project.
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

// Use this define to diagnose issues in the JSON reader below.
// #define DEBUG_JSON_READER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Orts.Formats.OR.Parsers
{
    public class JsonReader
    {
        public static void ReadFile(string fileName, Func<JsonReader, bool> tryParse)
        {
            using (var reader = new JsonTextReader(File.OpenText(fileName))
            {
                CloseInput = true,
            })
            {
                new JsonReader(fileName, reader).ReadBlock(tryParse);
            }
        }

        private readonly string fileName;
        private JsonTextReader reader;
        private StringBuilder path;
        private Stack<int> pathPositions;

        public string Path { get; private set; }

        private JsonReader(string fileName, JsonTextReader reader)
        {
            this.fileName = fileName;
            this.reader = reader;
            path = new StringBuilder();
            pathPositions = new Stack<int>();
        }

        public void ReadBlock(Func<JsonReader, bool> tryParse)
        {
            var basePosition = pathPositions.Count > 0 ? pathPositions.Peek() : 0;

#if DEBUG_JSON_READER
            Trace.WriteLine();
            Trace.WriteLine($"JsonReader({_path.ToString()} ({string.Join(",", _pathPositions.Select(p => p.ToString()).ToArray())})).ReadBlock(): base={basePosition}");
#endif

            while (reader.Read())
            {
#if DEBUG_JSON_READER
                Trace.WriteLine($"JsonReader.ReadBlock({_path.ToString()} ({string.Join(",", _pathPositions.Select(p => p.ToString()).ToArray())})): token={_reader.TokenType} value={_reader.Value} type={_reader.ValueType}");
#endif
                switch (reader.TokenType)
                {
                    case JsonToken.StartArray:
                        pathPositions.Push(path.Length);
                        path.Append("[]");
                        break;
                    case JsonToken.EndArray:
                        path.Length = pathPositions.Pop();
                        break;
                    case JsonToken.StartObject:
                        pathPositions.Push(path.Length);
                        if (pathPositions.Count > 1) path.Append('.');
                        pathPositions.Push(path.Length);
                        break;
                    case JsonToken.PropertyName:
                        Debug.Assert(reader.ValueType == typeof(string));
                        path.Length = pathPositions.Peek();
                        path.Append((string)reader.Value);
                        break;
                    case JsonToken.EndObject:
                        var end = pathPositions.Pop();
                        path.Length = pathPositions.Pop();
                        if (end == basePosition) return;
                        break;
                }

                switch (reader.TokenType)
                {
                    case JsonToken.StartObject:
                    case JsonToken.Boolean:
                    case JsonToken.Bytes:
                    case JsonToken.Date:
                    case JsonToken.Float:
                    case JsonToken.Integer:
                    case JsonToken.Null:
                    case JsonToken.String:
                        Path = path.ToString().Substring(basePosition);
                        if (!tryParse(this)) TraceInformation($"Skipped unknown {reader.TokenType} \"{reader.Value}\" in {Path}");
                        break;
                }
            }
        }

        public T AsEnum<T>(T defaultValue)
        {
            Debug.Assert(typeof(T).IsEnum, "Must use type inheriting from Enum for AsEnum()");
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    var value = (string)reader.Value;
                    return (T)Enum.Parse(typeof(T), value, true);
                default:
                    TraceWarning($"Expected string (enum) value in {Path}; got {reader.TokenType}");
                    return defaultValue;
            }
        }

        public float AsFloat(float defaultValue)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Float:
                    return (float)(double)reader.Value;
                case JsonToken.Integer:
                    return (long)reader.Value;
                default:
                    TraceWarning($"Expected floating point value in {Path}; got {reader.TokenType}");
                    return defaultValue;
            }
        }

        public int AsInteger(int defaultValue)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                    return (int)(long)reader.Value;
                default:
                    TraceWarning($"Expected integer value in {Path}; got {reader.TokenType}");
                    return defaultValue;
            }
        }

        public string AsString(string defaultValue)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    return (string)reader.Value;
                default:
                    TraceWarning($"Expected string value in {Path}; got {reader.TokenType}");
                    return defaultValue;
            }
        }

        public float AsTime(float defaultValue)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    var time = ((string)reader.Value).Split(':');
                    var StartTime = new TimeSpan(int.Parse(time[0]), time.Length > 1 ? int.Parse(time[1]) : 0, time.Length > 2 ? int.Parse(time[2]) : 0);
                    return (float)StartTime.TotalSeconds;
                default:
                    TraceWarning($"Expected string (time) value in {Path}; got {reader.TokenType}");
                    return defaultValue;
            }
        }

        public void TraceWarning(string message)
        {
            Trace.TraceWarning("{2} in {0}:line {1}", fileName, reader.LineNumber, message);
        }

        public void TraceInformation(string message)
        {
            Trace.TraceInformation("{2} in {0}:line {1}", fileName, reader.LineNumber, message);
        }
    }
}
