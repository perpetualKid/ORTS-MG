// COPYRIGHT 2014 by the Open Rails project.
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

// This file is the responsibility of the 3D & Environment Team.

// File is partially backported to XNA 3.1 and adapted to OpenRails needs.
// Original license is as follows:

#region File Description + License
//-----------------------------------------------------------------------------
// DDSFromFile.cs
// Read/Write dds files from/to files or from streams.
// Version 1.86
// Popescu Alexandru Cristian(kiki_karon@yahoo.com)
// Copyright (C) Popescu Alexandru Cristian All rights reserved.
//-----------------------------------------------------------------------------

//The MIT License

//Copyright (c) 2010 Popescu Alexandru Cristian

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in
//all copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//THE SOFTWARE.
#endregion

using System;
using System.IO;

using Microsoft.Xna.Framework.Graphics;

namespace Orts.ActivityRunner.Viewer3D
{
    /// <summary>
    /// Read/Write dds files from/to files or from streams.
    /// </summary>
    public static class DDSLib
    {
#pragma warning disable CA1823 // Avoid unused private fields
#pragma warning disable IDE0051 // Remove unused private members
        private const int DDSD_CAPS = 0x1; //Required in every .dds file.	
        private const int DDSD_HEIGHT = 0x2; //Required in every .dds file.
        private const int DDSD_WIDTH = 0x4; //Required in every .dds file.
        private const int DDSD_PITCH = 0x8; //Required when pitch is provided for an uncompressed texture.
        private const int DDSD_PIXELFORMAT = 0x1000; //Required in every .dds file.
        private const int DDSD_MIPMAPCOUNT = 0x20000; //Required in a mipmapped texture.
        private const int DDSD_LINEARSIZE = 0x80000; //Required when pitch is provided for a compressed texture.
        private const int DDSD_DEPTH = 0x800000; //Required in a depth texture.

        private const int DDPF_ALPHAPIXELS = 0x1; //Texture contains alpha data; dwRGBAlphaBitMask contains valid data.	
        private const int DDPF_ALPHA = 0x2;  //Used in some older DDS files for alpha channel only uncompressed data (dwRGBBitCount contains the alpha channel bitcount; dwABitMask contains valid data)	
        private const int DDPF_FOURCC = 0x4;	 //Texture contains compressed RGB data; dwFourCC contains valid data.	
        private const int DDPF_RGB = 0x40;	 //Texture contains uncompressed RGB data; dwRGBBitCount and the RGB masks (dwRBitMask, dwRBitMask, dwRBitMask) contain valid data.	
        private const int DDPF_YUV = 0x200;	 //Used in some older DDS files for YUV uncompressed data (dwRGBBitCount contains the YUV bit count; dwRBitMask contains the Y mask, dwGBitMask contains the U mask, dwBBitMask contains the V mask)	
        private const int DDPF_LUMINANCE = 0x2000;	 //Used in some older DDS files for single channel color uncompressed data (dwRGBBitCount contains the luminance channel bit count; dwRBitMask contains the channel mask). Can be combined with DDPF_ALPHAPIXELS for a two channel DDS file.	
        private const int DDPF_Q8W8V8U8 = 0x00080000; //Used by Microsoft tools when a Q8W8V8U8 is present, this is not a documeneted flag.

        private const int DDSCAPS_COMPLEX = 0x8;	 //Optional; must be used on any file that contains more than one surface (a mipmap, a cubic environment map, or mipmapped volume texture).	
        private const int DDSCAPS_MIPMAP = 0x400000; //Optional; should be used for a mipmap.	
        private const int DDSCAPS_TEXTURE = 0x1000;	 //Required	

        private const int DDSCAPS2_CUBEMAP = 0x200; //Required for a cube map.	
        private const int DDSCAPS2_CUBEMAP_POSITIVEX = 0x400;	//Required when these surfaces are stored in a cube map.	
        private const int DDSCAPS2_CUBEMAP_NEGATIVEX = 0x800;	//Required when these surfaces are stored in a cube map.	
        private const int DDSCAPS2_CUBEMAP_POSITIVEY = 0x1000;	//Required when these surfaces are stored in a cube map.	
        private const int DDSCAPS2_CUBEMAP_NEGATIVEY = 0x2000;	//Required when these surfaces are stored in a cube map.	
        private const int DDSCAPS2_CUBEMAP_POSITIVEZ = 0x4000;	//Required when these surfaces are stored in a cube map.	
        private const int DDSCAPS2_CUBEMAP_NEGATIVEZ = 0x8000;	//Required when these surfaces are stored in a cube map.	
        private const int DDSCAPS2_VOLUME = 0x200000; //Required for a volume texture.

        private const uint DDS_MAGIC = 0x20534444; // "DDS "
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning restore CA1823 // Avoid unused private fields

