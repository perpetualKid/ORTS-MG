using System;

using Orts.Formats.Msts.Parsers;

namespace Orts.Formats.Msts.Models
{
    public class WaterLayer
    {
        public float Height { get; private set; }
        public string TextureName { get; private set; }

        internal WaterLayer(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("world_water_layer_height", ()=>{ Height = stf.ReadFloatBlock(STFReader.Units.Distance, null); }),
                new STFReader.TokenProcessor("world_anim_shader", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("world_shader", ()=>{ stf.MustMatchBlockStart(); stf.ReadString()/*TextureMode*/; stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("terrain_texslots", ()=>{ stf.MustMatchBlockStart(); stf.ReadInt(null)/*Count*/; stf.ParseBlock(new STFReader.TokenProcessor[] {
                            new STFReader.TokenProcessor("terrain_texslot", ()=>{ stf.MustMatchBlockStart(); TextureName = stf.ReadString(); stf.SkipRestOfBlock(); }),
                            });}),
                        });}),
                    });}),
                });
        }
    }

    public class SkyLayer
    {
        public string FadeinStartTime { get; private set; }
        public string FadeinEndTime { get; private set; }
        public string TextureName { get; private set; }
        public string TextureMode { get; private set; }
        public float TileX { get; private set; }
        public float TileY { get; private set; }

        internal SkyLayer(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("world_sky_layer_fadein", ()=>{ stf.MustMatchBlockStart(); FadeinStartTime = stf.ReadString(); FadeinEndTime = stf.ReadString(); stf.SkipRestOfBlock();}),
                        new STFReader.TokenProcessor("world_anim_shader", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                            new STFReader.TokenProcessor("world_anim_shader_frames", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                                new STFReader.TokenProcessor("world_anim_shader_frame", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                                    new STFReader.TokenProcessor("world_anim_shader_frame_uvtiles", ()=>{ stf.MustMatchBlockStart(); TileX = stf.ReadFloat(STFReader.Units.Any, 1.0f); TileY = stf.ReadFloat(STFReader.Units.Any, 1.0f); stf.ParseBlock(Array.Empty<STFReader.TokenProcessor>()
                                        );}),
                                });}),
                            });}),
                            new STFReader.TokenProcessor("world_shader", ()=>{ stf.MustMatchBlockStart(); TextureMode = stf.ReadString(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                                new STFReader.TokenProcessor("terrain_texslots", ()=>{ stf.MustMatchBlockStart(); stf.ReadInt(null)/*Count*/; stf.ParseBlock(new STFReader.TokenProcessor[] {
                                    new STFReader.TokenProcessor("terrain_texslot", ()=>{ stf.MustMatchBlockStart(); TextureName = stf.ReadString(); stf.SkipRestOfBlock(); }),
                                    });}),
                                });}),
                            });}),
            });

        }
    }

    public class SkySatellite
    {

        public string TextureName { get; private set; }
        public string TextureMode { get; private set; }

        internal SkySatellite(STFReader stf)
        {
            stf.MustMatchBlockStart();
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("world_anim_shader", ()=>{ stf.MustMatchBlockStart(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                    new STFReader.TokenProcessor("world_shader", ()=>{ stf.MustMatchBlockStart(); TextureMode = stf.ReadString(); stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("terrain_texslots", ()=>{ stf.MustMatchBlockStart(); stf.ReadInt(null)/*Count*/; stf.ParseBlock(new STFReader.TokenProcessor[] {
                            new STFReader.TokenProcessor("terrain_texslot", ()=>{ stf.MustMatchBlockStart(); TextureName = stf.ReadString(); stf.SkipRestOfBlock(); }),
                            });}),
                        });}),
                    });}),
                });
        }
    }
}
