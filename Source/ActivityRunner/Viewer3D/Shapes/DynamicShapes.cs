using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Common;
using Orts.ActivityRunner.Viewer3D.Sound;
using Orts.Common;
using Orts.Common.Position;
using Orts.Common.Xna;
using Orts.Formats.Msts;
using Orts.Formats.Msts.Models;
using Orts.Simulation;
using Orts.Simulation.RollingStocks;

namespace Orts.ActivityRunner.Viewer3D.Shapes
{
    /// <summary>
    /// Has a heirarchy of objects that can be moved by adjusting the XNAMatrices
    /// at each node.
    /// </summary>
    public class PoseableShape : BaseShape
    {
        private readonly IWorldPosition positionSource;

        static Dictionary<string, bool> SeenShapeAnimationError = new Dictionary<string, bool>();

        public Matrix[] XNAMatrices = new Matrix[0];  // the positions of the subobjects

        public readonly int[] Hierarchy;

        public override ref readonly WorldPosition WorldPosition => ref positionSource.WorldPosition;

        public PoseableShape(string path, IWorldPosition positionSource, ShapeFlags flags) :
            base(path, flags)
        {
            this.positionSource = positionSource;

            XNAMatrices = new Matrix[SharedShape.Matrices.Length];
            for (int iMatrix = 0; iMatrix < SharedShape.Matrices.Length; ++iMatrix)
                XNAMatrices[iMatrix] = SharedShape.Matrices[iMatrix];

            if (SharedShape.LodControls.Length > 0 && SharedShape.LodControls[0].DistanceLevels.Length > 0 && SharedShape.LodControls[0].DistanceLevels[0].SubObjects.Length > 0 && SharedShape.LodControls[0].DistanceLevels[0].SubObjects[0].ShapePrimitives.Length > 0)
                Hierarchy = SharedShape.LodControls[0].DistanceLevels[0].SubObjects[0].ShapePrimitives[0].Hierarchy;
            else
                Hierarchy = new int[0];
        }

        public PoseableShape(string path, IWorldPosition positionSource)
            : this(path, positionSource, ShapeFlags.None)
        {
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }

        /// <summary>
        /// Adjust the pose of the specified node to the frame position specifed by key.
        /// </summary>
        public void AnimateMatrix(int iMatrix, double key)
        {
            // Animate the given matrix.
            AnimateOneMatrix(iMatrix, key);

            // Animate all child nodes in the hierarchy too.
            for (var i = 0; i < Hierarchy.Length; i++)
                if (Hierarchy[i] == iMatrix)
                    AnimateMatrix(i, key);
        }