        //Compression formats.
        [Flags()]
        private enum FourCC : uint
        {
            D3DFMT_DXT1 = 0x31545844,
            D3DFMT_DXT2 = 0x32545844,
            D3DFMT_DXT3 = 0x33545844,
            D3DFMT_DXT4 = 0x34545844,
            D3DFMT_DXT5 = 0x35545844,
            DX10 = 0x30315844,
            DXGI_FORMAT_BC4_UNORM = 0x55344342,
            DXGI_FORMAT_BC4_SNORM = DXGI_FORMAT_BC4_UNORM, //0x53344342,
            DXGI_FORMAT_BC5_UNORM = 0x32495441,
            DXGI_FORMAT_BC5_SNORM = 0x53354342,

            //DXGI_FORMAT_R8G8_B8G8_UNORM
            D3DFMT_R8G8_B8G8 = 0x47424752,

            //DXGI_FORMAT_G8R8_G8B8_UNORM
            D3DFMT_G8R8_G8B8 = 0x42475247,

            //DXGI_FORMAT_R16G16B16A16_UNORM
            D3DFMT_A16B16G16R16 = 36,

            //DXGI_FORMAT_R16G16B16A16_SNORM
            D3DFMT_Q16W16V16U16 = 110,

            //DXGI_FORMAT_R16_FLOAT
            D3DFMT_R16F = 111,

            //DXGI_FORMAT_R16G16_FLOAT
            D3DFMT_G16R16F = 112,

            //DXGI_FORMAT_R16G16B16A16_FLOAT
            D3DFMT_A16B16G16R16F = 113,

            //DXGI_FORMAT_R32_FLOAT
            D3DFMT_R32F = 114,

            //DXGI_FORMAT_R32G32_FLOAT
            D3DFMT_G32R32F = 115,

            //DXGI_FORMAT_R32G32B32A32_FLOAT
            D3DFMT_A32B32G32R32F = 116,

            D3DFMT_UYVY = 0x59565955,
            D3DFMT_YUY2 = 0x32595559,
            D3DFMT_CxV8U8 = 117,

            //This is set only by the nvidia exporter, it is not set by the dx texture tool
            //,it is ignored by the dx texture tool but it returns the ability to be opened in photoshop so I decided to keep it.
            D3DFMT_Q8W8V8U8 = 63,
        }

        // Indicates whether this texture is cube map.
        private static bool IsCubemapTest(int ddsCaps1, int ddsCaps2)
        {
            return ((ddsCaps1 & DDSCAPS_COMPLEX) != 0) && ((ddsCaps2 & DDSCAPS2_CUBEMAP) != 0);
        }

        // Indicates whether this texture is volume map. 
        private static bool IsVolumeTextureTest(int ddsCaps2)
        {
            return ((ddsCaps2 & DDSCAPS2_VOLUME) != 0);
        }

        //Test if the texture is using any compression.
        private static bool IsCompressedTest(uint pfFlags)
        {
            return ((pfFlags & DDPF_FOURCC) != 0);
        }

        private static bool HasAlphaTest(uint pfFlags)
        {
            return ((pfFlags & DDPF_ALPHAPIXELS) != 0);
        }

        //We need the mip size, we shift until we get there but the smallest mip must be at least of 1 pixel.
        private static int MipMapSize(int map, int size)
        {
            for (int i = 0; i < map; i++)
                size >>= 1;
            if (size <= 0)
                return 1;
            return size;
        }

        //Surface formats that we can load from a dds
        //I am not using the XNA SurfaceFormat as that one is missing a few formats.
        private enum LoadSurfaceFormat
        {
            Unknown,
            Dxt1,
            Dxt3,
            Dxt5,
            R8G8B8,
            B8G8R8,
            Bgra5551,
            Bgra4444,
            Bgr565,
            Alpha8,
            X8R8G8B8,
            A8R8G8B8,
            A8B8G8R8,
            X8B8G8R8,
            RGB555,
            R32F,
            R16F,
            A32B32G32R32F,
            A16B16G16R16F,
            Q8W8V8U8,
            CxV8U8,
            G16R16F,
            G32R32F,
            G16R16,
            A2B10G10R10,
            A16B16G16R16,
        }

