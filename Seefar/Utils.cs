namespace Seefar;

public static class Utils
{
    public static long MapRegionIndex2D(int regionX, int regionZ)
    {
        return (long)regionZ * (long)16 + regionX;
    }

}
