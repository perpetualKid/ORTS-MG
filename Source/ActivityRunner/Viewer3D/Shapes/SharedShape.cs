using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Common;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts.Files;
using Orts.Formats.Msts.Models;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Orts.ActivityRunner.Viewer3D.Shapes
{
    public class SharedShape
    {
        private static readonly List<string> shapeWarnings = new List<string>();

        // This data is common to all instances of the shape
        public List<string> MatrixNames = new List<string>();
        public Matrix[] Matrices = new Matrix[0];  // the original natural pose for this shape - shared by all instances
        public Animations Animations;
        public LodControl[] LodControls;
        public bool HasNightSubObj;
        public int RootSubObjectIndex = 0;
        public string SoundFileName = "";
        public float BellAnimationFPS = 8;


        protected static Viewer viewer;
        public readonly string FilePath;
        public readonly string ReferencePath;

        /// <summary>
        /// Create an empty shape used as a sub when the shape won't load
        /// </summary>
        /// <param name="viewer"></param>
        private SharedShape()
        {
            FilePath = "Empty";
            LodControls = new LodControl[0];
        }

        internal static void Initialize(Viewer viewer)
        {
            SharedShape.viewer = viewer;
        }

        public static SharedShape Empty = new SharedShape();
        /// <summary>
        /// MSTS shape from shape file
        /// </summary>
        /// <param name="viewer"></param>
        /// <param name="filePath">Path to shape's S file</param>
        public SharedShape(string filePath)
        {
            FilePath = filePath;
            if (filePath.Contains('\0'))
            {
                var parts = filePath.Split('\0');
                FilePath = parts[0];
                ReferencePath = parts[1];
            }
            LoadContent();
        }

        /// <summary>
        /// Only one copy of the model is loaded regardless of how many copies are placed in the scene.
        /// </summary>
        private void LoadContent()
        {
            Trace.Write("S");
            var filePath = FilePath;
            // commented lines allow reading the animation block from an additional file in an Openrails subfolder
            //           string dir = Path.GetDirectoryName(filePath);
            //            string file = Path.GetFileName(filePath);
            //            string orFilePath = dir + @"\openrails\" + file;
            var sFile = new ShapeFile(filePath, viewer.Settings.SuppressShapeWarnings);
            //            if (file.ToLower().Contains("turntable") && File.Exists(orFilePath))
            //            {
            //                sFile.ReadAnimationBlock(orFilePath);
            //            }


            var textureFlags = Helpers.TextureFlags.None;
            if (File.Exists(FilePath + "d"))
            {
                var sdFile = new ShapeDescriptorFile(FilePath + "d");
                textureFlags = (Helpers.TextureFlags)sdFile.Shape.EsdAlternativeTexture;
                if (FilePath != null && FilePath.Contains("\\global\\")) textureFlags |= Helpers.TextureFlags.SnowTrack;//roads and tracks are in global, as MSTS will always use snow texture in snow weather
                HasNightSubObj = sdFile.Shape.EsdSubObject;
                if ((textureFlags & Helpers.TextureFlags.Night) != 0 && FilePath.Contains("\\trainset\\"))
                    textureFlags |= Helpers.TextureFlags.Underground;
                SoundFileName = sdFile.Shape.EsdSoundFileName;
                BellAnimationFPS = sdFile.Shape.EsdBellAnimationFps;
            }

            Matrices = sFile.Shape.Matrices.ToArray();
            MatrixNames = sFile.Shape.Matrices.MatrixNames;
            //var matrixCount = sFile.shape.matrices.Count;
            //MatrixNames.Capacity = matrixCount;
            //Matrices = new Matrix[matrixCount];
            //for (var i = 0; i < matrixCount; ++i)
            //{
            //    MatrixNames.Add(sFile.shape.matrices[i].Name.ToUpper());
            //    Matrices[i] = XNAMatrixFromMSTS(sFile.shape.matrices[i]);
            //}
            Animations = sFile.Shape.Animations;

#if DEBUG_SHAPE_HIERARCHY
            var debugShapeHierarchy = new StringBuilder();
            debugShapeHierarchy.AppendFormat("Shape {0}:\n", Path.GetFileNameWithoutExtension(FilePath).ToUpper());
            for (var i = 0; i < MatrixNames.Count; ++i)
                debugShapeHierarchy.AppendFormat("  Matrix {0,-2}: {1}\n", i, MatrixNames[i]);
            for (var i = 0; i < sFile.shape.prim_states.Count; ++i)
                debugShapeHierarchy.AppendFormat("  PState {0,-2}: flags={1,-8:X8} shader={2,-15} alpha={3,-2} vstate={4,-2} lstate={5,-2} zbias={6,-5:F3} zbuffer={7,-2} name={8}\n", i, sFile.shape.prim_states[i].flags, sFile.shape.shader_names[sFile.shape.prim_states[i].ishader], sFile.shape.prim_states[i].alphatestmode, sFile.shape.prim_states[i].ivtx_state, sFile.shape.prim_states[i].LightCfgIdx, sFile.shape.prim_states[i].ZBias, sFile.shape.prim_states[i].ZBufMode, sFile.shape.prim_states[i].Name);
            for (var i = 0; i < sFile.shape.vtx_states.Count; ++i)
                debugShapeHierarchy.AppendFormat("  VState {0,-2}: flags={1,-8:X8} lflags={2,-8:X8} lstate={3,-2} material={4,-3} matrix2={5,-2}\n", i, sFile.shape.vtx_states[i].flags, sFile.shape.vtx_states[i].LightFlags, sFile.shape.vtx_states[i].LightCfgIdx, sFile.shape.vtx_states[i].LightMatIdx, sFile.shape.vtx_states[i].Matrix2);
            for (var i = 0; i < sFile.shape.light_model_cfgs.Count; ++i)
            {
                debugShapeHierarchy.AppendFormat("  LState {0,-2}: flags={1,-8:X8} uv_ops={2,-2}\n", i, sFile.shape.light_model_cfgs[i].flags, sFile.shape.light_model_cfgs[i].uv_ops.Count);
                for (var j = 0; j < sFile.shape.light_model_cfgs[i].uv_ops.Count; ++j)
                    debugShapeHierarchy.AppendFormat("    UV OP {0,-2}: texture_address_mode={1,-2}\n", j, sFile.shape.light_model_cfgs[i].uv_ops[j].TexAddrMode);
            }
            Trace.Write(debugShapeHierarchy.ToString());
#endif
            LodControls = (from Formats.Msts.Models.LodControl lod in sFile.Shape.LodControls
                           select new LodControl(lod, textureFlags, sFile, this)).ToArray();
            if (LodControls.Length == 0)
                throw new InvalidDataException("Shape file missing lod_control section");
            else if (LodControls[0].DistanceLevels.Length > 0 && LodControls[0].DistanceLevels[0].SubObjects.Length > 0)
            {
                // Zero the position offset of the root matrix for compatibility with MSTS
                if (LodControls[0].DistanceLevels[0].SubObjects[0].ShapePrimitives.Length > 0 && LodControls[0].DistanceLevels[0].SubObjects[0].ShapePrimitives[0].Hierarchy[0] == -1)
                {
                    Matrices[0].M41 = 0;
                    Matrices[0].M42 = 0;
                    Matrices[0].M43 = 0;
                }
                // Look for root subobject, it is not necessarily the first (see ProTrain signal)
                for (int soIndex = 0; soIndex <= LodControls[0].DistanceLevels[0].SubObjects.Length - 1; soIndex++)
                {
                    Formats.Msts.Models.SubObject subObject = sFile.Shape.LodControls[0].DistanceLevels[0].SubObjects[soIndex];
                    if (subObject.SubObjectHeader.GeometryInfo.GeometryNodeMap[0] == 0)
                    {
                        RootSubObjectIndex = soIndex;
                        break;
                    }
                }
            }
        }

        public class LodControl
        {
            public DistanceLevel[] DistanceLevels;

            public LodControl(Formats.Msts.Models.LodControl MSTSlod_control, Helpers.TextureFlags textureFlags, ShapeFile sFile, SharedShape sharedShape)
            {
#if DEBUG_SHAPE_HIERARCHY
                Trace.WriteLine("  LOD control:");
#endif
                DistanceLevels = (from Formats.Msts.Models.DistanceLevel level in MSTSlod_control.DistanceLevels
                                  select new DistanceLevel(level, textureFlags, sFile, sharedShape)).ToArray();
                if (DistanceLevels.Length == 0)
                    throw new InvalidDataException("Shape file missing distance_level");
            }

            internal void Mark()
            {
                foreach (var dl in DistanceLevels)
                    dl.Mark();
            }
        }

        public class DistanceLevel
        {
            public float ViewingDistance;
            public float ViewSphereRadius;
            public SubObject[] SubObjects;

            public DistanceLevel(Formats.Msts.Models.DistanceLevel MSTSdistance_level, Helpers.TextureFlags textureFlags, ShapeFile sFile, SharedShape sharedShape)
            {
#if DEBUG_SHAPE_HIERARCHY
                Trace.WriteLine("    Distance level {0}: hierarchy={1}", MSTSdistance_level.distance_level_header.dlevel_selection, String.Join(" ", MSTSdistance_level.distance_level_header.hierarchy.Select(i => i.ToString()).ToArray()));
#endif
                ViewingDistance = MSTSdistance_level.DistanceLevelHeader.DistanceLevelSelection;
                // TODO, work out ViewShereRadius from all sub_object radius and centers.
                if (sFile.Shape.Volumes.Count > 0)
                    ViewSphereRadius = sFile.Shape.Volumes[0].Radius;
                else
                    ViewSphereRadius = 100;

                var index = 0;
#if DEBUG_SHAPE_HIERARCHY
                var subObjectIndex = 0;
                SubObjects = (from sub_object obj in MSTSdistance_level.sub_objects
                              select new SubObject(obj, ref index, MSTSdistance_level.distance_level_header.hierarchy, textureFlags, subObjectIndex++, sFile, sharedShape)).ToArray();
#else
                SubObjects = (from Formats.Msts.Models.SubObject obj in MSTSdistance_level.SubObjects
                              select new SubObject(obj, ref index, MSTSdistance_level.DistanceLevelHeader.Hierarchy, textureFlags, sFile, sharedShape)).ToArray();
#endif
                if (SubObjects.Length == 0)
                    throw new InvalidDataException("Shape file missing sub_object");
            }

            internal void Mark()
            {
                foreach (var so in SubObjects)
                    so.Mark();
            }
        }

        public class SubObject
        {
            private static readonly SceneryMaterialOptions[] UVTextureAddressModeMap = new[] {
                SceneryMaterialOptions.TextureAddressModeWrap,
                SceneryMaterialOptions.TextureAddressModeMirror,
                SceneryMaterialOptions.TextureAddressModeClamp,
                SceneryMaterialOptions.TextureAddressModeBorder,
            };
            private static readonly Dictionary<string, SceneryMaterialOptions> ShaderNames = new Dictionary<string, SceneryMaterialOptions> {
                { "Tex", SceneryMaterialOptions.ShaderFullBright },
                { "TexDiff", SceneryMaterialOptions.Diffuse },
                { "BlendATex", SceneryMaterialOptions.AlphaBlendingBlend | SceneryMaterialOptions.ShaderFullBright},
                { "BlendATexDiff", SceneryMaterialOptions.AlphaBlendingBlend | SceneryMaterialOptions.Diffuse },
                { "AddATex", SceneryMaterialOptions.AlphaBlendingAdd | SceneryMaterialOptions.ShaderFullBright},
                { "AddATexDiff", SceneryMaterialOptions.AlphaBlendingAdd | SceneryMaterialOptions.Diffuse },
            };
            private static readonly SceneryMaterialOptions[] VertexLightModeMap = new[] {
                SceneryMaterialOptions.ShaderDarkShade,
                SceneryMaterialOptions.ShaderHalfBright,
                SceneryMaterialOptions.ShaderVegetation, // Not certain this is right.
                SceneryMaterialOptions.ShaderVegetation,
                SceneryMaterialOptions.ShaderFullBright,
                SceneryMaterialOptions.None | SceneryMaterialOptions.Specular750,
                SceneryMaterialOptions.None | SceneryMaterialOptions.Specular25,
                SceneryMaterialOptions.None | SceneryMaterialOptions.None,
            };

            public ShapePrimitive[] ShapePrimitives;

#if DEBUG_SHAPE_HIERARCHY
            public SubObject(sub_object sub_object, ref int totalPrimitiveIndex, int[] hierarchy, Helpers.TextureFlags textureFlags, int subObjectIndex, SFile sFile, SharedShape sharedShape)
#else
            public SubObject(Formats.Msts.Models.SubObject sub_object, ref int totalPrimitiveIndex, int[] hierarchy, Helpers.TextureFlags textureFlags, ShapeFile sFile, SharedShape sharedShape)
#endif
            {
#if DEBUG_SHAPE_HIERARCHY
                var debugShapeHierarchy = new StringBuilder();
                debugShapeHierarchy.AppendFormat("      Sub object {0}:\n", subObjectIndex);
#endif
                var vertexBufferSet = new VertexBufferSet(sub_object, sFile, viewer.RenderProcess.GraphicsDevice);
#if DEBUG_SHAPE_NORMALS
                var debugNormalsMaterial = viewer.MaterialManager.Load("DebugNormals");
#endif

#if OPTIMIZE_SHAPES_ON_LOAD
                var primitiveMaterials = sub_object.primitives.Cast<primitive>().Select((primitive) =>
#else
                var primitiveIndex = 0;
#if DEBUG_SHAPE_NORMALS
                ShapePrimitives = new ShapePrimitive[sub_object.primitives.Count * 2];
#else
                ShapePrimitives = new ShapePrimitive[sub_object.Primitives.Count];
#endif
                foreach (Primitive primitive in sub_object.Primitives)
#endif
                {
                    var primitiveState = sFile.Shape.PrimaryStates[primitive.PrimitiveStateIndex];
                    var vertexState = sFile.Shape.VertexStates[primitiveState.VertexStateIndex];
                    var lightModelConfiguration = sFile.Shape.LightModelConfigs[vertexState.LightConfigIndex];
                    var options = SceneryMaterialOptions.None;

                    // Validate hierarchy position.
                    var hierarchyIndex = vertexState.MatrixIndex;
                    while (hierarchyIndex != -1)
                    {
                        if (hierarchyIndex < 0 || hierarchyIndex >= hierarchy.Length)
                        {
                            var hierarchyList = new List<int>();
                            hierarchyIndex = vertexState.MatrixIndex;
                            while (hierarchyIndex >= 0 && hierarchyIndex < hierarchy.Length)
                            {
                                hierarchyList.Add(hierarchyIndex);
                                hierarchyIndex = hierarchy[hierarchyIndex];
                            }
                            hierarchyList.Add(hierarchyIndex);
                            Trace.TraceWarning("Ignored invalid primitive hierarchy {1} in shape {0}", sharedShape.FilePath, String.Join(" ", hierarchyList.Select(hi => hi.ToString()).ToArray()));
                            break;
                        }
                        hierarchyIndex = hierarchy[hierarchyIndex];
                    }

                    if (lightModelConfiguration.UVOperations.Count > 0)
                        if (lightModelConfiguration.UVOperations[0].TextureAddressMode - 1 >= 0 && lightModelConfiguration.UVOperations[0].TextureAddressMode - 1 < UVTextureAddressModeMap.Length)
                            options |= UVTextureAddressModeMap[lightModelConfiguration.UVOperations[0].TextureAddressMode - 1];
                        else if (!shapeWarnings.Contains("texture_addressing_mode:" + lightModelConfiguration.UVOperations[0].TextureAddressMode))
                        {
                            Trace.TraceInformation("Skipped unknown texture addressing mode {1} first seen in shape {0}", sharedShape.FilePath, lightModelConfiguration.UVOperations[0].TextureAddressMode);
                            shapeWarnings.Add("texture_addressing_mode:" + lightModelConfiguration.UVOperations[0].TextureAddressMode);
                        }

                    if (primitiveState.AlphaTestMode == 1)
                        options |= SceneryMaterialOptions.AlphaTest;

                    if (ShaderNames.ContainsKey(sFile.Shape.ShaderNames[primitiveState.ShaderIndex]))
                        options |= ShaderNames[sFile.Shape.ShaderNames[primitiveState.ShaderIndex]];
                    else if (!shapeWarnings.Contains("shader_name:" + sFile.Shape.ShaderNames[primitiveState.ShaderIndex]))
                    {
                        Trace.TraceInformation("Skipped unknown shader name {1} first seen in shape {0}", sharedShape.FilePath, sFile.Shape.ShaderNames[primitiveState.ShaderIndex]);
                        shapeWarnings.Add("shader_name:" + sFile.Shape.ShaderNames[primitiveState.ShaderIndex]);
                    }

                    if (12 + vertexState.LightMatrixIndex >= 0 && 12 + vertexState.LightMatrixIndex < VertexLightModeMap.Length)
                        options |= VertexLightModeMap[12 + vertexState.LightMatrixIndex];
                    else if (!shapeWarnings.Contains("lighting_model:" + vertexState.LightMatrixIndex))
                    {
                        Trace.TraceInformation("Skipped unknown lighting model index {1} first seen in shape {0}", sharedShape.FilePath, vertexState.LightMatrixIndex);
                        shapeWarnings.Add("lighting_model:" + vertexState.LightMatrixIndex);
                    }

                    if ((textureFlags & Helpers.TextureFlags.Night) != 0)
                        options |= SceneryMaterialOptions.NightTexture;

                    if ((textureFlags & Helpers.TextureFlags.Underground) != 0)
                        options |= SceneryMaterialOptions.UndergroundTexture;

                    Material material;
                    if (primitiveState.TextureIndices.Length != 0)
                    {
                        var texture = sFile.Shape.Textures[primitiveState.TextureIndices[0]];
                        var imageName = sFile.Shape.ImageNames[texture.ImageIndex];
                        if (String.IsNullOrEmpty(sharedShape.ReferencePath))
                            material = viewer.MaterialManager.Load("Scenery", Helpers.GetRouteTextureFile(viewer.Simulator, textureFlags, imageName), (int)options, texture.MipMapLODBias);
                        else
                            material = viewer.MaterialManager.Load("Scenery", Helpers.GetTextureFile(viewer.Simulator, textureFlags, sharedShape.ReferencePath, imageName), (int)options, texture.MipMapLODBias);
                    }
                    else
                    {
                        material = viewer.MaterialManager.Load("Scenery", null, (int)options);
                    }

#if DEBUG_SHAPE_HIERARCHY
                    debugShapeHierarchy.AppendFormat("        Primitive {0,-2}: pstate={1,-2} vstate={2,-2} lstate={3,-2} matrix={4,-2}", primitiveIndex, primitive.prim_state_idx, primitiveState.ivtx_state, vertexState.LightCfgIdx, vertexState.imatrix);
                    var debugMatrix = vertexState.imatrix;
                    while (debugMatrix >= 0)
                    {
                        debugShapeHierarchy.AppendFormat(" {0}", sharedShape.MatrixNames[debugMatrix]);
                        debugMatrix = hierarchy[debugMatrix];
                    }
                    debugShapeHierarchy.Append("\n");
#endif

#if OPTIMIZE_SHAPES_ON_LOAD
                    return new { Key = material.ToString() + "/" + vertexState.imatrix.ToString(), Primitive = primitive, Material = material, HierachyIndex = vertexState.imatrix };
                }).ToArray();
#else
                    if (primitive.IndexedTriList.VertexIndices.Count == 0)
                    {
                        Trace.TraceWarning("Skipped primitive with 0 indices in {0}", sharedShape.FilePath);
                        continue;
                    }

                    var indexData = new List<ushort>(primitive.IndexedTriList.VertexIndices.Count * 3);
                    foreach (VertexIndex vertex_idx in primitive.IndexedTriList.VertexIndices)
                        foreach (var index in new[] { vertex_idx.A, vertex_idx.B, vertex_idx.C })
                            indexData.Add((ushort)index);

                    ShapePrimitives[primitiveIndex] = new ShapePrimitive(material, vertexBufferSet, indexData, viewer.RenderProcess.GraphicsDevice, hierarchy, vertexState.MatrixIndex);
                    ShapePrimitives[primitiveIndex].SortIndex = ++totalPrimitiveIndex;
                    ++primitiveIndex;
#if DEBUG_SHAPE_NORMALS
                    ShapePrimitives[primitiveIndex] = new ShapeDebugNormalsPrimitive(debugNormalsMaterial, vertexBufferSet, indexData, sharedShape.Viewer.RenderProcess.GraphicsDevice, hierarchy, vertexState.imatrix);
                    ShapePrimitives[primitiveIndex].SortIndex = totalPrimitiveIndex;
                    ++primitiveIndex;
#endif
                }
#endif

#if OPTIMIZE_SHAPES_ON_LOAD
                var indexes = new Dictionary<string, List<short>>(sub_object.primitives.Count);
                foreach (var primitiveMaterial in primitiveMaterials)
                {
                    var baseIndex = 0;
                    var indexData = new List<short>(0);
                    if (indexes.TryGetValue(primitiveMaterial.Key, out indexData))
                    {
                        baseIndex = indexData.Count;
                        indexData.Capacity += primitiveMaterial.Primitive.indexed_trilist.vertex_idxs.Count * 3;
                    }
                    else
                    {
                        indexData = new List<short>(primitiveMaterial.Primitive.indexed_trilist.vertex_idxs.Count * 3);
                        indexes.Add(primitiveMaterial.Key, indexData);
                    }

                    var primitiveState = sFile.shape.prim_states[primitiveMaterial.Primitive.prim_state_idx];
                    foreach (vertex_idx vertex_idx in primitiveMaterial.Primitive.indexed_trilist.vertex_idxs)
                    {
                        indexData.Add((short)vertex_idx.a);
                        indexData.Add((short)vertex_idx.b);
                        indexData.Add((short)vertex_idx.c);
                    }
                }

                ShapePrimitives = new ShapePrimitive[indexes.Count];
                var primitiveIndex = 0;
                foreach (var index in indexes)
                {
                    var indexBuffer = new IndexBuffer(sharedShape.Viewer.RenderProcess.GraphicsDevice, typeof(short), index.Value.Count, BufferUsage.WriteOnly);
                    indexBuffer.SetData(index.Value.ToArray());
                    var primitiveMaterial = primitiveMaterials.First(d => d.Key == index.Key);
                    ShapePrimitives[primitiveIndex] = new ShapePrimitive(primitiveMaterial.Material, vertexBufferSet, indexBuffer, index.Value.Min(), index.Value.Max() - index.Value.Min() + 1, index.Value.Count / 3, hierarchy, primitiveMaterial.HierachyIndex);
                    ++primitiveIndex;
                }
                if (sub_object.primitives.Count != indexes.Count)
                    Trace.TraceInformation("{1} -> {2} primitives in {0}", sharedShape.FilePath, sub_object.primitives.Count, indexes.Count);
#else
                if (primitiveIndex < ShapePrimitives.Length)
                    ShapePrimitives = ShapePrimitives.Take(primitiveIndex).ToArray();
#endif

#if DEBUG_SHAPE_HIERARCHY
                Trace.Write(debugShapeHierarchy.ToString());
#endif
            }

            internal void Mark()
            {
                foreach (var prim in ShapePrimitives)
                    prim.Mark();
            }
        }

        public class VertexBufferSet
        {
            public VertexBuffer Buffer;

#if DEBUG_SHAPE_NORMALS
            public VertexBuffer DebugNormalsBuffer;
            public VertexDeclaration DebugNormalsDeclaration;
            public int DebugNormalsVertexCount;
            public const int DebugNormalsVertexPerVertex = 3 * 4;
#endif

            public VertexBufferSet(VertexPositionNormalTexture[] vertexData, GraphicsDevice graphicsDevice)
            {
                Buffer = new VertexBuffer(graphicsDevice, typeof(VertexPositionNormalTexture), vertexData.Length, BufferUsage.WriteOnly);
                Buffer.SetData(vertexData);
            }

#if DEBUG_SHAPE_NORMALS
            public VertexBufferSet(VertexPositionNormalTexture[] vertexData, VertexPositionColor[] debugNormalsVertexData, GraphicsDevice graphicsDevice)
                :this(vertexData, graphicsDevice)
            {
                DebugNormalsVertexCount = debugNormalsVertexData.Length;
                DebugNormalsDeclaration = new VertexDeclaration(graphicsDevice, VertexPositionColor.VertexElements);
                DebugNormalsBuffer = new VertexBuffer(graphicsDevice, typeof(VertexPositionColor), DebugNormalsVertexCount, BufferUsage.WriteOnly);
                DebugNormalsBuffer.SetData(debugNormalsVertexData);
            }
#endif

            public VertexBufferSet(Formats.Msts.Models.SubObject sub_object, ShapeFile sFile, GraphicsDevice graphicsDevice)
#if DEBUG_SHAPE_NORMALS
                : this(CreateVertexData(sub_object, sFile.shape), CreateDebugNormalsVertexData(sub_object, sFile.shape), graphicsDevice)
#else
                : this(CreateVertexData(sub_object, sFile.Shape), graphicsDevice)
#endif
            {
            }

            private static VertexPositionNormalTexture[] CreateVertexData(Formats.Msts.Models.SubObject sub_object, Shape shape)
            {
                // TODO - deal with vertex sets that have various numbers of texture coordinates - ie 0, 1, 2 etc
                return (from Orts.Formats.Msts.Models.Vertex vertex in sub_object.Vertices
                        select XNAVertexPositionNormalTextureFromMSTS(vertex, shape)).ToArray();
            }

            private static VertexPositionNormalTexture XNAVertexPositionNormalTextureFromMSTS(Formats.Msts.Models.Vertex vertex, Shape shape)
            {
                var position = shape.Points[vertex.PointIndex];
                var normal = shape.Normals[vertex.NormalIndex];

                return new VertexPositionNormalTexture()
                {
                    Position = new Vector3(position.X, position.Y, -position.Z),
                    Normal = new Vector3(normal.X, normal.Y, -normal.Z),
                    TextureCoordinate = vertex.VertexUVs.Length > 0 ? shape.UVPoints[vertex.VertexUVs[0]] : new Vector2(0, 0)
                };
            }

#if DEBUG_SHAPE_NORMALS
            static VertexPositionColor[] CreateDebugNormalsVertexData(sub_object sub_object, shape shape)
            {
                var vertexData = new List<VertexPositionColor>();
                foreach (vertex vertex in sub_object.vertices)
                {
                    var position = new Vector3(shape.points[vertex.ipoint].X, shape.points[vertex.ipoint].Y, -shape.points[vertex.ipoint].Z);
                    var normal = new Vector3(shape.normals[vertex.inormal].X, shape.normals[vertex.inormal].Y, -shape.normals[vertex.inormal].Z);
                    var right = Vector3.Cross(normal, Math.Abs(normal.Y) > 0.5 ? Vector3.Left : Vector3.Up);
                    var up = Vector3.Cross(normal, right);
                    right /= 50;
                    up /= 50;
                    vertexData.Add(new VertexPositionColor(position + right, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + normal, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + up, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + up, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + normal, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position - right, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position - right, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + normal, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position - up, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position - up, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + normal, Color.LightGreen));
                    vertexData.Add(new VertexPositionColor(position + right, Color.LightGreen));
                }
                return vertexData.ToArray();
            }
#endif
        }

        public void PrepareFrame(RenderFrame frame, in WorldPosition location, ShapeFlags flags)
        {
            PrepareFrame(frame, location, Matrices, null, flags);
        }

        public void PrepareFrame(RenderFrame frame, in WorldPosition location, Matrix[] animatedXNAMatrices, ShapeFlags flags)
        {
            PrepareFrame(frame, location, animatedXNAMatrices, null, flags);
        }

        public void PrepareFrame(RenderFrame frame, in WorldPosition location, Matrix[] animatedXNAMatrices, bool[] subObjVisible, ShapeFlags flags)
        {
            var lodBias = ((float)viewer.Settings.LODBias / 100 + 1);

            // Locate relative to the camera
            var dTileX = location.TileX - viewer.Camera.TileX;
            var dTileZ = location.TileZ - viewer.Camera.TileZ;
            var mstsLocation = location.Location;
            mstsLocation.X += dTileX * 2048;
            mstsLocation.Z += dTileZ * 2048;
            var xnaDTileTranslation = location.XNAMatrix;
            xnaDTileTranslation.M41 += dTileX * 2048;
            xnaDTileTranslation.M43 -= dTileZ * 2048;

            foreach (var lodControl in LodControls)
            {
                // Start with the furthest away distance, then look for a nearer one in range of the camera.
                var displayDetailLevel = lodControl.DistanceLevels.Length - 1;

                // If this LOD group is not in the FOV, skip the whole LOD group.
                // TODO: This might imair some shadows.
                if (!viewer.Camera.InFov(mstsLocation, lodControl.DistanceLevels[displayDetailLevel].ViewSphereRadius))
                    continue;

                // We choose the distance level (LOD) to display first:
                //   - LODBias = 100 means we always use the highest detail.
                //   - LODBias < 100 means we operate as normal (using the highest detail in-range of the camera) but
                //     scaling it by LODBias.
                //
                // However, for the viewing distance (and view sphere), we use a slightly different calculation:
                //   - LODBias = 100 means we always use the *lowest* detail viewing distance.
                //   - LODBias < 100 means we operate as normal (see above).
                //
                // The reason for this disparity is that LODBias = 100 is special, because it means "always use
                // highest detail", but this by itself is not useful unless we keep using the normal (LODBias-scaled)
                // viewing distance - right down to the lowest detail viewing distance. Otherwise, we'll scale the
                // highest detail viewing distance up by 100% and then the object will just disappear!

                if (viewer.Settings.LODBias == 100)
                    // Maximum detail!
                    displayDetailLevel = 0;
                else if (viewer.Settings.LODBias > -100)
                    // Not minimum detail, so find the correct level (with scaling by LODBias)
                    while ((displayDetailLevel > 0) && viewer.Camera.InRange(mstsLocation, lodControl.DistanceLevels[displayDetailLevel - 1].ViewSphereRadius, lodControl.DistanceLevels[displayDetailLevel - 1].ViewingDistance * lodBias))
                        displayDetailLevel--;

                var displayDetail = lodControl.DistanceLevels[displayDetailLevel];
                var distanceDetail = viewer.Settings.LODBias == 100
                    ? lodControl.DistanceLevels[lodControl.DistanceLevels.Length - 1]
                    : displayDetail;

                // If set, extend the lowest LOD to the maximum viewing distance.
                if (viewer.Settings.LODViewingExtention && displayDetailLevel == lodControl.DistanceLevels.Length - 1)
                    distanceDetail.ViewingDistance = float.MaxValue;

                for (var i = 0; i < displayDetail.SubObjects.Length; i++)
                {
                    var subObject = displayDetail.SubObjects[i];

                    // The 1st subobject (note that index 0 is the main object itself) is hidden during the day if HasNightSubObj is true.
                    if ((subObjVisible != null && !subObjVisible[i]) || (i == 1 && HasNightSubObj && viewer.MaterialManager.sunDirection.Y >= 0))
                        continue;

                    foreach (var shapePrimitive in subObject.ShapePrimitives)
                    {
                        Matrix startingPoint = Matrix.Identity;
                        var hi = shapePrimitive.HierarchyIndex;
                        while (hi >= 0 && hi < shapePrimitive.Hierarchy.Length)
                        {
                            startingPoint = MatrixExtension.Multiply(in startingPoint, in animatedXNAMatrices[hi]);
                            hi = shapePrimitive.Hierarchy[hi];
                        }
                        MatrixExtension.Multiply(in startingPoint, in xnaDTileTranslation, out Matrix xnaMatrix);

                        // TODO make shadows depend on shape overrides

                        var interior = (flags & ShapeFlags.Interior) != 0;
                        frame.AddAutoPrimitive(mstsLocation, distanceDetail.ViewSphereRadius, distanceDetail.ViewingDistance * lodBias, shapePrimitive.Material, shapePrimitive, interior ? RenderPrimitiveGroup.Interior : RenderPrimitiveGroup.World, ref xnaMatrix, flags);
                    }
                }
            }
        }

        public Matrix GetMatrixProduct(int iNode)
        {
            int[] h = LodControls[0].DistanceLevels[0].SubObjects[0].ShapePrimitives[0].Hierarchy;
            Matrix matrix = Matrix.Identity;
            while (iNode != -1)
            {
                matrix *= Matrices[iNode];
                iNode = h[iNode];
            }
            return matrix;
        }

        public int GetParentMatrix(int iNode)
        {
            return LodControls[0].DistanceLevels[0].SubObjects[0].ShapePrimitives[0].Hierarchy[iNode];
        }

        internal void Mark()
        {
            viewer.ShapeManager.Mark(this);
            foreach (var lod in LodControls)
                lod.Mark();
        }
    }

}
