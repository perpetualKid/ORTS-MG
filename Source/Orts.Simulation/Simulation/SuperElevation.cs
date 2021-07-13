// COPYRIGHT 2013, 2014, 2015, 2016 by the Open Rails project.
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

using Microsoft.Xna.Framework;

using Orts.Common.Calc;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;

namespace Orts.Simulation
{
    public class SuperElevation
    {
        public List<List<TrackVectorSection>> Curves;
        public Dictionary<int, List<TrackVectorSection>> Sections;
        public float MaximumAllowedM;

        //check TDB for long curves and determine each section's position/elev in the curve
        public SuperElevation(Simulator simulator)
        {
            Curves = new List<List<TrackVectorSection>>();
            Sections = new Dictionary<int, List<TrackVectorSection>>();

            MaximumAllowedM = 0.07f + simulator.UseSuperElevation / 100f;//max allowed elevation controlled by user setting

            var SectionList = new List<TrackVectorSection>();
            foreach (var node in simulator.TrackDatabase.TrackDB.TrackNodes)
            {
                TrackVectorNode trackVectorNode = node as TrackVectorNode;
                if (trackVectorNode == null)
                    continue;
                var StartCurve = false; var CurveDir = 0; var Len = 0.0f;
                SectionList.Clear();
                int i = 0; int count = trackVectorNode.TrackVectorSections.Length;
                foreach (var section in trackVectorNode.TrackVectorSections)//loop all curves
                {
                    i++;
                    var sec = simulator.TSectionDat.TrackSections.Get(section.SectionIndex);
                    if (sec == null) continue;
                    if (Math.Abs(sec.Width - simulator.SuperElevationGauge) > 0.2) continue;//the main route has a gauge different than mine
                    float angle = sec.Angle;
                    if (sec.Curved && !angle.AlmostEqual(0f, 0.01f)) //a good curve
                    {
                        if (i == 1 || i == count)
                        {
                            //if (theCurve.Radius * (float)Math.Abs(theCurve.Angle * 0.0174) < 15f) continue; 
                        } //do not want the first and last piece of short curved track to be in the curve (they connected to switches)
                        if (!StartCurve) //we are beginning a curve
                        {
                            StartCurve = true;
                            CurveDir = Math.Sign(sec.Angle);
                            Len = 0f;
                        }
                        else if (CurveDir != Math.Sign(sec.Angle)) //we are in curve, but bending different dir
                        {
                            MarkSections(simulator, SectionList, Len); //treat the sections encountered so far, then restart with other dir
                            CurveDir = Math.Sign(sec.Angle);
                            SectionList.Clear();
                            Len = 0f; //StartCurve remains true as we are still in a curve
                        }
                        Len += sec.Radius * (float)Math.Abs(MathHelper.ToRadians(sec.Angle));
                        SectionList.Add(section);
                    }
                    else //meet a straight line
                    {
                        if (StartCurve == true) //we are in a curve, need to finish
                        {
                            MarkSections(simulator, SectionList, Len);
                            Len = 0f;
                            SectionList.Clear();
                        }
                        StartCurve = false;
                    }
                }
                if (StartCurve == true) // we are in a curve after looking at every section
                {
                    MarkSections(simulator, SectionList, Len);
                }
                Len = 0f; StartCurve = false;
                SectionList.Clear();
            }
        }

        private void MarkSections(Simulator simulator, List<TrackVectorSection> SectionList, float Len)
        {
            if (Len < simulator.SuperElevationMinLen || SectionList.Count == 0) return;//too short a curve or the list is empty
            var tSection = simulator.TSectionDat.TrackSections;
            var sectionData = tSection.Get(SectionList[0].SectionIndex);
            if (sectionData == null) return;
            //loop all section to determine the max elevation for the whole track
            double Curvature = sectionData.Angle * SectionList.Count * 33 / Len;//average radius in degree/100feet
            var Max = (float)(Math.Pow(simulator.Route.SpeedLimit * 2.25, 2) * 0.0007 * Math.Abs(Curvature) - 3); //in inch
            Max = Max * 2.5f;//change to cm
            Max = (float)Math.Round(Max * 2, MidpointRounding.AwayFromZero) / 200f;//closest to 5 mm increase;
            if (Max < 0.01f) return;
            if (Max > MaximumAllowedM) Max = MaximumAllowedM;//max
            Max = (float)Math.Atan(Max / 1.44f); //now change to rotation in radius by quick estimation as the angle is small

            Curves.Add(new List<TrackVectorSection>(SectionList)); //add the curve
            MapWFiles2Sections(SectionList);//map these sections to tiles, so we can compute it quicker later
            if (SectionList.Count == 1)//only one section in the curve
            {
                SectionList[0].StartElev = SectionList[0].EndElev = 0f; SectionList[0].MaxElev = Max;
            }
            else//more than one section in the curve
            {
                var count = 0;

                foreach (var section in SectionList)
                {
                    if (count == 0) { section.StartElev = 0f; section.MaxElev = Max; section.EndElev = Max; }
                    else if (count == SectionList.Count - 1) { section.StartElev = Max; section.MaxElev = Max; section.EndElev = 0f; }
                    else { section.StartElev = section.EndElev = section.MaxElev = Max; }
                    count++;
                }
            }
        }

        //find all sections in a tile, save the info to a look-up table
        private void MapWFiles2Sections(List<TrackVectorSection> sections)
        {
            foreach (var section in sections)
            {
                var key = Math.Abs(section.Location.TileX) + Math.Abs(section.Location.TileZ);
                if (Sections.ContainsKey(key)) Sections[key].Add(section);
                else
                {
                    List<TrackVectorSection> tmpSections = new List<TrackVectorSection>();
                    tmpSections.Add(section);
                    Sections.Add(key, tmpSections);
                }
            }
        }
    }
}
