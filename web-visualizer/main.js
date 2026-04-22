let graph = null;
let globalData = null;
let activeNodeId = null;

// ================= FILE & DRAG EVENTS =================
const fileInput = document.getElementById('file-input');
const btnUpload = document.getElementById('btn-upload');
const canvasWrapper = document.getElementById('canvas-wrapper');

btnUpload.addEventListener('click', () => fileInput.click());
fileInput.addEventListener('change', (e) => handleFile(e.target.files[0]));
canvasWrapper.addEventListener('dragover', (e) => { e.preventDefault(); canvasWrapper.classList.add('drag-over'); });
canvasWrapper.addEventListener('dragleave', () => canvasWrapper.classList.remove('drag-over'));
canvasWrapper.addEventListener('drop', (e) => {
    e.preventDefault(); canvasWrapper.classList.remove('drag-over');
    handleFile(e.dataTransfer.files[0]);
});

function handleFile(file) {
    if (!file || !file.name.endsWith('.json')) return;
    const reader = new FileReader();
    reader.onload = (event) => {
        globalData = JSON.parse(event.target.result);
        updateStatistics(globalData);
        buildFileTree(globalData.Classes);
        renderForceGraph(globalData);
    };
    reader.readAsText(file);
}

// ================= STATS & COLOR LOGIC =================
function updateStatistics(data) {
    let totalClasses = data.Classes.length;
    let totalDeps = 0;
    data.Classes.forEach(c => {
        c.Methods.forEach(m => { if (m.MethodDependencies) totalDeps += m.MethodDependencies.length; });
    });
    let coupling = totalClasses > 0 ? (totalDeps / totalClasses).toFixed(2) : 0;

    document.getElementById('stat-classes').innerText = totalClasses;
    document.getElementById('stat-deps').innerText = totalDeps;
    document.getElementById('stat-coupling').innerText = coupling;
}

function getNodeColor(cls) {
    if (cls.Kind === "Enum") return "#c678dd"; 
    if (cls.Kind === "Struct") return "#d19a66"; 
    if (cls.Kind === "Interface") return "#b5cea8"; 

    if (cls.Namespace.includes("Editor") || (cls.FolderPath && cls.FolderPath.includes("Editor")) || cls.BaseClass === "Editor" || cls.BaseClass === "EditorWindow") {
        return "#e06c75"; 
    }

    if (cls.BaseClass === "ScriptableObject") return "#e5c07b"; 
    if (cls.BaseClass === "MonoBehaviour") return "#569cd6"; 
    if (cls.Namespace.includes("Core")) return "#4ec9b0"; 

    return "#abb2bf"; 
}