        //Get pixel format from hader.
        private static LoadSurfaceFormat GetLoadSurfaceFormat(uint pixelFlags, uint pixelFourCC, int bitCount, uint rBitMask, uint gBitMask, uint bBitMask, uint aBitMask)
        {
            switch ((FourCC)pixelFourCC)
            {
                case FourCC.D3DFMT_A16B16G16R16: return LoadSurfaceFormat.A16B16G16R16;
                case FourCC.D3DFMT_G32R32F: return LoadSurfaceFormat.G32R32F;
                case FourCC.D3DFMT_G16R16F: return LoadSurfaceFormat.G16R16F;
                case FourCC.D3DFMT_Q8W8V8U8: return LoadSurfaceFormat.Q8W8V8U8; //This is true if the file was generated with the nvidia tools.
                case FourCC.D3DFMT_CxV8U8: return LoadSurfaceFormat.CxV8U8;
                case FourCC.D3DFMT_A16B16G16R16F: return LoadSurfaceFormat.A16B16G16R16F;
                case FourCC.D3DFMT_A32B32G32R32F: return LoadSurfaceFormat.A32B32G32R32F;
                case FourCC.D3DFMT_R32F: return LoadSurfaceFormat.R32F;
                case FourCC.D3DFMT_R16F: return LoadSurfaceFormat.R16F;
            }

            if ((pixelFlags & DDPF_FOURCC) != 0)
            {
                //The texture is compressed(Dxt1,Dxt3/Dxt2,Dxt5/Dxt4).
                switch (pixelFourCC)
                {
                    case 0x31545844: return LoadSurfaceFormat.Dxt1;
                    case 0x33545844:
                    case 0x32545844: return LoadSurfaceFormat.Dxt3;
                    case 0x35545844:
                    case 0x34545844: return LoadSurfaceFormat.Dxt5;
                }
            }

            if ((pixelFlags & DDPF_RGB) != 0)
            {
                if (pixelFlags == 0x40 && bitCount == 0x10 && pixelFourCC == 0 && rBitMask == 0x00007c00 && gBitMask == 0x000003e0 && bBitMask == 0x0000001f && aBitMask == 0x0)
                {
                    return LoadSurfaceFormat.RGB555;
                }

                if (pixelFlags == 0x41 && bitCount == 0x20 && pixelFourCC == 0 && rBitMask == 0xff0000 && gBitMask == 0xff00 && bBitMask == 0xff && aBitMask == 0xff000000)
                {
                    return LoadSurfaceFormat.A8R8G8B8;
                }

                if (pixelFlags == 0x40 && bitCount == 0x20 && pixelFourCC == 0 && rBitMask == 0xff0000 && gBitMask == 0xff00 && bBitMask == 0xff && aBitMask == 0x0)
                {
                    //DDS_FORMAT_X8R8G8B8
                    return LoadSurfaceFormat.X8R8G8B8;
                }

                if (pixelFlags == 0x41 && bitCount == 0x20 && pixelFourCC == 0 && rBitMask == 0xff && gBitMask == 0xff00 && bBitMask == 0xff0000 && aBitMask == 0xff000000)
                {
                    //DDS_FORMAT_A8B8G8R8
                    return LoadSurfaceFormat.A8B8G8R8;
                }

                if (pixelFlags == 0x40 && bitCount == 0x20 && pixelFourCC == 0 && rBitMask == 0xff && gBitMask == 0xff00 && bBitMask == 0xff0000 && aBitMask == 0x0)
                {
                    //DDS_FORMAT_X8B8G8R8
                    return LoadSurfaceFormat.X8B8G8R8;
                }

                if (pixelFlags == 0x41 && bitCount == 0x10 && pixelFourCC == 0 && rBitMask == 0x7c00 && gBitMask == 0x3e0 && bBitMask == 0x1f && aBitMask == 0x8000)
                {
                    return LoadSurfaceFormat.Bgra5551;
                }

                if (pixelFlags == 0x41 && bitCount == 0x10 && pixelFourCC == 0 && rBitMask == 0xf00 && gBitMask == 240 && bBitMask == 15 && aBitMask == 0xf000)
                {
                    return LoadSurfaceFormat.Bgra4444;
                }

                if (pixelFlags == 0x40 && bitCount == 0x18 && pixelFourCC == 0 && rBitMask == 0xff0000 && gBitMask == 0xff00 && bBitMask == 0xff && aBitMask == 0)
                {
                    //DDS_FORMAT_R8G8B8
                    return LoadSurfaceFormat.R8G8B8;
                }

                if (pixelFlags == 0x40 && bitCount == 0x10 && pixelFourCC == 0 && rBitMask == 0xf800 && gBitMask == 0x7e0 && bBitMask == 0x1f && aBitMask == 0)
                {
                    return LoadSurfaceFormat.Bgr565;
                }

                if (pixelFlags == 0x2 && bitCount == 0x8 && pixelFourCC == 0 && rBitMask == 0 && gBitMask == 0 && bBitMask == 0 && aBitMask == 255)
                {
                    return LoadSurfaceFormat.Alpha8;
                }

                if (pixelFlags == 0x40 && bitCount == 32 && pixelFourCC == 0 && rBitMask == 0x0000ffff && gBitMask == 0xffff0000 && bBitMask == 0 && aBitMask == 0)
                {
                    return LoadSurfaceFormat.G16R16;
                }

                if (pixelFlags == 0x41 && bitCount == 32 && pixelFourCC == 0 && rBitMask == 0x3ff00000 && gBitMask == 0x000ffc00 && bBitMask == 0x000003ff && aBitMask == 0xc0000000)
                {
                    return LoadSurfaceFormat.A2B10G10R10;
                }
            }

            //We consider the standard dx pixelFourCC + pixelFourCC == 63(nvidia tools generated dds)
            if (pixelFlags == 0x00080000 && bitCount == 32 && (pixelFourCC == 0 || pixelFourCC == 63) && rBitMask == 0x000000ff && gBitMask == 0x0000ff00 && bBitMask == 0x00ff0000 && aBitMask == 0xff000000)
            {
                return LoadSurfaceFormat.Q8W8V8U8;
            }

            return LoadSurfaceFormat.Unknown;
        }

