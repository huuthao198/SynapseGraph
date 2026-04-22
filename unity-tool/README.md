# 🧠 SynapseGraph (Unity Tool)

**SynapseGraph** is a professional static analysis tool for Unity that utilizes **Roslyn AST (Abstract Syntax Tree)** to map out complex code architectures.

## ✨ Key Features
- **Deep Semantic Analysis**: Powered by Microsoft Roslyn, it scans deep into method bodies to identify true dependencies, ignoring comments and strings.
- **Unity Editor Integration**: Dedicated Editor Window with a streamlined workflow for developers.
- **Extensible Pipeline**: Uses a modular processor system to extract metadata, members, and deep dependencies.
- **Optimized for UPM**: Fully compliant with the Unity Package Manager (UPM) standard for easy integration and updates.

## 🛠 Project Structure
- **Editor/**: Contains the core analysis logic, UI windows, and Roslyn DLL plugins.
- **Runtime/**: Defines the data models used for JSON serialization.
- **Tests/**: Sample scripts and mock classes for verifying analysis accuracy.

## 🚀 Quick Start Guide
1. Go to **Tools > SynapseGraph > Export Architecture**.
2. **Target Folders**: Drag and drop the folders containing the C# scripts you want to analyze.
3. **Scan & Export**: Click the button to process your code. 
4. **Result**: A JSON file will be generated in `Assets/SynapseData/`. This file can be uploaded to the [SynapseGraph Web Visualizer](https://huuthao198.github.io/SynapseGraph/) for a neural-style graph view.

## ⚙️ Requirements
- **Unity**: 2021.3 or higher.
- **Assembly Definitions**: Ensure your project uses `.asmdef` for better compilation performance.

## 📩 Support
- **Developer**: GaconStudio
- **Email**: [huuthao198@gmail.com](mailto:huuthao198@gmail.com)
- **License**: MIT