// ================= FORCE GRAPH =================
function renderForceGraph(data) {
    const elem = document.getElementById('canvas');
    elem.innerHTML = ''; 

    const gData = { nodes: [], links: [] };

    data.Classes.forEach(cls => {
        gData.nodes.push({ id: cls.Name, name: cls.Name, colorGroup: getNodeColor(cls), details: cls });
    });

    data.Classes.forEach(cls => {
        cls.Methods.forEach(method => {
            if (!method.MethodDependencies) return;
            method.MethodDependencies.forEach(dep => {
                if (data.Classes.find(c => c.Name === dep.TargetClass)) {
                    gData.links.push({ source: cls.Name, target: dep.TargetClass, label: method.Name, type: 'LogicCall' });
                }
            });
        });
    });

    // SMART TRACKING LINKS
    data.Classes.forEach(cls => {
        if (cls.Interfaces && cls.Interfaces.length > 0) {
            cls.Interfaces.forEach(iface => {
                if (!gData.nodes.find(n => n.id === iface)) {
                    gData.nodes.push({ id: iface, name: iface, colorGroup: "#b5cea8", details: { Kind: "Interface", Name: iface, Namespace: "System/Unity" } });
                }
                gData.links.push({ source: cls.Name, target: iface, type: 'Implements' });
            });
        }
        if (cls.Methods && cls.Methods.length > 0) {
            cls.Methods.forEach(method => {
                if (method.FiredSignals && method.FiredSignals.length > 0) {
                    method.FiredSignals.forEach(sig => {
                        if (!gData.nodes.find(n => n.id === sig)) {
                            gData.nodes.push({ id: sig, name: sig, colorGroup: "#ff55ff", details: { Kind: "Signal", Name: sig, Namespace: "EventSystem" } });
                        }
                        const exists = gData.links.find(l => l.source === cls.Name && l.target === sig && l.type === 'FiresSignal');
                        if (!exists) {
                            gData.links.push({ source: cls.Name, target: sig, type: 'FiresSignal' });
                        }
                    });
                }
            });
        }
    });

    const rect = canvasWrapper.getBoundingClientRect();
    
    graph = ForceGraph()(elem)
        .width(rect.width).height(rect.height)
        .graphData(gData)
        .nodeLabel(node => document.getElementById('check-labels').checked ? node.name : '')
        .nodeColor(node => node.id === activeNodeId ? '#fff' : node.colorGroup) 
        .nodeCanvasObject((node, ctx, globalScale) => {
            const isHighlight = node.id === activeNodeId;
            const baseSize = parseInt(document.getElementById('slider-nodesize').value) || 5;
            const size = isHighlight ? baseSize * 1.5 : baseSize;
            
            ctx.beginPath();
            ctx.arc(node.x, node.y, size, 0, 2 * Math.PI, false);
            ctx.fillStyle = isHighlight ? '#fff' : node.colorGroup;
            if(isHighlight) { ctx.shadowColor = '#fff'; ctx.shadowBlur = 10; }
            ctx.fill();
            ctx.shadowBlur = 0; 

            if (document.getElementById('check-labels').checked) {
                const fontSize = 12 / globalScale;
                ctx.font = `${fontSize}px Sans-Serif`;
                ctx.textAlign = 'center'; ctx.textBaseline = 'top';
                ctx.fillStyle = isHighlight ? '#fff' : '#c9d1d9';
                ctx.fillText(node.name, node.x, node.y + size + 2);
            }
        })
        .linkColor(link => {
            if (activeNodeId && (link.source.id === activeNodeId || link.target.id === activeNodeId)) return '#e5c07b';
            if (link.type === 'FiresSignal') return '#ff55ff'; 
            if (link.type === 'Implements') return '#b5cea8';
            return '#30363d';
        })
        .linkWidth(link => (activeNodeId && (link.source.id === activeNodeId || link.target.id === activeNodeId)) ? 2 : 1)
        .linkDirectionalParticles(link => {
            if (activeNodeId && (link.source.id === activeNodeId || link.target.id === activeNodeId)) return 4;
            if (link.type === 'FiresSignal') return 3;
            return 2;
        })
        .linkDirectionalParticleSpeed(0.005)
        .onNodeClick((node, event) => {
            focusNode(node.id);
            showQuickTooltip(node);
        })
        .onBackgroundClick(() => {
            activeNodeId = null;
            document.getElementById('quick-tooltip').style.display = 'none';
            if(graph) {
                const currentFunc = graph.nodeCanvasObject();
                graph.nodeCanvasObject(currentFunc); 
                graph.linkColor(graph.linkColor()).linkWidth(graph.linkWidth()).linkDirectionalParticles(graph.linkDirectionalParticles());
            }
        });

    applyGraphSettings();
    window.addEventListener('resize', () => {
        const newRect = canvasWrapper.getBoundingClientRect();
        graph.width(newRect.width).height(newRect.height);
    });
}

// ================= GRAPH SETTINGS =================
const repelSlider = document.getElementById('slider-repel');
const distSlider = document.getElementById('slider-distance');
const nodeSizeSlider = document.getElementById('slider-nodesize');
const labelCheck = document.getElementById('check-labels');

repelSlider.addEventListener('input', e => {
    document.getElementById('val-repel').innerText = e.target.value;
    applyGraphSettings();
});
distSlider.addEventListener('input', e => {
    document.getElementById('val-distance').innerText = e.target.value;
    applyGraphSettings();
});
nodeSizeSlider.addEventListener('input', e => {
    document.getElementById('val-nodesize').innerText = e.target.value;
    if(graph) {
        const currentFunc = graph.nodeCanvasObject();
        graph.nodeCanvasObject(currentFunc);
    }
});
labelCheck.addEventListener('change', () => {
    if(graph) graph.nodeLabel(node => labelCheck.checked ? node.name : '');
});