        //Get compression format.
        private static FourCC GetCompressionFormat(uint pixelFlags, uint pixelFourCC)
        {
            if ((pixelFlags & DDPF_FOURCC) != 0)
                return (FourCC)pixelFourCC;
            else return 0;
        }

        //Get the size in bytes for a mip-map level.
        private static int MipMapSizeInBytes(int map, int width, int height, bool isCompressed, FourCC compressionFormat, int depth)
        {
            width = MipMapSize(map, width);
            height = MipMapSize(map, height);

            //We hardcoded some compression formats as some flags are not set by all the tools for them,
            //as a result for this formats we must hardcode the outcome.
            switch (compressionFormat)
            {
                case FourCC.D3DFMT_R32F: return width * height * 4;
                case FourCC.D3DFMT_R16F: return width * height * 2;
                case FourCC.D3DFMT_A32B32G32R32F: return width * height * 16;
                case FourCC.D3DFMT_A16B16G16R16F: return width * height * 8;
                case FourCC.D3DFMT_CxV8U8: return width * height * 2;
                case FourCC.D3DFMT_Q8W8V8U8: return width * height * 4;
                case FourCC.D3DFMT_G16R16F: return width * height * 4;
                case FourCC.D3DFMT_G32R32F: return width * height * 8;
                case FourCC.D3DFMT_A16B16G16R16: return width * height * 8;
            }

            if (isCompressed)
            {
                int blockSize = (compressionFormat == FourCC.D3DFMT_DXT1 ? 8 : 16);
                return (width + 3) / 4 * ((height + 3) / 4) * blockSize;
            }
            else
            {
                return width * height * (depth / 8);
            }
        }

        //Get the byte data from a mip-map level.
        private static void GetMipMaps(int offsetInStream, int map, int width, int height, bool isCompressed, FourCC compressionFormat, int rgbBitCount, BinaryReader reader, LoadSurfaceFormat loadSurfaceFormat, ref byte[] data, out int numBytes)
        {
            int seek = 128 + offsetInStream;

            for (int i = 0; i < map; i++)
            {
                seek += MipMapSizeInBytes(i, width, height, isCompressed, compressionFormat, rgbBitCount);
            }

            reader.BaseStream.Seek(seek, SeekOrigin.Begin);

            numBytes = MipMapSizeInBytes(map, width, height, isCompressed, compressionFormat, rgbBitCount);

            if (data == null || data.Length < numBytes)
            {
                data = new byte[numBytes];
            }

            if (!isCompressed && rgbBitCount == 24)
            {
                numBytes += (numBytes / 3);
            }
            if (!isCompressed && loadSurfaceFormat == LoadSurfaceFormat.R8G8B8)
            {
                for (int i = 0; i < numBytes; i += 4)
                {
                    data[i] = reader.ReadByte();
                    data[i + 1] = reader.ReadByte();
                    data[i + 2] = reader.ReadByte();
                    data[i + 3] = 255;
                }
            }
            else
            {
                reader.Read(data, 0, numBytes);
            }

            if (loadSurfaceFormat == LoadSurfaceFormat.X8R8G8B8 || loadSurfaceFormat == LoadSurfaceFormat.X8B8G8R8)
            {
                for (int i = 0; i < numBytes; i += 4)
                {
                    data[i + 3] = 255;
                }
            }

            if (loadSurfaceFormat == LoadSurfaceFormat.A8R8G8B8 ||
                loadSurfaceFormat == LoadSurfaceFormat.X8R8G8B8 ||
                loadSurfaceFormat == LoadSurfaceFormat.R8G8B8)
            {
                int bytesPerPixel = (rgbBitCount == 32 || rgbBitCount == 24) ? 4 : 3;

                byte g, b;
                if (bytesPerPixel == 3)
                {
                    for (int i = 0; i < numBytes - 2; i += 3)
                    {
                        g = data[i];
                        b = data[i + 2];
                        data[i] = b;
                        data[i + 2] = g;
                    }
                }
                else
                {
                    for (int i = 0; i < numBytes - 3; i += 4)
                    {
                        g = data[i];
                        b = data[i + 2];
                        data[i] = b;
                        data[i + 2] = g;
                    }
                }
            }
        }

