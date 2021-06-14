
using Microsoft.Xna.Framework;

using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    /// <summary>
    /// Individual camera object from the config file
    /// </summary>
    public class Camera
    {
        internal Camera(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("camtype", ()=>{ CameraType = stf.ReadStringBlock(null); CameraControl = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("cameraoffset", ()=>{ stf.ReadVector3Block(STFReader.Units.None, ref cameraOffset); }),
                new STFReader.TokenProcessor("direction", ()=>{ stf.ReadVector3Block(STFReader.Units.None, ref direction); }),
                new STFReader.TokenProcessor("objectoffset", ()=>{ stf.ReadVector3Block(STFReader.Units.None, ref objectOffset); }),
                new STFReader.TokenProcessor("rotationlimit", ()=>{ stf.ReadVector3Block(STFReader.Units.None, ref rotationLimit); }),
                new STFReader.TokenProcessor("description", ()=>{ Description = stf.ReadStringBlock(null); }),
                new STFReader.TokenProcessor("fov", ()=>{ Fov = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("zclip", ()=>{ ZClip = stf.ReadFloatBlock(STFReader.Units.None, null); }),
                new STFReader.TokenProcessor("wagonnum", ()=>{ WagonNumber = stf.ReadIntBlock(null); }),
            });
        }

        private Vector3 cameraOffset, direction, objectOffset, rotationLimit;

        public string CameraType { get; private set; }
        public string CameraControl { get; private set; }
        public ref readonly Vector3 CameraOffset => ref cameraOffset;
        public ref readonly Vector3 Direction => ref direction;
        public float Fov { get; private set; } = 55f;
        public float ZClip { get; private set; } = 0.1f;
        public int WagonNumber { get; private set; } = -1;
        public ref readonly Vector3 ObjectOffset => ref objectOffset;
        public ref readonly Vector3 RotationLimit => ref rotationLimit;
        public string Description { get; private set; } = "";
    }
}