function applyGraphSettings() {
    if(!graph) return;
    graph.d3Force('charge').strength(-repelSlider.value);
    graph.d3Force('link').distance(distSlider.value);
    graph.alpha(1).restart();
}

// ================= QUICK TOOLTIP =================
window.closeTooltip = function(e) {
    if(e) e.stopPropagation(); 
    document.getElementById('quick-tooltip').style.display = 'none';
}

function showQuickTooltip(node) {
    const tooltip = document.getElementById('quick-tooltip');
    const cls = node.details;
    const { x, y } = graph.graph2ScreenCoords(node.x, node.y);
    
    tooltip.style.left = `${x + 20}px`;
    tooltip.style.top = `${y + 20}px`;
    tooltip.style.display = 'block';

    tooltip.innerHTML = `
        <div style="display:flex; justify-content:space-between; align-items:flex-start;">
            <h4 style="margin:0; padding-right:20px; color:#569cd6; font-size:14px;">${cls.Kind || 'Class'} ${cls.Name}</h4>
            <div style="cursor:pointer; color:#e06c75; font-size:14px; font-weight:bold; line-height:1;" onclick="closeTooltip(event)">✕</div>
        </div>
        <div class="t-row" style="margin-top:8px;"><b>Namespace:</b> <span class="t-val">${cls.Namespace}</span></div>
        <div class="t-row"><b>BaseClass:</b> <span class="t-val">${cls.BaseClass || 'None'}</span></div>
        <div class="t-row" style="margin-top:5px; font-style:italic; color:#858585; font-size:11px;">(Xem chi tiết ở Inspector)</div>
    `;
}

// ================= NESTED TREE VIEW =================
function buildFileTree(classes) {
    const treeContainer = document.getElementById('file-tree');
    const fileTree = {};
    classes.forEach(cls => {
        const parts = (cls.FolderPath || "Root").split('/');
        let currentLevel = fileTree;
        parts.forEach(part => {
            if (!currentLevel[part]) currentLevel[part] = { _files: [] };
            currentLevel = currentLevel[part];
        });
        currentLevel._files.push(cls.Name);
    });

    function renderTree(node, isRoot = false) {
        let html = '';
        Object.keys(node).sort().forEach(key => {
            if (key === '_files') return;
            html += `<div class="tree-node"><div class="tree-caret">${key}</div><div class="tree-nested">${renderTree(node[key])}</div></div>`;
        });
        if (node._files) {
            node._files.sort().forEach(file => {
                html += `<div class="tree-file" onclick="focusNode('${file}')">${file}.cs</div>`;
            });
        }
        return html;
    }

    treeContainer.innerHTML = renderTree(fileTree, true);
    document.querySelectorAll('.tree-caret').forEach(caret => {
        caret.addEventListener('click', function() {
            this.classList.toggle('caret-down');
            this.nextElementSibling.classList.toggle('tree-active');
        });
    });
}

// ================= FOCUS & INSPECTOR (WITH COLLAPSIBLE) =================
window.focusNode = function(clsName) {
    if (!graph) return;
    activeNodeId = clsName;

    const currentFunc = graph.nodeCanvasObject();
    graph.nodeCanvasObject(currentFunc);
    graph.linkColor(graph.linkColor()).linkWidth(graph.linkWidth()).linkDirectionalParticles(graph.linkDirectionalParticles());
    
    const nodeData = graph.graphData().nodes.find(n => n.id === clsName);
    if (nodeData) {
        graph.centerAt(nodeData.x, nodeData.y, 1000);
        graph.zoom(2.5, 1000);
        updateInspector(nodeData.details);
    }
}

