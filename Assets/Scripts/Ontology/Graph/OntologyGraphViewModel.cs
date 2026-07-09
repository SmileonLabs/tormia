using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Graph
{
    public enum OntologyGraphNodeKind
    {
        Center,
        Visual,
        Semantic,
        Functional,
        Simulation,
        Concept
    }

    public sealed class OntologyGraphViewModel
    {
        public readonly List<OntologyGraphNode> Nodes = new();
        public readonly List<OntologyGraphEdge> Edges = new();
    }

    public sealed class OntologyGraphNode
    {
        public string Id;
        public string Label;
        public OntologyGraphNodeKind Kind;
        public Color Color;
        public Vector3 Position;
        public float Radius = 0.12f;
    }

    public sealed class OntologyGraphEdge
    {
        public string FromId;
        public string ToId;
        public string Label;
        public string PredicateId;
        public Color Color;
    }
}