        //Xna only supporst mip-map on textures with full chains == last-mip is 1x1
        private static bool CheckFullMipChain(int width, int height, int numMip)
        {
            int max = Math.Max(width, height);
            int imaginariMipMax = 0;
            while (max > 1)
            {
                max /= 2;
                imaginariMipMax++;
            }

            return (imaginariMipMax <= numMip);
        }

        /// <summary>
        /// Open a dds from file.
        /// (Supported formats : Dxt1,Dxt2,Dxt3,Dxt4,Dxt5,A8R8G8B8/Color,X8R8G8B8,R8G8B8,A4R4G4B4,A1R5G5B5,R5G6B5,A8,
        /// FP32/Single,FP16/HalfSingle,FP32x4/Vector4,FP16x4/HalfVector4,CxV8U8/NormalizedByte2/CxVU,Q8VW8V8U8/NormalizedByte4/8888QWVU
        /// ,HalfVector2/G16R16F/16.16fGR,Vector2/G32R32F,G16R16/RG32/1616GB,B8G8R8,X8B8G8R8,A8B8G8R8/Color,L8,A2B10G10R10/Rgba1010102,A16B16G16R16/Rgba64)
        /// </summary>
        /// <param name="fileName">File containing the data.</param>
        /// <param name="device">Graphic device where you want the texture to be loaded.</param>
        /// <param name="texture">The reference to the loaded texture.</param>
        /// <param name="streamOffset">Offset in the stream to where the DDS is located.</param>
        /// <param name="loadMipMap">If true it will load the mip-map chain for this texture.</param>
        public static void DDSFromFile(string fileName, GraphicsDevice device, bool loadMipMap, out Texture2D texture)
        {
            using (Stream stream = File.OpenRead(fileName))
            {
                InternalDDSFromStream(stream, device, 0, loadMipMap, out Texture tex);

                texture = tex as Texture2D;
                if (texture == null)
                {
                    throw new InvalidDataException($"The data in the stream contains a {tex.GetType().Name} but not Texture2D");
                }
            }
        }

        /// <summary>
        /// Open a dds from file.
        /// (Supported formats : Dxt1,Dxt2,Dxt3,Dxt4,Dxt5,A8R8G8B8/Color,X8R8G8B8,R8G8B8,A4R4G4B4,A1R5G5B5,R5G6B5,A8,
        /// FP32/Single,FP16/HalfSingle,FP32x4/Vector4,FP16x4/HalfVector4,CxV8U8/NormalizedByte2/CxVU,Q8VW8V8U8/NormalizedByte4/8888QWVU
        /// ,HalfVector2/G16R16F/16.16fGR,Vector2/G32R32F,G16R16/RG32/1616GB,B8G8R8,X8B8G8R8,A8B8G8R8/Color,L8,A2B10G10R10/Rgba1010102,A16B16G16R16/Rgba64)
        /// </summary>
        /// <param name="fileName">File containing the data.</param>
        /// <param name="device">Graphic device where you want the texture to be loaded.</param>
        /// <param name="texture">The reference to the loaded texture.</param>
        /// <param name="streamOffset">Offset in the stream to where the DDS is located.</param>
        /// <param name="loadMipMap">If true it will load the mip-map chain for this texture.</param>
        public static void DDSFromFile(string fileName, GraphicsDevice device, bool loadMipMap, out TextureCube texture)
        {
            using (Stream stream = File.OpenRead(fileName))
            {
                InternalDDSFromStream(stream, device, 0, loadMipMap, out Texture tex);

                texture = tex as TextureCube;
                if (texture == null)
                {
                    throw new InvalidDataException($"The data in the stream contains a {tex.GetType().Name} but not TextureCube");
                }
            }
        }

        /// <summary>
        /// Open a dds from file.
        /// (Supported formats : Dxt1,Dxt2,Dxt3,Dxt4,Dxt5,A8R8G8B8/Color,X8R8G8B8,R8G8B8,A4R4G4B4,A1R5G5B5,R5G6B5,A8,
        /// FP32/Single,FP16/HalfSingle,FP32x4/Vector4,FP16x4/HalfVector4,CxV8U8/NormalizedByte2/CxVU,Q8VW8V8U8/NormalizedByte4/8888QWVU
        /// ,HalfVector2/G16R16F/16.16fGR,Vector2/G32R32F,G16R16/RG32/1616GB,B8G8R8,X8B8G8R8,A8B8G8R8/Color,L8,A2B10G10R10/Rgba1010102,A16B16G16R16/Rgba64)
        /// </summary>
        /// <param name="fileName">File containing the data.</param>
        /// <param name="device">Graphic device where you want the texture to be loaded.</param>
        /// <param name="texture">The reference to the loaded texture.</param>
        /// <param name="streamOffset">Offset in the stream to where the DDS is located.</param>
        /// <param name="loadMipMap">If true it will load the mip-map chain for this texture.</param>
        public static void DDSFromFile(string fileName, GraphicsDevice device, bool loadMipMap, out Texture3D texture)
        {
            using (Stream stream = File.OpenRead(fileName))
            {
                InternalDDSFromStream(stream, device, 0, loadMipMap, out Texture tex);

                texture = tex as Texture3D;
                if (texture == null)
                {
                    throw new InvalidDataException($"The data in the stream contains a {tex.GetType().Name} but not Texture3D");
                }
            }
        }

