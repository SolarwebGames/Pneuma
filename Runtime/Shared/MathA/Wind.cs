using Unity.Collections;

namespace SolarWeb.Pneuma.MathA
{
  public static class Wind
  {
    public static float ComputeExposure(int i, int width, int height, NativeArray<byte> blocks, NativeArray<byte> roofed)
    {
      if (roofed[i] == 1) return 0f;

      int x = i % width;
      int z = i / width;

      int totalUnblocked = 0;

      for (int dx = 1; dx <= 10; dx++)
      {
        int nx = x + dx;
        if (nx >= width || blocks[z * width + nx] == 1) break;
        totalUnblocked++;
      }
      for (int dx = 1; dx <= 10; dx++)
      {
        int nx = x - dx;
        if (nx < 0 || blocks[z * width + nx] == 1) break;
        totalUnblocked++;
      }
      for (int dz = 1; dz <= 10; dz++)
      {
        int nz = z + dz;
        if (nz >= height || blocks[nz * width + x] == 1) break;
        totalUnblocked++;
      }
      for (int dz = 1; dz <= 10; dz++)
      {
        int nz = z - dz;
        if (nz < 0 || blocks[nz * width + x] == 1) break;
        totalUnblocked++;
      }

      return totalUnblocked / 40.0f;
    }
  }
}