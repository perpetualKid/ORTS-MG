// COPYRIGHT 2013, 2015 by the Open Rails project.
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

using System.Diagnostics;

using Orts.Formats.Msts.Models;
using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Files
{
    public class ShapeFile
    {
        public Shape Shape { get; private set; }

        private void Validate(string fileName)
        {
            if (Shape.LodControls.Count < 1)
                Trace.TraceWarning("Missing at least one LOD Control element in shape {0}", fileName);

            for (int distanceLevelIndex = 0; distanceLevelIndex < Shape.LodControls[0].DistanceLevels.Count; distanceLevelIndex++)
            {
                DistanceLevel distanceLevel = Shape.LodControls[0].DistanceLevels[distanceLevelIndex];

                if (distanceLevel.DistanceLevelHeader.Hierarchy.Length != Shape.Matrices.Count)
                    Trace.TraceWarning("Expected {2} hierarchy elements; got {3} in distance level {1} in shape {0}", fileName, distanceLevelIndex, Shape.Matrices.Count, distanceLevel.DistanceLevelHeader.Hierarchy.Length);

                for (int hierarchyIndex = 0; hierarchyIndex < distanceLevel.DistanceLevelHeader.Hierarchy.Length; hierarchyIndex++)
                {
                    int matrixIndex = distanceLevel.DistanceLevelHeader.Hierarchy[hierarchyIndex];
                    if (matrixIndex < -1 || matrixIndex >= Shape.Matrices.Count)
                        Trace.TraceWarning("Hierarchy element {2} out of range (expected {3} to {4}; got {5}) in distance level {1} in shape {0}", fileName, distanceLevelIndex, hierarchyIndex, -1, Shape.Matrices.Count - 1, matrixIndex);
                }

                for (int subObjectIndex = 0; subObjectIndex < distanceLevel.SubObjects.Count; subObjectIndex++)
                {
                    SubObject subObject = distanceLevel.SubObjects[subObjectIndex];

                    if (subObject.SubObjectHeader.GeometryInfo.GeometryNodeMap.Length != Shape.Matrices.Count)
                        Trace.TraceWarning("Expected {3} geometry node map elements; got {4} in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, Shape.Matrices.Count, subObject.SubObjectHeader.GeometryInfo.GeometryNodeMap.Length);

                    int[] geometryNodeMap = subObject.SubObjectHeader.GeometryInfo.GeometryNodeMap;
                    for (int geometryNodeMapIndex = 0; geometryNodeMapIndex < geometryNodeMap.Length; geometryNodeMapIndex++)
                    {
                        int geometryNode = geometryNodeMap[geometryNodeMapIndex];
                        if (geometryNode < -1 || geometryNode >= subObject.SubObjectHeader.GeometryInfo.GeometryNodes.Count)
                            Trace.TraceWarning("Geometry node map element {3} out of range (expected {4} to {5}; got {6}) in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, geometryNodeMapIndex, -1, subObject.SubObjectHeader.GeometryInfo.GeometryNodes.Count - 1, geometryNode);
                    }

                    Vertices vertices = subObject.Vertices;
                    for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
                    {
                        Vertex vertex = vertices[vertexIndex];

                        if (vertex.PointIndex < 0 || vertex.PointIndex >= Shape.Points.Count)
                            Trace.TraceWarning("Point index out of range (expected {4} to {5}; got {6}) in vertex {3} in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, vertexIndex, 0, Shape.Points.Count - 1, vertex.PointIndex);

                        if (vertex.NormalIndex < 0 || vertex.NormalIndex >= Shape.Normals.Count)
                            Trace.TraceWarning("Normal index out of range (expected {4} to {5}; got {6}) in vertex {3} in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, vertexIndex, 0, Shape.Normals.Count - 1, vertex.NormalIndex);

                        if (vertex.VertexUVs.Length < 1)
                            Trace.TraceWarning("Missing UV index in vertex {3} in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, vertexIndex);
                        else if (vertex.VertexUVs[0] < 0 || vertex.VertexUVs[0] >= Shape.UVPoints.Count)
                            Trace.TraceWarning("UV index out of range (expected {4} to {5}; got {6}) in vertex {3} in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, vertexIndex, 0, Shape.UVPoints.Count - 1, vertex.VertexUVs[0]);
                    }

                    for (int primitiveIndex = 0; primitiveIndex < subObject.Primitives.Count; primitiveIndex++)
                    {
                        IndexedTriList triangleList = subObject.Primitives[primitiveIndex].IndexedTriList;
                        for (int triangleListIndex = 0; triangleListIndex < triangleList.VertexIndices.Count; triangleListIndex++)
                        {
                            if (triangleList.VertexIndices[triangleListIndex].A < 0 || triangleList.VertexIndices[triangleListIndex].A >= vertices.Count)
                                Trace.TraceWarning("Vertex out of range (expected {4} to {5}; got {6}) in primitive {3} in sub-object {2} in distance level {1} in shape {0}", fileName, distanceLevelIndex, subObjectIndex, primitiveIndex, 0, vertices.Count - 1, triangleList.VertexIndices[triangleListIndex].A);
                        }
                    }
                }
            }
        }

        public ShapeFile(string fileName, bool suppressShapeWarnings)
        {
            using (SBR file = SBR.Open(fileName))
            {
                Shape = new Shape(file.ReadSubBlock());
                //                file.VerifyEndOfBlock();//covered through "using" Dispose-implementation
                if (!suppressShapeWarnings)
                    Validate(fileName);
            }
        }
    }
}
