// COPYRIGHT 2009 - 2020 by the Open Rails project.
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
//
// Based on original work by Dan Reynolds 2017-12-21

/* To add your own API:
 * - Add a new #region, like /API/TrackMonitor/ below.
 * - Code a method like ApiTrackMonitor below.
 * - In the constructor for WebApi(), add an entry into ApiDict
 * - Add a folder into RunActivity\WebServices\Web\API\
 * - Add index.html, index.js and perhaps index.css into that folder using \API\Template\index.* as a template
 */



using Orts.Simulation.Physics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orts.ActivityRunner.Viewer3D.WebServices
{
    public class WebApi
    {
        // API routing classes & functions
        public static Dictionary<string, Func<string, object>> ApiDict = new Dictionary<string, Func<string, object>>(StringComparer.InvariantCultureIgnoreCase);

        // Viewer object from Viewer3D - needed by APIs that access data from windows in Viewer3D\Popups\*
        public Viewer Viewer;

        public WebApi()
        {
            // "127.0.0.1:2150/API/HUD/" is the URL, "ApiHud" is the name of the method that gets called.
            ApiDict.Add("/API/HUD/", ApiHud);
            ApiDict.Add("/API/TEMPLATE/", ApiTemplate);
            ApiDict.Add("/API/TRACKMONITOR/", ApiTrackMonitor);
        }

        #region /API/Template/
        // Shows a way to pass various data types and arrays and lists to HTML
        public class CustomObject
        {
            public string Str;
            public int Int;
        }

        public class ApiTemplateModel
        {
            public bool boolData;
            public int intData;
            public float floatData;
            public string strData;
            public DateTime dateData;
            public CustomObject customObject;
            public string[] strArrayData;
            public List<int> intList;
        }

        public object ApiTemplate(string Parameters)
        {
            var sampleData = new ApiTemplateModel();

            sampleData.boolData = true;
            sampleData.intData = 576;
            sampleData.floatData = 3.142f;
            sampleData.strData = "Sample String";
            sampleData.dateData = new DateTime(2018, 1, 1);

            sampleData.customObject = new CustomObject();
            sampleData.customObject.Str = "customObject String";
            sampleData.customObject.Int = 123;

            sampleData.strArrayData = new string[5];
            sampleData.strArrayData[0] = "First item";
            sampleData.strArrayData[1] = "Second item";
            sampleData.strArrayData[2] = "Third item";
            sampleData.strArrayData[3] = "Forth item";
            sampleData.strArrayData[4] = "Fifth item";

            sampleData.intList = new List<int>();
            sampleData.intList.Add(0);
            sampleData.intList.Add(1);
            sampleData.intList.Add(2);

            return (sampleData);
        }
        #endregion

        #region /API/HUD/
        // API to display the HUD Windows
        public class ApiHudTable
        {
            public int nRows;
            public int nCols;
            public string[] values;

            public ApiHudTable(int nRows, int nCols, string[] values)
            {
                this.nRows = nRows;
                this.nCols = nCols;
                this.values = values;
            }
        }

        public class HudApiArray
        {
            public int nTables;
            public ApiHudTable commonTable;
            public ApiHudTable extraTable;
        }

        public object ApiHud(string Parameters)
        {
            if (Parameters == null)
                return (null);

            int index = Parameters.IndexOf('=');
            if (index == -1)
                return (null);
            string strPageno = Parameters.Substring(index + 1, Parameters.Length - index - 1);
            strPageno = strPageno.Trim();
            int pageNo = Int32.Parse(strPageno);

            var hudApiArray = new HudApiArray();

            hudApiArray.commonTable = ApiProcessHudTable(0);
            if (pageNo == 0)
            {
                hudApiArray.nTables = 1;
                hudApiArray.extraTable = null;
            }
            else
            {
                hudApiArray.nTables = 2;
                hudApiArray.extraTable = ApiProcessHudTable(pageNo);
            }
            return hudApiArray;
        }

        public ApiHudTable ApiProcessHudTable(int pageNo)
        {
            Viewer3D.Popups.HUDWindow.TableData hudTable = Viewer.HUDWindow.PrepareTable(pageNo);

            var apiTable = new ApiHudTable
                (hudTable.Cells.GetLength(0)
                , hudTable.Cells.GetLength(1)
                , new string[hudTable.Cells.GetLength(0) * hudTable.Cells.GetLength(1)]
                );
            try
            {
                var nextCell = 0;
                for (int i = 0; i < apiTable.nRows; ++i)
                {
                    for (int j = 0; j < apiTable.nCols; ++j)
                    {
                        apiTable.values[nextCell++] = hudTable.Cells[i, j];
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
            }
            return (apiTable);
        }
        #endregion

        #region /API/TrackMonitor/
        // Provides most of the data in the Track Monitor F4
        public class TrackMonitorInfo
        {
            public Train.TRAIN_CONTROL controlMode;          // present control mode 
            public float speedMpS;                           // present speed
            public float projectedSpeedMpS;                  // projected speed
            public float allowedSpeedMpS;                    // max allowed speed
            public float currentElevationPercent;            // elevation %
            public int direction;                            // present direction (0=forward, 1=backward)
            public int cabOrientation;                       // present cab orientation (0=forward, 1=backward)
            public bool isOnPath;                            // train is on defined path (valid in Manual mode only)
        }

        public object ApiTrackMonitor(string Parameters)
        {
            var trainInfo = Viewer.PlayerTrain.GetTrainInfo();

            return new TrackMonitorInfo
            { controlMode = trainInfo.ControlMode
            , speedMpS = trainInfo.speedMpS
            , projectedSpeedMpS = trainInfo.projectedSpeedMpS
            , allowedSpeedMpS = trainInfo.allowedSpeedMpS
            , currentElevationPercent = trainInfo.currentElevationPercent
            , direction = trainInfo.direction
            , cabOrientation = trainInfo.cabOrientation
            , isOnPath = trainInfo.isOnPath
            };
        }
        #endregion
    }
}