        [ThreadStatic]
        private static byte[] mipData;

        //try to evaluate the xna compatible surface for the present data
        private static SurfaceFormat SurfaceFormatFromLoadFormat(LoadSurfaceFormat loadSurfaceFormat, FourCC compressionFormat, uint pixelFlags, int rgbBitCount)
        {
            switch (loadSurfaceFormat)
            {
                case LoadSurfaceFormat.Alpha8: return SurfaceFormat.Alpha8;
                case LoadSurfaceFormat.Bgr565: return SurfaceFormat.Bgr565;
                case LoadSurfaceFormat.Bgra4444: return SurfaceFormat.Bgra4444;
                case LoadSurfaceFormat.Bgra5551: return SurfaceFormat.Bgra5551;
                case LoadSurfaceFormat.A8R8G8B8: return SurfaceFormat.Color;
                case LoadSurfaceFormat.Dxt1: return SurfaceFormat.Dxt1;
                case LoadSurfaceFormat.Dxt3: return SurfaceFormat.Dxt3;
                case LoadSurfaceFormat.Dxt5: return SurfaceFormat.Dxt5;
                case LoadSurfaceFormat.R8G8B8: return SurfaceFormat.Color;//Updated at load time to X8R8B8B8
                case LoadSurfaceFormat.X8B8G8R8: return SurfaceFormat.Color;
                case LoadSurfaceFormat.X8R8G8B8: return SurfaceFormat.Color;
                case LoadSurfaceFormat.A8B8G8R8: return SurfaceFormat.Color;
                case LoadSurfaceFormat.R32F: return SurfaceFormat.Single;
                case LoadSurfaceFormat.A32B32G32R32F: return SurfaceFormat.Vector4;
                case LoadSurfaceFormat.G32R32F: return SurfaceFormat.Vector2;
                case LoadSurfaceFormat.R16F: return SurfaceFormat.HalfSingle;
                case LoadSurfaceFormat.G16R16F: return SurfaceFormat.HalfVector2;
                case LoadSurfaceFormat.A16B16G16R16F: return SurfaceFormat.HalfVector4;
                case LoadSurfaceFormat.CxV8U8: return SurfaceFormat.NormalizedByte2;
                case LoadSurfaceFormat.Q8W8V8U8: return SurfaceFormat.NormalizedByte4;
                case LoadSurfaceFormat.G16R16: return SurfaceFormat.Rg32;
                case LoadSurfaceFormat.A2B10G10R10: return SurfaceFormat.Rgba1010102;
                case LoadSurfaceFormat.A16B16G16R16: return SurfaceFormat.Rgba64;
                case LoadSurfaceFormat.Unknown:
                    switch (compressionFormat)
                    {
                        case FourCC.D3DFMT_DXT1: return SurfaceFormat.Dxt1;
                        case FourCC.D3DFMT_DXT3: return SurfaceFormat.Dxt3;
                        case FourCC.D3DFMT_DXT5: return SurfaceFormat.Dxt5;
                        case 0:
                            switch (rgbBitCount)
                            {
                                case 8: return SurfaceFormat.Alpha8;
                                case 16: return HasAlphaTest(pixelFlags) ? SurfaceFormat.Bgr565 : SurfaceFormat.Bgra4444;
                                case 24:
                                case 32: return SurfaceFormat.Color;
                            }
                            break;
                        default:
                            throw new InvalidDataException("Unsupported format");
                    };
                    break;
                default:
                    throw new InvalidDataException(loadSurfaceFormat.ToString() + " is an unsuported format");
            }

            throw new InvalidDataException("Unsupported format");
        }

        //new cube-map texture
        private static TextureCube GenerateNewCubeTexture(LoadSurfaceFormat loadSurfaceFormat, FourCC compressionFormat, GraphicsDevice device, int width, uint pixelFlags, int rgbBitCount)
        {
            SurfaceFormat expectedFormat = SurfaceFormatFromLoadFormat(loadSurfaceFormat, compressionFormat, pixelFlags, rgbBitCount);

            TextureCube texture = new TextureCube(device, width, true, expectedFormat); //hasMipMaps

            if (texture.Format != expectedFormat)
            {
                throw new InvalidDataException($"Can't generate a {expectedFormat} surface.");
            }

            return texture;
        }

