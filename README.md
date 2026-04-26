# Pneuma

Pneuma is a high-performance atmospheric simulation system designed for complex gas transport, combustion, and biological exchange. It utilizes Unity's Job System and Burst compiler to achieve high-speed, scalable data processing.

## Repository Structure

This repository follows the official Unity Package Manager (UPM) layout.

- **Runtime/**: Core simulation logic, shared data structures, and Burst-compiled jobs.
  - **Shared/**: Foundational blittable types and constants.
  - **Jobs/**: High-performance physics and metabolism jobs.
  - **Simulation/**: The `AtmosphereManager` and orchestration logic.
- **Editor/**: Integration tools and build hooks for the Unity Editor.
- **Samples~/**: Example scenes and scripts demonstrating package usage.
- **package.json**: UPM manifest defining dependencies and package metadata.

## Installation

### As a Unity Package (UPM)
To use Pneuma in your Unity project, add it via the Package Manager:

1. Open the **Package Manager** in Unity (`Window > Package Manager`).
2. Click the **+** icon and select **Add package from git URL...**
3. Enter `https://github.com/SolarwebGames/Pneuma.git`.

#### Importing Samples
Once installed, import samples through the Package Manager:
1. Select **Pneuma** in the Package Manager list.
2. Find the **Samples** section and click **Import** next to "Basic Simulation".

Samples are currently in progress, the basic simulation sample is not complete and not expected to work at this time.

### Standalone Compilation
This repository includes `.dotnet.csproj` files at the root to allow for standalone compilation of DLLs outside of the Unity Editor. Using these files will skip Burst compilation.

**Note:** For production distribution, the jobs must be compiled within the Unity Editor to leverage the Burst compiler.

1. Ensure you have the .NET SDK installed.
2. Run `dotnet build Pneuma.dotnet.sln` to compile the assemblies.

## Overview

The simulation operates on a region-based graph for efficient macro-scale gas transport while supporting cell-level precision through dynamic region splitting - any region can be split into cell-sized regions for higher fidelity. Key physical processes include:

- **Diffusion**: Pressure-driven and concentration-driven gas flow between regions.
- **Combustion**: Multi-gas chemical reactions with thermal energy release and byproduct production.
- **Phase Transitions**: Handling state changes (condensation, evaporation) within the atmospheric grid.
- **Metabolism**: A branchless SIMD-vectorized model for biological gas exchange, employing an Array of Structures of Arrays (AoSoA) memory layout.

## Setup and Dependencies

Pneuma requires the following packages (automatically resolved via UPM):

- `com.unity.burst`
- `com.unity.collections`
- `com.unity.mathematics`

### Auto-Copy

Pneuma includes a post-build script (`CopyBuiltSimulation.cs`) that can automatically export the compiled libraries (both managed assemblies and the Burst-compiled native binaries) when you build your Unity project. 

To use this feature, ensure you have a `PneumaSettings` asset created in your project. The script will use the configuration defined in this asset to determine the output paths:
- **Managed Assemblies**: Copied to the `AssembliesFolder` within the specified `OutputPath`.
- **Burst Libraries**: Copied to the `BurstFolder` within the `OutputPath`, appropriately organized by platform (`Windows`, `OSX`, `Linux`) and renamed to `PneumaJobs`.

### Loading Burst Assemblies
When using pre-compiled binaries or running outside of the standard Unity build pipeline, you must manually load the Burst-compiled libraries to retain native performance. 

Pneuma provides the `BurstAssemblyLoader` utility to dynamically load platform-specific Burst assemblies (`.dll`, `.dylib`, or `.so`). The loader expects the provided directory to contain subdirectories for each platform (`Windows`, `OSX`, `Linux`).

```csharp
using SolarWeb.Pneuma;

// Loads the appropriate Burst library for the current platform
List<string> loadedAssemblyNames = BurstAssemblyLoader.LoadBurstAssemblies("Path/To/Burst/Assemblies");
```
*(Note: If the jobs fail to load, Pneuma will gracefully fall back to managed execution with a significant performance penalty.)*

## Quick Start

### 1. Initialization
Initialization requires registering the atomic and gas definitions that will be present in the simulation.

```csharp
// 1. Define Atoms and Gases
var atoms = new List<AtomDefinition> { /* ... */ };
var gases = new List<GasDefinition> { /* ... */ };

// 2. Build the Grid
var config = new SimulationConfig { /* ... */ };
var grid = AtmosphereGridBuilder.RebuildSimulation(config, new GasRegistry());

// 3. Initialize the Manager
AtmosphereManager manager = new AtmosphereManager(grid);
manager.Initialize(atoms, gases);
```

### 2. Topology Management
Update physical properties of connections (e.g., doors, pumps) or split cells into dynamic regions:

```csharp
// Update connection properties
AtmosphereGridBuilder.UpdateRegionFaceProperties(
  grid, 
  faceIndex, 
  permeability, 
  thermalConductivity, 
  minColDia, 
  maxColDia, 
  flowDir, // sbyte: -1, 0, or 1
  pumpRate,
  maxPumpPressureKpa,
  regulatorKpa
);

// Queue a cell split for localized precision
manager.EnqueueSplit(worldCellIndex);
```

### 3. Simulation Loop
Update the simulation in your main loop. `manager.Tick` handles synchronization, job scheduling, and maintenance.

```csharp
void Update() {
  manager.Tick(Time.deltaTime, 1);
}
```

## Tests
Tests are currently in progress and not working at this time. Feel free to submit your own contributions to help add test coverage!

## Contributing
Contributions are welcome! Please follow these guidelines to maintain the project's standards:

### Code Conventions
To ensure consistency and performance (especially for Burst compatibility), all contributions must adhere to the following:
- **Indentation**: Use **2 spaces** for indentation.
- **Data Layout**: Keep shared data structures (in `Runtime/Shared`) **blittable** for compatibility with Unity's Job System and Burst compiler.
- **Performance**: Avoid managed allocations (garbage collection) within the simulation loop and Jobs.
- **Style**: Follow standard C# naming conventions (PascalCase for methods/properties, camelCase for local variables).

### Bug Reports
If you find a bug, please open an issue with the following information:
1.  **Reproduction Steps**: A clear, numbered list of steps to reproduce the issue.
2.  **Expected vs. Actual Behavior**: What you expected to happen and what actually happened.
3.  **Environment Details**: Unity version, package versions, and target platform.
4.  **Screenshots/Logs**: If applicable, include relevant console logs or visual evidence of the bug.

### Pull Requests
1. **Fork the Repository**: Create your own fork and branch off `main`.
2. **Commit Changes**: Ensure your commits are descriptive and follow the project's code conventions.
3. **Tests**: Add tests for any new features or bug fixes.
4. **Submit**: Open a PR against the `main` branch. Provide a clear description of the problem solved or feature added.

For major changes, please open an issue first to discuss your proposed updates.

## License
This project is licensed under the [MIT License](LICENSE).
