# MARUS2 Core

Core systems, shared utilities, and base sensor/networking frameworks for the MARUS2 simulator. Designed for Unity 6.

## Installation

This package is structured as a standard Unity Package Manager (UPM) package. You can install it directly into your Unity 6 project via Git URL:

1. Open your Unity project.
2. Navigate to **Window > Package Manager**.
3. Click the **`+`** button in the top-left corner.
4. Select **"Add package from git URL..."**.
5. Paste the repository URL (optionally appending a version tag or branch, e.g., `#v1.0.0` or `#main`):
   ```text
   https://github.com/MARUSimulator/marus2-core.git
   ```

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

Link the local clones to your Unity test project via UPM so any changes you make in your IDE are immediately compiled by the Unity editor:

1. Open your Unity 6 test project.
2. Open **Window > Package Manager**.
3. Click the **`+`** button and select **"Add package from disk..."**.
4. Navigate to your local `marus2-proto` folder and select its `package.json`.
5. Repeat the process for `marus2-core`: select **"Add package from disk..."** and choose `marus2-core/package.json`.

Alternatively, add them directly to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.labust.marus2.proto": "file:../../path/to/marus2-proto",
    "com.labust.marus2.core": "file:../../path/to/marus2-core"
  }
}
```

### 3. Workflow & Guidelines

* **Working with Protobufs:** If your changes require modifications to network messages, RPC endpoints, or sensor data structures, update the `.proto` files in the `main` branch of `marus2-proto` first. Let the CI/CD pipeline generate the C# bindings on the `csharp` branch, pull those changes, and then update `marus2-core`.
* **Branching:** Create a feature branch for your work (`git checkout -b feature/your-feature-name`). If your changes span both repositories, ensure corresponding branches are created in both `marus2-core` and `marus2-proto`.
* **Performance Considerations:** Code targeting per-frame updates or sensor raycasting should leverage Unity's C# Job System, Burst Compiler, and `Unity.Collections` allocations without generating per-frame managed garbage.