namespace SolarWeb.Pneuma.Data
{
  public struct WorldPos
  {
    public int X;
    public int Z;

    public static readonly WorldPos North = new(0, 1);

    public static readonly WorldPos East = new(1, 0);

    public static readonly WorldPos South = new(0, -1);

    public static readonly WorldPos West = new(-1, 0);

    public static WorldPos operator +(WorldPos a, WorldPos b)
    {
      return new WorldPos(a.X + b.X, a.Z + b.Z);
    }

    public static WorldPos operator -(WorldPos a, WorldPos b)
    {
      return new WorldPos(a.X - b.X, a.Z - b.Z);
    }

    public static int PosToIndex(int x, int z, int sizeX)
    {
      return z * sizeX + x;
    }

    public WorldPos(int x, int z)
    {
      X = x;
      Z = z;
    }
  }
}