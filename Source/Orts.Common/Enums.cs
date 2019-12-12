// COPYRIGHT 2009, 2011 by the Open Rails project.
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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Orts.Common
{
    #region Enum Extension
    public static class EnumExtension
    {
        private static class EnumCache<T> where T : Enum
        {
            internal static readonly IList<string> Names;
            internal static readonly IList<T> Values;
            internal static readonly Dictionary<T, string> ValueToDescriptionMap;
            internal static string EnumDescription;
            internal static IDictionary<string, T> NameValuePairs;

            static EnumCache()
            {
                Values = new ReadOnlyCollection<T>((T[])Enum.GetValues(typeof(T)));
                Names = new ReadOnlyCollection<string>(Enum.GetNames(typeof(T)));
                ValueToDescriptionMap = new Dictionary<T, string>();
                EnumDescription = typeof(T).GetCustomAttributes(typeof(DescriptionAttribute), false).
                    Cast<DescriptionAttribute>().
                    Select(x => x.Description).
                    FirstOrDefault();
                foreach (T value in Values)//(T[])Enum.GetValues(typeof(T)))
                {
                    ValueToDescriptionMap[value] = GetDescription(value);
                }

                NameValuePairs = Names.Zip(Values, (k, v) => new { k, v })
                              .ToDictionary(x => x.k, x => x.v, StringComparer.OrdinalIgnoreCase);
            }

            private static string GetDescription(T value)
            {
                FieldInfo field = typeof(T).GetField(value.ToString());
                return field.GetCustomAttributes(typeof(DescriptionAttribute), false)
                            .Cast<DescriptionAttribute>()
                            .Select(x => x.Description)
                            .FirstOrDefault();
            }
        }

        /// <summary>
        /// returns the Description attribute for the particular enum value
        /// </summary>
        public static string GetDescription<T>(this T item) where T : Enum
        {
            if (EnumCache<T>.ValueToDescriptionMap.TryGetValue(item, out string description))
            {
                return description;
            }
            throw new ArgumentOutOfRangeException("item");
        }

        /// <summary>
        /// returns the Description attribute for the enum type
        /// </summary>
        public static string EnumDescription<T>() where T : Enum
        {
            return EnumCache<T>.EnumDescription;
        }

        /// <summary>
        /// returns a static list of all names in this enum
        /// </summary>
        public static IList<string> GetNames<T>() where T : Enum
        {
            return EnumCache<T>.Names;
        }

        /// <summary>
        /// returns a static list of all values in this enum
        /// </summary>
        public static IList<T> GetValues<T>() where T : Enum
        {
            return EnumCache<T>.Values;
        }

        /// <summary>
        /// returns a number of elements in this enum
        /// </summary>
        public static int GetLength<T>() where T : Enum
        {
            return EnumCache<T>.Values.Count;
        }

        /// <summary>
        /// Similar as Enum.TryParse, but based on statically cached dictionary
        /// </summary>
        public static bool GetValue<T>(string name, out T result) where T: Enum
        {
            return EnumCache<T>.NameValuePairs.TryGetValue(name, out result);
        }

        /// <summary>
        /// allows to enumerate forward over enum values
        /// </summary>
        public static T Next<T>(this T item) where T : Enum
        {
            return EnumCache<T>.Values[(EnumCache<T>.Values.IndexOf(item) + 1) % EnumCache<T>.Values.Count];
        }

        /// <summary>
        /// allows to enumerate backward over enum values
        /// </summary>
        public static T Previous<T>(this T item) where T : Enum
        {
            return EnumCache<T>.Values[(EnumCache<T>.Values.IndexOf(item) - 1 + EnumCache<T>.Values.Count) % EnumCache<T>.Values.Count];
        }
    }
    #endregion

    [Description("Reverser")]
    public enum Direction
    {
        [Description("Forward")] Forward,
        [Description("Reverse")] Reverse,
        [Description("N")] N
    }

    [Description("Rotation")]
    public enum Rotation
    {
        CounterClockwise = -1,
        None = 0,
        Clockwise = 1,
    }
    public class DirectionControl
    {
        public static Direction Flip(Direction direction)
        {
            //return direction == Direction.Forward ? Direction.Reverse : Direction.Forward;
            if (direction == Direction.N)
                return Direction.N;
            if (direction == Direction.Forward)
                return Direction.Reverse;
            else
                return Direction.Forward;
        }
    }

    public enum TrackMonitorSignalAspect
    {
        None,
        Clear_2,
        Clear_1,
        Approach_3,
        Approach_2,
        Approach_1,
        Restricted,
        StopAndProceed,
        Stop,
        Permission,
    }

    public enum ScreenMode
    {
        WindowedPresetResolution,
        FullscreenPresetResolution,
        FullscreenNativeResolution,
    }


}
