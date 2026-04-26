using Unity.Collections;

namespace SolarWeb.Pneuma.Atoms
{
    public struct AtomicDataBuffer
    {
        public NativeArray<float> AtomicMass;
        public NativeArray<float> AtomicRadius;
        public NativeArray<float> Electronegativity;
        public NativeArray<float> IonizationEnergy;
        public NativeArray<float> Radioactivity;
        
        // --- Derived Geometry & Physics ---
        public NativeArray<float> InverseRadius;
        public NativeArray<float> AtomicVolume;
        
        // --- Chemical Behavior ---
        public NativeArray<float> ChemicalHardness;
        public NativeArray<float> MolarHeatCapacity;
        public NativeArray<int> PrincipalOxidationState;
        public NativeArray<int> StandardOxideGasId;
        
        // --- Coordination Profile ---
        public NativeArray<int> DefaultCoordination;
        public NativeArray<int> MaxCoordination;
        
        // --- Periodic Table Position ---
        public NativeArray<int> Group;
        public NativeArray<int> Period;
    }
}