// [NEW] Hàm Helper tạo Component Cuộn (Accordion)
function buildCollapsibleSection(title, contentHtml, isOpen = false) {
    if (!contentHtml || contentHtml.includes('Không có')) {
        // Nếu không có data, in ra mờ nhạt, không cho click cuộn
        return `
        <div class="inspect-section">
            <div class="inspect-title" style="color:#858585; cursor:default;">
                <span style="display:inline-block; width:10px;"></span> ${title}
            </div>
            <div class="inspect-row" style="color:#858585; padding-left:15px; margin-top:5px;">Trống</div>
        </div>`;
    }

    const activeClass = isOpen ? 'active' : '';
    const openClass = isOpen ? 'open' : '';
    return `
    <div class="inspect-section">
        <div class="inspect-title inspect-header ${activeClass}" onclick="this.classList.toggle('active'); this.nextElementSibling.classList.toggle('open');">
            <span class="inspect-caret">▶</span> ${title}
        </div>
        <div class="inspect-content ${openClass}">
            ${contentHtml}
        </div>
    </div>`;
}

function updateInspector(cls) {
    const ins = document.getElementById('node-inspector');
    
    const traits = cls.Traits || [];
    const attributes = cls.Attributes || [];
    const interfaces = cls.Interfaces || [];
    const usings = cls.Usings || [];
    const fields = cls.Fields || [];
    const properties = cls.Properties || [];
    const methods = cls.Methods || [];
    const enumValues = cls.EnumValues || [];

    // --- Build Dữ liệu các khối ---
    let couplingHtml = '';
    if (cls.BaseClass && cls.BaseClass !== "None" && cls.BaseClass !== "Object") {
        couplingHtml += `<div class="inspect-row" style="color:#e5c07b; margin-top:5px;">🔗 Kế thừa (Inherits): <b>${cls.BaseClass}</b></div>`;
    }
    if (interfaces.length > 0) {
        interfaces.forEach(i => { couplingHtml += `<div class="inspect-row" style="color:#b5cea8;">⚡ Thực thi (Implements): <b>${i}</b></div>`; });
    }

    let usingsHtml = usings.length > 0 ? usings.map(u => `<div class="using-item">using ${u};</div>`).join('') : '';
    let enumHtml = (cls.Kind === "Enum" && enumValues.length > 0) ? enumValues.map(e => `<div class="inspect-row">- ${e}</div>`).join('') : '';
    let fieldsHtml = fields.length > 0 ? fields.map(f => `<div class="inspect-row">- <span class="tag">${f.Type}</span> ${f.Name}</div>`).join('') : '';
    let propsHtml = properties.length > 0 ? properties.map(p => `<div class="inspect-row">- <span class="tag">${p.Type}</span> ${p.Name} <span style="color:#858585; font-size:10px;">[${p.HasSetter ? 'get, set' : 'get'}]</span></div>`).join('') : '';
    
    let methodsHtml = methods.length > 0 ? methods.map(m => {
        let deps = (m.MethodDependencies || []).map(d => {
            let dColor = d.DependencyType === 'SignalFire' ? '#ff55ff' : '#4ec9b0';
            let safeContext = d.RawContext ? d.RawContext.replace(/</g, "&lt;").replace(/>/g, "&gt;") : ''; 

            couplingHtml += `
            <div class="inspect-row" style="margin-top:6px; padding-left:8px; border-left:2px solid ${dColor}; background:rgba(0,0,0,0.15); padding-top:6px; padding-bottom:6px; border-radius: 0 4px 4px 0;">
                <span style="color:#858585; font-size:11px;">[${d.DependencyType}]</span> tại hàm <b>${m.Name}()</b><br/>
                ↳ Gọi tới: <span style="color:${dColor};">${d.TargetClass}.${d.TargetMethod}()</span>
                ${safeContext ? `<div style="color:#d19a66; font-size:11px; margin-top:4px; font-family:monospace; background:#1e1e1e; padding:4px; border-radius:3px; word-break: break-all;">${safeContext}</div>` : ''}
            </div>`;

            return `
            <div style="margin-left:10px; color:#c678dd; font-size: 11px;">↳ Gọi <b>${d.TargetClass}</b>.${d.TargetMethod}()</div>
            ${safeContext ? `<div class="raw-context" style="font-family:monospace; font-size:10px; color:#d19a66; margin-left:10px; padding:2px 4px; background:#1e1e1e; border-radius:2px; word-break: break-all;">${safeContext}</div>` : ''}
            `;
        }).join('');

        if (m.FiredSignals && m.FiredSignals.length > 0) {
            m.FiredSignals.forEach(sig => {
                couplingHtml += `
                <div class="inspect-row" style="margin-top:6px; padding-left:8px; border-left:2px solid #ff55ff; background:rgba(0,0,0,0.15); padding-top:6px; padding-bottom:6px; border-radius: 0 4px 4px 0;">
                    <span style="color:#858585; font-size:11px;">[FiresSignal]</span> tại hàm <b>${m.Name}()</b><br/>
                    ↳ Bắn sự kiện: <span style="color:#ff55ff;">${sig}</span>
                </div>`;
            });
        }

        let smartDataHtml = '';
        if (m.ImplementedInterface) smartDataHtml += `<div style="color:#b5cea8; font-size:11px; margin-top:4px; margin-left:10px;">⚡ Map: <b>${m.ImplementedInterface}</b></div>`;
        if (m.MutatedFields && m.MutatedFields.length > 0) smartDataHtml += `<div style="color:#d19a66; font-size:11px; margin-top:2px; margin-left:10px;">✍️ Ghi: <b>${m.MutatedFields.join(', ')}</b></div>`;
        if (m.FiredSignals && m.FiredSignals.length > 0) smartDataHtml += `<div style="color:#ff55ff; font-size:11px; margin-top:2px; margin-left:10px;">📡 Phát: <b>${m.FiredSignals.join(', ')}</b></div>`;

        return `<div class="inspect-row" style="margin-top:10px; border-left: 2px solid #3e3e42; padding-left: 8px;">
            <b>${m.Name}()</b> : <span style="color:#569cd6">${m.ReturnType}</span>
            ${smartDataHtml}
            ${deps}
        </div>`;
    }).join('') : '';

    if (couplingHtml === '') couplingHtml = '<div class="inspect-row" style="color:#858585; padding-left:15px; margin-top:5px;">Isolated Node (Không có liên kết đi ra)</div>';

    // --- Build Khối Core Info (Luôn hiện ở trên cùng) ---
    let traitsHtml = traits.length > 0 ? `<div class="inspect-row">` + traits.map(t => `<span class="tag tag-trait">${t}</span>`).join(' ') + `</div>` : '';
    let attrHtml = attributes.length > 0 ? attributes.map(a => `<div class="inspect-row"><span class="tag tag-attr">[${a}]</span></div>`).join('') : '';
    let ifaceHtml = interfaces.length > 0 ? interfaces.join(', ') : 'None';

    let coreInfoHtml = `
        <div class="inspect-section" style="border-bottom:none;">
            ${attrHtml}
            ${traitsHtml}
            <div class="inspect-title" style="margin-top:5px; font-size:16px; color:#fff;">${cls.Kind || 'Class'} ${cls.Name}</div>
            <div class="inspect-row"><b>Namespace:</b> <span style="color:#9cdcfe">${cls.Namespace}</span></div>
            <div class="inspect-row"><b>Path:</b> <span style="color:#ce9178">${cls.FolderPath}</span></div>
            <div class="inspect-row" style="margin-top:8px;"><b>BaseClass:</b> <span style="color:#e5c07b">${cls.BaseClass || 'None'}</span></div>
            <div class="inspect-row"><b>Interfaces:</b> <span style="color:#b5cea8">${ifaceHtml}</span></div>
        </div>
    `;

    // --- Ráp toàn bộ lại bằng tính năng Cuộn ---
    // Cấu hình: Coupling (Mở), Usings (Đóng), Fields (Đóng), Properties (Đóng), Methods (Mở)
    ins.innerHTML = `
        ${coreInfoHtml}
        <hr style="border-color:#444; margin-bottom:15px;">
        ${buildCollapsibleSection('🔗 Coupling & Relational Matrix', couplingHtml, true)}
        ${buildCollapsibleSection('📑 Usings', usingsHtml, false)}
        ${cls.Kind === "Enum" ? buildCollapsibleSection('🔢 Enum Values', enumHtml, true) : ''}
        ${buildCollapsibleSection('📦 Fields', fieldsHtml, false)}
        ${buildCollapsibleSection('⚙️ Properties', propsHtml, false)}
        ${buildCollapsibleSection('⚡ Methods & Logic Trace', methodsHtml, true)}
    `;
}