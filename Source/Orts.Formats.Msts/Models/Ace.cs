namespace Orts.Formats.Msts.Models
{
    public class SimisAceChannel
    {
        public int Size { get; private set; }
        public SimisAceChannelId Type { get; private set; }

        public SimisAceChannel(int size, SimisAceChannelId type)
        {
            Size = size;
            Type = type;
        }
    }

    //public class SimisAceImage
    //{
    //    public int[] Color { get; private set; }
    //    public int[] Mask { get; private set; }

    //    public SimisAceImage(int[] color, int[] mask)
    //    {
    //        Color = color;
    //        Mask = mask;
    //    }
    //}
}
