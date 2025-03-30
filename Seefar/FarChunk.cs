namespace Seefar;

public class FarChunk
{
    int[] heightmap;

    public int[] Heightmap => heightmap;

    public FarChunk(int[] heightmap)
    {
        this.heightmap = heightmap;
    }
}