        //new 2d-map texture
        private static Texture2D GenerateNewTexture2D(LoadSurfaceFormat loadSurfaceFormat, FourCC compressionFormat, GraphicsDevice device, int width, int height, bool hasMipMaps, uint pixelFlags, int rgbBitCount)
        {
            SurfaceFormat expectedFormat = SurfaceFormatFromLoadFormat(loadSurfaceFormat, compressionFormat, pixelFlags, rgbBitCount);

            Texture2D texture = new Texture2D(device, width, height, hasMipMaps, expectedFormat);
            texture.Tag = XNATextureNumAlphaBits(texture);

            if (texture.Format != expectedFormat)
            {
                throw new InvalidDataException($"Can't generate a {expectedFormat} surface.");
            }

            return texture;
        }

        //new 3d-map texture
        private static Texture3D GenerateNewTexture3D(LoadSurfaceFormat loadSurfaceFormat, FourCC compressionFormat, GraphicsDevice device, int width, int height, int depth, bool hasMipMaps, uint pixelFlags, int rgbBitCount)
        {
            SurfaceFormat expectedFormat = SurfaceFormatFromLoadFormat(loadSurfaceFormat, compressionFormat, pixelFlags, rgbBitCount);

            Texture3D texture = new Texture3D(device, width, height, depth, hasMipMaps, expectedFormat);

            if (texture.Format != expectedFormat)
            {
                throw new InvalidDataException($"Can't generate a {expectedFormat} surface.");
            }

            return texture;
        }