        private void AnimateOneMatrix(int iMatrix, double key)
        {
            if (SharedShape.Animations == null || SharedShape.Animations.Count == 0)
            {
                if (!SeenShapeAnimationError.ContainsKey(SharedShape.FilePath))
                    Trace.TraceInformation("Ignored missing animations data in shape {0}", SharedShape.FilePath);
                SeenShapeAnimationError[SharedShape.FilePath] = true;
                return;  // animation is missing
            }

            if (iMatrix < 0 || iMatrix >= SharedShape.Animations[0].AnimationNodes.Count || iMatrix >= XNAMatrices.Length)
            {
                if (!SeenShapeAnimationError.ContainsKey(SharedShape.FilePath))
                    Trace.TraceInformation("Ignored out of bounds matrix {1} in shape {0}", SharedShape.FilePath, iMatrix);
                SeenShapeAnimationError[SharedShape.FilePath] = true;
                return;  // mismatched matricies
            }

            var anim_node = SharedShape.Animations[0].AnimationNodes[iMatrix];
            if (anim_node.Controllers.Count == 0)
                return;  // missing controllers

            // Start with the intial pose in the shape file.
            var xnaPose = SharedShape.Matrices[iMatrix];

            foreach (Controller controller in anim_node.Controllers)
            {
                // Determine the frame index from the current frame ('key'). We will be interpolating between two key
                // frames (the items in 'controller') so we need to find the last one LESS than the current frame
                // and interpolate with the one after it.
                var index = 0;
                for (var i = 0; i < controller.Count; i++)
                    if (controller[i].Frame <= key)
                        index = i;
                    else if (controller[i].Frame > key) // Optimisation, not required for algorithm.
                        break;

                var position1 = controller[index];
                var position2 = index + 1 < controller.Count ? controller[index + 1] : controller[index];
                var frame1 = position1.Frame;
                var frame2 = position2.Frame;

                // Make sure to clamp the amount, as we can fall outside the frame range. Also ensure there's a
                // difference between frame1 and frame2 or we'll crash.
                float amount = frame1 < frame2 ? (float)MathHelperD.Clamp((key - frame1) / (frame2 - frame1), 0, 1) : 0;

                if (position1 is SlerpRotation slerp1 && position2 is SlerpRotation slerp2)  // rotate the existing matrix
                {

                    ref readonly Quaternion slerp1Quaternion = ref slerp1.Quaternion;
                    ref readonly Quaternion slerp2Quaternion = ref slerp2.Quaternion;
                    Quaternion q = Quaternion.Slerp(slerp1Quaternion, slerp2Quaternion, amount);
                    Vector3 location = xnaPose.Translation;
                    xnaPose = Matrix.CreateFromQuaternion(q);
                    xnaPose.Translation = location;
                }
                else if (position1 is LinearKey key1 && position2 is LinearKey key2)  // a key sets an absolute position, vs shifting the existing matrix
                {
                    xnaPose.Translation = Vector3.Lerp(key1.Position, key2.Position, amount);
                }
                else if (position1 is TcbKey tcbkey1 && position2 is TcbKey tcbkey2) // a tcb_key sets an absolute rotation, vs rotating the existing matrix
                {
                    ref readonly Quaternion tcb1Quaternion = ref tcbkey1.Quaternion;
                    ref readonly Quaternion tcb2Quaternion = ref tcbkey2.Quaternion;
                    Quaternion q = Quaternion.Slerp(tcb1Quaternion, tcb2Quaternion, amount);
                    Vector3 location = xnaPose.Translation;
                    xnaPose = Matrix.CreateFromQuaternion(q);
                    xnaPose.Translation = location;
                }
            }
            XNAMatrices[iMatrix] = xnaPose;  // update the matrix
        }
    }

    /// <summary>
    /// An animated shape has a continuous repeating motion defined
    /// in the animations of the shape file.
    /// </summary>
    public class AnimatedShape : PoseableShape
    {
        protected double animationKey;  // advances with time
        protected readonly float frameRateMultiplier = 1f; // e.g. in passenger view shapes MSTS divides by 30 the frame rate; this is the inverse

        /// <summary>
        /// Construct and initialize the class
        /// </summary>
        public AnimatedShape(string path, IWorldPosition positionSource, ShapeFlags flags, float frameRateDivisor = 1.0f)
            : base(path, positionSource, flags)
        {
            frameRateMultiplier = 1 / frameRateDivisor;
        }

        public AnimatedShape(string path, IWorldPosition positionSource)
            : this(path, positionSource, ShapeFlags.None)
        {
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            // if the shape has animations
            if (SharedShape.Animations != null && SharedShape.Animations.Count > 0 && SharedShape.Animations[0].FrameCount > 1)
            {
                animationKey += SharedShape.Animations[0].FrameRate * elapsedTime.ClockSeconds * frameRateMultiplier;
                while (animationKey > SharedShape.Animations[0].FrameCount)
                    animationKey -= SharedShape.Animations[0].FrameCount;
                while (animationKey < 0)
                    animationKey += SharedShape.Animations[0].FrameCount;

                // Update the pose for each matrix
                for (var matrix = 0; matrix < SharedShape.Matrices.Length; ++matrix)
                    AnimateMatrix(matrix, animationKey);
            }
            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }
    }

    public class SwitchTrackShape : PoseableShape
    {
        protected double animationKey;  // tracks position of points as they move left and right

        private readonly TrackJunctionNode trackJunctionNode;  // has data on current aligment for the switch
        private readonly uint mainRoute;                  // 0 or 1 - which route is considered the main route

