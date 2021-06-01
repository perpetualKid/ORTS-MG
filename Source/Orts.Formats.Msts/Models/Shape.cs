using System;
using System.Collections.Generic;

using Microsoft.Xna.Framework;

using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class ShapeDescriptor
    {
        public string Name { get; private set; }
        public int EsdDetailLevel { get; private set; }
        public int EsdAlternativeTexture { get; private set; }
        public EsdBoundingBox EsdBoundingBox { get; private set; }
        public bool EsdNoVisualObstruction { get; private set; }
        public bool EsdSnapable { get; private set; }
        public bool EsdSubObject { get; private set; }
        public string EsdSoundFileName { get; private set; } = string.Empty;
        public float EsdBellAnimationFps { get; private set; } = 8;

        public ShapeDescriptor()
        {
            EsdBoundingBox = new EsdBoundingBox();
        }

        public ShapeDescriptor(STFReader stf)
        {
            Name = stf.ReadString(); // Ignore the filename string. TODO: Check if it agrees with the SD file name? Is this important?
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("esd_detail_level", ()=>{ EsdDetailLevel = stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("esd_alternative_texture", ()=>{ EsdAlternativeTexture = stf.ReadIntBlock(null); }),
                    new STFReader.TokenProcessor("esd_no_visual_obstruction", ()=>{ EsdNoVisualObstruction = stf.ReadBoolBlock(true); }),
                    new STFReader.TokenProcessor("esd_snapable", ()=>{ EsdSnapable = stf.ReadBoolBlock(true); }),
                    new STFReader.TokenProcessor("esd_subobj", ()=>{ EsdSubObject = true; stf.SkipBlock(); }),
                    new STFReader.TokenProcessor("esd_bounding_box", ()=>{
                        EsdBoundingBox = new EsdBoundingBox(stf);
                        if (EsdBoundingBox.Min == null || EsdBoundingBox.Max == null)  // ie quietly handle ESD_Bounding_Box()
                            EsdBoundingBox = null;
                    }),
                    new STFReader.TokenProcessor("esd_ortssoundfilename", ()=>{ EsdSoundFileName = stf.ReadStringBlock(null); }),
                    new STFReader.TokenProcessor("esd_ortsbellanimationfps", ()=>{ EsdBellAnimationFps = stf.ReadFloatBlock(STFReader.Units.Frequency, null); }),
                });
            // TODO - some objects have no bounding box - ie JP2BillboardTree1.sd
            //if (ESD_Bounding_Box == null) throw new STFException(stf, "Missing ESD_Bound_Box statement");
        }
    }

    public class EsdBoundingBox
    {
        private Vector3 min;
        private Vector3 max;

        public ref readonly Vector3 Min => ref min;
        public ref readonly Vector3 Max => ref max;

        public EsdBoundingBox() // default used for files with no SD file
        {
            min = new Vector3();
            max = new Vector3();
        }

        public EsdBoundingBox(STFReader stf)
        {
            stf.MustMatchBlockStart();
            string item = stf.ReadString();
            if (item == ")") return;    // quietly return on ESD_Bounding_Box()
            stf.StepBackOneItem();
            min = new Vector3(stf.ReadFloat(null), stf.ReadFloat(null), stf.ReadFloat(null));
            max = new Vector3(stf.ReadFloat(null), stf.ReadFloat(null), stf.ReadFloat(null));
            // JP2indirt.sd has extra parameters
            stf.SkipRestOfBlock();
        }

    }

    public class Shape
    {
        public ShapeHeader ShapeHeader { get; private set; }
        public Volumes Volumes { get; private set; }
        public ShaderNames ShaderNames { get; private set; }
        public TextureFilterNames TextureFilterNames { get; private set; }
        public Points Points { get; private set; }
        public UVPoints UVPoints { get; private set; }
        public Normals Normals { get; private set; }
        public SortVectors SortVectors { get; private set; }
        public Colors Colors { get; private set; }
        public Matrices Matrices { get; private set; }
        public ImageNames ImageNames { get; private set; }
        public Textures Textures { get; private set; }
        public LightMaterials LightMaterials { get; private set; }
        public LightModelConfigs LightModelConfigs { get; private set; }
        public VertexStates VertexStates { get; private set; }
        public PrimaryStates PrimaryStates { get; private set; }
        public LodControls LodControls { get; private set; }
        public Animations Animations { get; private set; }

        public Shape(SBR block)
        {
            block.VerifyID(TokenID.Shape);
            ShapeHeader = new ShapeHeader(block.ReadSubBlock());
            Volumes = new Volumes(block.ReadSubBlock());
            ShaderNames = new ShaderNames(block.ReadSubBlock());
            TextureFilterNames = new TextureFilterNames(block.ReadSubBlock());
            Points = new Points(block.ReadSubBlock());
            UVPoints = new UVPoints(block.ReadSubBlock());
            Normals = new Normals(block.ReadSubBlock());
            SortVectors = new SortVectors(block.ReadSubBlock());
            Colors = new Colors(block.ReadSubBlock());
            Matrices = new Matrices(block.ReadSubBlock());
            ImageNames = new ImageNames(block.ReadSubBlock());
            Textures = new Textures(block.ReadSubBlock());
            LightMaterials = new LightMaterials(block.ReadSubBlock());
            LightModelConfigs = new LightModelConfigs(block.ReadSubBlock());
            VertexStates = new VertexStates(block.ReadSubBlock());
            PrimaryStates = new PrimaryStates(block.ReadSubBlock());
            LodControls = new LodControls(block.ReadSubBlock());
            if (!block.EndOfBlock())
                Animations = new Animations(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class ShapeHeader
    {
        public uint Flags1 { get; private set; }
        public uint Flags2 { get; private set; }

        public ShapeHeader(SBR block)
        {
            block.VerifyID(TokenID.Shape_Header);
            Flags1 = block.ReadFlags();
            if (!block.EndOfBlock())
                Flags2 = block.ReadFlags();
            block.VerifyEndOfBlock();
        }
    }

    public class Volumes : List<VolumeSphere>
    {
        public Volumes(SBR block)
        {
            block.VerifyID(TokenID.Volumes);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new VolumeSphere(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class VolumeSphere
    {
        public Vector3 Vector { get; private set; }
        public float Radius { get; private set; }

        public VolumeSphere(SBR block)
        {
            block.VerifyID(TokenID.Vol_Sphere);
            var vectorBlock = block.ReadSubBlock();
            Vector = new Vector3(vectorBlock.ReadFloat(), vectorBlock.ReadFloat(), vectorBlock.ReadFloat());
            vectorBlock.VerifyEndOfBlock();
            Radius = block.ReadFloat();
            block.VerifyEndOfBlock();
        }
    }

    public class ShaderNames : List<string>
    {
        public ShaderNames(SBR block)
        {
            block.VerifyID(TokenID.Shader_Names);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Named_Shader);
                Add(subBlock.ReadString());
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class TextureFilterNames : List<string>
    {
        public TextureFilterNames(SBR block)
        {
            block.VerifyID(TokenID.Texture_Filter_Names);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Named_Filter_Mode);
                Add(subBlock.ReadString());
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class Points : List<Vector3>
    {
        public Points(SBR block)
        {
            block.VerifyID(TokenID.Points);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Point);
                Add(new Vector3(subBlock.ReadFloat(), subBlock.ReadFloat(), subBlock.ReadFloat()));
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class UVPoints : List<Vector2>
    {
        public UVPoints(SBR block)
        {
            block.VerifyID(TokenID.UV_Points);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.UV_Point);
                Add(new Vector2(subBlock.ReadFloat(), subBlock.ReadFloat()));
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class Normals : List<Vector3>
    {
        public Normals(SBR block)
        {
            block.VerifyID(TokenID.Normals);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Vector);
                Add(new Vector3(subBlock.ReadFloat(), subBlock.ReadFloat(), subBlock.ReadFloat()));
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class SortVectors: List<Vector3>
    {
        public SortVectors(SBR block)
        {
            block.VerifyID(TokenID.Sort_Vectors);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Vector);
                Add(new Vector3(subBlock.ReadFloat(), subBlock.ReadFloat(), subBlock.ReadFloat()));
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class Colors : List<Color>
    {
        public Colors(SBR block)
        {
            block.VerifyID(TokenID.Colours);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Colour);

                float alpha = subBlock.ReadFloat();
                Add(new Color(subBlock.ReadFloat(), subBlock.ReadFloat(), subBlock.ReadFloat(), alpha));
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class Matrices : List<Matrix>
    {
        public List<string> MatrixNames { get; } = new List<string>();

        public Matrices(SBR block)
        {
            block.VerifyID(TokenID.Matrices);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(ReadMatrix(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }

        private Matrix ReadMatrix(SBR block)
        {
            block.VerifyID(TokenID.Matrix);
            MatrixNames.Add(string.IsNullOrEmpty(block.Label) ? string.Empty : block.Label.ToUpperInvariant());

            Matrix result = new Matrix(
                block.ReadFloat(), block.ReadFloat(), - block.ReadFloat(), 0.0f,
                block.ReadFloat(), block.ReadFloat(), - block.ReadFloat(), 0.0f,
                - block.ReadFloat(), - block.ReadFloat(), block.ReadFloat(), 0.0f,
                block.ReadFloat(), block.ReadFloat(), - block.ReadFloat(), 1.0f);
            block.VerifyEndOfBlock();
            return result;
        }    
    }

    public class ImageNames : List<string>
    {
        public ImageNames(SBR block)
        {
            block.VerifyID(TokenID.Images);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Image);
                Add(subBlock.ReadString());
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class Textures : List<Texture>
    {
        public Textures(SBR block)
        {
            block.VerifyID(TokenID.Textures);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new Texture(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class Texture
    {
        /*texture                 ==> :uint,ImageIdx :uint,FilterMode :float,MipMapLODBias [:dword,BorderColor] .
				// Provides attributes for each image
				eg	texture ( 1 0 -3 ff000000 )

				MipMapLODBias  -3  fixes blurring, 0  can cause some texture blurring
         */
        public int ImageIndex { get; private set; }
        public int FilterMode { get; private set; }
        public float MipMapLODBias { get; private set; }
        public uint BorderColor { get; private set; }

        public Texture(SBR block)
        {
            block.VerifyID(TokenID.Texture);
            ImageIndex = block.ReadInt();
            FilterMode = block.ReadInt();
            MipMapLODBias = block.ReadFloat();
            if (!block.EndOfBlock())
                BorderColor = block.ReadFlags();
            block.VerifyEndOfBlock();
        }

        public Texture(int imageIndex)
        {
            ImageIndex = imageIndex;
            FilterMode = 0;
            MipMapLODBias = -3;
            BorderColor = 0xff000000U;
        }
    }

    public class LightMaterials : List<LightMaterial>
    {
        public LightMaterials(SBR block)
        {
            block.VerifyID(TokenID.Light_Materials);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new LightMaterial(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class LightMaterial
    {
        /*light_material          ==> :dword,flags :uint,DiffColIdx :uint,AmbColIdx :uint,SpecColIdx :uint,EmissiveColIdx :float,SpecPower .
				// Never seen it used
				eg	light_materials ( 0 )
         */
        public uint Flags { get; private set; }
        public int DiffuseColorIndex { get; private set; }
        public int AmbientColorIndex { get; private set; }
        public int SpecularColorIndex { get; private set; }
        public int EmissiveColorIndex { get; private set; }
        public float SpecPower { get; private set; }

        public LightMaterial(SBR block)
        {
            block.VerifyID(TokenID.Light_Material);
            Flags = block.ReadFlags();
            DiffuseColorIndex = block.ReadInt();
            AmbientColorIndex = block.ReadInt();
            SpecularColorIndex = block.ReadInt();
            EmissiveColorIndex = block.ReadInt();
            SpecPower = block.ReadFloat();
            block.VerifyEndOfBlock();
        }
    }

    public class LightModelConfigs : List<LightModelConfig>
    {
        public LightModelConfigs(SBR block)
        {
            block.VerifyID(TokenID.Light_Model_Cfgs);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new LightModelConfig(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class LightModelConfig
    {
        public uint Flags { get; private set; }
        public UVOperations UVOperations { get; private set; }

        public LightModelConfig(SBR block)
        {
            block.VerifyID(TokenID.Light_Model_Cfg);
            Flags = block.ReadFlags();
            UVOperations = new UVOperations(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class UVOperations : List<UVOperation>
    {
        public UVOperations(SBR block)
        {
            block.VerifyID(TokenID.UV_Ops);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                switch (subBlock.ID)
                {
                    case TokenID.UV_OP_Copy: Add(new UVOperationCopy(subBlock)); break;
                    case TokenID.UV_Op_ReflectMapFull: Add(new UVOperationReflectMapFull(subBlock)); break;
                    case TokenID.UV_Op_Reflectmap: Add(new UVOperationReflectMap(subBlock)); break;
                    case TokenID.UV_Op_UniformScale: this.Add(new UVOperationUniformScale(subBlock)); break;
                    case TokenID.UV_Op_NonUniformScale: this.Add(new UVOperationNonUniformScale(subBlock)); break;
                    default: throw new System.Exception("Unexpected uv_op: " + subBlock.ID.ToString());
                }
            }
            block.VerifyEndOfBlock();
        }
    }

    public abstract class UVOperation
    {
        public int TextureAddressMode { get; protected set; }
    }

    // TODO  Add a bunch more uv_ops

    public class UVOperationCopy : UVOperation
    {
        public int SourceUVIndex { get; private set; }

        public UVOperationCopy(SBR block)
        {
            block.VerifyID(TokenID.UV_OP_Copy);
            TextureAddressMode = block.ReadInt();
            SourceUVIndex = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class UVOperationReflectMapFull : UVOperation
    {
        public UVOperationReflectMapFull(SBR block)
        {
            block.VerifyID(TokenID.UV_Op_ReflectMapFull);
            TextureAddressMode = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class UVOperationReflectMap : UVOperation
    {
        public UVOperationReflectMap(SBR block)
        {
            block.VerifyID(TokenID.UV_Op_Reflectmap);
            TextureAddressMode = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class UVOperationUniformScale : UVOperation
    {
        public int SourceUVIndex { get; private set; }
        public float UnknownParameter3 { get; private set; }
        public float UnknownParameter4 { get; private set; }

        public UVOperationUniformScale(SBR block)
        {
            block.VerifyID(TokenID.UV_Op_UniformScale);
            TextureAddressMode = block.ReadInt();
            SourceUVIndex = block.ReadInt();
            UnknownParameter3 = block.ReadFloat();
            block.VerifyEndOfBlock();
            block.TraceInformation($"{block.ID} was treated as uv_op_copy");
        }
    }

    public class UVOperationNonUniformScale : UVOperation
    {
        public int SourceUVIndex { get; private set; }
        public float UnknownParameter3 { get; private set; }
        public float UnknownParameter4 { get; private set; }


        public UVOperationNonUniformScale(SBR block)
        {
            block.VerifyID(TokenID.UV_Op_NonUniformScale);
            TextureAddressMode = block.ReadInt();
            SourceUVIndex = block.ReadInt();
            UnknownParameter3 = block.ReadFloat();
            UnknownParameter4 = block.ReadFloat();
            block.VerifyEndOfBlock();
            block.TraceInformation($"{block.ID} was treated as uv_op_copy");
        }
    }

    public class VertexStates : List<VertexState>
    {
        public VertexStates(SBR block)
        {
            block.VerifyID(TokenID.Vtx_States);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new VertexState(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class VertexState
    {
        // eg	vtx_state ( 00000000 0 -5 0 00000002 )
        // dword,flags :uint,MatrixIdx :sint,LightMatIdx :uint,LightCfgIdx :dword,LightFlags [:sint,matrix2] .
        public uint Flags { get; private set; }
        public int MatrixIndex { get; private set; }
        public int LightMatrixIndex { get; private set; } = -5;
        public int LightConfigIndex { get; private set; }
        public uint LightFlags { get; private set; } = 2;
        public int Matrix2 { get; private set; } = -1;

        public VertexState(int matrixIndex)
        {
            MatrixIndex = matrixIndex;
        }

        public VertexState(SBR block)
        {
            block.VerifyID(TokenID.Vtx_State);
            Flags = block.ReadFlags();
            MatrixIndex = block.ReadInt();
            LightMatrixIndex = block.ReadInt();
            LightConfigIndex = block.ReadInt();
            LightFlags = block.ReadFlags();
            if (!block.EndOfBlock())
                Matrix2 = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class PrimaryStates : List<PrimaryState>
    {
        public PrimaryStates(SBR block)
        {
            block.VerifyID(TokenID.Prim_States);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new PrimaryState(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class PrimaryState
    {/* prim_state              ==> :dword,flags :uint,ShaderIdx :tex_idxs :float,ZBias :sint,VertStateIdx [:uint,alphatestmode] [:uint,LightCfgIdx] [:uint,ZBufMode] .
        tex_idxs                ==> :uint,NumTexIdxs [{:uint}] .
				eg  	prim_state ( 00000000 0
						tex_idxs ( 1 0 ) 0 0 0 0 1
					)*/

        public string Name { get; private set; }
        public uint Flags { get; private set; }
        public int ShaderIndex { get; private set; }
        public int[] TextureIndices { get; private set; }
        public float ZBias { get; private set; }
        public int VertexStateIndex { get; private set; }
        public int AlphaTestMode { get; private set; }
        public int LightConfigIndex { get; private set; }
        public int ZBufferMode { get; private set; }
        public int TextureIndex { get { return TextureIndices[0]; } }

        public PrimaryState(int textureIndex, int shaderIndex, int vertexStateIndex)
        {
            Flags = 0;
            ShaderIndex = shaderIndex;
            TextureIndices = new int[1];
            TextureIndices[0] = textureIndex;
            ZBias = 0;
            VertexStateIndex = vertexStateIndex;
            AlphaTestMode = 0;
            LightConfigIndex = 0;
            ZBufferMode = 1;
        }

        public PrimaryState(SBR block)
        {
            block.VerifyID(TokenID.Prim_State);

            Name = block.Label;

            Flags = block.ReadFlags();
            ShaderIndex = block.ReadInt();
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Tex_Idxs);
                TextureIndices = new int[subBlock.ReadInt()];
                for (var i = 0; i < TextureIndices.Length; ++i) TextureIndices[i] = subBlock.ReadInt();
                subBlock.VerifyEndOfBlock();
            }
            ZBias = block.ReadFloat();
            VertexStateIndex = block.ReadInt();
            AlphaTestMode = block.ReadInt();
            LightConfigIndex = block.ReadInt();
            ZBufferMode = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class LodControls : List<LodControl>
    {
        public LodControls(SBR block)
        {
            block.VerifyID(TokenID.Lod_Controls);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new LodControl(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class LodControl
    {
        public DistanceLevelsHeader DistanceLevelsHeader { get; private set; }
        public DistanceLevels DistanceLevels { get; private set; }

        public LodControl(SBR block)
        {
            block.VerifyID(TokenID.Lod_Control);
            DistanceLevelsHeader = new DistanceLevelsHeader(block.ReadSubBlock());
            DistanceLevels = new DistanceLevels(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class DistanceLevelsHeader
    {
        public int DistanceLevelBias { get; private set; }

        public DistanceLevelsHeader(SBR block)
        {
            block.VerifyID(TokenID.Distance_Levels_Header);
            DistanceLevelBias = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class DistanceLevels : List<DistanceLevel>
    {
        public DistanceLevels(SBR block)
        {
            block.VerifyID(TokenID.Distance_Levels);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new DistanceLevel(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class DistanceLevel
    {
        public DistanceLevelHeader DistanceLevelHeader { get; private set; }
        public SubObjects SubObjects { get; private set; }

        public DistanceLevel(SBR block)
        {
            block.VerifyID(TokenID.Distance_Level);
            DistanceLevelHeader = new DistanceLevelHeader(block.ReadSubBlock());
            SubObjects = new SubObjects(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class DistanceLevelHeader
    {
        public float DistanceLevelSelection { get; private set; }
        public int[] Hierarchy { get; private set; }

        public DistanceLevelHeader(SBR block)
        {
            block.VerifyID(TokenID.Distance_Level_Header);
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.DLevel_Selection);
                DistanceLevelSelection = subBlock.ReadFloat();
                subBlock.VerifyEndOfBlock();
            }
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Hierarchy);
                Hierarchy = new int[subBlock.ReadInt()];
                for (var i = 0; i < Hierarchy.Length; ++i)
                    Hierarchy[i] = subBlock.ReadInt();
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class SubObjects : List<SubObject>
    {
        public SubObjects(SBR block)
        {
            block.VerifyID(TokenID.Sub_Objects);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new SubObject(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class SubObject
    {
        public SubObjectHeader SubObjectHeader { get; private set; }
        public Vertices Vertices { get; private set; }
        public VertexSets VertexSets { get; private set; }
        public Primitives Primitives { get; private set; }

        public SubObject(SBR block)
        {
            block.VerifyID(TokenID.Sub_Object);
            SubObjectHeader = new SubObjectHeader(block.ReadSubBlock());
            Vertices = new Vertices(block.ReadSubBlock());
            VertexSets = new VertexSets(block.ReadSubBlock());
            Primitives = new Primitives(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class SubObjectHeader
    {
        //:dword,flags :sint,SortVectorIdx :sint,VolIdx :dword,SrcVtxFmtFlags :dword,DstVtxFmtFlags :geometry_info,GeomInfo 
        //                               [:subobject_shaders,SubObjShaders] [:subobject_light_cfgs,SubObjLightCfgs] [:uint,SubObjID] .
        public uint Flags { get; private set; }
        public int SortVectorIndex { get; private set; }
        public int VolumeIndex { get; private set; }
        public uint SourceVertexFormatFlags { get; private set; }
        public uint DestinationVertexFormatFlags { get; private set; }
        public GeometryInfo GeometryInfo { get; private set; }
        public int[] SubObjectShaders { get; private set; }
        public int[] SubObjectLightConfigs { get; private set; }
        public int SubObjectID { get; private set; }

        public SubObjectHeader(SBR block)
        {
            block.VerifyID(TokenID.Sub_Object_Header);

            Flags = block.ReadFlags();
            SortVectorIndex = block.ReadInt();
            VolumeIndex = block.ReadInt();
            SourceVertexFormatFlags = block.ReadFlags();
            DestinationVertexFormatFlags = block.ReadFlags();
            GeometryInfo = new GeometryInfo(block.ReadSubBlock());

            if (!block.EndOfBlock())
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.SubObject_Shaders);
                SubObjectShaders = new int[subBlock.ReadInt()];
                for (var i = 0; i < SubObjectShaders.Length; ++i)
                    SubObjectShaders[i] = subBlock.ReadInt();
                subBlock.VerifyEndOfBlock();
            }

            if (!block.EndOfBlock())
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.SubObject_Light_Cfgs);
                SubObjectLightConfigs = new int[subBlock.ReadInt()];
                for (var i = 0; i < SubObjectLightConfigs.Length; ++i)
                    SubObjectLightConfigs[i] = subBlock.ReadInt();
                subBlock.VerifyEndOfBlock();
            }

            if (!block.EndOfBlock())
                SubObjectID = block.ReadInt();

            block.VerifyEndOfBlock();
        }
    }

    public class GeometryInfo
    {
        public int FaceNormals { get; private set; }
        public int TextureLightCmds { get; private set; }
        public int NodeXTextureLightCmds { get; private set; }
        public int TriListIndices { get; private set; }
        public int LineListIndices { get; private set; }
        public int NodeXTriListIndices { get; private set; }
        public int TriLists { get; private set; }
        public int LineLists { get; private set; }
        public int PointLists { get; private set; }
        public int NodeXTriLists { get; private set; }
        public GeometryNodes GeometryNodes { get; private set; }
        public int[] GeometryNodeMap { get; private set; }

        public GeometryInfo(SBR block)
        {
            block.VerifyID(TokenID.Geometry_Info);
            FaceNormals = block.ReadInt();
            TextureLightCmds = block.ReadInt();
            NodeXTriListIndices = block.ReadInt();
            TriListIndices = block.ReadInt();
            LineListIndices = block.ReadInt();
            NodeXTriListIndices = block.ReadInt();
            TriLists = block.ReadInt();
            LineLists = block.ReadInt();
            PointLists = block.ReadInt();
            NodeXTriLists = block.ReadInt();
            GeometryNodes = new GeometryNodes(block.ReadSubBlock());
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Geometry_Node_Map);
                GeometryNodeMap = new int[subBlock.ReadInt()];
                for (var i = 0; i < GeometryNodeMap.Length; ++i)
                    GeometryNodeMap[i] = subBlock.ReadInt();
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class GeometryNodes : List<GeometryNode>
    {
        public GeometryNodes(SBR block)
        {
            block.VerifyID(TokenID.Geometry_Nodes);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new GeometryNode(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class GeometryNode
    {
        public int TextureLightCmds { get; private set; }
        public int NodeXTextureLightCmds { get; private set; }
        public int TriLists { get; private set; }
        public int LineLists { get; private set; }
        public int PointLists { get; private set; }
        public CullablePrims CullablePrims { get; private set; }

        public GeometryNode(SBR block)
        {
            block.VerifyID(TokenID.Geometry_Node);
            TextureLightCmds = block.ReadInt();
            NodeXTextureLightCmds = block.ReadInt();
            TriLists = block.ReadInt();
            LineLists = block.ReadInt();
            PointLists = block.ReadInt();
            CullablePrims = new CullablePrims(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class CullablePrims
    {
        public int NumPrims { get; private set; }
        public int NumFlatSections { get; private set; }
        public int NumPrimIndices { get; private set; }

        public CullablePrims(SBR block)
        {
            block.VerifyID(TokenID.Cullable_Prims);
            NumPrims = block.ReadInt();
            NumFlatSections = block.ReadInt();
            NumPrimIndices = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class Vertices : List<Vertex>
    {
        public Vertices(SBR block)
        {
            block.VerifyID(TokenID.Vertices);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new Vertex(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class Vertex
    {
        public uint Flags { get; private set; }
        public int PointIndex { get; private set; }
        public int NormalIndex { get; private set; }
        public uint Color1 { get; private set; }
        public uint Color2 { get; private set; }
        public int[] VertexUVs { get; private set; }

        public Vertex(SBR block)
        {
            block.VerifyID(TokenID.Vertex);
            Flags = block.ReadFlags();
            PointIndex = block.ReadInt();
            NormalIndex = block.ReadInt();
            Color1 = block.ReadFlags();
            Color2 = block.ReadFlags();
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Vertex_UVs);
                VertexUVs = new int[subBlock.ReadInt()];
                for (var i = 0; i < VertexUVs.Length; ++i) VertexUVs[i] = subBlock.ReadInt();
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }

        public Vertex()
        {
            Flags = 0;
            PointIndex = 0;
            NormalIndex = 0;
            Color1 = 0xffffffffU;
            Color2 = 0xff000000U;
            VertexUVs = new int[1];
        }
    }

    public class VertexSets : List<VertexSet>
    {
        public VertexSets(SBR block)
        {
            block.VerifyID(TokenID.Vertex_Sets);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new VertexSet(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class VertexSet
    {
        public int VertexStateIndex { get; private set; }
        public int StartVertexIndex { get; private set; }
        public int VertexCount { get; private set; }

        public VertexSet(SBR block)
        {
            block.VerifyID(TokenID.Vertex_Set);
            VertexStateIndex = block.ReadInt();
            StartVertexIndex = block.ReadInt();
            VertexCount = block.ReadInt();
            block.VerifyEndOfBlock();
        }
    }

    public class Primitives : List<Primitive>
    {
        public Primitives(SBR block)
        {
            block.VerifyID(TokenID.Primitives);
            var last_prim_state_idx = 0;
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                switch (subBlock.ID)
                {
                    case TokenID.Prim_State_Idx: last_prim_state_idx = subBlock.ReadInt(); subBlock.VerifyEndOfBlock(); break;
                    case TokenID.Indexed_TriList: Add(new Primitive(subBlock, last_prim_state_idx)); break;
                    default: throw new System.Exception("Unexpected primitive type " + subBlock.ID.ToString());
                }
            }
            block.VerifyEndOfBlock();
        }
    }

    public class Primitive
    {
        public int PrimitiveStateIndex { get; private set; }
        public IndexedTriList IndexedTriList { get; private set; }

        public Primitive(SBR block, int last_prim_state_idx)
        {
            PrimitiveStateIndex = last_prim_state_idx;
            IndexedTriList = new IndexedTriList(block);
        }
    }

    public class IndexedTriList
    {
        public VertexIndices VertexIndices { get; private set; }
        public int[] NormalIndices { get; private set; }
        public uint[] Flags { get; private set; }

        public IndexedTriList(SBR block)
        {
            block.VerifyID(TokenID.Indexed_TriList);
            VertexIndices = new VertexIndices(block.ReadSubBlock());
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Normal_Idxs);
                NormalIndices = new int[subBlock.ReadInt()];
                for (var i = 0; i < NormalIndices.Length; ++i)
                {
                    NormalIndices[i] = subBlock.ReadInt();
                    subBlock.ReadInt(); // skip the '3' value - its purpose unknown
                }
                subBlock.VerifyEndOfBlock();
            }
            {
                var subBlock = block.ReadSubBlock();
                subBlock.VerifyID(TokenID.Flags);
                Flags = new uint[subBlock.ReadInt()];
                for (var i = 0; i < Flags.Length; ++i) Flags[i] = subBlock.ReadFlags();
                subBlock.VerifyEndOfBlock();
            }
            block.VerifyEndOfBlock();
        }
    }

    public class VertexIndices : List<VertexIndex>
    {
        public VertexIndices(SBR block)
        {
            block.VerifyID(TokenID.Vertex_Idxs);
            var count = Capacity = block.ReadInt() / 3;
            while (count-- > 0) Add(new VertexIndex(block));
            block.VerifyEndOfBlock();
        }
    }

    public class VertexIndex
    {
        public int A { get; private set; }
        public int B { get; private set; }
        public int C { get; private set; }

        public VertexIndex(SBR block)
        {
            A = block.ReadInt();
            B = block.ReadInt();
            C = block.ReadInt();
        }

        public VertexIndex(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    public class Animations : List<Animation>
    {
        public Animations(SBR block)
        {
            block.VerifyID(TokenID.Animations);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new Animation(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class Animation
    {
        public int FrameCount { get; private set; }          // :uint,num_frames
        public int FrameRate { get; private set; }          // :uint,frame_rate 
        public AnimationNodes AnimationNodes { get; private set; }    // :anim_nodes,AnimNodes .

        public Animation(SBR block)
        {
            block.VerifyID(TokenID.Animation);
            FrameCount = block.ReadInt();
            FrameRate = block.ReadInt();
            AnimationNodes = new AnimationNodes(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class AnimationNodes : List<AnimationNode>
    {
        public AnimationNodes(SBR block)
        {
            block.VerifyID(TokenID.Anim_nodes);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new AnimationNode(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class AnimationNode
    {
        public string Name { get; private set; }
        public Controllers Controllers { get; private set; }

        public AnimationNode(SBR block)
        {
            block.VerifyID(TokenID.Anim_node);
            Name = block.Label;
            Controllers = new Controllers(block.ReadSubBlock());
            block.VerifyEndOfBlock();
        }
    }

    public class Controllers : List<Controller>
    {
        public Controllers(SBR block)
        {
            block.VerifyID(TokenID.Controllers);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                switch (subBlock.ID)
                {
                    case TokenID.Linear_Pos: Add(new LinearPosition(subBlock)); break;
                    case TokenID.Tcb_Rot: Add(new TcbRotation(subBlock)); break;
                    default: throw new System.Exception("Unexpected animation controller " + subBlock.ID.ToString());
                }
            }
            block.VerifyEndOfBlock();
        }
    }

    public abstract class Controller : List<KeyPosition>
    {
    }

    public abstract class KeyPosition
    {
        public int Frame { get; protected set; }
    }

    public class TcbRotation : Controller
    {
        public TcbRotation(SBR block)
        {
            block.VerifyID(TokenID.Tcb_Rot);
            var count = Capacity = block.ReadInt();
            while (count-- > 0)
            {
                var subBlock = block.ReadSubBlock();
                switch (subBlock.ID)
                {
                    case TokenID.Slerp_Rot: Add(new SlerpRotation(subBlock)); break;
                    case TokenID.Tcb_Key: Add(new TcbKey(subBlock)); break;
                    default: throw new System.Exception("Unexpected block " + subBlock.ID.ToString());
                }
            }
            block.VerifyEndOfBlock();
        }
    }

    public class SlerpRotation : KeyPosition
    {
        private Quaternion quaternion;
        public ref readonly Quaternion Quaternion => ref quaternion; 

        public SlerpRotation(SBR block)
        {
            block.VerifyID(TokenID.Slerp_Rot);
            Frame = block.ReadInt();
            quaternion = new Quaternion(block.ReadFloat(), block.ReadFloat(), -block.ReadFloat(), block.ReadFloat());
            block.VerifyEndOfBlock();
        }
    }

    public class LinearPosition : Controller
    {
        public LinearPosition(SBR block)
        {
            block.VerifyID(TokenID.Linear_Pos);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new LinearKey(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class LinearKey : KeyPosition
    {
        private Vector3 position;
        public ref readonly Vector3 Position => ref position;

        public LinearKey(SBR block)
        {
            block.VerifyID(TokenID.Linear_Key);
            Frame = block.ReadInt();
            position = new Vector3(block.ReadFloat(), block.ReadFloat(), - block.ReadFloat());
            block.VerifyEndOfBlock();
        }
    }

    public class TcbPosition : Controller
    {
        public TcbPosition(SBR block)
        {
            block.VerifyID(TokenID.Tcb_Pos);
            var count = Capacity = block.ReadInt();
            while (count-- > 0) Add(new TcbKey(block.ReadSubBlock()));
            block.VerifyEndOfBlock();
        }
    }

    public class TcbKey : KeyPosition
    {
        private Quaternion quaternion;
        public ref readonly Quaternion Quaternion => ref quaternion;
        public float Tension { get; private set; }
        public float Continuity { get; private set; }
        public float Bias { get; private set; }
        public float In { get; private set; }
        public float Out { get; private set; }

        public TcbKey(SBR block)
        {
            block.VerifyID(TokenID.Tcb_Key);
            Frame = block.ReadInt();
            quaternion = new Quaternion(block.ReadFloat(), block.ReadFloat(), -block.ReadFloat(), block.ReadFloat());
            Tension = block.ReadFloat();
            Continuity = block.ReadFloat();
            Bias = block.ReadFloat();
            In = block.ReadFloat();
            Out = block.ReadFloat();
            block.VerifyEndOfBlock();
        }
    }

}