        //loads the data from a stream in to a texture object.
        private static void InternalDDSFromStream(Stream stream, GraphicsDevice device, int streamOffset, bool loadMipMap, out Texture texture)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream), "Can't read from a null stream");
            }

            using (BinaryReader reader = new BinaryReader(stream))
            {

                if (streamOffset > reader.BaseStream.Length)
                {
                    throw new InvalidDataException("The stream you offered is smaller then the offset you are proposing for it.");
                }

                reader.BaseStream.Seek(streamOffset, SeekOrigin.Begin);

                //First element of a dds file is a "magic-number" a system to identify that the file is a dds if translated as asci chars the first 4 charachters should be 'DDS '
                if (!(reader.ReadUInt32() == DDS_MAGIC))
                {
                    throw new InvalidDataException("Can't open non DDS data.");
                }

                reader.BaseStream.Position += 8;

                //size in pixels for the texture.
                int height = reader.ReadInt32();
                int width = reader.ReadInt32();

                reader.BaseStream.Position += 4;

                //depth
                int depth = reader.ReadInt32();

                //number of mip-maps.
                int numMips = reader.ReadInt32();

                reader.BaseStream.Position += 4 * 12;

                //pixel format flags
                uint pixelFlags = reader.ReadUInt32();

                // (FOURCC code)
                uint pixelFourCC = reader.ReadUInt32();

                //color bit depth
                int rgbBitCount = reader.ReadInt32();

                //mask for red.
                uint rBitMask = reader.ReadUInt32();

                //mask for green.
                uint gBitMask = reader.ReadUInt32();

                //mask for blue.
                uint bBitMask = reader.ReadUInt32();


                //mask for alpha.
                uint aBitMask = reader.ReadUInt32();

                //reader.BaseStream.Position += 16;

                //texture + mip-map flags.
                int ddsCaps1 = reader.ReadInt32();

                //extra info flags.
                int ddsCaps2 = reader.ReadInt32();
                //ddsCaps3
                //reader.ReadInt32();
                //ddsCaps4
                //reader.ReadInt32();

                //reader.ReadInt32();

                reader.BaseStream.Position += 12;

                bool isCubeMap = IsCubemapTest(ddsCaps1, ddsCaps2);
                bool isVolumeTexture = IsVolumeTextureTest(ddsCaps2);

                FourCC compressionFormat = GetCompressionFormat(pixelFlags, pixelFourCC);

                if (compressionFormat == FourCC.DX10)
                {
                    throw new NotImplementedException("The Dxt 10 header reader is not implemented");
                }

                LoadSurfaceFormat loadSurfaceFormat = GetLoadSurfaceFormat(pixelFlags, pixelFourCC, rgbBitCount, rBitMask, gBitMask, bBitMask, aBitMask);

                bool isCompressed = IsCompressedTest(pixelFlags);
                bool hasMipMaps = CheckFullMipChain(width, height, numMips);
                bool hasAnyMipmaps = numMips > 0;
                hasMipMaps &= loadMipMap;

                if (isCubeMap)
                {
                    TextureCube tex = GenerateNewCubeTexture(loadSurfaceFormat, compressionFormat, device, width, pixelFlags, rgbBitCount);

                    int byteAcumulator = 0;

                    if (numMips == 0)
                    {
                        numMips = 1;
                    }

                    if (!hasMipMaps)
                    {
                        for (int j = 0; j < numMips; j++)
                        {
                            byteAcumulator += MipMapSizeInBytes(j, width, height, isCompressed, compressionFormat, rgbBitCount);
                        }
                    }

                    for (int j = 0; j < numMips; j++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps(streamOffset, j, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        if (hasMipMaps)
                        {
                            byteAcumulator += numBytes;
                        }

                        if (j == 0 || hasMipMaps)
                        {
                            tex.SetData(CubeMapFace.PositiveX, j, null, localMipData, 0, numBytes);
                        }
                        else
                        {
                            break;
                        }
                    }

                    for (int j = 0; j < numMips; j++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps(byteAcumulator + streamOffset, j, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        if (j == 0 || hasMipMaps)
                        {
                            tex.SetData(CubeMapFace.NegativeX, j, null, localMipData, 0, numBytes);
                        }
                        else
                        {
                            break;
                        }
                    }

                    for (int j = 0; j < numMips; j++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps((byteAcumulator * 2) + streamOffset, j, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        if (j == 0 || hasMipMaps)
                        {
                            tex.SetData(CubeMapFace.PositiveY, j, null, localMipData, 0, numBytes);
                        }
                        else
                        {
                            break;
                        }
                    }

                    for (int j = 0; j < numMips; j++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps((byteAcumulator * 3) + streamOffset, j, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        if (j == 0 || hasMipMaps)
                        {
                            tex.SetData(CubeMapFace.NegativeY, j, null, localMipData, 0, numBytes);
                        }
                        else
                        {
                            break;
                        }
                    }

                    for (int j = 0; j < numMips; j++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps((byteAcumulator * 4) + streamOffset, j, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        if (j == 0 || hasMipMaps)
                        {
                            tex.SetData(CubeMapFace.PositiveZ, j, null, localMipData, 0, numBytes);
                        }
                        else
                        {
                            break;
                        }
                    }

                    for (int j = 0; j < numMips; j++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps((byteAcumulator * 5) + streamOffset, j, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        if (j == 0 || hasMipMaps)
                        {
                            tex.SetData(CubeMapFace.NegativeZ, j, null, localMipData, 0, numBytes);
                        }
                        else
                        {
                            break;
                        }
                    }

                    texture = tex;
                }
                else if (isVolumeTexture)
                {
                    Texture3D tex = GenerateNewTexture3D(loadSurfaceFormat, compressionFormat, device, width, height, depth, hasMipMaps, pixelFlags, rgbBitCount);

                    int localStreamOffset = streamOffset;
                    for (int i = 0; i < tex.LevelCount; i++)
                    {
                        int localWidth = MipMapSize(i, width);
                        int localHeight = MipMapSize(i, height);
                        int localDepth = MipMapSize(i, depth);
                        for (int j = 0; j < localDepth; j++)
                        {
                            byte[] localMipData = mipData;
                            GetMipMaps(localStreamOffset, 0, localWidth, localHeight, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                            localStreamOffset += numBytes;
                            mipData = localMipData;

                            tex.SetData(i, 0, 0, localWidth, localHeight, j, j + 1, localMipData, 0, numBytes);
                        }

                    }

                    texture = tex;
                }
                else
                {
                    Texture2D tex = GenerateNewTexture2D(loadSurfaceFormat, compressionFormat, device, width, height, hasMipMaps, pixelFlags, rgbBitCount);

                    for (int i = 0; i < tex.LevelCount; i++)
                    {
                        byte[] localMipData = mipData;
                        GetMipMaps(streamOffset, i, width, height, isCompressed, compressionFormat, rgbBitCount, reader, loadSurfaceFormat, ref localMipData, out int numBytes);
                        mipData = localMipData;

                        tex.SetData(i, null, localMipData, 0, numBytes);
                    }

                    texture = tex;
                }
            }

        }

        private static byte XNATextureNumAlphaBits(Texture texture)
        {
            switch ((texture).Format)
            {
                case SurfaceFormat.Vector2:
                case SurfaceFormat.HalfVector2:
                case SurfaceFormat.Rg32:
                case SurfaceFormat.NormalizedByte2:
                case SurfaceFormat.NormalizedByte4:
                case SurfaceFormat.Bgr565:
                case SurfaceFormat.Single:
                case SurfaceFormat.HalfSingle:
                    return 0;
                case SurfaceFormat.Dxt1:
                case SurfaceFormat.Bgra5551:
                    return 1;
                case SurfaceFormat.Rgba1010102:
                    return 2;
                case SurfaceFormat.Dxt3:
                case SurfaceFormat.Dxt5:
                case SurfaceFormat.Bgra4444:
                    return 4;
                case SurfaceFormat.Alpha8:
                case SurfaceFormat.Color:
                    return 8;
                case SurfaceFormat.Rgba64:
                case SurfaceFormat.HalfVector4:
                    return 16;
                case SurfaceFormat.Vector4:
                    return 32;
                default:
                    return 0;
            }
        }
    }
}
