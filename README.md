# 🧠 SynapseGraph Ecosystem

**SynapseGraph** is a high-performance source code architecture analysis toolset designed specifically for Unity developers. It bridges the gap between complex codebases and visual understanding by mapping class relationships into an interactive neural-style graph.

## 📁 Project Components
* **`unity-tool`**: A Unity Package (UPM) that performs static analysis on your C# scripts using **Roslyn AST** and exports structured data to JSON.
* **`web-visualizer`**: A lightweight, web-based dashboard that renders the exported JSON into a dynamic, force-directed neural network graph.

## ✨ Key Features
* **Deep Semantic Analysis**: Powered by Microsoft Roslyn, it analyzes method calls, field types, and property assignments within the method bodies, ignoring comments and string literals for 100% accuracy.
* **Modular Pipeline**: Built with an extensible processor architecture (Basic Info, Reflection-based Members, and Roslyn AST).
* **Unity Editor Integration**: Features a user-friendly editor window with drag-and-drop support for target folders.
* **High-Speed Visualization**: Web-based renderer capable of handling complex projects with hundreds of interconnected nodes.

## 📥 Installation (Unity Tool)
To add this tool to your Unity project via the **Package Manager**:
1. Click the **+** button in the top left corner.
2. Select **Add package from git URL...**.
3. Paste the following URL:
   `https://github.com/huuthao198/SynapseGraph.git?path=/unity-tool`

## 🌐 Live Demo (Web Visualizer)
You can access the online visualizer here:
👉 **[https://huuthao198.github.io/SynapseGraph/](https://huuthao198.github.io/SynapseGraph/)**

## 📖 Quick Start Guide
1. Open the tool in Unity: **Tools > SynapseGraph > Export Architecture**.
2. Drag and drop your script folders into the **Target Folders** list.
3. Click **SCAN & EXPORT**.
4. Locate the generated JSON in `Assets/SynapseData/`.
5. Upload the JSON file to the Web Visualizer to see your code's "neural network".

## 📩 Contact & Support
* **Author**: GaconStudio
* **Email**: [huuthao198@gmail.com](mailto:huuthao198@gmail.com)
* **License**: MIT
