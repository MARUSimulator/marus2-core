To install **MARUS2 Core** and its required core dependencies, add them directly to your Unity project's **`Packages/manifest.json`** file under the `"dependencies"` block:

```json
{
  "dependencies": {
    "com.marus2.proto": "https://github.com/MARUSimulator/marus2-proto.git#csharp",
    "com.marus2.core": "https://github.com/MARUSimulator/marus2-core.git"
  }
}
```

# Core usage

MARUS2 Core provides the foundational architecture for the MARUS2 marine robotics simulator in Unity, including networking, transform handling, marine physics, actuation, vehicle controllers, and sensor base abstractions.

## ROS Connection

Manages the central gRPC connection between Unity and the external ROS/ROS2 adapter (`grpc_ros_adapter`). It orchestrates background communication threads, connection lifecycles, and gRPC client creation for all sensor and actuator modules.

## Time Handler

Synchronizes Unity simulation time with external ROS time and clock topics, supporting deterministic simulation stepping and pause control.

## TF Handler

Manages the coordinate frame transformation tree (TF) between vehicles, sensors, and the global simulation frame, handling coordinate frame conversions between Unity's coordinate system and standard robotics conventions (NED/ENU).

## Boat Physics & Buoyancy

Simulates marine surface and subsurface vessel hydrodynamics, including watercraft mesh-based buoyancy, hydrodynamic drag, slamming forces, and wave surface interactions.

## Actuators

Provides propulsion and steering models for marine vehicles, including thrusters (`Thruster`), differential thruster controllers (`DifferentialThrusterController`), and dynamic motor response modeling.

## Vehicle Controllers

Primitive control interfaces for autonomous surface vehicles (`ASVPrimitiveController`), autonomous underwater vehicles (`AUVPrimitiveController`), and force/velocity interfaces (`VesselForceController`, `VesselVelocityController`).

## Geographic Frame & GeoPoint

Converts between real-world geodetic coordinates (WGS84 latitude, longitude, and altitude) and Unity Cartesian coordinates.

## Raycast Job Helper & Sensor Base

High-performance Burst-compatible raycasting job batching utilities and base classes for building custom sensors with minimal garbage collection.

## Noise Distributions

Configurable statistical noise models (Gaussian, uniform, random walk) for realistic sensor and actuation noise simulation.

---

## Contributing & Local Development

If you plan to contribute code or modify the core systems, do not install via Git URL. UPM Git installations are placed in the read-only `Library/PackageCache` directory.

Because `marus2-core` depends directly on generated gRPC and serialization schemas, **you must also clone `marus2-proto` alongside this repository** and switch it to the `csharp` branch to access the generated Unity bindings.

### 1. Clone the Repositories

Clone both repositories into the same parent folder (or inside your development workspace):

```bash
# Clone the core engine package
git clone https://github.com/MARUSimulator/marus2-core.git

# Clone the shared protobuf definitions (required)
git clone https://github.com/MARUSimulator/marus2-proto.git

# VERY IMPORTANT: Switch marus2-proto to the C# generated branch
cd marus2-proto
git checkout csharp
```

> **Note:** The `main` branch of `marus2-proto` only contains raw `.proto` definitions. The generated C# code (required by Unity) is automatically generated and stored on the orphan `csharp` branch.

### 2. Add Local Packages to Your Unity Project

Link the local clones to your Unity test project via UPM:

1. Open your Unity 6 test project.
2. Open **Window > Package Manager**.
3. Click the **`+`** button and select **"Add package from disk..."**.
4. Navigate to your local `marus2-proto` folder and select its `package.json`.
5. Repeat the process for `marus2-core`: select **"Add package from disk..."** and choose `marus2-core/package.json`.

Alternatively, add them directly to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.marus2.proto": "file:../../path/to/marus2-proto",
    "com.marus2.core": "file:../../path/to/marus2-core"
  }
}
```

### 3. Workflow & Guidelines

* **Working with Protobufs:** If your changes require modifications to network messages, RPC endpoints, or sensor data structures, update the `.proto` files in the `main` branch of `marus2-proto` first. Let the CI/CD pipeline generate the C# bindings on the `csharp` branch, pull those changes, and then update `marus2-core`.
* **Branching:** Create a feature branch for your work (`git checkout -b feature/your-feature-name`). If your changes span both repositories, ensure corresponding branches are created in both `marus2-core` and `marus2-proto`.
* **Performance Considerations:** Code targeting per-frame updates or sensor raycasting should leverage Unity's C# Job System, Burst Compiler, and `Unity.Collections` allocations without generating per-frame managed garbage.