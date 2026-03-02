// DTOs for Babylon.js Node Particle Editor (NodeParticleSystemSet) JSON format.
// Structure matches core/Particles/Node serialization (nodeParticleSystemSet.ts, nodeParticleBlock.ts, etc.)

using System;
using System.Collections.Generic;

namespace BabylonNodeParticle
{
    [Serializable]
    public class NodeParticleSystemSetJson
    {
        public string customType = "BABYLON.NodeParticleSystemSet";
        public string name;
        public string comment;
        public EditorDataJson editorData;
        public List<BlockJson> blocks = new List<BlockJson>();
    }

    [Serializable]
    public class EditorDataJson
    {
        public List<LocationJson> locations = new List<LocationJson>();
    }

    [Serializable]
    public class LocationJson
    {
        public int blockId;
        public float x;
        public float y;
        public bool isCollapsed;
    }

    /// <summary>Base block: id, name, customType, inputs, outputs. Subclasses add block-specific fields.</summary>
    [Serializable]
    public class BlockJson
    {
        public string customType;
        public int id;
        public string name;
        public List<InputPortJson> inputs = new List<InputPortJson>();
        public List<OutputPortJson> outputs = new List<OutputPortJson>();
    }

    /// <summary>Input port: either connection (targetBlockId, targetConnectionName, inputName) or value (value, valueType).</summary>
    [Serializable]
    public class InputPortJson
    {
        public string name;
        public string displayName;
        public string valueType; // "number" or "BABYLON.Vector3", "BABYLON.Vector2", "BABYLON.Color4"
        public object value;
        public int? targetBlockId;
        public string targetConnectionName;
        public string inputName;
    }

    [Serializable]
    public class OutputPortJson
    {
        public string name;
        public string displayName;
    }

    // --- Block types used by Shuriken converter ---

    [Serializable]
    public class SystemBlockJson : BlockJson
    {
        public int capacity = 1000;
        public int manualEmitCount = -1;
        public int blendMode = 0;
        public float updateSpeed = 0.0167f;
        public float preWarmCycles;
        public float preWarmStepOffset = 1f;
        public bool isBillboardBased = true;
        public int billBoardMode = 0;
        public bool isLocal;
        public bool disposeOnStop;
        public bool doNoStart;
        public int renderingGroupId;
        public float startDelay;
        public object emitRate; // number when constant
        public float[] emitter; // Vector3 as [x,y,z] or null
        public float targetStopDuration;
        public object translationPivot;
        public object textureMask;
    }

    [Serializable]
    public class CreateParticleBlockJson : BlockJson { }

    [Serializable]
    public class PointShapeBlockJson : BlockJson { }

    [Serializable]
    public class BoxShapeBlockJson : BlockJson { }

    [Serializable]
    public class SphereShapeBlockJson : BlockJson
    {
        public bool isHemispheric;
    }

    [Serializable]
    public class ConeShapeBlockJson : BlockJson
    {
        public bool emitFromSpawnPointOnly;
    }

    [Serializable]
    public class CylinderShapeBlockJson : BlockJson { }

    [Serializable]
    public class ParticleInputBlockJson : BlockJson
    {
        public int type; // NodeParticleBlockConnectionPointTypes
        public int? contextualValue;
        public int? systemSource;
        public int min;
        public int max;
        public string groupInInspector;
        public bool displayInInspector;
        /// <summary>Block-level value (NPE format). When set, inputs are empty.</summary>
        public string valueType;
        public object value;
    }

    [Serializable]
    public class ParticleRandomBlockJson : BlockJson
    {
        public int lockMode; // ParticleRandomBlockLocks: 0 PerParticle, 1 OncePerParticle, 2 PerSystem
    }

    [Serializable]
    public class ParticleMathBlockJson : BlockJson
    {
        public int operation; // ParticleMathBlockOperations: Add=0, Subtract=1, Multiply=2, etc.
    }

    [Serializable]
    public class ParticleLerpBlockJson : BlockJson { }

    [Serializable]
    public class ParticleGradientBlockJson : BlockJson
    {
        public int _entryCount = 1;
    }

    /// <summary>One color stop in a gradient. reference = position on gradient 0..1 (key time); value input = color at this key.</summary>
    [Serializable]
    public class ParticleGradientValueBlockJson : BlockJson
    {
        /// <summary>Gradient key position 0..1 for this stop. Serialized as number in JSON.</summary>
        public float reference;
    }

    [Serializable]
    public class UpdatePositionBlockJson : BlockJson { }

    [Serializable]
    public class UpdateColorBlockJson : BlockJson { }

    [Serializable]
    public class UpdateSizeBlockJson : BlockJson { }

    [Serializable]
    public class UpdateAngleBlockJson : BlockJson { }

    [Serializable]
    public class ParticleClampBlockJson : BlockJson
    {
        public float minimum;
        public float maximum = 1f;
    }

    [Serializable]
    public class ParticleTextureSourceBlockJson : BlockJson
    {
        public string url;
        public bool serializedCachedData;
        public bool invertY;
        public string textureDataUrl;
    }
}
