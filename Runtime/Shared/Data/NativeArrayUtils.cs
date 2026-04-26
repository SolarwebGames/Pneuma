using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SolarWeb.Pneuma.Data
{
  public static class NativeArrayUtils
  {
    public static void Resize<T>(ref this NativeArray<T> array, int newSize, Allocator allocator = Allocator.Persistent) where T : unmanaged
    {
      var newArray = new NativeArray<T>(newSize, allocator);
      if (array.IsCreated)
      {
        NativeArray<T>.Copy(array, newArray, math.min(array.Length, newSize));
        array.Dispose();
      }
      array = newArray;
    }

    public unsafe static void Clear<T>(ref this NativeArray<T> array) where T : unmanaged
    {
      if (array.IsCreated)
      {
        UnsafeUtility.MemClear(array.GetUnsafePtr(), (long)array.Length * UnsafeUtility.SizeOf<T>());
      }
    }

    public static void Fill<T>(this NativeArray<T> array, T value) where T : unmanaged
    {
      if (array.IsCreated)
      {
        for (int i = 0; i < array.Length; i++) array[i] = value;
      }
    }

    public static void SafeDispose<T>(ref this NativeArray<T> array) where T : unmanaged
    {
      if (array.IsCreated) array.Dispose();
    }

    public static void SafeDispose<T>(ref this NativeList<T> array) where T : unmanaged
    {
      if (array.IsCreated) array.Dispose();
    }
  }
}