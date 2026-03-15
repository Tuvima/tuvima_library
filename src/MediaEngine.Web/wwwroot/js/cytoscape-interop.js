// cytoscape-interop.js — Blazor <-> Cytoscape.js bridge
// Cytoscape.js is MIT licensed — compatible with AGPLv3

let _cy = null;
let _dotNetRef = null;

window.CytoscapeInterop = {
    /**
     * Initialize the Cytoscape graph in a container element.
     * @param {string} containerId - DOM element ID for the graph container
     * @param {string} nodesJson - JSON array of node objects
     * @param {string} edgesJson - JSON array of edge objects
     * @param {string} layout - Layout algorithm name (cose, dagre, concentric, grid)
     * @param {object} dotNetRef - DotNetObjectReference for callbacks
     */
    initGraph: function (containerId, nodesJson, edgesJson, layout, dotNetRef) {
        _dotNetRef = dotNetRef;
        var nodes = JSON.parse(nodesJson);
        var edges = JSON.parse(edgesJson);

        if (_cy) { _cy.destroy(); _cy = null; }

        _cy = cytoscape({
            container: document.getElementById(containerId),
            elements: [
                ...nodes.map(function (n) {
                    return {
                        group: 'nodes',
                        data: {
                            id: n.id,
                            label: n.label,
                            type: n.type,
                            description: n.description || '',
                            image: n.image || null
                        }
                    };
                }),
                ...edges.map(function (e) {
                    return {
                        group: 'edges',
                        data: {
                            id: e.source + '-' + e.type + '-' + e.target,
                            source: e.source,
                            target: e.target,
                            type: e.type,
                            label: e.label,
                            confidence: e.confidence,
                            start_time: e.start_time || null,
                            end_time: e.end_time || null
                        }
                    };
                })
            ],
            style: [
                // Node base style
                {
                    selector: 'node',
                    style: {
                        'label': 'data(label)',
                        'text-valign': 'bottom',
                        'text-halign': 'center',
                        'font-size': '11px',
                        'color': '#e0e0e0',
                        'text-outline-color': '#000',
                        'text-outline-width': 1,
                        'width': 40,
                        'height': 40,
                        'border-width': 2,
                        'border-color': '#555'
                    }
                },
                // Character nodes — circle, blue
                {
                    selector: 'node[type="Character"]',
                    style: {
                        'background-color': '#3b82f6',
                        'shape': 'ellipse',
                        'border-color': '#60a5fa'
                    }
                },
                // Location nodes — diamond, green
                {
                    selector: 'node[type="Location"]',
                    style: {
                        'background-color': '#22c55e',
                        'shape': 'diamond',
                        'border-color': '#4ade80'
                    }
                },
                // Organization nodes — hexagon, amber
                {
                    selector: 'node[type="Organization"]',
                    style: {
                        'background-color': '#f59e0b',
                        'shape': 'hexagon',
                        'border-color': '#fbbf24'
                    }
                },
                // Node with image
                {
                    selector: 'node[image]',
                    style: {
                        'background-image': 'data(image)',
                        'background-fit': 'cover',
                        'background-clip': 'node'
                    }
                },
                // Selected node — golden amber accent
                {
                    selector: 'node:selected',
                    style: {
                        'border-width': 3,
                        'border-color': '#c9922e',
                        'overlay-opacity': 0.1
                    }
                },
                // Edge base style
                {
                    selector: 'edge',
                    style: {
                        'label': 'data(label)',
                        'font-size': '9px',
                        'color': '#999',
                        'text-rotation': 'autorotate',
                        'text-outline-color': '#000',
                        'text-outline-width': 1,
                        'width': 1.5,
                        'line-color': '#555',
                        'target-arrow-color': '#555',
                        'target-arrow-shape': 'triangle',
                        'curve-style': 'bezier',
                        'opacity': 0.7
                    }
                },
                // High confidence edges
                {
                    selector: 'edge[confidence >= 0.8]',
                    style: {
                        'width': 2,
                        'opacity': 0.9
                    }
                },
                // Selected edge — golden amber accent
                {
                    selector: 'edge:selected',
                    style: {
                        'line-color': '#c9922e',
                        'target-arrow-color': '#c9922e',
                        'width': 3,
                        'opacity': 1
                    }
                }
            ],
            layout: { name: layout || 'cose', animate: true, animationDuration: 500 },
            minZoom: 0.3,
            maxZoom: 3,
            wheelSensitivity: 0.3
        });

        // Register click handlers
        _cy.on('tap', 'node', function (evt) {
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnNodeClick', evt.target.data('id'));
            }
        });

        _cy.on('tap', 'edge', function (evt) {
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnEdgeClick',
                    evt.target.data('source'),
                    evt.target.data('target'),
                    evt.target.data('type'));
            }
        });
    },

    /**
     * Replace graph data entirely.
     */
    updateGraph: function (nodesJson, edgesJson) {
        if (!_cy) return;
        var nodes = JSON.parse(nodesJson);
        var edges = JSON.parse(edgesJson);
        _cy.elements().remove();
        _cy.add([
            ...nodes.map(function (n) {
                return {
                    group: 'nodes',
                    data: {
                        id: n.id,
                        label: n.label,
                        type: n.type,
                        description: n.description || '',
                        image: n.image || null
                    }
                };
            }),
            ...edges.map(function (e) {
                return {
                    group: 'edges',
                    data: {
                        id: e.source + '-' + e.type + '-' + e.target,
                        source: e.source,
                        target: e.target,
                        type: e.type,
                        label: e.label,
                        confidence: e.confidence,
                        start_time: e.start_time || null,
                        end_time: e.end_time || null
                    }
                };
            })
        ]);
        _cy.layout({ name: 'cose', animate: true, animationDuration: 500 }).run();
    },

    /**
     * Filter edges by timeline year — hide edges that start after the given year.
     */
    filterByTimelineYear: function (year) {
        if (!_cy) return;
        _cy.edges().forEach(function (edge) {
            var startTime = edge.data('start_time');
            if (startTime) {
                var startYear = parseInt(startTime.substring(0, 4).replace('+', ''), 10);
                if (!isNaN(startYear) && startYear > year) {
                    edge.style('display', 'none');
                } else {
                    edge.style('display', 'element');
                }
            } else {
                edge.style('display', 'element');
            }
        });
    },

    /**
     * Center and highlight a specific node.
     */
    focusNode: function (nodeId) {
        if (!_cy) return;
        var node = _cy.getElementById(nodeId);
        if (node.length > 0) {
            _cy.animate({ center: { eles: node }, zoom: 1.5 }, { duration: 400 });
            _cy.elements().unselect();
            node.select();
        }
    },

    /**
     * Change graph layout algorithm.
     */
    setLayout: function (layoutName) {
        if (!_cy) return;
        _cy.layout({ name: layoutName, animate: true, animationDuration: 500 }).run();
    },

    /**
     * Cleanup the graph instance.
     */
    destroy: function () {
        if (_cy) {
            _cy.destroy();
            _cy = null;
        }
        _dotNetRef = null;
    }
};