        public SwitchTrackShape(string path, IWorldPosition positionSource, TrackJunctionNode trackJunctionNode)
            : base(path, positionSource, ShapeFlags.AutoZBias)
        {
            this.trackJunctionNode = trackJunctionNode;
            mainRoute = viewer.Simulator.TSectionDat.TrackShapes[trackJunctionNode.ShapeIndex].MainRoute;
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            // ie, with 2 frames of animation, the key will advance from 0 to 1
            if (trackJunctionNode.SelectedRoute == mainRoute)
            {
                if (animationKey > 0.001)
                    animationKey -= 0.002 * elapsedTime.ClockSeconds * 1000.0;
                if (animationKey < 0.001)
                    animationKey = 0;
            }
            else
            {
                if (animationKey < 0.999)
                    animationKey += 0.002 * elapsedTime.ClockSeconds * 1000.0;
                if (animationKey > 0.999)
                    animationKey = 1.0;
            }

            // Update the pose
            for (int iMatrix = 0; iMatrix < SharedShape.Matrices.Length; ++iMatrix)
                AnimateMatrix(iMatrix, animationKey);

            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }
    }

    public class SpeedPostShape : PoseableShape
    {
        private readonly SpeedPostObject speedPostObject;  // has data on current aligment for the switch
        private readonly VertexPositionNormalTexture[] vertices;
        private readonly int numberVertices;
        private readonly int numberIndices;
        private readonly short[] triangleListIndices;// Array of indices to vertices for triangles

        protected readonly double animationKey;  // tracks position of points as they move left and right
        private ShapePrimitive shapePrimitive;

        public SpeedPostShape(string path, IWorldPosition positionSource, SpeedPostObject speedPostObject)
            : base(path, positionSource, ShapeFlags.None)
        {

            this.speedPostObject = speedPostObject;
            int maxVertex = speedPostObject.SignShapes.Count * 48;// every face has max 7 digits, each has 2 triangles
            Material material = viewer.MaterialManager.Load("Scenery", Helpers.GetRouteTextureFile(viewer.Simulator, Helpers.TextureFlags.None, speedPostObject.TextureFile), (int)(SceneryMaterialOptions.None | SceneryMaterialOptions.AlphaBlendingBlend), 0);

            // Create and populate a new ShapePrimitive
            int i = 0;
            float size = speedPostObject.TextSize.Size;
            int idlocation = 0;
            while (idlocation < speedPostObject.TrackItemIds.TrackDbItems.Count)
            {
                int id = speedPostObject.TrackItemIds.TrackDbItems[idlocation];
//                SpeedPostItem item;
                string speed = string.Empty;
                    if (!(viewer.Simulator.TDB.TrackDB.TrackItems[id] is SpeedPostItem item))
                        throw new InvalidCastException(viewer.Simulator.TDB.TrackDB.TrackItems[id].ItemName);  // Error to be handled in Scenery.cs

                //determine what to show: speed or number used in German routes
                if (item.ShowNumber)
                {
                    speed += item.NumberShown;
                    if (!item.ShowDot)
                        speed = speed.Replace(".", "");
                }
                else
                {
                    //determine if the speed is for passenger or freight
                    if (item.IsFreight && !item.IsPassenger)
                        speed += "F";
                    else if (!item.IsFreight && item.IsPassenger)
                        speed += "P";

                    if (item != null)
                        speed += item.Distance;
                }

                vertices = new VertexPositionNormalTexture[maxVertex];
                triangleListIndices = new short[maxVertex / 2 * 3]; // as is NumIndices

                for (i = 0; i < speedPostObject.SignShapes.Count; i++)
                {
                    //start position is the center of the text
                    Vector3 start = new Vector3(speedPostObject.SignShapes[i].X, speedPostObject.SignShapes[i].Y, speedPostObject.SignShapes[i].Z);                    float rotation = speedPostObject.SignShapes[i].W;

                    //find the left-most of text
                    Vector3 offset;
                    if (Math.Abs(speedPostObject.TextSize.Offset.Y) > 0.01)
                        offset = new Vector3(0 - size / 2, 0, 0);
                    else
                        offset = new Vector3(0, 0 - size / 2, 0);

                    offset.X -= speed.Length * speedPostObject.TextSize.Offset.X / 2;
                    offset.Y -= speed.Length * speedPostObject.TextSize.Offset.Y / 2;

                    for (int j = 0; j < speed.Length; j++)
                    {
                        float tX = GetTextureCoordX(speed[j]);
                        float tY = GetTextureCoordY(speed[j]);
                        Matrix rot = Matrix.CreateRotationY(-rotation);

                        //the left-bottom vertex
                        Vector3 v = new Vector3(offset.X, offset.Y, 0.01f);
                        v = Vector3.Transform(v, rot);
                        v += start; Vertex v1 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX, tY);

                        //the right-bottom vertex
                        v.X = offset.X + size; v.Y = offset.Y; v.Z = 0.01f;
                        v = Vector3.Transform(v, rot);
                        v += start; Vertex v2 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX + 0.25f, tY);

                        //the right-top vertex
                        v.X = offset.X + size; v.Y = offset.Y + size; v.Z = 0.01f;
                        v = Vector3.Transform(v, rot);
                        v += start; Vertex v3 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX + 0.25f, tY - 0.25f);

                        //the left-top vertex
                        v.X = offset.X; v.Y = offset.Y + size; v.Z = 0.01f;
                        v = Vector3.Transform(v, rot);
                        v += start; Vertex v4 = new Vertex(v.X, v.Y, v.Z, 0, 0, -1, tX, tY - 0.25f);

                        //memory may not be enough
                        if (numberVertices > maxVertex - 4)
                        {
                            VertexPositionNormalTexture[] TempVertexList = new VertexPositionNormalTexture[maxVertex + 128];
                            short[] TempTriangleListIndices = new short[(maxVertex + 128) / 2 * 3]; // as is NumIndices
                            for (var k = 0; k < maxVertex; k++)
                                TempVertexList[k] = vertices[k];
                            for (var k = 0; k < maxVertex / 2 * 3; k++)
                                TempTriangleListIndices[k] = triangleListIndices[k];
                            triangleListIndices = TempTriangleListIndices;
                            vertices = TempVertexList;
                            maxVertex += 128;
                        }

                        //create first triangle
                        triangleListIndices[numberIndices++] = (short)numberVertices;
                        triangleListIndices[numberIndices++] = (short)(numberVertices + 2);
                        triangleListIndices[numberIndices++] = (short)(numberVertices + 1);
                        // Second triangle:
                        triangleListIndices[numberIndices++] = (short)numberVertices;
                        triangleListIndices[numberIndices++] = (short)(numberVertices + 3);
                        triangleListIndices[numberIndices++] = (short)(numberVertices + 2);

                        //create vertex
                        vertices[numberVertices].Position = v1.Position; vertices[numberVertices].Normal = v1.Normal; vertices[numberVertices].TextureCoordinate = v1.TexCoord;
                        vertices[numberVertices + 1].Position = v2.Position; vertices[numberVertices + 1].Normal = v2.Normal; vertices[numberVertices + 1].TextureCoordinate = v2.TexCoord;
                        vertices[numberVertices + 2].Position = v3.Position; vertices[numberVertices + 2].Normal = v3.Normal; vertices[numberVertices + 2].TextureCoordinate = v3.TexCoord;
                        vertices[numberVertices + 3].Position = v4.Position; vertices[numberVertices + 3].Normal = v4.Normal; vertices[numberVertices + 3].TextureCoordinate = v4.TexCoord;
                        numberVertices += 4;
                        offset.X += speedPostObject.TextSize.Offset.X; offset.Y += speedPostObject.TextSize.Offset.Y; //move to next digit
                    }

                }
                idlocation++;
            }
            //create the shape primitive
            short[] newTList = new short[numberIndices];
            for (i = 0; i < numberIndices; i++)
                newTList[i] = triangleListIndices[i];
            VertexPositionNormalTexture[] newVList = new VertexPositionNormalTexture[numberVertices];
            for (i = 0; i < numberVertices; i++)
                newVList[i] = vertices[i];
            IndexBuffer indexBuffer = new IndexBuffer(viewer.RenderProcess.GraphicsDevice, typeof(short),
                                                            numberIndices, BufferUsage.WriteOnly);
            indexBuffer.SetData(newTList);
            shapePrimitive = new ShapePrimitive(viewer.RenderProcess.GraphicsDevice, material, new SharedShape.VertexBufferSet(newVList, viewer.RenderProcess.GraphicsDevice), indexBuffer, 0, numberVertices, numberIndices / 3, new[] { -1 }, 0);

        }

        static float GetTextureCoordX(char c)
        {
            float x;
            switch(c)
            {
                case '.':
                    x = 0f; break;
                case 'P':
                    x = 0.5f; break;
                case 'F':
                    x = 0.75f; break;
                default:
                    x = (c - '0') % 4 * 0.25f; break;
            }
            Debug.Assert(x <= 1);
            Debug.Assert(x >= 0);
            return x;
        }

        static float GetTextureCoordY(char c)
        {
            switch (c)
            {
                case '0': case '1': case '2': case '3':
                    return 0.25f;
                case '4': case '5': case '6': case '7':
                    return 0.5f;
                 case '8': case '9': case 'P': case 'F':
                    return 0.75f;
                default:
                    return 1f;
           }
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            // Offset relative to the camera-tile origin
            int dTileX = WorldPosition.TileX - viewer.Camera.TileX;
            int dTileZ = WorldPosition.TileZ - viewer.Camera.TileZ;
            Vector3 tileOffsetWrtCamera = new Vector3(dTileX * 2048, 0, -dTileZ * 2048);

            // Initialize xnaXfmWrtCamTile to object-tile to camera-tile translation:
            MatrixExtension.Multiply(WorldPosition.XNAMatrix, Matrix.CreateTranslation(tileOffsetWrtCamera), out Matrix xnaXfmWrtCamTile);
            // (Transformation is now with respect to camera-tile origin)

            // TODO: Make this use AddAutoPrimitive instead.
            frame.AddPrimitive(shapePrimitive.Material, shapePrimitive, RenderPrimitiveGroup.World, ref xnaXfmWrtCamTile, ShapeFlags.None);

            // Update the pose
            for (int iMatrix = 0; iMatrix < SharedShape.Matrices.Length; ++iMatrix)
                AnimateMatrix(iMatrix, animationKey);

            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }

        internal override void Mark()
        {
            shapePrimitive.Mark();
            base.Mark();
        }
    } // class SpeedPostShape

    public class LevelCrossingShape : PoseableShape
    {
        private readonly LevelCrossingObject levelCrossingObject;
        private readonly SoundSource soundSource;
        private readonly LevelCrossing levelCrossing;

        private readonly float animationFrames;
        private readonly float animationSpeed;
        private bool opening = true;
        private double animationKey;

        public LevelCrossingShape(string path, IWorldPosition positionSource, ShapeFlags shapeFlags, LevelCrossingObject crossingObj)
            : base(path, positionSource, shapeFlags)
        {
            levelCrossingObject = crossingObj;
            if (!levelCrossingObject.Silent)
            {
                string soundFileName = null;
                if (!string.IsNullOrEmpty(levelCrossingObject.SoundFileName))
                    soundFileName = levelCrossingObject.SoundFileName;
                else if (!string.IsNullOrEmpty(SharedShape.SoundFileName))
                    soundFileName = SharedShape.SoundFileName;
                else if (!string.IsNullOrEmpty(viewer.Simulator.TRK.Route.DefaultCrossingSMS))
                    soundFileName = viewer.Simulator.TRK.Route.DefaultCrossingSMS;
                if (!string.IsNullOrEmpty(soundFileName))
                {
                    var soundPath = viewer.Simulator.RoutePath + @"\\sound\\" + soundFileName;
                    try
                    {
                        soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.Crossing, soundPath);
                        viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                    }
                    catch
                    {
                        soundPath = viewer.Simulator.BasePath + @"\\sound\\" + soundFileName;
                        try
                        {
                            soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.Crossing, soundPath);
                            viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                        }
                        catch (Exception error)
                        {
                            Trace.WriteLine(new FileLoadException(soundPath, error));
                        }
                    }
                }
            }
            levelCrossing = viewer.Simulator.LevelCrossings.CreateLevelCrossing(WorldPosition,
                levelCrossingObject.TrackItemIds.TrackDbItems, levelCrossingObject.TrackItemIds.RoadDbItems,
                levelCrossingObject.WarningTime, levelCrossingObject.MinimumDistance);
            // If there are no animations, we leave the frame count and speed at 0 and nothing will try to animate.
            if (SharedShape.Animations != null && SharedShape.Animations.Count > 0)
            {
                // LOOPED COSSINGS (animTiming < 0)
                //     MSTS plays through all the frames of the animation for "closed" and sits on frame 0 for "open". The
                //     speed of animation is the normal speed (frame rate at 30FPS) scaled by the timing value. Since the
                //     timing value is negative, the animation actually plays in reverse.
                // NON-LOOPED CROSSINGS (animTiming > 0)
                //     MSTS plays through the first 1.0 seconds of the animation forwards for closing and backwards for
                //     opening. The number of frames defined doesn't matter; the animation is limited by time so the frame
                //     rate (based on 30FPS) is what's needed.
                animationFrames = levelCrossingObject.AnimationTiming < 0 ? SharedShape.Animations[0].FrameCount : SharedShape.Animations[0].FrameRate / 30f;
                animationSpeed = SharedShape.Animations[0].FrameRate / 30f / levelCrossingObject.AnimationTiming;
            }
        }

        public override void Unload()
        {
            if (soundSource != null)
            {
                viewer.SoundProcess.RemoveSoundSources(this);
                soundSource.Dispose();
            }
            base.Unload();
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if (!levelCrossingObject.Visible)
                return;

            if (opening == levelCrossing.HasTrain)
            {
                opening = !levelCrossing.HasTrain;
                    soundSource?.HandleEvent(opening ? TrainEvent.CrossingOpening : TrainEvent.CrossingClosing);
            }

            if (opening)
                animationKey -= (float)elapsedTime.ClockSeconds * animationSpeed;
            else
                animationKey += (float)elapsedTime.ClockSeconds * animationSpeed;

            if (levelCrossingObject.AnimationTiming < 0)
            {
                // Stick to frame 0 for "open" and loop for "closed".
                if (opening)
                    animationKey = 0;
                if (animationKey < 0)
                    animationKey += animationFrames;
            }
            if (animationKey < 0)
                animationKey = 0;
            if (animationKey > animationFrames)
                animationKey = animationFrames;

            for (var i = 0; i < SharedShape.Matrices.Length; ++i)
                AnimateMatrix(i, animationKey);

            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }
    }

    public class HazardShape : PoseableShape
    {
        readonly HazardObject hazardObject;
        readonly Hazzard hazard;

        private readonly int animationFrames;
        private double moved = 0f;
        private double animationKey;
        private double delayHazAnimation;

        public static HazardShape CreateHazzard(string path, IWorldPosition positionSource, ShapeFlags shapeFlags, HazardObject hazardObject)
        {
            var h = viewer.Simulator.HazzardManager.AddHazzardIntoGame(hazardObject.ItemId, hazardObject.FileName);
            if (h == null)
                return null;
            return new HazardShape(viewer.Simulator.BasePath + @"\Global\Shapes\" + h.HazFile.Hazard.FileName + "\0" + viewer.Simulator.BasePath + @"\Global\Textures", positionSource, shapeFlags, hazardObject, h);

        }

        public HazardShape(string path, IWorldPosition positionSource, ShapeFlags shapeFlags, HazardObject hazardObject, Hazzard h)
            : base(path, positionSource, shapeFlags)
        {
            this.hazardObject = hazardObject;
            hazard = h;
            animationFrames = SharedShape.Animations[0].FrameCount;
        }

        public override void Unload()
        {
            viewer.Simulator.HazzardManager.RemoveHazzardFromGame(hazardObject.ItemId);
            base.Unload();
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            if (hazard == null)
                return;
            Vector2 currentRange;
            animationKey += elapsedTime.ClockSeconds * 24;
            delayHazAnimation += elapsedTime.ClockSeconds;
            switch (hazard.state)
            {
                case Hazzard.State.Idle1:
                    currentRange = hazard.HazFile.Hazard.IdleKey; break;
                case Hazzard.State.Idle2:
                    currentRange = hazard.HazFile.Hazard.IdleKey2; break;
                case Hazzard.State.LookLeft:
                    currentRange = hazard.HazFile.Hazard.SurpriseKeyLeft; break;
                case Hazzard.State.LookRight:
                    currentRange = hazard.HazFile.Hazard.SurpriseKeyRight; break;
                case Hazzard.State.Scared:
                default:
                    currentRange = hazard.HazFile.Hazard.SuccessScarperKey;
                    if (moved < hazard.HazFile.Hazard.Distance)
                    {
                        var m = hazard.HazFile.Hazard.Speed * elapsedTime.ClockSeconds;
                        moved += m;
                        // Shape's position isn't stored but only calculated dynamically as it's passed to PrepareFrame further down
                        // this seems acceptable as the number of Hazardous objects is rather small
                        //WorldPosition.SetLocation(HazardObj.Position.X, HazardObj.Position.Y, HazardObj.Position.Z);
                        hazardObject.UpdatePosition((float)m);
                    }
                    else
                    {
                        moved = 0;
                        hazard.state = Hazzard.State.Idle1;
                    }
                    break;
            }

            switch (hazard.state)
            {
                case Hazzard.State.Idle1:
                case Hazzard.State.Idle2:
                    if (delayHazAnimation > 5.0)
                    {
                        if (animationKey < currentRange.X)
                        {
                            animationKey = currentRange.X;
                            delayHazAnimation = 0;
                        }

                        if (animationKey > currentRange.Y)
                        {
                            animationKey = currentRange.Y;
                            delayHazAnimation = 0;
                        }
                    }
                    break;
                case Hazzard.State.LookLeft:
                case Hazzard.State.LookRight:
                    if (animationKey < currentRange.X)
                        animationKey = currentRange.X;
                    if (animationKey > currentRange.Y)
                        animationKey = currentRange.Y;
                    break;
                case Hazzard.State.Scared:
                    if (animationKey < currentRange.X)
                        animationKey = currentRange.X;
                    if (animationKey > currentRange.Y)
                        animationKey = currentRange.Y;
                    break;
            }

            for (var i = 0; i < SharedShape.Matrices.Length; ++i)
                AnimateMatrix(i, animationKey);

            //SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
//            SharedShape.PrepareFrame(frame, WorldPosition.SetMstsTranslation(hazardObject.Position.X, hazardObject.Position.Y, hazardObject.Position.Z), XNAMatrices, Flags);
            SharedShape.PrepareFrame(frame, hazardObject.WorldPosition, XNAMatrices, Flags);
        }
    }

    public class FuelPickupItemShape : PoseableShape
    {
        private readonly PickupObject fuelPickupItemObject;
        private readonly FuelPickupItem fuelPickupItem;
        private readonly SoundSource soundSource;
        private readonly float frameRate;

        private readonly int animationFrames;
        protected double animationKey;


        public FuelPickupItemShape(string path, IWorldPosition positionSource, ShapeFlags shapeFlags, PickupObject fuelpickupitemObj)
            : base(path, positionSource, shapeFlags)
        {
            fuelPickupItemObject = fuelpickupitemObj;


            if (viewer.Simulator.TRK.Route.DefaultDieselTowerSMS != null && fuelPickupItemObject.PickupType == PickupType.FuelDiesel) // Testing for Diesel PickupType
            {
                var soundPath = viewer.Simulator.RoutePath + @"\\sound\\" + viewer.Simulator.TRK.Route.DefaultDieselTowerSMS;
                try
                {
                    soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.FuelTower, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                }
                catch
                {
                    soundPath = viewer.Simulator.BasePath + @"\\sound\\" + viewer.Simulator.TRK.Route.DefaultDieselTowerSMS;
                    try
                    {
                        soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.FuelTower, soundPath);
                        viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                    }
                    catch (Exception error)
                    {
                        Trace.WriteLine(new FileLoadException(soundPath, error));
                    }
                }
            }
            if (viewer.Simulator.TRK.Route.DefaultWaterTowerSMS != null && fuelPickupItemObject.PickupType == PickupType.FuelWater) // Testing for Water PickupType
            {
                var soundPath = viewer.Simulator.RoutePath + @"\\sound\\" + viewer.Simulator.TRK.Route.DefaultWaterTowerSMS;
                try
                {
                    soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.FuelTower, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                }
                catch
                {
                    soundPath = viewer.Simulator.BasePath + @"\\sound\\" + viewer.Simulator.TRK.Route.DefaultWaterTowerSMS;
                    try
                    {
                        soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.FuelTower, soundPath);
                        viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                    }
                    catch (Exception error)
                    {
                        Trace.WriteLine(new FileLoadException(soundPath, error));
                    }
                }
            }
            if (viewer.Simulator.TRK.Route.DefaultCoalTowerSMS != null && (fuelPickupItemObject.PickupType == PickupType.FuelCoal || fuelPickupItemObject.PickupType == PickupType.FreightCoal))
            {
                var soundPath = viewer.Simulator.RoutePath + @"\\sound\\" + viewer.Simulator.TRK.Route.DefaultCoalTowerSMS;
                try
                {
                    soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.FuelTower, soundPath);
                    viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                }
                catch
                {
                    soundPath = viewer.Simulator.BasePath + @"\\sound\\" + viewer.Simulator.TRK.Route.DefaultCoalTowerSMS;
                    try
                    {
                        soundSource = new SoundSource(viewer, WorldPosition.WorldLocation, SoundEventSource.FuelTower, soundPath);
                        viewer.SoundProcess.AddSoundSources(this, new List<SoundSourceBase>() { soundSource });
                    }
                    catch (Exception error)
                    {
                        Trace.WriteLine(new FileLoadException(soundPath, error));
                    }
                }
            }
            fuelPickupItem = viewer.Simulator.FuelManager.CreateFuelStation(WorldPosition, fuelPickupItemObject.TrackItemIds.TrackDbItems);
            animationFrames = 1;
            frameRate = 1;
            if (SharedShape.Animations != null && SharedShape.Animations.Count > 0 && SharedShape.Animations[0].AnimationNodes != null && SharedShape.Animations[0].AnimationNodes.Count > 0)
            {
                frameRate = SharedShape.Animations[0].FrameCount / fuelPickupItemObject.Options.AnimationSpeed;
                foreach (var anim_node in SharedShape.Animations[0].AnimationNodes)
                    if (anim_node.Name == "ANIMATED_PARTS")
                    {
                        animationFrames = SharedShape.Animations[0].FrameCount;
                        break;
                    }
            }
        }

        public override void Unload()
        {
            if (soundSource != null)
            {
                viewer.SoundProcess.RemoveSoundSources(this);
                soundSource.Dispose();
            }
            base.Unload();
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {

            // 0 can be used as a setting for instant animation.
            if (fuelPickupItem.ReFill() && fuelPickupItemObject.UiD == MSTSWagon.RefillProcess.ActivePickupObjectUID)
            {
                if (animationKey == 0 && soundSource != null) soundSource.HandleEvent(TrainEvent.FuelTowerDown);
                if (fuelPickupItemObject.Options.AnimationSpeed == 0) animationKey = 1.0f;
                else if (animationKey < animationFrames)
                    animationKey += elapsedTime.ClockSeconds * frameRate;
            }

            if (!fuelPickupItem.ReFill() && animationKey > 0)
            {
                if (animationKey == animationFrames && soundSource != null)
                {
                    soundSource.HandleEvent(TrainEvent.FuelTowerTransferEnd);
                    soundSource.HandleEvent(TrainEvent.FuelTowerUp);
                }
                animationKey -= elapsedTime.ClockSeconds * frameRate;
            }

            if (animationKey < 0)
            {
                animationKey = 0;
            }
            if (animationKey > animationFrames)
            {
                animationKey = animationFrames;
                if (soundSource != null) soundSource.HandleEvent(TrainEvent.FuelTowerTransferStart);
            }

            for (var i = 0; i < SharedShape.Matrices.Length; ++i)
                AnimateMatrix(i, animationKey);

            SharedShape.PrepareFrame(frame, WorldPosition, XNAMatrices, Flags);
        }
    } // End Class FuelPickupItemShape

    public class RoadCarShape : AnimatedShape
    {
        public RoadCarShape(string path, IWorldPosition positionSource)
            : base(path, positionSource, ShapeFlags.ShadowCaster)
        {
        }
    }

}
